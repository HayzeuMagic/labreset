// Lab Reset - scanning for extracted copies. C# 5 / .NET Framework 4.5+ compatible.
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
    // ------------------------------------------------------------------ scanning
    public class ScanResult
    {
        public string Computer, LocalPath, AccessPath, ZipName;
        public bool IsFolder;
        public int Matched, Checked;
        public DateTime Modified;
        public int Percent { get { return Checked == 0 ? 0 : (int)Math.Round(Matched * 100.0 / Checked); } }
    }

    public class Scanner
    {
        const int MaxFilesChecked = 200;

        readonly string pc;
        readonly bool isLocal;
        readonly List<ZipSignature> zips;
        readonly int minPercent;
        readonly List<string> protectTree;   // only applies on this PC
        readonly List<string> protectExact;
        readonly CancellationToken ct;

        public Action<ScanResult> Found;
        public Action<string> Status;
        public Action<string> Log;
        public int FoldersScanned, FoldersUnreadable, ItemsFound;

        public Scanner(string pc, List<ZipSignature> zips, int minPercent, List<string> protectTree, List<string> protectExact, CancellationToken ct)
        {
            this.pc = pc;
            isLocal = PcNames.IsLocal(pc);
            this.zips = zips;
            this.minPercent = minPercent;
            this.protectTree = (protectTree ?? new List<string>()).Select(Paths.Normalize).Where(x => x != null).ToList();
            this.protectExact = protectExact ?? new List<string>();
            this.ct = ct;
        }

        void Say(string s) { if (Log != null) Log(s); }

        public List<string> GetDrives()
        {
            var list = new List<string>();
            if (isLocal)
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) list.Add(d.RootDirectory.FullName); }
                    catch { }
                }
            }
            else
            {
                for (char c = 'C'; c <= 'Z'; c++)
                {
                    ct.ThrowIfCancellationRequested();
                    try { if (Directory.Exists(@"\\" + pc + @"\" + c + @"$\")) list.Add(c + @":\"); }
                    catch { }
                }
            }
            return list;
        }

        public void Run(List<string> scanFolders)
        {
            List<string> roots = (scanFolders == null || scanFolders.Count == 0) ? GetDrives() : scanFolders;
            foreach (string r in roots)
            {
                ct.ThrowIfCancellationRequested();
                string root = Paths.Normalize(r);
                if (root == null) continue;
                if (Safety.ShouldSkipScan(root)) { Say(PcNames.Display(pc) + ": " + root + " is a system folder - not scanned."); continue; }
                if (!Directory.Exists(Paths.ToAccess(pc, root))) { Say(PcNames.Display(pc) + ": folder " + root + " not found - skipped."); continue; }
                Walk(root);
            }
        }

        static bool NameMatches(string dirName, string baseName)
        {
            return dirName.Equals(baseName, StringComparison.OrdinalIgnoreCase)
                || dirName.Equals(Paths.SafeFileName(baseName), StringComparison.OrdinalIgnoreCase)
                || dirName.StartsWith(baseName + " (", StringComparison.OrdinalIgnoreCase);
        }

        static int Pct(int m, int c) { return c == 0 ? 0 : (int)Math.Round(m * 100.0 / c); }

        void Score(string accessBase, ZipSignature z, out int matched, out int checkedCount)
        {
            matched = 0; checkedCount = 0;
            int n = z.Files.Count;
            int step = Math.Max(1, (n + MaxFilesChecked - 1) / MaxFilesChecked);
            for (int i = 0; i < n; i += step)
            {
                checkedCount++;
                try { if (File.Exists(Path.Combine(accessBase, z.Files[i]))) matched++; }
                catch { }
            }
        }

        string WhyProtected(string local)
        {
            return Safety.WhyProtected(local, isLocal ? protectTree : null, protectExact);
        }

        void Report(string local, string access, bool isFolder, DateTime modified, ZipSignature z, int m, int c)
        {
            ItemsFound++;
            if (Found != null)
                Found(new ScanResult
                {
                    Computer = pc, LocalPath = local, AccessPath = access, IsFolder = isFolder,
                    Modified = modified, ZipName = z.FileName, Matched = m, Checked = c
                });
        }

        void Walk(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string local = stack.Pop();
                if (Safety.ShouldSkipScan(local)) continue;
                if (isLocal && protectTree.Any(t => Paths.IsSameOrUnder(local, t))) continue;

                string access = Paths.ToAccess(pc, local);
                FileSystemInfo[] children;
                try { children = new DirectoryInfo(access).GetFileSystemInfos(); }
                catch { FoldersUnreadable++; continue; }
                FoldersScanned++;
                if (Status != null) Status(local);

                var byName = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (FileSystemInfo c in children) byName[c.Name] = c;

                // Layout A: this folder was made by extracting a zip into a folder named after the zip.
                string dirName = Path.GetFileName(local);
                bool selfReported = false;
                if (!string.IsNullOrEmpty(dirName))
                {
                    foreach (ZipSignature z in zips)
                    {
                        if (!NameMatches(dirName, z.BaseName)) continue;
                        int m, c;
                        Score(access, z, out m, out c);
                        if (Pct(m, c) < minPercent || WhyProtected(local) != null) continue;
                        DateTime mod = DateTime.MinValue;
                        try { mod = Directory.GetLastWriteTime(access); } catch { }
                        Report(local, access, true, mod, z, m, c);
                        selfReported = true;
                        break;
                    }
                }
                if (selfReported) continue; // whole folder is a match, no need to look inside

                // Layout B: the zip's top-level items were extracted straight into this folder.
                var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipSignature z in zips)
                {
                    List<string> present = z.TopNames.Where(byName.ContainsKey).ToList();
                    if (present.Count == 0 || present.Count * 100 < minPercent * z.TopNames.Count) continue;
                    int m, c;
                    Score(access, z, out m, out c);
                    if (Pct(m, c) < minPercent) continue;
                    foreach (string name in present)
                    {
                        if (handled.Contains(name)) continue;
                        string itemLocal = Path.Combine(local, name);
                        string why = WhyProtected(itemLocal);
                        if (why != null) { Say(PcNames.Display(pc) + ": match " + itemLocal + " not listed (" + why + ")."); continue; }
                        FileSystemInfo info = byName[name];
                        Report(itemLocal, Path.Combine(access, name), info is DirectoryInfo, info.LastWriteTime, z, m, c);
                        handled.Add(name);
                    }
                }

                foreach (FileSystemInfo ch in children)
                {
                    var d = ch as DirectoryInfo;
                    if (d == null || handled.Contains(d.Name)) continue;
                    if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue; // don't follow junctions/links
                    stack.Push(Path.Combine(local, d.Name));
                }
            }
        }
    }
}
