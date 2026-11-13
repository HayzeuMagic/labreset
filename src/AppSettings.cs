// Lab Reset - saved settings. C# 5 / .NET Framework 4.5+ compatible.
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
    // ------------------------------------------------------------------ settings & log
    public class AppSettings
    {
        public string ZipFolder = "Zips";
        public string Destination = @"C:\__RivanVMs";
        public string ScanFolders = @"C:\Users; C:\__RivanVMs";
        public string NetUser = "";
        public bool OwnFolder = true, CleanFirst = true, OwnCopy = true;
        public int MinMatch = 60;
        public List<KeyValuePair<string, bool>> Pcs = new List<KeyValuePair<string, bool>>();
        public List<string> CheckedZips = new List<string>();

        public static AppSettings Load(string file)
        {
            var s = new AppSettings();
            if (!File.Exists(file)) return s;
            foreach (string raw in File.ReadAllLines(file))
            {
                int i = raw.IndexOf('=');
                if (i <= 0) continue;
                string k = raw.Substring(0, i).Trim().ToLowerInvariant(), v = raw.Substring(i + 1).Trim();
                int n;
                switch (k)
                {
                    case "zipfolder": if (v.Length > 0) s.ZipFolder = v; break;
                    case "destination": s.Destination = v; break;
                    case "scanfolders": s.ScanFolders = v; break;
                    case "netuser": s.NetUser = v; break;
                    case "ownfolder": s.OwnFolder = v == "1"; break;
                    case "cleanfirst": s.CleanFirst = v == "1"; break;
                    case "owncopy": s.OwnCopy = v == "1"; break;
                    case "minmatch": if (int.TryParse(v, out n)) s.MinMatch = Math.Max(10, Math.Min(100, n)); break;
                    case "zip": if (v.Length > 0) s.CheckedZips.Add(v); break;
                    case "pc":
                        int b = v.IndexOf('|');
                        if (b > 0 && b < v.Length - 1) s.Pcs.Add(new KeyValuePair<string, bool>(v.Substring(b + 1), v.Substring(0, b) == "1"));
                        break;
                }
            }
            return s;
        }

        public void Save(string file)
        {
            var lines = new List<string>
            {
                "# Lab Reset settings",
                "ZipFolder=" + ZipFolder,
                "Destination=" + Destination,
                "ScanFolders=" + ScanFolders,
                "NetUser=" + NetUser,
                "OwnFolder=" + (OwnFolder ? "1" : "0"),
                "CleanFirst=" + (CleanFirst ? "1" : "0"),
                "OwnCopy=" + (OwnCopy ? "1" : "0"),
                "MinMatch=" + MinMatch
            };
            foreach (string z in CheckedZips) lines.Add("Zip=" + z);
            foreach (var p in Pcs) lines.Add("PC=" + (p.Value ? "1" : "0") + "|" + p.Key);
            File.WriteAllLines(file, lines);
        }
    }
}
