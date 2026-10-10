using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FpsGuard {
    sealed class FakeSystem : ISystem {
        public Snapshot Snapshot = new Snapshot { TotalMemory = 16UL * 1024 * 1024 * 1024, AvailableMemory = 1024 * 1024 * 1024, MemoryLoad = 90, IdleSeconds = 90 };
        public Dictionary<string, int> Priorities = new Dictionary<string, int>();
        public string Power = "original-custom-power";
        public int Trims, Changes, Saved, Captures, CloseRequests;
        public bool Deny, SaveFails;
        public Action OnCapture;
        public ProcessInfo Add(string name, int id) {
            var p = new ProcessInfo { Name = name, Id = id, Started = id + 100, WorkingSet = 500L * 1048576 };
            Snapshot.Processes.Add(p); Priorities[p.Key] = (int)ProcessPriorityClass.Normal; return p;
        }
        public Snapshot Capture(Settings settings = null, bool lightweight = false) { Captures++; if (OnCapture != null) OnCapture(); return Snapshot; }
        public int? ReadPriority(ProcessInfo p) { return Deny ? (int?)null : Priorities.ContainsKey(p.Key) ? Priorities[p.Key] : (int?)null; }
        public bool SetPriority(ProcessInfo p, int value) { if (Deny) return false; Changes++; Priorities[p.Key] = value; return true; }
        public bool? SameProcess(PriorityChange c) { if (Deny) return null; return Snapshot.Processes.Any(p => p.Key == c.Key); }
        public string GetPower() { return Power; }
        public bool SetPower(string guid) { Power = guid; Changes++; return true; }
        public long Trim(ProcessInfo p) { Trims++; return 100L * 1048576; }
        public bool CloseWindow(ProcessInfo p) { CloseRequests++; return true; }
        public int WallpaperStops, WallpaperPlays;
        public bool WallpaperFails;
        public bool ControlWallpaper(string command) { if(WallpaperFails)return false; if(command=="stop")WallpaperStops++;else WallpaperPlays++;return true; }
        public void SaveRecovery(RecoveryState state) { if (SaveFails) throw new IOException("Injected persistence failure"); Saved++; }
        public void Log(string text) { }
    }
    static class Tests {
        static int passed;
        static readonly DateTime Now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
        static Engine New(FakeSystem f, Settings s) { return new Engine(f, s, new RecoveryState()); }
        static int Main(string[] args) {
            try {
                Check(Policy.IsGame("VALORANT-Win64-Shipping", new Settings { }), "Valorant case-insensitive recognition");
                var editor=new FakeSystem();var code=editor.Add("Code",41);editor.Add("cs2",42);var editorEngine=New(editor,new Settings());editorEngine.Tick(Now);Check(editor.Priorities[code.Key]==(int)ProcessPriorityClass.BelowNormal,"Background editor optimization enabled");editor.Snapshot.ForegroundName="Code";editorEngine.Tick(Now.AddSeconds(5));Check(editor.Priorities[code.Key]==(int)ProcessPriorityClass.Normal,"Foreground editor priority restored");
                editor=new FakeSystem();code=editor.Add("Code",41);editor.Add("cs2",42);editorEngine=New(editor,new Settings{LowerEditors=false});editorEngine.Tick(Now);Check(editor.Priorities[code.Key]==(int)ProcessPriorityClass.Normal,"Editor optimization opt-out respected");
                var stable = new FakeSystem(); Engine stableEngine;
                stable=new FakeSystem();stable.Add("chrome",1);stableEngine=New(stable,new Settings());stableEngine.ManualSession=true;stableEngine.Tick(Now);Check(stable.Changes==1&&stableEngine.Games=="手动守护","Manual session applies to unknown games");stableEngine.Clean(Now,true);Check(stable.Trims==0,"Manual session prevents memory cleanup");stableEngine.CloseSelected(stable.Snapshot.Processes[0].Key);Check(stable.CloseRequests==0,"Manual session prevents application closure");stableEngine.ManualSession=false;stableEngine.Tick(Now.AddSeconds(10));Check(stable.Priorities[stable.Snapshot.Processes[0].Key]==(int)ProcessPriorityClass.Normal,"Manual session restores background adjustments");
                stable=new FakeSystem();var inferred=stable.Add("UnlistedGame",1);inferred.DetectedGame=true;stable.Add("chrome",2);stableEngine=New(stable,new Settings{CleanList="UnlistedGame",BackgroundList="UnlistedGame"});stableEngine.Tick(Now);stableEngine.Clean(Now,true);Check(stable.Trims==0&&stableEngine.Games=="UnlistedGame"&&stable.Priorities[inferred.Key]==(int)ProcessPriorityClass.Normal,"Path-detected game receives full cleanup and priority protection");
                Check(Policy.IsGame("r5apex",new Settings())&&Policy.IsGame("eldenring",new Settings())&&Policy.IsGame("tslgame",new Settings()),"Expanded common game recognition");
                Check(GameDiscovery.Inside(@"C:\Games\Example\bin\game.exe",@"C:\Games\Example"),"Game directory matches child executable");
                Check(!GameDiscovery.Inside(@"C:\Games\Example2\game.exe",@"C:\Games\Example"),"Game directory respects path boundary");
                Check(!GameDiscovery.Inside(@"C:\Games\game.exe",@"C:\"),"Whole drive rejected as game root");
                Check(!GameDiscovery.Matches("CrashReport",@"C:\Games\Example\CrashReport.exe",new[]{@"C:\Games\Example"}),"Common launcher and crash helpers excluded");
                var frame=FrameAnalysis.Calculate(Enumerable.Repeat(5.0,990).Concat(Enumerable.Repeat(20.0,10)));
                Check(Math.Abs(frame.Low1Fps-50)<0.001&&Math.Abs(frame.AverageFps-1000/5.15)<0.001,"1% low computed from slowest one percent frame times");
                Check(frame.P99Ms==5&&frame.P999Ms==20&&frame.Above16==10,"Frame-time percentiles and slow-frame counts");
                bool tooShort=false;try{FrameAnalysis.Calculate(new[]{5.0,Double.NaN,-1,Double.PositiveInfinity});}catch(InvalidDataException){tooShort=true;}Check(tooShort,"Invalid and insufficient frame samples rejected");
                string csv=Path.Combine(Path.GetTempPath(),"FpsGuard-frames-"+Guid.NewGuid().ToString("N")+".csv");
                try {
                    File.WriteAllLines(csv,new[]{"Application,ProcessID,SwapChainAddress,MsBetweenPresents"}.Concat(Enumerable.Repeat("\"Game, Test.exe\",123,0x001,5",100)));
                    Check(Math.Abs(FrameAnalysis.Load(csv).Low1Fps-200)<0.001,"Quoted PresentMon CSV parsing");
                    File.AppendAllText(csv,"Other.exe,124,0x001,10\r\n");bool mixed=false;try{FrameAnalysis.Load(csv);}catch(InvalidDataException){mixed=true;}Check(mixed,"Mixed process frame streams rejected");
                    File.WriteAllLines(csv,new[]{"CPUFrameTime"}.Concat(Enumerable.Repeat("5",100)));bool cpu=false;try{FrameAnalysis.Load(csv);}catch(InvalidDataException){cpu=true;}Check(cpu,"CPUFrameTime not mistaken for presented frame time");
                } finally {if(File.Exists(csv))File.Delete(csv);}
                var wall = new FakeSystem();wall.Add("cs2",1);wall.Add("wallpaper64",2);var wallState=new RecoveryState();var wallEngine=new Engine(wall,new Settings{StopWallpaperInGame=true},wallState);
                wallEngine.Tick(Now);wallEngine.Tick(Now.AddSeconds(5));Check(wall.WallpaperStops==1&&wallState.WallpaperStopped,"Game wallpaper stop sends once and persists recovery");
                wall.Snapshot.Processes.RemoveAll(p=>p.Name=="cs2");wallEngine.Tick(Now.AddSeconds(10));Check(wall.WallpaperPlays==1&&!wallState.WallpaperStopped,"Game exit resumes opted-in wallpaper");
                wall=new FakeSystem();wall.Add("cs2",1);wall.Add("wallpaper64",2);wallEngine=New(wall,new Settings { });wallEngine.Tick(Now);Check(wall.WallpaperStops==0,"Wallpaper automation opt-in only");
                wall=new FakeSystem();wall.Add("cs2",1);wall.Add("wallpaper64",2);wallState=new RecoveryState();wallEngine=new Engine(wall,new Settings{StopWallpaperInGame=true},wallState);wallEngine.Tick(Now);wall.WallpaperFails=true;wallEngine.RestoreAll();Check(wallState.WallpaperStopped,"Wallpaper resume failure retains recovery");wall.WallpaperFails=false;wallEngine.RestoreAll();Check(!wallState.WallpaperStopped,"Wallpaper recovery retries");
                Check(Policy.IsGame("SCPSL", new Settings { }) && Policy.IsGame("cs2", new Settings { }), "CS2 and SCP detection");
                Check(Policy.IsGame("MyGame", new Settings { ExtraGames = "MyGame.exe, Another.exe" }), "Custom process names");
                Check(!Policy.IsGame("cs2-helper", new Settings { }), "Exact matching avoids substring false positive");
                Check(Policy.ExtraNames("../evil.exe,cs2.exe,cs2.exe").SequenceEqual(new[] { "cs2" }), "Custom names validation");
                var f = new FakeSystem(); f.Add("msedge", 1); var e = New(f, new Settings { });
                e.Tick(Now); e.Tick(Now.AddSeconds(5)); Check(f.Trims == 0, "Requires sustained memory pressure");
                e.Tick(Now.AddSeconds(10)); Check(f.Trims == 1, "Automatic cleanup of idle browser under pressure");
                e.Tick(Now.AddSeconds(15)); Check(f.Trims == 1, "Automatic cooldown prevents repeated trims");
                e.Clean(Now.AddSeconds(20), true); Check(f.Trims == 1, "Manual cooldown after automatic trim");
                f = new FakeSystem(); f.Add("msedge", 1); f.Add("cs2", 2); e = New(f, new Settings { });
                e.Clean(Now, true); Check(f.Trims == 0, "Manual cleanup blocked while game runs");
                e.Tick(Now); e.Tick(Now.AddSeconds(5)); e.Tick(Now.AddSeconds(10)); Check(f.Trims == 0, "Automatic cleanup blocked in game");
                f.Snapshot.Processes.RemoveAll(p => p.Name == "cs2"); e.Clean(Now.AddSeconds(20), true); Check(f.Trims == 0, "Post-game cleanup grace period");
                f = new FakeSystem(); var browser = f.Add("msedge", 1); f.Snapshot.VisibleNames.Add("msedge"); e = New(f, new Settings { }); e.Clean(Now, true);
                Check(f.Trims == 0, "Visible browser protects all its subprocesses");
                f.Snapshot.VisibleNames.Clear(); f.Snapshot.ForegroundName = "msedge"; e.Clean(Now.AddMinutes(3), true); Check(f.Trims == 0, "Foreground process protection");
                f = new FakeSystem(); f.Add("oopz", 1); f.Add("QQ", 2); f.Add("MsMpEng", 3); e = New(f, new Settings { }); e.Clean(Now, true); Check(f.Trims == 0, "Voice and security processes excluded");
                f = new FakeSystem(); for (int i = 1; i < 7; i++) f.Add("chrome", i); e = New(f, new Settings { MaxCleanProcesses = 4 }); e.Clean(Now, true); Check(f.Trims == 4, "Custom process limit enforced");
                f = new FakeSystem(); f.Add("chrome", 1); f.Snapshot.IdleSeconds = 10; e = New(f, new Settings { }); e.Tick(Now); e.Tick(Now.AddSeconds(5)); e.Tick(Now.AddSeconds(10));
                Check(f.Trims == 0, "Automatic cleanup requires user idle");
                f = new FakeSystem(); f.Add("chrome", 1); f.Snapshot.MemoryLoad = 40; e = New(f, new Settings { }); e.Tick(Now); e.Tick(Now.AddSeconds(5)); e.Tick(Now.AddSeconds(10)); Check(f.Trims == 0, "No automatic trim at normal memory load");
                f = new FakeSystem(); f.Add("chrome", 1); f.Snapshot.Complete = false; e = New(f, new Settings { }); e.Clean(Now, true); Check(f.Trims == 0, "Incomplete snapshots fail closed");
                f = new FakeSystem(); f.Add("chrome", 1); var race = f; f.OnCapture = delegate { if (race.Captures == 2) race.Add("valorant-win64-shipping", 5); }; e = New(f, new Settings { }); e.Clean(Now, true);
                Check(f.Trims == 0, "Game starting between cleanup snapshots blocks trimming");
                f = new FakeSystem(); browser = f.Add("chrome", 1); var game = f.Add("cs2", 2); e = New(f, new Settings { BoostGame = true }); e.Tick(Now);
                Check(f.Priorities[browser.Key] == (int)ProcessPriorityClass.BelowNormal && f.Priorities[game.Key] == (int)ProcessPriorityClass.AboveNormal, "Game session priority policy");
                f.Snapshot.ForegroundName = "chrome"; e.Tick(Now.AddSeconds(5)); Check(f.Priorities[browser.Key] == (int)ProcessPriorityClass.Normal, "Alt-tab restores foreground browser priority");
                e.Settings.Enabled = false; e.Tick(Now.AddSeconds(10)); Check(f.Priorities[game.Key] == (int)ProcessPriorityClass.Normal, "Pause restores game priority");
                f = new FakeSystem(); game = f.Add("valorant-win64-shipping", 1); e = New(f, new Settings { BoostGame = true }); e.Tick(Now); Check(f.Changes == 0, "Valorant priority never changed");
                f = new FakeSystem(); browser = f.Add("chrome", 1); f.Add("cs2", 2); f.Priorities[browser.Key] = (int)ProcessPriorityClass.High; e = New(f, new Settings { }); e.Tick(Now); Check(f.Priorities[browser.Key] == (int)ProcessPriorityClass.High, "Preserve existing user priority");
                f = new FakeSystem(); browser = f.Add("chrome", 1); f.Add("cs2", 2); var journal = new RecoveryState(); e = new Engine(f, new Settings { }, journal); e.Tick(Now);
                f.Priorities[browser.Key] = (int)ProcessPriorityClass.High; e.RestoreAll(); Check(f.Priorities[browser.Key] == (int)ProcessPriorityClass.High, "Restore does not overwrite later manual priority");
                f = new FakeSystem(); browser = f.Add("chrome", 1); f.Add("cs2", 2); journal = new RecoveryState(); e = new Engine(f, new Settings { }, journal); e.Tick(Now);
                browser.Started += 999; e.RestoreAll(); Check(journal.Priorities.Count == 0 && f.Changes == 1, "PID reuse never changes replacement process");
                f = new FakeSystem(); browser = f.Add("chrome", 1); f.Add("cs2", 2); journal = new RecoveryState(); e = new Engine(f, new Settings { }, journal); e.Tick(Now); f.Deny = true; e.RestoreAll(); Check(journal.Priorities.Count == 1, "Transient restore failure retains journal");
                f.Deny = false; e.RestoreAll(); Check(journal.Priorities.Count == 0 && f.Priorities[browser.Key] == (int)ProcessPriorityClass.Normal, "Restore retries after access recovers");
                f = new FakeSystem(); f.Add("cs2", 1); e = New(f, new Settings { }); e.Tick(Now); Check(f.Power == "original-custom-power", "Default preserves custom power plan");
                e.Tick(Now.AddSeconds(5)); e.RestoreAll(); Check(f.Power == "original-custom-power", "No power changes during game or restore");
                f = new FakeSystem(); f.Add("cs2", 1); e = New(f, new Settings { }); e.Tick(Now); f.Power = "user-manual-plan"; e.RestoreAll(); Check(f.Power == "user-manual-plan", "Preserve manual power change");
                f = new FakeSystem(); f.Add("steamwebhelper",1); e = New(f,new Settings { }); e.Clean(Now,true); Check(f.Trims==1,"Expanded Steam cleanup target");
                f = new FakeSystem(); f.Add("steamwebhelper",1); e = New(f,new Settings{CleanSteam=false}); e.Clean(Now,true); Check(f.Trims==0,"Steam cleanup opt-out");
                f = new FakeSystem(); f.Add("MyApp",1); e = New(f,new Settings{CleanList="MyApp.exe"}); e.Clean(Now,true); Check(f.Trims==1,"Custom cleanup allowlist");
                f = new FakeSystem(); f.Add("MyApp",1); e = New(f,new Settings{CleanList="MyApp",ProtectedList="myapp"}); e.Clean(Now,true); Check(f.Trims==0,"Custom protection overrides allowlist");
                f = new FakeSystem(); f.Add("oopz",1); f.Add("MsMpEng",2); f.Add("audiodg",3); f.Add("vgc",4); f.Add("obs64",5); e = New(f,new Settings{CleanList="oopz,MsMpEng,audiodg,vgc,obs64"}); e.Clean(Now,true); Check(f.Trims==0,"Built-in protection cannot be overridden by allowlist");
                f = new FakeSystem(); browser=f.Add("chrome",1); browser.UserProcess=false; e=New(f,new Settings { }); e.Clean(Now,true); Check(f.Trims==0,"System and other-session processes excluded");
                f = new FakeSystem(); browser=f.Add("chrome",1); f.Snapshot.VisibleNames.Add("chrome"); e=New(f,new Settings{ManualIncludeVisible=true}); e.Clean(Now,true); Check(f.Trims==1,"Explicit manual visible-window mode");
                f = new FakeSystem(); browser=f.Add("chrome",1); f.Snapshot.VisibleNames.Add("chrome"); e=New(f,new Settings{ManualIncludeVisible=true}); e.Tick(Now);e.Tick(Now.AddSeconds(5));e.Tick(Now.AddSeconds(10));Check(f.Trims==0,"Manual visible mode never weakens automatic protection");
                f = new FakeSystem(); browser=f.Add("chrome",1);f.Snapshot.ForegroundName="chrome";e=New(f,new Settings{ManualIncludeVisible=true});e.Clean(Now,true);Check(f.Trims==0,"Manual expanded mode still protects foreground");
                f = new FakeSystem();browser=f.Add("chrome",1);browser.WorkingSet=40L*1048576;e=New(f,new Settings{MinWorkingSetMB=64});e.Clean(Now,true);Check(f.Trims==0,"Custom minimum working set enforced");e.Settings.MinWorkingSetMB=32;e.Clean(Now,true);Check(f.Trims==1,"Smaller custom minimum includes target");
                f = new FakeSystem();browser=f.Add("chrome",1);var other=f.Add("chrome",2);e=New(f,new Settings { });e.Clean(Now,true,other.Key);Check(f.Trims==1,"Selected cleanup affects only requested PID identity");
                f = new FakeSystem();browser=f.Add("MyApp",1);f.Add("cs2",2);e=New(f,new Settings{BackgroundList="MyApp"});e.Tick(Now);Check(f.Priorities[browser.Key]==(int)ProcessPriorityClass.BelowNormal,"Custom background priority list");e.Settings.ProtectedList="MyApp";e.Tick(Now.AddSeconds(5));Check(f.Priorities[browser.Key]==(int)ProcessPriorityClass.Normal,"Adding protection restores prior adjustment");
                f = new FakeSystem();game=f.Add("cs2",1);e=New(f,new Settings{BoostGame=true,ProtectedList="cs2"});e.Tick(Now);Check(f.Changes==0,"Custom protection overrides optional game boost");
                f = new FakeSystem();browser=f.Add("MyApp",1);e=New(f,new Settings { });e.CloseSelected(browser.Key);Check(f.CloseRequests==1,"User-selected graceful close request");f.Add("cs2",2);e.CloseSelected(browser.Key);Check(f.CloseRequests==1,"Graceful close blocked during games");
                f = new FakeSystem();browser=f.Add("oopz",1);e=New(f,new Settings { });e.CloseSelected(browser.Key);Check(f.CloseRequests==0,"Voice app cannot be closed by manager");
                f = new FakeSystem();browser=f.Add("chrome",1);browser.UserProcess=false;e=New(f,new Settings { });e.CloseSelected(browser.Key);Check(f.CloseRequests==0,"Other-session close blocked");
                f = new FakeSystem();browser=f.Add("chrome",1);string oldKey=browser.Key;browser.Started++;e=New(f,new Settings { });e.CloseSelected(oldKey);Check(f.CloseRequests==0,"Stale close selection rejected");
                var bounds=new Settings{CheckSeconds=0,MinWorkingSetMB=-1,MaxCleanProcesses=999,CleanIntervalMinutes=0,IdleSeconds=0,MemoryThreshold=1};bounds.Normalize();Check(bounds.CheckSeconds==3&&bounds.MinWorkingSetMB==16&&bounds.MaxCleanProcesses==60&&bounds.CleanIntervalMinutes==2&&bounds.IdleSeconds==15&&bounds.MemoryThreshold==60,"Custom settings normalized to supported bounds");
                using(var xml=new StringReader("<Settings><GamePower>true</GamePower><MemoryThreshold>85</MemoryThreshold><BoostGame>true</BoostGame></Settings>")){
                    var migrated=(Settings)new System.Xml.Serialization.XmlSerializer(typeof(Settings)).Deserialize(xml);
                    Check(migrated.BoostGame&&migrated.MaxCleanProcesses==12&&migrated.CleanSteam,"Legacy settings migrate and discard power option");
                }
                f = new FakeSystem(); f.Add("chrome", 1); f.Add("cs2", 2); f.SaveFails = true; e = New(f, new Settings { }); bool failed = false; try { e.Tick(Now); } catch (IOException) { failed = true; } Check(failed && f.Changes == 0, "No system mutation before durable recovery record");
                f = new FakeSystem(); browser = f.Add("chrome", 1); f.Add("cs2", 2); journal = new RecoveryState(); e = new Engine(f, new Settings { }, journal); e.Tick(Now); new Engine(f, new Settings { }, journal).RestoreAll(); Check(f.Priorities[browser.Key] == (int)ProcessPriorityClass.Normal, "Recovery after simulated restart");
                string oldRoot = Store.Root; string testRoot = Path.Combine(Path.GetTempPath(), "FpsGuard-test-" + Guid.NewGuid().ToString("N"));
                try { Store.Root = testRoot; Store.Save("settings.xml", new Settings { MemoryThreshold = 88 }); Store.Save("settings.xml", new Settings { MemoryThreshold = 89 }); Check(Store.Read<Settings>("settings.xml").MemoryThreshold == 89, "Atomic XML settings replacement roundtrip"); }
                finally { Store.Root = oldRoot; if (File.Exists(Path.Combine(testRoot, "settings.xml"))) File.Delete(Path.Combine(testRoot, "settings.xml")); if (Directory.Exists(testRoot)) Directory.Delete(testRoot); }
                var real = new WindowsSystem(); var snapshot = real.Capture(); Check(snapshot.Complete && snapshot.TotalMemory > 0 && snapshot.Processes.Count > 0, "Native read-only hardware/process integration");
                using (var current = Process.GetCurrentProcess()) {
                    var captured = snapshot.Processes.Find(p => p.Id == current.Id);
                    Check(captured != null && captured.WorkingSet > 0 && (ulong)captured.WorkingSet <= snapshot.TotalMemory, "Native memory counters return valid current-process working set");
                }
                Check(!String.IsNullOrEmpty(real.GetPower()), "Native power-plan read");
                // Exercise native priority and working-set APIs on this disposable test process only.
                using (var self = Process.GetCurrentProcess()) {
                    var info = new ProcessInfo { Id = self.Id, Started = self.StartTime.ToUniversalTime().ToFileTimeUtc(), Name = self.ProcessName, WorkingSet = self.WorkingSet64 };
                    int? original = real.ReadPriority(info);
                    Check(original.HasValue, "Native priority read with creation-time identity");
                    try { Check(real.SetPriority(info, (int)ProcessPriorityClass.BelowNormal) && real.ReadPriority(info) == (int)ProcessPriorityClass.BelowNormal, "Native priority change on disposable test process"); }
                    finally { if (original.HasValue && !real.SetPriority(info, original.Value)) throw new Exception("Test-process priority restore failed"); }
                    Check(real.Trim(info) >= 0, "Native working-set trim on disposable test process");
                    info.Started++; Check(!real.SetPriority(info, (int)ProcessPriorityClass.High), "Native process identity mismatch rejected");
                }
                Console.WriteLine("ALL " + passed + " CHECKS PASSED"); return 0;
            } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
