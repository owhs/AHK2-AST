using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AstHarness
{
    // Win32 interop used by the sandbox (hidden desktop, job objects, window inspection).
    internal static class Native
    {
        public const uint GENERIC_ALL = 0x10000000;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
        public const uint CREATE_ALWAYS = 2, OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        public const uint CREATE_SUSPENDED = 0x4;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x400;
        public const uint CREATE_NO_WINDOW = 0x08000000;
        public const int STARTF_USESHOWWINDOW = 0x1;
        public const int STARTF_USESTDHANDLES = 0x100;
        public const short SW_SHOWNOACTIVATE = 4;

        public const uint WAIT_OBJECT_0 = 0, WAIT_TIMEOUT = 0x102;

        public const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100;
        public const uint JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x400;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
        public const int JobObjectBasicProcessIdList = 3;
        public const int JobObjectBasicUIRestrictions = 4;
        public const int JobObjectExtendedLimitInformation = 9;

        public const uint JOB_OBJECT_UILIMIT_READCLIPBOARD = 0x2;
        public const uint JOB_OBJECT_UILIMIT_WRITECLIPBOARD = 0x4;
        public const uint JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS = 0x8;
        public const uint JOB_OBJECT_UILIMIT_DISPLAYSETTINGS = 0x10;
        public const uint JOB_OBJECT_UILIMIT_DESKTOP = 0x40;
        public const uint JOB_OBJECT_UILIMIT_EXITWINDOWS = 0x80;

        public const uint SEM_FAILCRITICALERRORS = 0x1, SEM_NOGPFAULTERRORBOX = 0x2, SEM_NOOPENFILEERRORBOX = 0x8000;

        public const int WM_NULL = 0x0, WM_GETTEXT = 0xD, WM_GETTEXTLENGTH = 0xE, WM_CLOSE = 0x10, WM_COMMAND = 0x111;
        public const int DM_GETDEFID = 0x400;
        public const int BM_CLICK = 0xF5;
        public const uint SMTO_ABORTIFHUNG = 0x2, SMTO_BLOCK = 0x1;
        public const uint PW_RENDERFULLCONTENT = 0x2;
        public const int GWL_ID = -12, GWL_STYLE = -16;

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        // Desktops
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateDesktop(string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode, int dwFlags, uint dwDesiredAccess, IntPtr lpsa);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseDesktop(IntPtr hDesktop);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetThreadDesktop(IntPtr hDesktop);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpfn, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpfn, IntPtr lParam);

        // Windows
        [DllImport("user32.dll")]
        public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
        public static extern IntPtr SendMessageTimeoutI(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        // Jobs
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfo);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool QueryInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfo, out uint lpReturnLength);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

        // Processes / files
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(string lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr hThread);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, ref SECURITY_ATTRIBUTES lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll")]
        public static extern uint SetErrorMode(uint uMode);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        /// <summary>Only used on a process handle we created ourselves (suspended, never resumed).</summary>
        public static void TerminateProcessSafe(IntPtr hProcess)
        {
            try { TerminateProcess(hProcess, 1); } catch { }
        }

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        static extern IntPtr CreateFileRaw(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inSize, IntPtr outBuf, int outSize, out int returned, IntPtr overlapped);

        /// <summary>Creates an NTFS directory junction (no admin rights needed). Returns false on failure.</summary>
        public static bool CreateJunction(string link, string target)
        {
            const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
            const uint FSCTL_SET_REPARSE_POINT = 0x000900A4, IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
            System.IO.Directory.CreateDirectory(link);
            IntPtr h = CreateFileRaw(link, GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (h == INVALID_HANDLE_VALUE) return false;
            try
            {
                byte[] sub = Encoding.Unicode.GetBytes(@"\??\" + target.TrimEnd('\\'));
                byte[] print = Encoding.Unicode.GetBytes(target.TrimEnd('\\'));
                int pathBytes = sub.Length + 2 + print.Length + 2;
                var buf = new byte[8 + 8 + pathBytes];
                BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(buf, 0);
                BitConverter.GetBytes((ushort)(8 + pathBytes)).CopyTo(buf, 4);        // ReparseDataLength
                BitConverter.GetBytes((ushort)0).CopyTo(buf, 8);                       // SubstituteNameOffset
                BitConverter.GetBytes((ushort)sub.Length).CopyTo(buf, 10);             // SubstituteNameLength
                BitConverter.GetBytes((ushort)(sub.Length + 2)).CopyTo(buf, 12);       // PrintNameOffset
                BitConverter.GetBytes((ushort)print.Length).CopyTo(buf, 14);           // PrintNameLength
                sub.CopyTo(buf, 16);
                print.CopyTo(buf, 16 + sub.Length + 2);
                int ret;
                return DeviceIoControl(h, FSCTL_SET_REPARSE_POINT, buf, buf.Length, IntPtr.Zero, 0, out ret, IntPtr.Zero);
            }
            finally { CloseHandle(h); }
        }

        public static string GetClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>WM_GETTEXT with a timeout so a hung target can never block the caller.</summary>
        public static string GetText(IntPtr hwnd)
        {
            IntPtr len;
            if (SendMessageTimeoutI(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, 500, out len) == IntPtr.Zero)
                return "";
            int n = (int)len;
            if (n <= 0) return "";
            if (n > 65536) n = 65536;
            var sb = new StringBuilder(n + 2);
            IntPtr ignored;
            SendMessageTimeout(hwnd, WM_GETTEXT, new IntPtr(sb.Capacity), sb, SMTO_ABORTIFHUNG | SMTO_BLOCK, 500, out ignored);
            return sb.ToString();
        }

        public static bool IsResponsive(IntPtr hwnd, uint timeoutMs)
        {
            IntPtr r;
            return SendMessageTimeoutI(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, timeoutMs, out r) != IntPtr.Zero;
        }
    }
}
