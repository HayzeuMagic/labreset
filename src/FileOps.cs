// Lab Reset - delete and extract. C# 5 / .NET Framework 4.5+ compatible.
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
    // ------------------------------------------------------------------ delete / extract
    public static class FileOps
    {
        public static bool Exists(string access) { return File.Exists(access) || Directory.Exists(access); }

        public static void DeletePath(string access)
        {
            if (File.Exists(access))
            {
                File.SetAttributes(access, FileAttributes.Normal);
                File.Delete(access);
                return;
            }
            if (Directory.Exists(access)) DeleteDir(new DirectoryInfo(access));
        }

        static void DeleteDir(DirectoryInfo d)
        {
            if ((d.Attributes & FileAttributes.ReadOnly) != 0) d.Attributes &= ~FileAttributes.ReadOnly;
            if ((d.Attributes & FileAttributes.ReparsePoint) != 0) { d.Delete(false); return; } // remove the link only
            foreach (FileSystemInfo c in d.GetFileSystemInfos())
            {
                var sub = c as DirectoryInfo;
                if (sub != null) DeleteDir(sub);
                else { c.Attributes = FileAttributes.Normal; c.Delete(); }
            }
            d.Delete(false);
        }

        /// <summary>Where an archive's files end up when extracted to destLocal.</summary>
        public static string TargetFolder(ZipSignature z, string destLocal, bool ownFolder)
        {
            // An archive that already has one top folder (Forti.7z -> Forti\...) is not wrapped in a second folder.
            bool wrap = ownFolder && !z.HasSingleTopFolder;
            return wrap ? Path.Combine(destLocal, Paths.SafeFileName(z.BaseName)) : destLocal;
        }

        /// <summary>Where the extracted copy will be, for messages (e.g. C:\LabFiles\Forti).</summary>
        public static string ResultFolder(ZipSignature z, string destLocal, bool ownFolder)
        {
            string t = TargetFolder(z, destLocal, ownFolder);
            return z.HasSingleTopFolder ? Path.Combine(t, z.TopNames[0]) : t;
        }

        /// <summary>Extracts an archive to destLocal on a PC. Returns the number of files written.</summary>
        public static int ExtractArchive(ZipSignature z, string pc, string destLocal, bool ownFolder, bool cleanFirst,
                                         List<string> protectTree, Action<string> log, Action<int> percent, CancellationToken ct)
        {
            destLocal = Paths.Normalize(destLocal);
            string targetLocal = TargetFolder(z, destLocal, ownFolder);
            string targetAccess = Paths.ToAccess(pc, targetLocal);
            List<string> tree = PcNames.IsLocal(pc) ? protectTree : null;
            var exact = new List<string> { destLocal };

            if (cleanFirst)
            {
                List<string> old = targetLocal != destLocal
                    ? new List<string> { targetLocal }
                    : z.TopNames.Select(t => Path.Combine(destLocal, t)).ToList();
                foreach (string loc in old)
                {
                    string acc = Paths.ToAccess(pc, loc);
                    if (!FileOps.Exists(acc)) continue;
                    string why = Safety.WhyProtected(loc, tree, exact);
                    if (why == null && ZipSignature.IsArchive(Path.GetFileName(loc))) why = "it is an archive file";
                    if (why != null) { log(PcNames.Display(pc) + ": old copy " + loc + " not removed (" + why + ")."); continue; }
                    log("Removing old copy " + loc + " ...");
                    DeletePath(acc);
                }
            }

            Directory.CreateDirectory(targetAccess);
            if (!z.IsZip)
            {
                SevenZip.Extract(z.ZipPath, z.IsTarGz, targetAccess, percent, ct);
                return z.Files.Count;
            }

            string rootFull = Path.GetFullPath(targetAccess).TrimEnd('\\') + "\\";
            int count = 0;
            using (ZipArchive a = ZipFile.OpenRead(z.ZipPath))
            {
                long total = Math.Max(1, a.Entries.Sum(e => e.Length)), done = 0;
                var buf = new byte[1 << 20];
                foreach (ZipArchiveEntry e in a.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    string n = ZipSignature.CleanEntry(e.FullName);
                    if (n.Length == 0 || n.Split('\\')[0].Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) continue;
                    string full;
                    try { full = Path.GetFullPath(Path.Combine(targetAccess, n)); }
                    catch { log("  skipped entry with an invalid name: " + e.FullName); continue; }
                    if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    {
                        log("  skipped unsafe entry (points outside the destination): " + e.FullName);
                        continue;
                    }
                    if (n.EndsWith("\\")) { Directory.CreateDirectory(full); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    if (File.Exists(full)) File.SetAttributes(full, FileAttributes.Normal);
                    using (Stream src = e.Open())
                    using (var dst = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        int r;
                        while ((r = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            dst.Write(buf, 0, r);
                            done += r;
                            if (percent != null) percent((int)(done * 100 / total));
                        }
                    }
                    try { File.SetLastWriteTime(full, e.LastWriteTime.DateTime); } catch { }
                    count++;
                }
            }
            return count;
        }
    }
}
