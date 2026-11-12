// Lab Reset - finding the __RivanVMs folder on a PC. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LabReset
{
    /// <summary>
    /// Every lab PC keeps its VMs in X:\__RivanVMs, where X can be any drive.
    /// Extracting and deleting only ever happens inside that folder.
    /// </summary>
    public static class VmFolder
    {
        public const string Name = "__RivanVMs";

        /// <summary>Finds X:\__RivanVMs on every drive of a PC (local paths like D:\__RivanVMs).</summary>
        public static List<string> Find(string pc, CancellationToken ct)
        {
            var list = new List<string>();
            if (PcNames.IsLocal(pc))
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        string p = Path.Combine(d.RootDirectory.FullName, Name);
                        if (Directory.Exists(p)) list.Add(p);
                    }
                    catch { }
                }
                return list;
            }
            for (char c = 'C'; c <= 'Z'; c++)
            {
                ct.ThrowIfCancellationRequested();
                try { if (Directory.Exists(@"\\" + pc + @"\" + c + @"$\" + Name)) list.Add(c + @":\" + Name); }
                catch { }
            }
            return list;
        }

        /// <summary>True for something inside X:\__RivanVMs (not the folder itself).</summary>
        public static bool IsInside(string local)
        {
            string p = Paths.Normalize(local);
            if (p == null || !Paths.IsDrivePath(p)) return false;
            string[] s = Paths.Segments(p);
            return s.Length >= 2 && s[0].Equals(Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
