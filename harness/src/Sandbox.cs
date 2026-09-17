using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AstHarness
{
    public class LaunchOptions
    {
        public string Exe;
        public string Args;
        public string WorkDir;
        public int TimeoutMs = 20000;
        /// <summary>AHK error dialog => record it and kill the job (otherwise press Continue).</summary>
        public bool KillOnError = true;
        /// <summary>Plain MsgBox / warning dialogs => record and press the default / Continue button.</summary>
        public bool DismissDialogs = true;
        /// <summary>If set, every captured window is screenshotted (PrintWindow) into this folder.</summary>
        public string ShotDir;
        /// <summary>Script file name, used to recognise AHK's own dialogs (their title is the script name).</summary>
        public string ScriptName;
        /// <summary>At the deadline, screenshot every visible window of the job before killing it (needs ShotDir).</summary>
        public bool FinalShot;
        public ulong MemoryLimitBytes = 2UL * 1024 * 1024 * 1024;
    }

    public class WindowCapture
    {
        public string Kind;        // MsgBox | AhkError | AhkWarning | Window
        public string Class;
        public string Title;
        public string Text;        // non-button control text (message body for dialogs)
        public List<string> Buttons = new List<string>();
        public List<string> Controls = new List<string>(); // "Class|Text" of every child control
        public int Width, Height;
        public long AtMs;
        public string Action;      // what the sandbox did with it
        public string Screenshot;  // png path, if taken
    }

    public class LaunchResult
    {
        public int ExitCode = -1;
        public bool TimedOut;
        public bool KilledOnError;
        public string Stdout = "";
        public string Stderr = "";
        public List<WindowCapture> Windows = new List<WindowCapture>();
        public long ElapsedMs;
        public string LaunchError;
        public int LeakedWindows; // windows seen on the *user's* desktop (must always be 0)

        public IEnumerable<WindowCapture> OfKind(string kind)
        {
            foreach (var w in Windows) if (w.Kind == kind) yield return w;
        }
    }

    /// <summary>
    /// Runs processes on a private, invisible desktop inside a kill-on-close Job Object.
    ///  - Nothing launched here can appear on, or take focus from, the user's desktop.
    ///  - Only processes started by this sandbox are ever terminated (via their own job).
    ///  - A watcher thread attached to the hidden desktop reads, screenshots and dismisses dialogs.
    /// </summary>
    public sealed class Sandbox : IDisposable
    {
        static readonly object InstanceLock = new object();
        static Sandbox _instance;
        public static Sandbox Instance
        {
            get
            {
                lock (InstanceLock)
                {
                    if (_instance == null) _instance = new Sandbox();
                    return _instance;
                }
            }
        }

        /// <summary>Held around every process creation in this process (sandbox + workers) so inheritable handles never leak between children.</summary>
        internal static readonly object LaunchLock = new object();

        readonly string _deskName;
        readonly IntPtr _desk;
        readonly Thread _watcher;
        readonly ManualResetEvent _ready = new ManualResetEvent(false);
        volatile bool _stop;
        string _watcherError;
        readonly List<Tracked> _active = new List<Tracked>();

        public string DesktopName { get { return _deskName; } }

        class WinState
        {
            public long FirstSeen;
            public bool Done;
        }

        class Tracked
        {
            public IntPtr Job;
            public int Pid;
            public LaunchOptions Opt;
            public Stopwatch Clock;
            public readonly Dictionary<IntPtr, WinState> Seen = new Dictionary<IntPtr, WinState>();
            public readonly List<WindowCapture> Windows = new List<WindowCapture>();
            public volatile bool ErrorKill;
            public int ShotCounter;
            public volatile bool FinalShotRequested;
            public readonly ManualResetEvent FinalShotDone = new ManualResetEvent(false);
        }

        Sandbox()
        {
            Native.SetErrorMode(Native.SEM_FAILCRITICALERRORS | Native.SEM_NOGPFAULTERRORBOX | Native.SEM_NOOPENFILEERRORBOX);
            _deskName = "AstHarness-" + Process.GetCurrentProcess().Id;
            _desk = Native.CreateDesktop(_deskName, IntPtr.Zero, IntPtr.Zero, 0, Native.GENERIC_ALL, IntPtr.Zero);
            if (_desk == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDesktop failed");
            _watcher = new Thread(WatchLoop);
            _watcher.IsBackground = true;
            _watcher.Name = "sandbox-watcher";
            _watcher.Start();
            _ready.WaitOne();
            if (_watcherError != null) throw new InvalidOperationException(_watcherError);
        }

        public void Dispose()
        {
            _stop = true;
            try { _watcher.Join(2000); } catch { }
            // Closing the handle is safe even while processes still run on it; they keep it alive.
            Native.CloseDesktop(_desk);
        }

        // ------------------------------------------------------------------ launching

        public LaunchResult Run(LaunchOptions opt)
        {
            var result = new LaunchResult();
            string ioDir = Path.Combine(Path.GetTempPath(), "AstHarness", "io");
            Directory.CreateDirectory(ioDir);
            string tag = Guid.NewGuid().ToString("N");
            string outFile = Path.Combine(ioDir, tag + ".out");
            string errFile = Path.Combine(ioDir, tag + ".err");
            if (opt.ShotDir != null) Directory.CreateDirectory(opt.ShotDir);

            var pi = new Native.PROCESS_INFORMATION();
            IntPtr job = IntPtr.Zero;
            var clock = Stopwatch.StartNew();
            Tracked tracked = null;
            try
            {
                job = CreateJob(opt.MemoryLimitBytes);

                // Inheritable handles only ever exist inside this lock, so concurrent launches
                // can't leak each other's stdout/stderr handles.
                lock (LaunchLock)
                {
                    var sa = new Native.SECURITY_ATTRIBUTES();
                    sa.nLength = Marshal.SizeOf(typeof(Native.SECURITY_ATTRIBUTES));
                    sa.bInheritHandle = true;
                    uint share = Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE;
                    IntPtr hOut = Native.CreateFile(outFile, Native.GENERIC_WRITE, share, ref sa, Native.CREATE_ALWAYS, Native.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                    IntPtr hErr = Native.CreateFile(errFile, Native.GENERIC_WRITE, share, ref sa, Native.CREATE_ALWAYS, Native.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                    IntPtr hIn = Native.CreateFile("NUL", Native.GENERIC_READ, share, ref sa, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    try
                    {
                        var si = new Native.STARTUPINFO();
                        si.cb = Marshal.SizeOf(typeof(Native.STARTUPINFO));
                        si.lpDesktop = "WinSta0\\" + _deskName;
                        si.dwFlags = Native.STARTF_USESTDHANDLES | Native.STARTF_USESHOWWINDOW;
                        si.wShowWindow = Native.SW_SHOWNOACTIVATE;
                        si.hStdInput = hIn;
                        si.hStdOutput = hOut;
                        si.hStdError = hErr;
                        var cmd = new StringBuilder("\"" + opt.Exe + "\" " + (opt.Args ?? ""));
                        bool ok = Native.CreateProcess(opt.Exe, cmd, IntPtr.Zero, IntPtr.Zero, true,
                            Native.CREATE_SUSPENDED | Native.CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero,
                            opt.WorkDir ?? Path.GetDirectoryName(opt.Exe), ref si, out pi);
                        if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed for " + opt.Exe);
                    }
                    finally
                    {
                        Native.CloseHandle(hOut);
                        Native.CloseHandle(hErr);
                        Native.CloseHandle(hIn);
                    }
                }

                if (!Native.AssignProcessToJobObject(job, pi.hProcess))
                {
                    int e = Marshal.GetLastWin32Error();
                    Native.TerminateProcessSafe(pi.hProcess); // still suspended and ours; never resumed
                    throw new Win32Exception(e, "AssignProcessToJobObject failed");
                }

                tracked = new Tracked { Job = job, Pid = pi.dwProcessId, Opt = opt, Clock = clock };
                lock (_active) _active.Add(tracked);
                Native.ResumeThread(pi.hThread);

                var pids = new HashSet<int>();
                long nextLeakCheck = 300;
                while (true)
                {
                    uint w = Native.WaitForSingleObject(pi.hProcess, 25);
                    if (w == Native.WAIT_OBJECT_0) break;
                    if (tracked.ErrorKill) { result.KilledOnError = true; break; }
                    if (clock.ElapsedMilliseconds > opt.TimeoutMs)
                    {
                        result.TimedOut = true;
                        if (opt.FinalShot && opt.ShotDir != null)
                        {
                            tracked.FinalShotRequested = true;
                            tracked.FinalShotDone.WaitOne(8000);
                        }
                        break;
                    }
                    if (clock.ElapsedMilliseconds > nextLeakCheck)
                    {
                        nextLeakCheck = clock.ElapsedMilliseconds + 500;
                        foreach (int p in JobPids(job)) pids.Add(p);
                        result.LeakedWindows = Math.Max(result.LeakedWindows, CountVisibleOnCallerDesktop(pids));
                    }
                }

                // Give the watcher one last pass so a dialog that just appeared is recorded.
                if (!result.TimedOut) Thread.Sleep(10);
                Native.TerminateJobObject(job, 0xA11); // only ever our own processes
                Native.WaitForSingleObject(pi.hProcess, 3000);
                uint code;
                if (Native.GetExitCodeProcess(pi.hProcess, out code)) result.ExitCode = unchecked((int)code);
            }
            catch (Exception ex)
            {
                result.LaunchError = ex.Message;
            }
            finally
            {
                if (tracked != null)
                {
                    lock (_active) _active.Remove(tracked);
                    lock (tracked) result.Windows.AddRange(tracked.Windows);
                    if (tracked.ErrorKill) result.KilledOnError = true;
                }
                if (job != IntPtr.Zero) { Native.TerminateJobObject(job, 0xA11); Native.CloseHandle(job); }
                if (pi.hProcess != IntPtr.Zero) Native.CloseHandle(pi.hProcess);
                if (pi.hThread != IntPtr.Zero) Native.CloseHandle(pi.hThread);
                result.ElapsedMs = clock.ElapsedMilliseconds;
                result.Stdout = ReadAndDelete(outFile);
                result.Stderr = ReadAndDelete(errFile);
            }
            return result;
        }

        static string ReadAndDelete(string path)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    string s;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                        s = sr.ReadToEnd();
                    try { File.Delete(path); } catch { }
                    return s;
                }
                catch (FileNotFoundException) { return ""; }
                catch (IOException) { Thread.Sleep(50); }
            }
            return "";
        }

        static IntPtr CreateJob(ulong memLimit)
        {
            IntPtr job = Native.CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed");

            var ext = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            ext.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                | Native.JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION
                | Native.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
            ext.ProcessMemoryLimit = new UIntPtr(memLimit);
            SetJobInfo(job, Native.JobObjectExtendedLimitInformation, ext);

            // Keep sandboxed scripts away from the user's clipboard, system settings, display, session and desktops.
            uint ui = Native.JOB_OBJECT_UILIMIT_READCLIPBOARD | Native.JOB_OBJECT_UILIMIT_WRITECLIPBOARD
                | Native.JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS | Native.JOB_OBJECT_UILIMIT_DISPLAYSETTINGS
                | Native.JOB_OBJECT_UILIMIT_DESKTOP | Native.JOB_OBJECT_UILIMIT_EXITWINDOWS;
            SetJobInfo(job, Native.JobObjectBasicUIRestrictions, ui);
            return job;
        }

        static void SetJobInfo<T>(IntPtr job, int cls, T value)
        {
            int size = Marshal.SizeOf(typeof(T));
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, p, false);
                if (!Native.SetInformationJobObject(job, cls, p, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject(" + cls + ") failed");
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        static List<int> JobPids(IntPtr job)
        {
            var list = new List<int>();
            const int max = 256;
            int size = 8 + max * IntPtr.Size;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                uint ret;
                if (Native.QueryInformationJobObject(job, Native.JobObjectBasicProcessIdList, buf, (uint)size, out ret))
                {
                    int n = Marshal.ReadInt32(buf, 4);
                    for (int i = 0; i < n && i < max; i++)
                        list.Add((int)Marshal.ReadIntPtr(buf, 8 + i * IntPtr.Size).ToInt64());
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return list;
        }

        /// <summary>Counts visible windows owned by the given pids on the calling thread's desktop (the user's).</summary>
        public static int CountVisibleOnCallerDesktop(HashSet<int> pids)
        {
            if (pids.Count == 0) return 0;
            int count = 0;
            Native.EnumWindows((h, l) =>
            {
                int pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pids.Contains(pid) && Native.IsWindowVisible(h)) count++;
                return true;
            }, IntPtr.Zero);
            return count;
        }

        // ------------------------------------------------------------------ watcher (runs ON the hidden desktop)

        void WatchLoop()
        {
            if (!Native.SetThreadDesktop(_desk))
                _watcherError = "SetThreadDesktop failed: " + Marshal.GetLastWin32Error();
            _ready.Set();
            if (_watcherError != null) return;

            while (!_stop)
            {
                Tracked[] snapshot;
                lock (_active) snapshot = _active.ToArray();
                if (snapshot.Length > 0)
                {
                    try { Scan(snapshot); }
                    catch (Exception ex) { Console.Error.WriteLine("[sandbox] watcher error: " + ex.Message); }
                }
                Thread.Sleep(snapshot.Length > 0 ? 20 : 60);
            }
        }

        void Scan(Tracked[] procs)
        {
            var byPid = new Dictionary<int, Tracked>();
            foreach (var t in procs)
                foreach (int pid in JobPids(t.Job)) byPid[pid] = t;

            var top = new List<IntPtr>();
            Native.EnumDesktopWindows(_desk, (h, l) => { top.Add(h); return true; }, IntPtr.Zero);

            foreach (var t in procs)
            {
                if (!t.FinalShotRequested || t.FinalShotDone.WaitOne(0)) continue;
                foreach (IntPtr hwnd in top)
                {
                    int owner;
                    Native.GetWindowThreadProcessId(hwnd, out owner);
                    Tracked ot;
                    if (!byPid.TryGetValue(owner, out ot) || ot != t || !Native.IsWindowVisible(hwnd)) continue;
                    var cap = Capture(hwnd);
                    cap.Kind = "Window";
                    cap.AtMs = t.Clock.ElapsedMilliseconds;
                    cap.Action = "final-shot";
                    int n = Interlocked.Increment(ref t.ShotCounter);
                    string png = Path.Combine(t.Opt.ShotDir, string.Format("{0:D2}_final.png", n));
                    if (Native.IsResponsive(hwnd, 2000) && Screenshot(hwnd, png)) cap.Screenshot = png;
                    lock (t) t.Windows.Add(cap);
                }
                t.FinalShotDone.Set();
            }

            foreach (IntPtr hwnd in top)
            {
                int pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                Tracked t;
                if (!byPid.TryGetValue(pid, out t)) continue;
                if (!Native.IsWindowVisible(hwnd)) continue;

                WinState st;
                lock (t)
                {
                    if (!t.Seen.TryGetValue(hwnd, out st))
                    {
                        st = new WinState { FirstSeen = t.Clock.ElapsedMilliseconds };
                        t.Seen[hwnd] = st;
                    }
                }
                if (st.Done) continue;

                // Let the window finish painting / populating before we read or photograph it.
                long settle = t.Opt.ShotDir != null ? 250 : 60;
                if (t.Clock.ElapsedMilliseconds - st.FirstSeen < settle) continue;
                st.Done = true;
                Handle(t, hwnd);
            }
        }

        void Handle(Tracked t, IntPtr hwnd)
        {
            var cap = Capture(hwnd);
            cap.AtMs = t.Clock.ElapsedMilliseconds;
            cap.Kind = Classify(cap, t.Opt.ScriptName);

            if (t.Opt.ShotDir != null && Native.IsResponsive(hwnd, 500))
            {
                int n = Interlocked.Increment(ref t.ShotCounter);
                string png = Path.Combine(t.Opt.ShotDir, string.Format("{0:D2}_{1}.png", n, cap.Kind));
                if (Screenshot(hwnd, png)) cap.Screenshot = png;
            }

            if (cap.Kind == "AhkError")
            {
                if (t.Opt.KillOnError)
                {
                    cap.Action = "killed";
                    t.ErrorKill = true;
                    Native.TerminateJobObject(t.Job, 0xE77);
                }
                else cap.Action = Click(hwnd, "Continue") ? "continue" : "none";
            }
            else if (cap.Kind == "AhkWarning")
            {
                cap.Action = t.Opt.DismissDialogs && Click(hwnd, "Continue") ? "continue" : "none";
            }
            else if (cap.Kind == "MsgBox" && t.Opt.DismissDialogs)
            {
                cap.Action = PressDefault(hwnd) ? "default-button" : "none";
            }
            else cap.Action = "observed";

            lock (t) t.Windows.Add(cap);
        }

        static WindowCapture Capture(IntPtr hwnd)
        {
            var cap = new WindowCapture();
            cap.Class = Native.GetClass(hwnd);
            cap.Title = Native.GetText(hwnd);
            Native.RECT r;
            if (Native.GetWindowRect(hwnd, out r)) { cap.Width = r.Right - r.Left; cap.Height = r.Bottom - r.Top; }
            var text = new StringBuilder();
            Native.EnumChildWindows(hwnd, (h, l) =>
            {
                string cls = Native.GetClass(h);
                string txt = Native.GetText(h);
                cap.Controls.Add(cls + "|" + txt + "|#" + (Native.GetWindowLong(h, Native.GWL_ID) & 0xFFFF));
                if (cls.Equals("Button", StringComparison.OrdinalIgnoreCase) && IsPushButton(h))
                    cap.Buttons.Add(txt.Replace("&", ""));
                else if (txt.Length > 0 && Native.IsWindowVisible(h))
                {
                    if (text.Length > 0) text.Append('\n');
                    text.Append(txt);
                }
                return true;
            }, IntPtr.Zero);
            cap.Text = text.ToString();
            return cap;
        }

        static bool IsPushButton(IntPtr h)
        {
            int style = Native.GetWindowLong(h, Native.GWL_STYLE) & 0xF;
            return style == 0 || style == 1; // BS_PUSHBUTTON / BS_DEFPUSHBUTTON
        }

        static string Classify(WindowCapture cap, string scriptName)
        {
            bool hasExitApp = cap.Buttons.Exists(b => b.Equals("ExitApp", StringComparison.OrdinalIgnoreCase));
            string body = cap.Text.TrimStart();
            if (hasExitApp || (cap.Class == "#32770" && (body.StartsWith("Error:") || body.StartsWith("Critical Error") || body.StartsWith("Warning:"))
                                && scriptName != null && cap.Title.Equals(scriptName, StringComparison.OrdinalIgnoreCase)))
            {
                return body.StartsWith("Warning") ? "AhkWarning" : "AhkError";
            }
            if (cap.Class == "#32770") return "MsgBox";
            return "Window";
        }

        static IntPtr FindButton(IntPtr dlg, string text)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumChildWindows(dlg, (h, l) =>
            {
                if (Native.GetClass(h).Equals("Button", StringComparison.OrdinalIgnoreCase)
                    && Native.GetText(h).Replace("&", "").Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static bool Click(IntPtr dlg, string buttonText)
        {
            IntPtr b = FindButton(dlg, buttonText);
            if (b == IntPtr.Zero) return false;
            int id = Native.GetWindowLong(b, Native.GWL_ID);
            return Native.PostMessage(dlg, Native.WM_COMMAND, new IntPtr(id & 0xFFFF), b);
        }

        static bool PressDefault(IntPtr dlg)
        {
            IntPtr r;
            if (Native.SendMessageTimeoutI(dlg, Native.DM_GETDEFID, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 500, out r) != IntPtr.Zero)
            {
                long v = r.ToInt64();
                if (((v >> 16) & 0xFFFF) == 0x534B) // DC_HASDEFID
                {
                    int id = (int)(v & 0xFFFF);
                    IntPtr btn = IntPtr.Zero;
                    Native.EnumChildWindows(dlg, (h, l) =>
                    {
                        if ((Native.GetWindowLong(h, Native.GWL_ID) & 0xFFFF) == id) { btn = h; return false; }
                        return true;
                    }, IntPtr.Zero);
                    // MB_OK boxes report IDOK as default but their only button is IDCANCEL; only trust ids that exist.
                    if (btn != IntPtr.Zero)
                        return Native.PostMessage(dlg, Native.WM_COMMAND, new IntPtr(id), btn);
                }
            }
            // No (usable) default: first push button, else close.
            IntPtr first = IntPtr.Zero;
            Native.EnumChildWindows(dlg, (h, l) =>
            {
                if (Native.GetClass(h).Equals("Button", StringComparison.OrdinalIgnoreCase) && IsPushButton(h)) { first = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (first != IntPtr.Zero)
                return Native.PostMessage(dlg, Native.WM_COMMAND, new IntPtr(Native.GetWindowLong(first, Native.GWL_ID) & 0xFFFF), first);
            return Native.PostMessage(dlg, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Renders a window (on the hidden desktop) to PNG. Must be called from the watcher thread.</summary>
        static bool Screenshot(IntPtr hwnd, string path)
        {
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return false;
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0 || w > 8000 || h > 8000) return false;
            foreach (uint flags in new uint[] { Native.PW_RENDERFULLCONTENT, 0 })
            {
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    bool ok;
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try { ok = Native.PrintWindow(hwnd, hdc, flags); }
                        finally { g.ReleaseHdc(hdc); }
                    }
                    if (ok && !IsBlank(bmp))
                    {
                        bmp.Save(path, ImageFormat.Png);
                        return true;
                    }
                }
            }
            return false;
        }

        static bool IsBlank(Bitmap bmp)
        {
            Color first = bmp.GetPixel(0, 0);
            int stepX = Math.Max(1, bmp.Width / 24), stepY = Math.Max(1, bmp.Height / 24);
            for (int y = 0; y < bmp.Height; y += stepY)
                for (int x = 0; x < bmp.Width; x += stepX)
                    if (bmp.GetPixel(x, y).ToArgb() != first.ToArgb()) return false;
            return true;
        }
    }
}
