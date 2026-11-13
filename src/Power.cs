// Lab Reset - shut down or restart lab PCs. C# 5 / .NET Framework 4.5+ compatible.
//
// Uses Windows' own remote shutdown (the same thing "shutdown /s /m \\PC" does), over the
// File and Printer Sharing connection Lab Reset already uses. Needs admin rights on the lab PC,
// which Lab Reset already needs for Extract and Delete.
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LabReset
{
    public static class Power
    {
        const uint SHUTDOWN_FORCE_OTHERS = 0x1, SHUTDOWN_FORCE_SELF = 0x2, SHUTDOWN_RESTART = 0x4,
                   SHUTDOWN_POWEROFF = 0x8, SHUTDOWN_GRACE_OVERRIDE = 0x20;
        // "Other (Planned)" - so the lab PC's event log shows it was a planned shutdown
        const uint SHTDN_REASON_MAJOR_OTHER = 0x0, SHTDN_REASON_MINOR_OTHER = 0x0, SHTDN_REASON_FLAG_PLANNED = 0x80000000;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern uint InitiateShutdown(string machine, string message, uint gracePeriod, uint flags, uint reason);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool AbortSystemShutdown(string machine);

        // ---- privileges (only needed to shut down THIS PC; remote PCs check the remote-shutdown right instead)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attr; }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupPrivilegeValue(string system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr retLen);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        static void EnablePrivilege(string name)
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), 0x28 /* ADJUST_PRIVILEGES | QUERY */, out token)) return;
            try
            {
                var tp = new TOKEN_PRIVILEGES { Count = 1, Attr = 2 /* SE_PRIVILEGE_ENABLED */ };
                if (LookupPrivilegeValue(null, name, out tp.Luid))
                    AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(token); }
        }

        static bool privilegesEnabled;

        /// <summary>
        /// Asks the PC to shut down (or restart) after delaySeconds. Open programs are closed without saving.
        /// Returns null if Windows accepted it, else the reason.
        /// </summary>
        public static string Send(string pc, bool restart, int delaySeconds, string message)
        {
            if (!privilegesEnabled)
            {
                EnablePrivilege("SeShutdownPrivilege");
                EnablePrivilege("SeRemoteShutdownPrivilege");
                privilegesEnabled = true;
            }
            string loginErr = Net.Connect(pc);
            string machine = PcNames.IsLocal(pc) ? null : pc;
            uint flags = SHUTDOWN_FORCE_OTHERS | SHUTDOWN_FORCE_SELF | (restart ? SHUTDOWN_RESTART : SHUTDOWN_POWEROFF);
            if (delaySeconds <= 0) flags |= SHUTDOWN_GRACE_OVERRIDE;
            uint r = InitiateShutdown(machine, message, (uint)Math.Max(0, delaySeconds), flags,
                                      SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED);
            if (r == 0) return null;
            string err = new Win32Exception((int)r).Message.Trim();
            if (r == 1115 || r == 1190) err = "a shutdown is already in progress on that PC";   // ERROR_SHUTDOWN_IN_PROGRESS / ..._IS_SCHEDULED
            if (r == 53 || r == 1722) err = "PC is off or unreachable (" + err + ")";           // bad net path / RPC unavailable
            return (loginErr != null ? "network login failed (" + loginErr.Trim() + "); " : "") + err;
        }

        /// <summary>Cancels a shutdown that is still counting down. Returns null if OK, else the reason.</summary>
        public static string Abort(string pc)
        {
            EnablePrivilege("SeShutdownPrivilege");
            EnablePrivilege("SeRemoteShutdownPrivilege");
            Net.Connect(pc);
            if (AbortSystemShutdown(PcNames.IsLocal(pc) ? null : pc)) return null;
            int e = Marshal.GetLastWin32Error();
            if (e == 1116) return "no shutdown was pending";   // ERROR_NO_SHUTDOWN_IN_PROGRESS
            return new Win32Exception(e).Message.Trim();
        }
    }
}
