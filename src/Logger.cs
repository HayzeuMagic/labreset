// Lab Reset - log file. C# 5 / .NET Framework 4.5+ compatible.
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
    public static class Logger
    {
        public static string Dir;
        public static event Action<string> Line;
        static readonly object lk = new object();

        public static void Write(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (lk)
            {
                try
                {
                    if (Dir != null)
                    {
                        Directory.CreateDirectory(Dir);
                        File.AppendAllText(Path.Combine(Dir, "LabReset-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"), line + Environment.NewLine);
                    }
                }
                catch { }
            }
            Action<string> h = Line;
            if (h != null) h(line);
        }
    }
}
