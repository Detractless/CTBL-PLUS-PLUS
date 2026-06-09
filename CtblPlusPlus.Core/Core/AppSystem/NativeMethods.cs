using System;
using System.Runtime.InteropServices;

namespace CtblPlusPlus.Core.AppSystem
{
    public static class NativeMethods
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int RtlSetProcessIsCritical(bool bNew, out bool pbOld, bool bNeedPrivilege);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        public const uint TOKEN_QUERY = 0x0008;
        public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        public const string SE_DEBUG_NAME = "SeDebugPrivilege";

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID_AND_ATTRIBUTES
        {
            public long Luid;
            public uint Attributes;
        }

        public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        public static bool SetCriticalProcess(bool isCritical)
        {
            try
            {
                IntPtr hToken;
                if (OpenProcessToken(global::System.Diagnostics.Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
                {
                    TOKEN_PRIVILEGES tp;
                    tp.PrivilegeCount = 1;
                    tp.Privileges.Attributes = SE_PRIVILEGE_ENABLED;
                    if (LookupPrivilegeValue(null, SE_DEBUG_NAME, out tp.Privileges.Luid))
                    {
                        AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                }

                bool old;
                int status = RtlSetProcessIsCritical(isCritical, out old, false);
                return status == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}


