// Lab Reset - makes a lab PC extract its OWN copy of an archive. C# 5 / .NET Framework 4.5+ compatible.
//
// How it works (same idea as PsExec, using the admin access Lab Reset already needs):
//   1. Copies LabResetAgent.exe + 7-Zip to \\PC\C$\ProgramData\LabReset
//   2. Writes job.ini there (which archives, options)
//   3. Installs/starts the "LabResetAgent" service on that PC through its Service Control Manager
//      (over the same File and Printer Sharing connection - no extra firewall rule)
//   4. Reads status.txt every second for progress and the agent's log lines
// The archive is read and extracted on the lab PC itself, so nothing big crosses the network.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace LabReset
{
    public class AgentResult
    {
        public int Ok, Failed;
        public string State = "", Message = "";
    }

    public static class RemoteAgent
    {
        public const string ServiceName = "LabResetAgent";
        public const string LocalDir = @"C:\ProgramData\LabReset";

        /// <summary>The teacher app's folder (holds LabResetAgent.exe and 7zip\).</summary>
        public static string SourceDir;

        // source (relative to SourceDir) -> file name on the lab PC
        static readonly string[][] AgentFiles = {
            new[] { "LabResetAgent.exe", "LabResetAgent.exe" },
            new[] { @"7zip\7z.exe", "7z.exe" },
            new[] { @"7zip\7z.dll", "7z.dll" } };

        // ------------------------------------------------------------------ Service Control Manager
        const uint SC_MANAGER_CONNECT = 0x0001, SC_MANAGER_CREATE_SERVICE = 0x0002;
        const uint SERVICE_ALL_ACCESS = 0xF01FF, SERVICE_NO_CHANGE = 0xFFFFFFFF;
        const uint SERVICE_WIN32_OWN_PROCESS = 0x10, SERVICE_DEMAND_START = 3, SERVICE_ERROR_NORMAL = 1;
        const int ERROR_SERVICE_EXISTS = 1073;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenSCManager(string machine, string database, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateService(IntPtr scm, string name, string display, uint access, uint type, uint start, uint error,
                                           string path, string group, IntPtr tag, string deps, string user, string password);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenService(IntPtr scm, string name, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool ChangeServiceConfig(IntPtr svc, uint type, uint start, uint error, string path, string group,
                                               IntPtr tag, string deps, string user, string password, string display);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CloseServiceHandle(IntPtr h);

        static string Win32Error(int code) { return new System.ComponentModel.Win32Exception(code).Message; }

        /// <summary>Creates the LabResetAgent service on the PC (or points the existing one at the right exe).</summary>
        static void InstallService(string pc)
        {
            string exe = "\"" + Path.Combine(LocalDir, "LabResetAgent.exe") + "\"";
            IntPtr scm = OpenSCManager(pc, null, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
            if (scm == IntPtr.Zero)
                throw new IOException("can't open the service manager on that PC: " + Win32Error(Marshal.GetLastWin32Error()));
            try
            {
                IntPtr svc = CreateService(scm, ServiceName, "Lab Reset Agent", SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS,
                                           SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL, exe, null, IntPtr.Zero, null, null, null);
                if (svc == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_SERVICE_EXISTS) throw new IOException("can't install the agent service: " + Win32Error(err));
                    svc = OpenService(scm, ServiceName, SERVICE_ALL_ACCESS);
                    if (svc == IntPtr.Zero) throw new IOException("can't open the agent service: " + Win32Error(Marshal.GetLastWin32Error()));
                    ChangeServiceConfig(svc, SERVICE_NO_CHANGE, SERVICE_DEMAND_START, SERVICE_NO_CHANGE, exe, null, IntPtr.Zero, null, null, null, null);
                }
                CloseServiceHandle(svc);
            }
            finally { CloseServiceHandle(scm); }
        }

        static ServiceControllerStatus? GetStatus(string pc)
        {
            try
            {
                using (var sc = new ServiceController(ServiceName, pc))
                {
                    sc.Refresh();
                    return sc.Status;
                }
            }
            catch (InvalidOperationException) { return null; } // not installed
        }

        static void StopService(string pc)
        {
            try
            {
                using (var sc = new ServiceController(ServiceName, pc))
                {
                    sc.Refresh();
                    if (sc.Status == ServiceControllerStatus.Stopped) return;
                    if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(40));
                }
            }
            catch (InvalidOperationException) { }            // not installed
            catch (System.ServiceProcess.TimeoutException) { } // still stopping - the copy below will report it
        }

        // ------------------------------------------------------------------ files
        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        /// <summary>Copies the agent and 7-Zip to the PC (only the files that changed).</summary>
        static void Deploy(string pc, string share)
        {
            if (SourceDir == null) throw new InvalidOperationException("RemoteAgent.SourceDir not set");
            foreach (string[] f in AgentFiles)
            {
                string src = Path.Combine(SourceDir, f[0]);
                if (!File.Exists(src))
                    throw new FileNotFoundException(f[0] + " is missing next to LabReset.exe - run build.bat again.");
            }
            Directory.CreateDirectory(share);
            StopService(pc);
            foreach (string[] f in AgentFiles)
            {
                string src = Path.Combine(SourceDir, f[0]), dst = Path.Combine(share, f[1]);
                var s = new FileInfo(src);
                var d = new FileInfo(dst);
                if (d.Exists && d.Length == s.Length && d.LastWriteTimeUtc == s.LastWriteTimeUtc) continue;
                if (d.Exists) File.SetAttributes(dst, FileAttributes.Normal);
                File.Copy(src, dst, true);
                File.SetLastWriteTimeUtc(dst, s.LastWriteTimeUtc);
            }
        }

        static string ReadShared(string file)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs, Encoding.UTF8))
                return r.ReadToEnd();
        }

        class Status
        {
            public string Id = "", State = "", Archive = "", Message = "";
            public int Index, Count, Percent;
            public List<string> Log = new List<string>();
            public List<string[]> Results = new List<string[]>();

            public bool Final { get { return State == "Done" || State == "Failed" || State == "Cancelled"; } }

            public static Status Parse(string text)
            {
                var s = new Status();
                foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (line.StartsWith("L\t")) { s.Log.Add(line.Substring(2)); continue; }
                    if (line.StartsWith("R\t")) { s.Results.Add(line.Substring(2).Split('\t')); continue; }
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    int n;
                    switch (k)
                    {
                        case "Id": s.Id = v; break;
                        case "State": s.State = v; break;
                        case "Archive": s.Archive = v; break;
                        case "Message": s.Message = v; break;
                        case "Index": if (int.TryParse(v, out n)) s.Index = n; break;
                        case "Count": if (int.TryParse(v, out n)) s.Count = n; break;
                        case "Percent": if (int.TryParse(v, out n)) s.Percent = n; break;
                    }
                }
                return s;
            }
        }

        // ------------------------------------------------------------------ run a job
        /// <summary>
        /// Has the lab PC extract its own copies of the named archives (found in its own __RivanVMs folder).
        /// progress(archive, index (1-based), count, percent). log gets the agent's log lines.
        /// Throws OperationCanceledException if ct was cancelled (after asking the agent to stop).
        /// </summary>
        public static AgentResult RunExtract(string pc, List<string> archives, bool ownFolder, bool cleanFirst,
                                             Action<string, int, int, int> progress, Action<string> log, CancellationToken ct)
        {
            string share = Paths.ToAccess(pc, LocalDir);
            string statusFile = Path.Combine(share, "status.txt"), cancelFile = Path.Combine(share, "cancel.flag");

            Deploy(pc, share);

            string id = Guid.NewGuid().ToString("N");
            TryDelete(statusFile);
            TryDelete(cancelFile);
            var job = new List<string> { "# Lab Reset job - written by " + Environment.MachineName + " at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                         "Id=" + id, "OwnFolder=" + (ownFolder ? "1" : "0"), "CleanFirst=" + (cleanFirst ? "1" : "0") };
            job.AddRange(archives.Select(a => "Archive=" + a));
            string jobFile = Path.Combine(share, "job.ini"), jobTmp = jobFile + ".tmp";
            File.WriteAllLines(jobTmp, job, new UTF8Encoding(false));
            TryDelete(jobFile);
            File.Move(jobTmp, jobFile);

            InstallService(pc);
            using (var sc = new ServiceController(ServiceName, pc)) sc.Start();

            var result = new AgentResult();
            int logged = 0, readErrors = 0;
            bool cancelSent = false;
            DateTime started = DateTime.UtcNow, cancelAt = DateTime.MinValue, lastServiceCheck = DateTime.UtcNow;
            Status st = null;

            while (true)
            {
                if (!cancelSent) ct.WaitHandle.WaitOne(1000);
                else Thread.Sleep(1000);

                if (ct.IsCancellationRequested && !cancelSent)
                {
                    try { File.WriteAllText(cancelFile, "stop"); } catch { }
                    cancelSent = true;
                    cancelAt = DateTime.UtcNow;
                }

                Status cur = null;
                try
                {
                    if (File.Exists(statusFile)) cur = Status.Parse(ReadShared(statusFile));
                    readErrors = 0;
                }
                catch (IOException) { readErrors++; }
                catch (UnauthorizedAccessException) { readErrors++; }

                if (cur != null && cur.Id == id)
                {
                    st = cur;
                    for (; logged < st.Log.Count; logged++) log(st.Log[logged]);
                    if (st.Index > 0) progress(st.Archive, st.Index, Math.Max(1, st.Count), st.Percent);
                    if (st.Final) break;
                }

                if (readErrors > 120) throw new IOException("lost the connection to the PC while it was extracting (the agent may still be running there).");

                if ((DateTime.UtcNow - lastServiceCheck).TotalSeconds >= 5)
                {
                    lastServiceCheck = DateTime.UtcNow;
                    ServiceControllerStatus? s = GetStatus(pc);
                    if (s == ServiceControllerStatus.Stopped && (st == null || !st.Final))
                    {
                        // one last look - the agent may have finished between our reads
                        try
                        {
                            Status last = Status.Parse(ReadShared(statusFile));
                            if (last.Id == id && last.Final)
                            {
                                st = last;
                                for (; logged < st.Log.Count; logged++) log(st.Log[logged]);
                                break;
                            }
                        }
                        catch { }
                        throw new IOException("the agent stopped unexpectedly. See " + LocalDir + @"\Logs on that PC.");
                    }
                    if (st == null && (DateTime.UtcNow - started).TotalSeconds > 90)
                    {
                        StopService(pc);
                        throw new IOException("the agent did not start on that PC.");
                    }
                }

                if (cancelSent && (DateTime.UtcNow - cancelAt).TotalSeconds > 60)
                {
                    StopService(pc);
                    break;
                }
            }

            if (st != null)
            {
                result.State = st.State;
                result.Message = st.Message;
                result.Ok = st.Results.Count(r => r.Length > 0 && r[0] == "OK");
                result.Failed = st.Results.Count(r => r.Length > 0 && r[0] == "FAIL");
                if (st.State == "Failed") result.Failed = Math.Max(result.Failed, archives.Count - result.Ok);
            }
            if (cancelSent) throw new OperationCanceledException(ct);
            return result;
        }
    }
}
