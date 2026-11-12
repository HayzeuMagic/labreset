// Lab Reset - 7-Zip wrapper. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LabReset
{
    // ------------------------------------------------------------------ 7-Zip (for .7z / .rar / .tar / .tar.gz)
    public static class SevenZip
    {
        public static string ExePath; // the copy bundled with the app (7zip\7z.exe)

        public static string Find()
        {
            if (ExePath != null && File.Exists(ExePath)) return ExePath;
            string pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"7-Zip\7z.exe");
            return File.Exists(pf) ? pf : null;
        }

        static string Exe()
        {
            string exe = Find();
            if (exe == null) throw new FileNotFoundException("7-Zip (7z.exe) was not found. It should be in the app's 7zip folder.");
            return exe;
        }

        static Process Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            return Process.Start(psi);
        }

        static Process Start(string args) { return Run(Exe(), args); }

        /// <summary>Runs "7z args1 | 7z args2" through cmd.exe, which passes the bytes between them untouched (used for .tar.gz).</summary>
        static Process StartPipe(string args1, string args2)
        {
            string exe = "\"" + Exe() + "\"";
            return Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"" + exe + " " + args1 + " | " + exe + " " + args2 + "\"");
        }

        /// <summary>Stops a process and everything it started (cmd.exe + both 7z.exe).</summary>
        static void Kill(Process p)
        {
            try
            {
                if (p.HasExited) return;
                using (var k = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/T /F /PID " + p.Id)
                                             { UseShellExecute = false, CreateNoWindow = true }))
                    k.WaitForExit(10000);
                if (!p.HasExited) p.Kill();
            }
            catch { }
        }

        static string ErrorText(string s)
        {
            var lines = (s ?? "").Replace("\b", "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.EndsWith("%")).ToList();
            var errs = lines.Where(l => l.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 || l.StartsWith("Can not", StringComparison.OrdinalIgnoreCase)).ToList();
            var pick = errs.Count > 0 ? errs : lines;
            return pick.Count == 0 ? "unknown error" : string.Join(" ", pick.Take(3));
        }

        /// <summary>Reads 7-Zip progress output ("45%") and reports percentages. Keeps the text for error messages.</summary>
        static string ReadProgress(StreamReader r, Action<int> percent)
        {
            var keep = new StringBuilder();
            var digits = new StringBuilder();
            var buf = new char[512];
            int n;
            while ((n = r.Read(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    char c = buf[i];
                    if (char.IsDigit(c)) digits.Append(c);
                    else
                    {
                        if (c == '%' && digits.Length > 0 && digits.Length <= 3 && percent != null) percent(int.Parse(digits.ToString()));
                        digits.Clear();
                    }
                }
                if (keep.Length < 20000) keep.Append(buf, 0, n);
            }
            return keep.ToString();
        }

        static List<KeyValuePair<string, bool>> ParseList(string output)
        {
            var result = new List<KeyValuePair<string, bool>>();
            bool started = false;
            string path = null;
            bool dir = false;
            foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.None))
            {
                string l = raw.TrimEnd();
                if (!started) { if (l == "----------") started = true; continue; }
                if (l.StartsWith("Path = "))
                {
                    if (path != null) result.Add(new KeyValuePair<string, bool>(path, dir));
                    path = l.Substring(7);
                    dir = false;
                }
                else if (l == "Folder = +") dir = true;
                else if (l.StartsWith("Attributes = ") && l.Length > 13 && l[13] == 'D') dir = true;
            }
            if (path != null) result.Add(new KeyValuePair<string, bool>(path, dir));
            return result;
        }

        public static List<KeyValuePair<string, bool>> List(string archive, bool tarGz)
        {
            string q = "\"" + archive + "\"";
            using (Process p = tarGz
                ? StartPipe("x -so -bd " + q, "l -slt -sccUTF-8 -si -ttar")
                : Start("l -slt -sccUTF-8 -p- " + q))
            {
                Task<string> err = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode > 1) throw new IOException("7-Zip could not read the archive: " + ErrorText(err.Result + output));
                return ParseList(output);
            }
        }

        public static void Extract(string archive, bool tarGz, string targetDir, Action<int> percent, CancellationToken ct)
        {
            targetDir = targetDir.TrimEnd('\\');
            string q = "\"" + archive + "\"", o = "\"-o" + targetDir + "\"";
            if (!tarGz)
            {
                using (Process p = Start("x -y -aoa -p- -bso0 -bsp1 -sccUTF-8 " + o + " " + q))
                using (ct.Register(() => Kill(p)))
                {
                    Task<string> err = p.StandardError.ReadToEndAsync();
                    ReadProgress(p.StandardOutput, percent);
                    p.WaitForExit();
                    ct.ThrowIfCancellationRequested();
                    if (p.ExitCode > 1) throw new IOException("7-Zip error: " + ErrorText(err.Result));
                }
            }
            else
            {
                // first 7z un-gzips (progress on stderr), second 7z un-tars into the target folder
                using (Process p = StartPipe("x -so -bsp2 " + q, "x -y -aoa -bso0 -bsp0 -sccUTF-8 -si -ttar " + o))
                using (ct.Register(() => Kill(p)))
                {
                    Task<string> outp = p.StandardOutput.ReadToEndAsync();
                    string err = ReadProgress(p.StandardError, percent);
                    p.WaitForExit();
                    ct.ThrowIfCancellationRequested();
                    if (p.ExitCode > 1) throw new IOException("7-Zip error: " + ErrorText(err + "\n" + outp.Result));
                }
            }
        }
    }
}
