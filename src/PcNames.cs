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

        public static bool IsLocal(string pc)
        {
            if (string.IsNullOrWhiteSpace(pc)) return true;
            pc = pc.Trim();
            return pc == ThisPc || pc == "."
                || pc.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || pc.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }

        public static string Display(string pc)
        {
            return IsLocal(pc) ? Environment.MachineName : pc;
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

                foreach (string n in names)
                {
                    if (!Valid.IsMatch(n)) { errors.Add(n + ": not a valid computer name"); continue; }
                    if (!result.Contains(n, StringComparer.OrdinalIgnoreCase)) result.Add(n);
                }
            }
            return result;
        }
    }
}
