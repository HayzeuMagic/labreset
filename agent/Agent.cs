// Lab Reset Agent - runs ON a lab PC and extracts that PC's own copy of the archives.
// C# 5 / .NET Framework 4.5+ compatible.
//
// The teacher's Lab Reset copies this program to C:\ProgramData\LabReset on the lab PC,
// writes job.ini there, and starts it as the "LabResetAgent" Windows service.
// The agent finds each archive in the PC's own X:\__RivanVMs folder, extracts it right there
// (nothing is sent over the network), and writes its progress to status.txt, which the
// teacher's Lab Reset reads to show progress. Creating cancel.flag stops the job.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace LabReset
{
    static class AgentProgram
    {
        static void Main(string[] args)
        {
            // "LabResetAgent.exe run" runs the current job.ini in the foreground (for testing).
            if (args.Length > 0 && args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                new AgentJob().Run(CancellationToken.None);
                return;
            }
            ServiceBase.Run(new AgentService());
        }
    }

    class AgentService : ServiceBase
    {
        CancellationTokenSource cts;
        Thread worker;

        public AgentService()
        {
            ServiceName = AgentJob.ServiceName;
            CanStop = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            cts = new CancellationTokenSource();
            worker = new Thread(() =>
            {
                try { new AgentJob().Run(cts.Token); }
                finally
                {
                    // the job is done: stop the service (from another thread, so OnStop can wait for this one)
                    ThreadPool.QueueUserWorkItem(_ => { try { Stop(); } catch { } });
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        protected override void OnStop()
        {
            if (cts != null) cts.Cancel();
            if (worker != null && worker != Thread.CurrentThread) worker.Join(30000);
        }
    }

    public class AgentJob
    {
        public const string ServiceName = "LabResetAgent";

        public static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        static string JobFile { get { return Path.Combine(Dir, "job.ini"); } }
        static string StatusFile { get { return Path.Combine(Dir, "status.txt"); } }
        static string CancelFile { get { return Path.Combine(Dir, "cancel.flag"); } }

        readonly object lk = new object();
        readonly List<string> logLines = new List<string>();
        readonly List<string> results = new List<string>();
        string id = "", state = "Running", archive = "", message = "";
        int index, count, percent;
        DateTime lastWrite = DateTime.MinValue;

        void Log(string msg)
        {
            Logger.Write(msg);
            lock (lk) logLines.Add(msg.Replace('\r', ' ').Replace('\n', ' '));
            WriteStatus(false);
        }

        public void Run(CancellationToken outer)
        {
            Logger.Dir = Path.Combine(Dir, "Logs");
            SevenZip.ExePath = Path.Combine(Dir, "7z.exe");

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(outer))
            using (var timer = new System.Threading.Timer(_ =>
                   {
                       try { if (File.Exists(CancelFile)) cts.Cancel(); } catch { }
                       WriteStatus(false);
                   }, null, 1000, 1000))
            {
                try
                {
                    RunJob(cts.Token);
                    lock (lk) state = "Done";
                }
                catch (OperationCanceledException)
                {
                    Log("Stopped by the teacher.");
                    lock (lk) state = "Cancelled";
                }
                catch (Exception ex)
                {
                    Log("ERROR: " + ex.Message);
                    lock (lk) { state = "Failed"; message = ex.Message; }
                }
                timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            WriteStatus(true);
        }

        void RunJob(CancellationToken ct)
        {
            if (!File.Exists(JobFile)) throw new FileNotFoundException("No job.ini - nothing to do.");
            bool own = true, clean = true;
            var archives = new List<string>();
            foreach (string raw in File.ReadAllLines(JobFile))
            {
                int i = raw.IndexOf('=');
                if (i <= 0) continue;
                string k = raw.Substring(0, i).Trim().ToLowerInvariant(), v = raw.Substring(i + 1).Trim();
                if (k == "id") lock (lk) id = v;
                else if (k == "ownfolder") own = v == "1";
                else if (k == "cleanfirst") clean = v == "1";
                else if (k == "archive" && v.Length > 0) archives.Add(Path.GetFileName(v));
            }
            lock (lk) count = archives.Count;
            WriteStatus(true);
            Log("Job started on " + Environment.MachineName + ": " + archives.Count + " archive(s).");

            List<string> folders = VmFolder.Find(PcNames.ThisPc, ct);
            if (folders.Count == 0) throw new DirectoryNotFoundException("No " + VmFolder.Name + " folder on any drive of this PC.");

            // Never delete the agent itself or any archive
            var tree = new List<string> { Dir };
            foreach (string f in folders)
                try { tree.AddRange(Directory.GetFiles(f).Where(x => ZipSignature.IsArchive(Path.GetFileName(x)))); } catch { }

            string cache = Path.Combine(Dir, "cache");
            Directory.CreateDirectory(cache);

            for (int n = 0; n < archives.Count; n++)
            {
                ct.ThrowIfCancellationRequested();
                string name = archives[n];
                lock (lk) { index = n + 1; archive = name; percent = 0; }
                WriteStatus(true);

                string folder = folders.FirstOrDefault(f => File.Exists(Path.Combine(f, name)));
                if (folder == null)
                {
                    Log("MISSING: " + name + " is not in " + string.Join(" or ", folders) + " - not extracted.");
                    lock (lk) results.Add("FAIL\t" + name + "\tnot found on this PC");
                    continue;
                }
                try
                {
                    Log("Reading " + name + " ...");
                    ZipSignature z = ZipSignature.Load(Path.Combine(folder, name), cache);
                    int files = FileOps.ExtractArchive(z, PcNames.ThisPc, folder, own, clean, tree, Log, p =>
                    {
                        lock (lk) percent = Math.Max(0, Math.Min(100, p));
                    }, ct);
                    lock (lk) percent = 100;
                    Log("Extracted " + name + " -> " + FileOps.ResultFolder(z, folder, own) + "  (" + files + " files)");
                    lock (lk) results.Add("OK\t" + name);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log("FAILED " + name + ": " + ex.Message + (ex is IOException ? "  (is the VM still open, or is the disk full?)" : ""));
                    lock (lk) results.Add("FAIL\t" + name + "\t" + ex.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
                }
            }
        }

        /// <summary>Writes status.txt (at most twice a second unless force). The teacher's Lab Reset reads it over the network.</summary>
        void WriteStatus(bool force)
        {
            lock (lk)
            {
                if (!force && (DateTime.UtcNow - lastWrite).TotalMilliseconds < 500) return;
                lastWrite = DateTime.UtcNow;
                var sb = new StringBuilder();
                sb.AppendLine("Id=" + id);
                sb.AppendLine("State=" + state);
                sb.AppendLine("Archive=" + archive);
                sb.AppendLine("Index=" + index);
                sb.AppendLine("Count=" + count);
                sb.AppendLine("Percent=" + percent);
                sb.AppendLine("Message=" + message.Replace('\r', ' ').Replace('\n', ' '));
                foreach (string l in logLines) sb.AppendLine("L\t" + l);
                foreach (string r in results) sb.AppendLine("R\t" + r);

                string tmp = StatusFile + ".tmp";
                for (int attempt = 0; attempt < (force ? 20 : 1); attempt++)
                {
                    try
                    {
                        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                        if (File.Exists(StatusFile)) File.Delete(StatusFile);
                        File.Move(tmp, StatusFile);
                        return;
                    }
                    catch { Thread.Sleep(250); }
                }
            }
        }
    }
}
