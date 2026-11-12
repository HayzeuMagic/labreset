// Lab Reset - computer names. C# 5 / .NET Framework 4.5+ compatible.
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
    // ------------------------------------------------------------------ computer names
    public static class PcNames
    {
        public const string ThisPc = "(This PC)";

        static readonly Regex Valid = new Regex(@"^[A-Za-z0-9][A-Za-z0-9\-_.]{0,62}$");
        static readonly Regex Range = new Regex(@"^(.*?)\{(\d{1,6})-(\d{1,6})\}(.*)$");

        static HashSet<string> localIps;

        static HashSet<string> LocalIps()
        {
            if (localIps != null) return localIps;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1" };
            try
            {
                foreach (System.Net.IPAddress a in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                    set.Add(a.ToString());
            }
            catch { }
            localIps = set;
            return set;
        }

        public static bool IsLocal(string pc)
        {
            if (string.IsNullOrWhiteSpace(pc)) return true;
            pc = pc.Trim();
            return pc == ThisPc || pc == "."
                || pc.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || pc.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                || LocalIps().Contains(pc);
        }

        static bool IsIPv4(string s)
        {
            System.Net.IPAddress ip;
            return Regex.IsMatch(s, @"^\d{1,3}(\.\d{1,3}){3}$") && System.Net.IPAddress.TryParse(s, out ip);
        }

        /// <summary>The PC number: last 2 digits of the IP address (10.3.3.162 -> "PC 62"), or the digits at the end of a name.</summary>
        public static string Number(string pc)
        {
            if (pc == ThisPc) return "This PC";
            if (IsIPv4(pc)) return "PC " + (int.Parse(pc.Substring(pc.LastIndexOf('.') + 1)) % 100).ToString("00");
            Match m = Regex.Match(pc, @"(\d+)$");
            return m.Success ? "PC " + m.Groups[1].Value : pc;
        }

        /// <summary>Name used in the log and results, e.g. "PC 62 (10.3.3.162)".</summary>
        public static string Display(string pc)
        {
            if (pc == ThisPc || (IsLocal(pc) && !IsIPv4(pc))) return "This PC (" + Environment.MachineName + ")";
            return Number(pc) + " (" + pc + ")";
        }

        /// <summary>Expands "LAB-PC{01-30}, ROOM2-5" into individual names.</summary>
        public static List<string> Expand(string input, List<string> errors)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return result;
            input = Regex.Replace(input, @"\{\s*(\d+)\s*-\s*(\d+)\s*\}", "{$1-$2}");
            foreach (string raw in input.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.Trim().TrimStart('\\');
                var names = new List<string>();
                Match m = Range.Match(tok);
                if (m.Success)
                {
                    int a = int.Parse(m.Groups[2].Value), b = int.Parse(m.Groups[3].Value);
                    if (a > b) { int t = a; a = b; b = t; }
                    if (b - a > 999) { errors.Add(tok + ": range is too large (max 1000 computers)"); continue; }
                    int pad = m.Groups[2].Value.Length;
                    for (int i = a; i <= b; i++)
                        names.Add(m.Groups[1].Value + i.ToString().PadLeft(pad, '0') + m.Groups[4].Value);
                }
                else names.Add(tok);

                foreach (string raw2 in names)
                {
                    string n = raw2;
                    if (Regex.IsMatch(n, @"^\d+(\.\d+){3}$"))
                    {
                        // IP address: drop leading zeros (10.3.3.062 -> 10.3.3.62) and check each part is 0-255
                        int[] parts = n.Split('.').Select(x => x.Length > 3 ? 999 : int.Parse(x)).ToArray();
                        if (parts.Any(x => x > 255)) { errors.Add(n + ": not a valid IP address"); continue; }
                        n = string.Join(".", parts);
                    }
                    if (!Valid.IsMatch(n)) { errors.Add(n + ": not a valid computer name"); continue; }
                    if (!result.Contains(n, StringComparer.OrdinalIgnoreCase)) result.Add(n);
                }
            }
            return result;
        }
    }
}
