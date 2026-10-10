using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FpsGuard {
    public sealed class WindowsSystem : ISystem {
        readonly int session = Process.GetCurrentProcess().SessionId;
        readonly GameDiscovery discovery = new GameDiscovery();
        readonly Dictionary<string,string> pathCache = new Dictionary<string,string>();
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref int size);
        [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint id,out uint sessionId);
        [StructLayout(LayoutKind.Sequential)] public class MemoryStatus {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtended;
        }
        [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size, Time; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct ProcessEntry {
            public uint Size, Usage, Id; public IntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        delegate bool WindowCallback(IntPtr window, IntPtr param);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr param);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("psapi.dll", SetLastError = true)] static extern bool EmptyWorkingSet(IntPtr process);
        [StructLayout(LayoutKind.Sequential)] struct MemoryCounters {
            public uint Size, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }
        [DllImport("psapi.dll", SetLastError = true)] static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint GetPriorityClass(IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetPriorityClass(IntPtr process, uint priority);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr user, out IntPtr guid);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
        public static MemoryStatus Memory() { var m = new MemoryStatus(); GlobalMemoryStatusEx(m); return m; }
        static IntPtr VerifiedHandle(ProcessInfo p, uint access) {
            IntPtr h = OpenProcess(access | 0x1000, false, p.Id);
            if (h == IntPtr.Zero) return h;
            long created, exited, kernel, user;
            if (!GetProcessTimes(h, out created, out exited, out kernel, out user) || created != p.Started) { CloseHandle(h); return IntPtr.Zero; }
            return h;
        }
        public Snapshot Capture(Settings settings = null, bool lightweight = false) {
            settings = settings ?? new Settings();
            string[] roots = discovery.Folders(settings, !lightweight);
            if (pathCache.Count > 4096) pathCache.Clear();
            var snap = new Snapshot(); var memory = Memory();
            snap.TotalMemory = memory.TotalPhys; snap.AvailableMemory = memory.AvailPhys; snap.MemoryLoad = memory.Load;
            LastInput input = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) };
            if (GetLastInputInfo(ref input)) snap.IdleSeconds = unchecked((uint)Environment.TickCount - input.Time) / 1000;
            IntPtr handle = CreateToolhelp32Snapshot(2, 0);
            if (handle == new IntPtr(-1)) { snap.Complete = false; return snap; }
            var byId = new Dictionary<int, ProcessInfo>();
            try {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                bool ok = Process32FirstW(handle, ref entry);
                if (!ok) snap.Complete = false;
                while (ok) {
                    string name = System.IO.Path.GetFileNameWithoutExtension(entry.Exe ?? "");
                    // Names remain in snapshot even when anti-cheat denies a process handle.
                    var p = new ProcessInfo { Id = (int)entry.Id, Name = name, UserProcess = false };
                    byId[p.Id] = p; snap.Processes.Add(p);
                    if (!Policy.IsProtected(name, settings) || (settings.BoostGame && (name.Equals("cs2", StringComparison.OrdinalIgnoreCase) || name.Equals("csgo", StringComparison.OrdinalIgnoreCase) || name.Equals("scpsl", StringComparison.OrdinalIgnoreCase)))) {
                        uint processSession;
                        if (ProcessIdToSessionId((uint)p.Id, out processSession) && processSession == session) {
                            IntPtr ph = OpenProcess(0x1000, false, p.Id);
                            if (ph != IntPtr.Zero) {
                                try {
                                    long created,exited,kernel,user;
                                    if (GetProcessTimes(ph,out created,out exited,out kernel,out user)) {
                                        p.Started=created;
                                        string path;
                                        if (!pathCache.TryGetValue(p.Key,out path)) {
                                            var buffer=new StringBuilder(32768); int length=buffer.Capacity;
                                            path=QueryFullProcessImageName(ph,0,buffer,ref length)?buffer.ToString():"";
                                            if(path.Length>0)pathCache[p.Key]=path;
                                        }
                                        p.Path=path;
                                        string windowsRoot=Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\')+"\\";
                                        p.UserProcess=path.Length>0 && !path.StartsWith(windowsRoot,StringComparison.OrdinalIgnoreCase);
                                        p.DetectedGame=GameDiscovery.Matches(name,path,roots);
                                        if(p.UserProcess && (!lightweight || Policy.IsBackground(name,settings))) {
                                            var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf(typeof(MemoryCounters)) };
                                            if (GetProcessMemoryInfo(ph, ref counters, counters.Size)) p.WorkingSet = (long)counters.WorkingSetSize.ToUInt64();
                                        }
                                    }
                                } finally {CloseHandle(ph);}
                            }
                        }
                    }
                    ok = Process32NextW(handle, ref entry);
                }
                if (Marshal.GetLastWin32Error() != 18) snap.Complete = false;
            } finally { CloseHandle(handle); }
            uint foreground; GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            ProcessInfo front; if (byId.TryGetValue((int)foreground, out front)) snap.ForegroundName = front.Name;
            bool windows = EnumWindows(delegate(IntPtr window, IntPtr param) {
                if (IsWindowVisible(window) && !IsIconic(window)) {
                    uint id; GetWindowThreadProcessId(window, out id);
                    ProcessInfo p; if (byId.TryGetValue((int)id, out p)) snap.VisibleNames.Add(p.Name);
                }
                return true;
            }, IntPtr.Zero);
            if (!windows) snap.Complete = false;
            return snap;
        }
        public int? ReadPriority(ProcessInfo p) {
            IntPtr handle = VerifiedHandle(p, 0x1000);
            if (handle == IntPtr.Zero) return null;
            try { uint value = GetPriorityClass(handle); return value == 0 ? (int?)null : (int)value; } finally { CloseHandle(handle); }
        }
        public bool SetPriority(ProcessInfo p, int value) {
            IntPtr handle = VerifiedHandle(p, 0x200);
            if (handle == IntPtr.Zero) return false;
            try { return SetPriorityClass(handle, (uint)value); } finally { CloseHandle(handle); }
        }
        public bool? SameProcess(PriorityChange p) {
            try { using (var proc = Process.GetProcessById(p.Id)) return proc.StartTime.ToUniversalTime().ToFileTimeUtc() == p.Started; }
            catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; } catch (Win32Exception) { return null; }
        }
        public string GetPower() {
            IntPtr ptr;
            if (PowerGetActiveScheme(IntPtr.Zero, out ptr) != 0) return "";
            try { return ((Guid)Marshal.PtrToStructure(ptr, typeof(Guid))).ToString(); } finally { LocalFree(ptr); }
        }
        public bool CloseWindow(ProcessInfo p) {
            IntPtr handle = VerifiedHandle(p, 0x1000);
            if (handle == IntPtr.Zero) return false;
            try { using (var proc = Process.GetProcessById(p.Id)) {
                return proc.StartTime.ToUniversalTime().ToFileTimeUtc() == p.Started && proc.CloseMainWindow();
            }} catch { return false; } finally { CloseHandle(handle); }
        }
        public bool ControlWallpaper(string command) {
            if (command != "stop" && command != "play") return false;
            bool found = false;
            foreach (string name in new[] { "wallpaper64", "wallpaper32" }) {
                var running = Process.GetProcessesByName(name);
                try {
                    foreach (var proc in running) {
                        try {
                            if (proc.SessionId != session) continue;
                            found = true;
                            string exe = proc.MainModule.FileName;
                            using (var sent = Process.Start(new ProcessStartInfo(exe, "-control " + command) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) {
                                if (sent != null) return true;
                            }
                        } catch { }
                    }
                } finally { foreach (var proc in running) proc.Dispose(); }
            }
            return command == "play" && !found;
        }
        public long Trim(ProcessInfo p) {
            IntPtr handle = VerifiedHandle(p, 0x100 | 0x400);
            if (handle == IntPtr.Zero) return -1;
            try {
                if (!EmptyWorkingSet(handle)) return -1;
                using (var proc = Process.GetProcessById(p.Id)) return Math.Max(0, p.WorkingSet - proc.WorkingSet64);
            } catch { return -1; } finally { CloseHandle(handle); }
        }
        public void SaveRecovery(RecoveryState state) { Store.Save("recovery.xml", state); }
        public void Log(string text) { Store.Log(text); }
    }
}
