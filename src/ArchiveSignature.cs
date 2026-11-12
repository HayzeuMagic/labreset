// Lab Reset - archive contents. C# 5 / .NET Framework 4.5+ compatible.
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
    // ------------------------------------------------------------------ archive contents
    public class ZipSignature
    {
        public string ZipPath, FileName, BaseName;
        public long Size;
        public DateTime Modified;
        public bool BuiltIn;
        public readonly List<string> Files = new List<string>();     // relative file paths, '\' separated
        public readonly List<string> TopNames = new List<string>();  // first path segment of every entry

        static readonly string[] Exts = { ".tar.gz", ".tgz", ".zip", ".7z", ".rar", ".tar" };

        public static bool IsArchive(string name)
        {
            return Exts.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        public static string StripExtension(string name)
        {
            foreach (string e in Exts)
                if (name.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - e.Length);
            return Path.GetFileNameWithoutExtension(name);
        }

        public bool IsZip { get { return FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase); } }
        public bool IsTarGz
        {
            get
            {
                return FileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || FileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>True if everything in the archive is inside one top-level folder (e.g. Forti\...).</summary>
        public bool HasSingleTopFolder
        {
            get { return TopNames.Count == 1 && Files.All(f => f.IndexOf('\\') > 0); }
        }

        public static string CleanEntry(string fullName)
        {
            string n = fullName.Replace('/', '\\');
            while (n.StartsWith("\\") || n.StartsWith(".\\")) n = n.StartsWith("\\") ? n.Substring(1) : n.Substring(2);
            return n;
        }

        static bool Ignored(string rel)
        {
            string first = rel.Split('\\')[0];
            string last = Path.GetFileName(rel.TrimEnd('\\'));
            return first.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)
                || last.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
                || rel.Split('\\').Contains("..");
        }

        void AddEntry(string raw, bool isDir, HashSet<string> tops)
        {
            string n = CleanEntry(raw);
            if (n.Length == 0 || Ignored(n)) return;
            if (n.EndsWith("\\")) isDir = true;
            n = n.TrimEnd('\\');
            if (n.Length == 0) return;
            string top = n.Split('\\')[0];
            if (tops.Add(top)) TopNames.Add(top);
            if (!isDir) Files.Add(n);
        }

        /// <summary>Reads the list of files in an archive. cacheDir (optional) stores the list so big archives are only read once.</summary>
        public static ZipSignature Load(string path, string cacheDir)
        {
            var fi = new FileInfo(path);
            var z = new ZipSignature
            {
                ZipPath = fi.FullName,
                FileName = fi.Name,
                BaseName = StripExtension(fi.Name),
                Size = fi.Length,
                Modified = fi.LastWriteTime
            };
            string stamp = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            string cacheFile = cacheDir == null ? null : Path.Combine(cacheDir, fi.Name + ".list");
            if (cacheFile != null && z.TryReadCache(cacheFile, stamp)) return z;

            var tops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (z.IsZip)
            {
                using (ZipArchive a = ZipFile.OpenRead(path))
                    foreach (ZipArchiveEntry e in a.Entries) z.AddEntry(e.FullName, false, tops);
            }
            else
            {
                foreach (var kv in SevenZip.List(path, z.IsTarGz)) z.AddEntry(kv.Key, kv.Value, tops);
            }
            if (z.Files.Count == 0) throw new InvalidDataException("the archive contains no files");

            if (cacheFile != null)
            {
                try
                {
                    var lines = new List<string> { stamp };
                    lines.AddRange(z.TopNames.Select(t => "T\t" + t));
                    lines.AddRange(z.Files.Select(f => "F\t" + f));
                    File.WriteAllLines(cacheFile, lines, Encoding.UTF8);
                }
                catch { }
            }
            return z;
        }

        bool TryReadCache(string cacheFile, string stamp)
        {
            try
            {
                if (!File.Exists(cacheFile)) return false;
                string[] lines = File.ReadAllLines(cacheFile, Encoding.UTF8);
                if (lines.Length < 2 || lines[0] != stamp) return false;
                foreach (string l in lines.Skip(1))
                {
                    if (l.StartsWith("T\t")) TopNames.Add(l.Substring(2));
                    else if (l.StartsWith("F\t")) Files.Add(l.Substring(2));
                }
                if (Files.Count > 0) return true;
            }
            catch { }
            TopNames.Clear();
            Files.Clear();
            return false;
        }
    }
}
