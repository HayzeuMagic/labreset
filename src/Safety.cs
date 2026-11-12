// Lab Reset - safety rules. C# 5 / .NET Framework 4.5+ compatible.
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
    // ------------------------------------------------------------------ safety rules
    public static class Safety
    {
        // Never scanned (top-level folders on a drive)
        static readonly HashSet<string> RootSystem = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
            "Recovery", "$WinREAgent", "$Windows.~BT", "$Windows.~WS", "$SysReset", "Windows.old", "Config.Msi",
            "PerfLogs", "MSOCache", "Boot", "EFI", "Documents and Settings" };

        // Never scanned at any depth
        static readonly HashSet<string> AnyLevelSystem = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "AppData", "$Recycle.Bin", "System Volume Information", "Application Data", "Local Settings" };

        // Searched inside, but never deleted themselves
        static readonly HashSet<string> KeepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Users", "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "OneDrive", "Public",
            "Default", "Default User", "All Users", "Favorites", "Contacts", "Links", "Saved Games", "Searches",
            "3D Objects", "My Documents", "My Music", "My Pictures", "My Videos", "Start Menu", "Templates" };

        public static bool ShouldSkipScan(string local)
        {
            string[] s = Paths.Segments(local);
            if (s.Length == 0) return false;
            if (RootSystem.Contains(s[0])) return true;
            foreach (string x in s) if (AnyLevelSystem.Contains(x)) return true;
            return false;
        }

        /// <summary>
        /// Returns why a path must not be deleted, or null if it may be deleted.
        /// protectTree: folders that may not be deleted, nor anything inside or above them (Zips folder, app folder).
        /// protectExact: folders that may not be deleted, nor anything above them (the extract destination); their contents may be deleted.
        /// </summary>
        public static string WhyProtected(string local, IEnumerable<string> protectTree, IEnumerable<string> protectExact)
        {
            string p = Paths.Normalize(local);
            if (p == null || !Paths.IsDrivePath(p)) return "not a normal drive path";
            string[] s = Paths.Segments(p);
            if (s.Length == 0) return "drive root";
            if (ShouldSkipScan(p)) return "Windows/system folder";
            if (KeepNames.Contains(s[s.Length - 1])) return "protected folder name '" + s[s.Length - 1] + "'";
            if (s.Length == 2 && s[0].Equals("Users", StringComparison.OrdinalIgnoreCase)) return "user profile folder";
            if (protectTree != null)
                foreach (string t in protectTree)
                {
                    string n = Paths.Normalize(t);
                    if (n == null) continue;
                    if (Paths.IsSameOrUnder(p, n) || Paths.IsSameOrUnder(n, p)) return "is or contains " + n;
                }
            if (protectExact != null)
                foreach (string t in protectExact)
                {
                    string n = Paths.Normalize(t);
                    if (n != null && Paths.IsSameOrUnder(n, p)) return "is or contains the destination folder " + n;
                }
            return null;
        }

        /// <summary>
        /// Final check before Lab Reset deletes anything: it must be inside an X:\__RivanVMs folder,
        /// must not be an archive file (the lab's source .zip/.7z/.rar), and must pass WhyProtected.
        /// Returns why it can't be deleted, or null if it can.
        /// </summary>
        public static string WhyNotDeletable(string local, IEnumerable<string> protectTree)
        {
            if (!VmFolder.IsInside(local)) return "not inside a " + VmFolder.Name + " folder";
            if (ZipSignature.IsArchive(Path.GetFileName(local.TrimEnd('\\')))) return "it is an archive file";
            return WhyProtected(local, protectTree, null);
        }

        /// <summary>Error text if the extract destination is not acceptable, else null.</summary>
        public static string CheckDestination(string dest)
        {
            string p = Paths.Normalize(dest);
            if (p == null || !Paths.IsDrivePath(p)) return "Enter a full destination folder such as C:\\LabFiles.";
            string[] s = Paths.Segments(p);
            if (s.Length == 0) return "The destination cannot be a whole drive (" + p + "). Use a folder such as C:\\LabFiles.";
            if (ShouldSkipScan(p)) return "The destination cannot be inside a Windows/system folder.";
            if (s.Length == 1 && s[0].Equals("Users", StringComparison.OrdinalIgnoreCase)) return "The destination cannot be the Users folder itself.";
            return null;
        }
    }
}
