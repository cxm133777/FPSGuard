using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace FpsGuard {
    public class Settings {
        public bool Enabled = true;
        public bool AutoClean = true;
        public bool LowerBackground = true;
        public bool LowerEditors = true;
        public bool BoostGame = false;
        public bool DetectSteamGames = true;
        public string GameFolders = "";
        public int MemoryThreshold = 80;
        public string ExtraGames = "";
        public int CheckSeconds = 5;
        public int CleanIntervalMinutes = 5;
        public int IdleSeconds = 45;
        public int MaxCleanProcesses = 12;
        public int MinWorkingSetMB = 64;
        public bool CleanSteam = true;
        public bool ManualIncludeVisible = false;
        public bool StopWallpaperInGame = false;
        public string CleanList = "";
        public string BackgroundList = "steamwebhelper,cloudmusic";
        public string ProtectedList = "";
        public void Normalize() {
            MemoryThreshold = Math.Max(60, Math.Min(95, MemoryThreshold));
            CheckSeconds = Math.Max(3, Math.Min(30, CheckSeconds));
            CleanIntervalMinutes = Math.Max(2, Math.Min(60, CleanIntervalMinutes));
            IdleSeconds = Math.Max(15, Math.Min(600, IdleSeconds));
            MaxCleanProcesses = Math.Max(1, Math.Min(60, MaxCleanProcesses));
            MinWorkingSetMB = Math.Max(16, Math.Min(1024, MinWorkingSetMB));
        }
    }
    public class ProcessInfo {
        public int Id;
        public long Started;
        public string Name = "";
        public long WorkingSet;
        public string Path = "";
        public bool UserProcess = true;
        public bool DetectedGame = false;
        public string Key { get { return Id + ":" + Started; } }
    }
    public class Snapshot {
        public List<ProcessInfo> Processes = new List<ProcessInfo>();
        public HashSet<string> VisibleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string ForegroundName = "";
        public ulong TotalMemory, AvailableMemory;
        public uint MemoryLoad, IdleSeconds;
        public bool Complete = true;
    }
    public class PriorityChange {
        public int Id;
        public long Started;
        public string Name;
        public int Before, Applied;
        public string Key { get { return Id + ":" + Started; } }
    }
    public class RecoveryState {
        public List<PriorityChange> Priorities = new List<PriorityChange>();
        public bool WallpaperStopped = false;
    }
    public interface ISystem {
        Snapshot Capture(Settings settings = null, bool lightweight = false);
        int? ReadPriority(ProcessInfo p);
        bool SetPriority(ProcessInfo p, int value);
        // null: cannot inspect; false: process is gone or PID was reused.
        bool? SameProcess(PriorityChange p);
        long Trim(ProcessInfo p);
        bool CloseWindow(ProcessInfo p);
        bool ControlWallpaper(string command);
        void SaveRecovery(RecoveryState state);
        void Log(string message);
    }
    public static class Policy {
        public static readonly string[] BuiltInGames = { "cs2", "csgo", "valorant", "valorant-win64-shipping", "scpsl", "gmod", "r5apex", "tslgame", "rainbowsix", "rainbowsix_vulkan", "rainbowsix_dx11", "overwatch", "fortniteclient-win64-shipping", "cod", "cod22-cod", "bf2042", "bfv", "bf1", "escapefromtarkov", "rustclient", "dota2", "league of legends", "rocketleague", "eldenring", "cyberpunk2077", "gta5", "gta5_enhanced", "rdr2", "witcher3", "baldur's gate 3", "bg3", "bg3_dx11", "helldivers2", "destiny2", "warframe.x64", "deadbydaylight-win64-shipping", "minecraft.windows", "genshinimpact", "yuanshen", "starrail", "zenlesszonezero", "wuthering waves", "client-win64-shipping" };
        public static bool IsGame(ProcessInfo p, Settings s) { return p.DetectedGame || IsGame(p.Name, s); }
        public static bool IsProtected(ProcessInfo p, Settings s) { return p.DetectedGame || IsProtected(p.Name, s); }
        public static readonly HashSet<string> Browsers = new HashSet<string>(new[] { "msedge", "chrome", "firefox" }, StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> Protected = new HashSet<string>(("system,idle,registry,secure system,memory compression,smss,csrss,wininit,services,lsass,winlogon,svchost,fontdrvhost,dwm,explorer,sihost,taskhostw,ctfmon,conhost,taskmgr,msmpeng,msmpengcp,nissrv,securityhealthservice,securityhealthsystray,securityhealthhost,mpdefendercoreservice,fpsguard,oopz,oopz-runner,kook,discord,teams,ms-teams,ts3client_win64,ts3client_win32,voicemeeter,voicemeeterpro,voicemeeter8,vb-audio,soundpad,audiodg,rtkauduservice64,vgc,vgtray,beservice,beservice_x64,easyanticheat,easyanticheat_eos,ace-tray,ace-service,faceitclient,faceitservice,obs64,obs32").Split(','), StringComparer.OrdinalIgnoreCase);
        public static bool IsProtected(string name, Settings settings) {
            return IsGame(name, settings) || Protected.Contains(name) || ExtraNames(settings.ProtectedList).Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.StartsWith("valorant", StringComparison.OrdinalIgnoreCase) || name.IndexOf("anticheat", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("defender", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static bool IsCleanTarget(string name, Settings settings) {
            return !IsProtected(name, settings) && (Browsers.Contains(name) || (settings.CleanSteam && name.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase))
                || ExtraNames(settings.CleanList).Contains(name, StringComparer.OrdinalIgnoreCase));
        }
        public static bool IsGame(string name, Settings settings) {
            return BuiltInGames.Contains(name, StringComparer.OrdinalIgnoreCase) || ExtraNames(settings.ExtraGames).Contains(name, StringComparer.OrdinalIgnoreCase);
        }
        public static string[] ExtraNames(string text) {
            return (text ?? "").Split(new[] { ',', ';', '\r', '\n', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x.Substring(0, x.Length - 4) : x)
                .Where(x => x.Length > 0 && x.Length < 100 && x.IndexOfAny(new[] { '/', '\\', ':', '*', '?' }) < 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static bool IsBackground(string name) {
            return Browsers.Contains(name) || name.Equals("wallpaper64", StringComparison.OrdinalIgnoreCase) || name.Equals("wallpaper32", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsBackground(string name, Settings settings) {
            return !IsProtected(name, settings) && (IsBackground(name) || ExtraNames(settings.BackgroundList).Contains(name, StringComparer.OrdinalIgnoreCase)
                || settings.LowerEditors && new[] { "Code", "OneDrive" }.Contains(name, StringComparer.OrdinalIgnoreCase));
        }
        public static bool CanAutoClean(Settings s, Snapshot snap, DateTime now, DateTime last, DateTime lastGame, int pressureCount) {
            return s.Enabled && s.AutoClean && snap.Complete && snap.TotalMemory > 0 && pressureCount >= 3 && snap.MemoryLoad >= s.MemoryThreshold
                && snap.IdleSeconds >= s.IdleSeconds && (now - last).TotalMinutes >= s.CleanIntervalMinutes && (now - lastGame).TotalSeconds >= 120
                && !snap.Processes.Any(p => IsGame(p, s));
        }
        public static bool CanTrim(ProcessInfo p, Snapshot s, Settings settings, bool manual = false) {
            return p.UserProcess && p.Started > 0 && !IsProtected(p, settings) && IsCleanTarget(p.Name, settings) && p.WorkingSet >= (long)settings.MinWorkingSetMB * 1024 * 1024
                && (manual && settings.ManualIncludeVisible || !s.VisibleNames.Contains(p.Name)) && !p.Name.Equals(s.ForegroundName, StringComparison.OrdinalIgnoreCase);
        }
    }
    public sealed class Engine {
        readonly ISystem system;
        readonly RecoveryState recovery;
        readonly HashSet<string> attempted = new HashSet<string>();
        int pressureCount;
        DateTime lastClean = DateTime.MinValue, lastGame = DateTime.MinValue;
        public Settings Settings;
        public Snapshot Latest = new Snapshot();
        public string Games = "";
        public string LastClean = "尚未清理";
        public bool ManualSession = false;
        public int CheckInterval { get { return Settings.CheckSeconds; } }
        public int ManagedCount { get { return recovery.Priorities.Count; } }
        public Engine(ISystem system, Settings settings, RecoveryState recovery) { this.system = system; Settings = settings; Settings.Normalize(); this.recovery = recovery; }
        public void Tick(DateTime now, bool fullDetails = false) {
            Latest = system.Capture(Settings, !fullDetails);
            if (!Latest.Complete) { system.Log("进程快照不完整，本轮跳过优化与清理。"); return; }
            var games = Latest.Processes.Where(p => Policy.IsGame(p, Settings)).ToList();
            string gameNames = String.Join(" / ", games.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase));
            if (ManualSession && gameNames.Length == 0) gameNames = "手动守护";
            if (gameNames != Games) system.Log(gameNames.Length > 0 ? "游戏已识别：" + gameNames + "；内存清理锁定。" : "游戏已退出，恢复本工具调整的设置。");
            Games = gameNames;
            if (games.Count > 0 || ManualSession) lastGame = now;
            if (!Settings.Enabled || (games.Count == 0 && !ManualSession)) {
                RestoreAll(); attempted.Clear();
            } else {
                var wanted = new Dictionary<string, int>();
                foreach (var p in Latest.Processes) {
                    if (!p.UserProcess || p.Started == 0) continue;
                    // Valorant is detected for protection but never opened for a priority change.
                    if (Settings.BoostGame && new[] { "cs2", "csgo", "scpsl" }.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
                        && !Policy.ExtraNames(Settings.ProtectedList).Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                        wanted[p.Key] = (int)ProcessPriorityClass.AboveNormal;
                    else if (Settings.LowerBackground && !Policy.IsProtected(p, Settings) && Policy.IsBackground(p.Name, Settings)
                        && !p.Name.Equals(Latest.ForegroundName, StringComparison.OrdinalIgnoreCase)) wanted[p.Key] = (int)ProcessPriorityClass.BelowNormal;
                }
                foreach (var c in recovery.Priorities.ToArray()) {
                    if (!wanted.ContainsKey(c.Key) || wanted[c.Key] != c.Applied) { Restore(c); attempted.Remove(c.Key); }
                }
                foreach (var p in Latest.Processes) {
                    int desired;
                    if (!wanted.TryGetValue(p.Key, out desired) || attempted.Contains(p.Key) || recovery.Priorities.Any(c => c.Key == p.Key)) continue;
                    attempted.Add(p.Key);
                    int? before = system.ReadPriority(p);
                    if (!before.HasValue || before.Value != (int)ProcessPriorityClass.Normal) continue;
                    var change = new PriorityChange { Id = p.Id, Started = p.Started, Name = p.Name, Before = before.Value, Applied = desired };
                    recovery.Priorities.Add(change);
                    system.SaveRecovery(recovery); // Write-ahead recovery: no mutation before persistence.
                    if (system.SetPriority(p, desired)) system.Log(p.Name + " 优先级 → " + (desired == (int)ProcessPriorityClass.AboveNormal ? "高于正常" : "低于正常"));
                    else { recovery.Priorities.Remove(change); system.SaveRecovery(recovery); system.Log(p.Name + " 不允许调整，已跳过。"); }
                }
                if (Settings.StopWallpaperInGame && !recovery.WallpaperStopped
                    && Latest.Processes.Any(p => p.Name.Equals("wallpaper64", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("wallpaper32", StringComparison.OrdinalIgnoreCase))) {
                    recovery.WallpaperStopped = true; system.SaveRecovery(recovery);
                    if (system.ControlWallpaper("stop")) system.Log("游戏开始，已发送停止壁纸命令；游戏结束将发送播放命令。");
                    else { recovery.WallpaperStopped = false; system.SaveRecovery(recovery); }
                }
                if (!Settings.StopWallpaperInGame) ResumeWallpaper();
            }
            pressureCount = Latest.MemoryLoad >= Settings.MemoryThreshold ? pressureCount + 1 : 0;
            if (!ManualSession && Policy.CanAutoClean(Settings, Latest, now, lastClean, lastGame, pressureCount)) Clean(now, false);
        }
        public string Clean(DateTime now, bool manual, string selectedKey = null) {
            // Fresh snapshot immediately before each pass protects a game started since the last tick.
            Latest = system.Capture(Settings);
            if (ManualSession || !Latest.Complete || Latest.Processes.Any(p => Policy.IsGame(p, Settings))) return LastClean = "游戏或手动守护中、或检测不可用，已跳过清理。";
            if (!Settings.Enabled) return LastClean = "守护已暂停，未清理。";
            if ((now - lastGame).TotalSeconds < 120) return LastClean = "游戏刚退出，等待 2 分钟后可清理。";
            if (manual && (now - lastClean).TotalSeconds < 30) return LastClean = "清理冷却中，请等待 30 秒。";
            var candidates = Latest.Processes.Where(p => (selectedKey == null || p.Key == selectedKey) && Policy.CanTrim(p, Latest, Settings, manual)).OrderByDescending(p => p.WorkingSet).Take(Settings.MaxCleanProcesses).ToArray();
            if (candidates.Length > 0) lastClean = now;
            long reduced = 0; int count = 0;
            foreach (var p in candidates) {
                var fresh = system.Capture(Settings);
                if (!fresh.Complete || fresh.Processes.Any(x => Policy.IsGame(x, Settings))) break;
                var current = fresh.Processes.FirstOrDefault(x => x.Key == p.Key);
                if (current == null || !Policy.CanTrim(current, fresh, Settings, manual)) continue;
                long result = system.Trim(current);
                if (result >= 0) { reduced += result; count++; }
            }
            var after = system.Capture(Settings);
            double availableDelta = ((double)after.AvailableMemory - Latest.AvailableMemory) / 1048576.0;
            LastClean = count == 0 ? "无可清理目标：检查名单、窗口状态和最小内存设置。" : String.Format("已清理 {0} 个进程：工作集减少约 {1:0} MB，可用内存变化 {2:+0;-0;0} MB。", count, reduced / 1048576.0, availableDelta);
            system.Log((manual ? "手动：" : "自动：") + LastClean);
            return LastClean;
        }
        public void RestoreAll() {
            foreach (var c in recovery.Priorities.ToArray()) Restore(c);
            ResumeWallpaper();
        }
        void ResumeWallpaper() {
            if (!recovery.WallpaperStopped) return;
            if (!system.ControlWallpaper("play")) return;
            recovery.WallpaperStopped = false; system.SaveRecovery(recovery);
            system.Log("壁纸自动控制结束，已发送恢复播放命令（若引擎已退出则无需恢复）。");
        }
        public string CloseSelected(string key) {
            var snap = system.Capture(Settings);
            var p = snap.Processes.FirstOrDefault(x => x.Key == key);
            if (ManualSession || !snap.Complete || snap.Processes.Any(x => Policy.IsGame(x, Settings))) return "游戏或手动守护中、或检测不可用，未关闭程序。";
            if (p == null || !p.UserProcess || p.Started == 0 || Policy.IsProtected(p, Settings)) return "目标受保护或已退出。";
            string message = system.CloseWindow(p) ? "已请求正常关闭 " + p.Name + "；请处理程序自己的保存提示。" : "没有可关闭的主窗口，或程序拒绝关闭。";
            system.Log(message); return message;
        }
        void Restore(PriorityChange c) {
            bool? same = system.SameProcess(c);
            if (!same.HasValue) return; // Retain recovery for transient access failures.
            if (same.Value) {
                var p = new ProcessInfo { Id = c.Id, Started = c.Started, Name = c.Name };
                int? current = system.ReadPriority(p);
                if (!current.HasValue) return;
                if (current.Value == c.Applied && !system.SetPriority(p, c.Before)) return;
            }
            recovery.Priorities.Remove(c); system.SaveRecovery(recovery);
        }
    }
    public static class Store {
        public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpsGuard");
        public static T Read<T>(string name) where T : new() {
            string path = Path.Combine(Root, name);
            if (!File.Exists(path)) return new T();
            using (var stream = File.OpenRead(path)) return (T)new XmlSerializer(typeof(T)).Deserialize(stream);
        }
        public static void Save<T>(string name, T value) {
            Directory.CreateDirectory(Root);
            string path = Path.Combine(Root, name), temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                new XmlSerializer(typeof(T)).Serialize(stream, value); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static void Log(string message) {
            try {
                Directory.CreateDirectory(Root);
                string path = Path.Combine(Root, "activity.log");
                if (File.Exists(path) && new FileInfo(path).Length > 262144) {
                    string previous = Path.Combine(Root, "activity.previous.log");
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(path, previous);
                }
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, System.Text.Encoding.UTF8);
            } catch { }
        }
    }
}
