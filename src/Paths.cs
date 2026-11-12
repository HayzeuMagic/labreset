// Lab Reset - path helpers. C# 5 / .NET Framework 4.5+ compatible.
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
    // ------------------------------------------------------------------ paths
    public static class Paths
    {
        /// <summary>Full, normalised local-style path (C:\x\y) without trailing slash (except roots). Null if invalid.</summary>
        public static string Normalize(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            try
            {
                p = Path.GetFullPath(p.Trim().Trim('"'));
                if (p.Length > 3) p = p.TrimEnd('\\');
                return p;
            }
            catch { return null; }
        }

        public static bool IsDrivePath(string p)
        {
            return p != null && p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && p[2] == '\\';
        }

        public static string[] Segments(string p)
        {
            return p.Length <= 3 ? new string[0] : p.Substring(3).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Turns C:\Users\x into \\PC\C$\Users\x for remote PCs (unchanged for this PC).</summary>
        public static string ToAccess(string pc, string local)
        {
            if (PcNames.IsLocal(pc)) return local;
            return @"\\" + pc + @"\" + char.ToUpperInvariant(local[0]) + "$" + local.Substring(2);
        }

        public static bool IsSameOrUnder(string path, string parent)
        {
            if (path == null || parent == null) return false;
            return path.Equals(parent, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            return name.Length == 0 ? "zip" : name;
        }
    }
}
