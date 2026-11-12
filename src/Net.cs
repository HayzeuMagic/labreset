// Lab Reset - network access to other lab PCs. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LabReset
{
    /// <summary>
    /// Reaches other lab PCs through their hidden admin shares (\\10.3.3.162\D$).
    /// Uses the Windows account Lab Reset runs as, or an optional network login.
    /// </summary>
    public static class Net
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class NETRESOURCE
        {
            public int dwScope, dwType, dwDisplayType, dwUsage;
            public string lpLocalName, lpRemoteName, lpComment, lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetAddConnection2(NETRESOURCE nr, string password, string user, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetCancelConnection2(string name, int flags, bool force);

        public static string User;      // optional, e.g. ".\LabAdmin"
        public static string Password;  // kept in memory only, never saved

        static readonly HashSet<string> connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly object lk = new object();

        /// <summary>Logs on to a lab PC with the optional network login. Returns null if OK or not needed.</summary>
        public static string Connect(string pc)
        {
            if (PcNames.IsLocal(pc) || string.IsNullOrEmpty(User)) return null;
            lock (lk) if (connected.Contains(pc)) return null;
            string user = User.StartsWith(@".\") ? pc + User.Substring(1) : User;
            var nr = new NETRESOURCE { dwType = 0, lpRemoteName = @"\\" + pc + @"\IPC$" };
            int r = WNetAddConnection2(nr, Password, user, 0);
            if (r == 0 || r == 1219) // 1219 = already connected with other credentials
            {
                lock (lk) connected.Add(pc);
                return null;
            }
            return new System.ComponentModel.Win32Exception(r).Message;
        }

        public static void DisconnectAll()
        {
            lock (lk)
            {
                foreach (string pc in connected)
                {
                    try { WNetCancelConnection2(@"\\" + pc + @"\IPC$", 0, true); } catch { }
                }
                connected.Clear();
            }
        }

        /// <summary>Checks we can open \\PC\C$. Returns null if OK, else the reason.</summary>
        public static string CheckAccess(string pc)
        {
            if (PcNames.IsLocal(pc)) return null;
            string loginErr = Connect(pc);
            try
            {
                Directory.GetFileSystemEntries(@"\\" + pc + @"\C$\");
                return null;
            }
            catch (Exception ex)
            {
                return (loginErr != null ? "network login failed (" + loginErr.Trim() + "); " : "") + ex.Message.Trim();
            }
        }

        public static string Test(string pc)
        {
            if (PcNames.IsLocal(pc)) return "OK (this computer)";
            bool ping = false;
            try { using (var p = new Ping()) ping = p.Send(pc, 1500).Status == IPStatus.Success; }
            catch { }
            string err = CheckAccess(pc);
            if (err == null) return "OK";
            if (!ping) return "Unreachable - " + err;
            return "Online, but no admin access - " + err;
        }
    }
}
