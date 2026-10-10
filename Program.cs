using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FpsGuard {
    static class Program {
        const string Instance = "Local\\FpsGuard.v1";
        public static EventWaitHandle ShowEvent, ExitEvent, CleanEvent;
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        static void EnableDpi(){try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{SetProcessDPIAware();}}
        [STAThread] static int Main(string[] args) {
            try {
                if (args.Contains("--ui-test") || args.Contains("--ui-test-live")) {
                    bool live=args.Contains("--ui-test-live");
                    if(args.Contains("--light"))Theme.Dark=false;
                    if(args.Contains("--dark"))Theme.Dark=true;
                    EnableDpi(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    var engine = new Engine(new WindowsSystem(), new Settings(), new RecoveryState());
                    engine.Latest = new WindowsSystem().Capture();
                    using (var form = new Dashboard(null, engine, SystemIcons.Application)) {
                        form.StartPosition = FormStartPosition.Manual; form.Location = live?new Point(100,60):new Point(-30000, -30000);
                        form.ShowInTaskbar = false; form.Show(); form.UpdateStatus(); Application.DoEvents();
                        int dpi=96;int dpiArg=Array.IndexOf(args,"--dpi");if(dpiArg>=0)dpi=int.Parse(args[dpiArg+1]);
                        var workArea=args.Contains("--small-screen")?new Rectangle(0,0,1920,1032):new Rectangle(0,0,2560,1392);
                        foreach(int checkDpi in new[]{96,144,192,120,96}){form.PreviewDpiMessage(checkDpi);Application.DoEvents();form.PreviewValidateLayout();}
                        form.PreviewDpi(dpi,workArea);Application.DoEvents();form.PreviewScrollBottom();
                        if(form.ClientSize.Width>workArea.Width||form.ClientSize.Height>workArea.Height)throw new InvalidOperationException("Window does not fit work area");
                        var tabs = form.PreviewTabs;
                        for(int repeat=0;repeat<3;repeat++)for(int page=0;page<tabs.TabPages.Count;page++){form.PreviewNavigate(page);Application.DoEvents();if(form.PreviewPageIndex!=page)throw new InvalidOperationException("Repeated navigation failed: "+page);}
                        for (int i = 0; i < tabs.TabPages.Count; i++) {
                            form.PreviewNavigate(i);
                            Application.DoEvents();
                            if(form.PreviewPageIndex!=i)throw new InvalidOperationException("Page switch failed: "+i);
                            form.PreviewValidateLayout();
                            using (var bitmap = new Bitmap(form.Width, form.Height)) {
                                form.Refresh();Application.DoEvents();
                                if(live){Thread.Sleep(120);Application.DoEvents();using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(form.Location,Point.Empty,form.Size);}
                                else form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, (live?"live-":"ui-")+(Theme.Dark?"dark-":"light-") + dpi + (args.Contains("--small-screen")?"-small":"")+"-"+i + ".png"));
                            }
                        }
                    }
                    return 0;
                }
                if (args.Contains("--probe")) {
                    var sys = new WindowsSystem(); var watch = Stopwatch.StartNew(); var s = sys.Capture();
                    double fullMs=watch.Elapsed.TotalMilliseconds;watch.Restart();var light=sys.Capture(new Settings(),true);double lightMs=watch.Elapsed.TotalMilliseconds;
                    string report = "Complete=" + s.Complete + "\r\nLightweightComplete="+light.Complete+"\r\nFullCaptureMs="+fullMs.ToString("0.00")+"\r\nCachedLightCaptureMs="+lightMs.ToString("0.00")+"\r\nMemoryLoad=" + s.MemoryLoad + "\r\nAvailableMB=" + s.AvailableMemory / 1048576 + "\r\nProcesses=" + s.Processes.Count + "\r\nSteamGameDirectories="+new GameDiscovery().Folders(new Settings()).Length+"\r\nKnownGameNames="+Policy.BuiltInGames.Length+"\r\nPower=" + sys.GetPower();
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe.txt"), report);
                    return s.Complete && light.Complete && s.TotalMemory > 0 ? 0 : 2;
                }
                if (args.Contains("--exit")) { Signal(Instance + ".Exit"); return 0; }
                if (args.Contains("--clean")) { Signal(Instance + ".Clean"); return 0; }
                if (args.Contains("--remove-startup")) { Startup.Set(false); return 0; }
                bool created;
                using (var mutex = new Mutex(true, Instance, out created)) {
                    if (!created) { if (!args.Contains("--background")) Signal(Instance + ".Show"); return 0; }
                    using (ShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Instance + ".Show"))
                    using (CleanEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Instance + ".Clean"))
                    using (ExitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Instance + ".Exit")) {
                        EnableDpi(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Store.Log("界面异常：" + e.Exception.Message); };
                        Application.Run(new TrayApp(!args.Contains("--background")));
                    }
                    mutex.ReleaseMutex();
                }
                return 0;
            } catch (Exception e) {
                Store.Log("启动失败：" + e);
                if (!args.Contains("--background")&&!args.Contains("--ui-test")&&!args.Contains("--ui-test-live")) MessageBox.Show("FPS 轻守未启动：\n" + e.Message + "\n配置与恢复记录保留在：\n" + Store.Root, "FPS 轻守");
                return 1;
            }
        }
        static void Signal(string name) { try { using (var e = EventWaitHandle.OpenExisting(name)) e.Set(); } catch (WaitHandleCannotBeOpenedException) { } }
    }
    static class Startup {
        const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        public static bool Enabled { get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && key.GetValue("FpsGuard") != null; } }
        public static void Set(bool enabled) {
            using (var key = Registry.CurrentUser.CreateSubKey(Key)) {
                if (enabled) key.SetValue("FpsGuard", "\"" + Application.ExecutablePath + "\" --background");
                else key.DeleteValue("FpsGuard", false);
            }
        }
    }
    sealed class TrayApp : ApplicationContext {
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer;
        readonly Engine engine;
        readonly Icon icon;
        readonly ToolStripMenuItem pause;
        Dashboard window;
        bool quitting;
        DateTime nextTick = DateTime.MinValue;
        int errors;
        public TrayApp(bool show) {
            var settings = Store.Read<Settings>("settings.xml");
            settings.Normalize();
            engine = new Engine(new WindowsSystem(), settings, Store.Read<RecoveryState>("recovery.xml"));
            engine.RestoreAll();
            Store.Save("settings.xml", settings);
            using (var proc = Process.GetCurrentProcess()) proc.PriorityClass = ProcessPriorityClass.BelowNormal;
            icon = MakeIcon();
            tray = new NotifyIcon { Icon = icon, Text = "FPS 轻守 · 正在启动", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开 FPS 轻守", null, delegate { Open(); });
            menu.Items.Add("清理后台内存", null, delegate { Clean(); });
            pause = new ToolStripMenuItem("暂停守护", null, delegate { SetEnabled(!engine.Settings.Enabled); });
            menu.Items.Add(pause);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出并恢复调整", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Open(); };
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += delegate {
                if (Program.ExitEvent.WaitOne(0)) { Quit(); return; }
                if (Program.ShowEvent.WaitOne(0)) Open();
                if (Program.CleanEvent.WaitOne(0)) Clean();
                if (DateTime.UtcNow >= nextTick) {
                    nextTick = DateTime.UtcNow.AddSeconds(engine.CheckInterval);
                    try { engine.Tick(DateTime.UtcNow, window != null && window.Visible && window.ProcessPageVisible); errors = 0; }
                    catch (Exception ex) {
                        Store.Log("本轮检测失败：" + ex.Message); errors++;
                        if (errors >= 3) {
                            engine.Settings.Enabled = false;
                            try { engine.RestoreAll(); } catch (Exception restore) { Store.Log("恢复待下次重试：" + restore.Message); }
                        }
                    }
                    Refresh();
                }
            };
            timer.Start();
            SystemEvents.SessionEnding += SessionEnding;
            Store.Log("FPS 轻守 3.0 启动；电源方案固定保留；游戏期间禁止内存清理。");
            if (show) Open();
        }
        public void Refresh() {
            pause.Text = engine.Settings.Enabled ? "暂停守护并恢复调整" : "恢复守护";
            string text = "FPS 轻守 · " + (!engine.Settings.Enabled ? "已暂停" : engine.Games.Length > 0 ? "游戏守护" : "待机") + " · 内存 " + engine.Latest.MemoryLoad + "%";
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            if (window != null && !window.IsDisposed && window.Visible) window.UpdateStatus();
        }
        public void Save() { engine.Settings.Normalize(); Store.Save("settings.xml", engine.Settings); nextTick = DateTime.MinValue; Refresh(); }
        public void RequestRefresh() { nextTick = DateTime.MinValue; }
        public void SetEnabled(bool enabled) {
            engine.Settings.Enabled = enabled;
            if (!enabled) engine.RestoreAll();
            Save(); Store.Log(enabled ? "守护已恢复。" : "守护已暂停，已尝试恢复所有调整。");
        }
        public void Clean() {
            string result = engine.Clean(DateTime.UtcNow, true); Refresh();
            if (window != null && window.Visible) window.ShowResult(result);
            else { tray.BalloonTipTitle = "FPS 轻守"; tray.BalloonTipText = result; tray.ShowBalloonTip(4000); }
        }
        void Open() {
            bool dark=Theme.SystemDark();
            if(dark!=Theme.Dark){Theme.Dark=dark;if(window!=null&&!window.IsDisposed){window.AllowClose=true;window.Dispose();window=null;}}
            if (window == null || window.IsDisposed) window = new Dashboard(this, engine, icon);
            window.Show(); window.WindowState = FormWindowState.Normal; window.Activate(); window.UpdateStatus();
        }
        void SessionEnding(object sender, SessionEndingEventArgs e) { try { engine.RestoreAll(); } catch (Exception ex) { Store.Log("会话结束恢复失败：" + ex.Message); } }
        void Quit() {
            if (quitting) return; quitting = true; timer.Stop();
            try { engine.RestoreAll(); } catch (Exception ex) { Store.Log("退出恢复失败，下次启动重试：" + ex.Message); }
            SystemEvents.SessionEnding -= SessionEnding;
            tray.Visible = false; tray.Dispose(); timer.Dispose();
            if (window != null) { window.AllowClose = true; window.Close(); window.Dispose(); }
            icon.Dispose(); Store.Log("FPS 轻守已退出。"); ExitThread();
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        static Icon MakeIcon() {
            using (var bitmap = new Bitmap(32, 32)) {
                using (var g = Graphics.FromImage(bitmap)) {
                    g.Clear(Color.FromArgb(22, 28, 35));
                    using (var pen = new Pen(Color.FromArgb(173, 240, 92), 3)) {
                        g.DrawEllipse(pen, 6, 6, 20, 20); g.DrawLine(pen, 16, 2, 16, 11); g.DrawLine(pen, 16, 21, 16, 30);
                        g.DrawLine(pen, 2, 16, 11, 16); g.DrawLine(pen, 21, 16, 30, 16);
                    }
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (var original = Icon.FromHandle(handle)) return (Icon)original.Clone(); } finally { DestroyIcon(handle); }
            }
        }
    }
}
