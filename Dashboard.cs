using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FpsGuard {
    sealed partial class Dashboard : Form {
        static readonly Color Bg = Color.FromArgb(17,21,27), Card = Color.FromArgb(28,34,42), Muted = Color.FromArgb(163,175,189), Accent = Color.FromArgb(179,239,107);
        readonly TrayApp app;
        readonly Engine engine;
        readonly Label status, stats, result, details;
        readonly ListView processes;
        readonly TabControl tabs;
        readonly SoftButton toggle;
        readonly System.Collections.Generic.List<Action> bindings = new System.Collections.Generic.List<Action>();
        bool updating;
        public bool AllowClose;
        public bool ProcessPageVisible { get { return tabs.SelectedIndex == 3; } }
        public Dashboard(TrayApp app, Engine engine, Icon icon) {
            this.app = app; this.engine = engine;
            Text = "FPS 轻守 3.0"; Icon = icon; BackColor = Bg; ForeColor = Color.White;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI",9F); ClientSize = new Size(850,740);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Label(this,"FPS 轻守",24,16,330,43,24,Color.White);
            status = Label(this,"检测中",490,25,335,29,11,Accent); status.TextAlign = ContentAlignment.MiddleRight;
            Label(this,"3.0  ·  把资源留给游戏",27,65,790,25,10,Muted);
            stats = Label(this,"读取内存…",25,106,800,55,16,Accent); stats.BackColor = Card; stats.Padding = new Padding(12);
            tabs = new TabControl { Bounds = new Rectangle(25,180,800,450), Padding = new Point(18,7) }; Controls.Add(tabs);
            var home = Page("守护"); var tuning = Page("清理参数"); var lists = Page("自定义名单"); var task = Page("进程管理"); var tools = Page("工具");
            var stability = Page("游戏识别"); var analysis = Page("帧时间分析");
            Label(home,"游戏守护",18,17,735,29,15,Accent);
            details = Label(home,"检测中",18,59,746,78,10,Color.White);
            Bool(home,"内存紧张且闲置时自动清理",18,148,()=>engine.Settings.AutoClean,v=>engine.Settings.AutoClean=v);
            Bool(home,"游戏时降低后台应用优先级",18,186,()=>engine.Settings.LowerBackground,v=>engine.Settings.LowerBackground=v);
            Bool(home,"CS2 / SCP:SL 使用高于正常优先级",18,224,()=>engine.Settings.BoostGame,v=>engine.Settings.BoostGame=v);
            Bool(home,"游戏开始停止壁纸，结束后播放（可选，结束后统一恢复播放）",18,262,()=>engine.Settings.StopWallpaperInGame,v=>engine.Settings.StopWallpaperInGame=v);
            var auto = new SlideSwitch { Text="登录 Windows 后自动启动", Bounds=new Rectangle(18,300,740,30), Checked=Startup.Enabled, ForeColor=Color.White }; home.Controls.Add(auto);
            auto.CheckedChanged += delegate { if (updating) return; try { Startup.Set(auto.Checked); ShowResult("登录自启设置已更新。"); } catch(Exception ex) { ShowResult(ex.Message); updating=true; auto.Checked=Startup.Enabled; updating=false; } };
            Label(home,"电源计划始终由你管理；游戏退出、暂停或关闭工具时恢复调整的优先级。\n自动壁纸控制默认关闭；启用后，即使之前手动暂停，游戏结束也会播放。",18,350,742,53,9,Muted);
            Number(tuning,"自动清理触发占用（%）",18,18,60,95,()=>engine.Settings.MemoryThreshold,v=>engine.Settings.MemoryThreshold=v);
            Number(tuning,"自动清理间隔（分钟）",403,18,2,60,()=>engine.Settings.CleanIntervalMinutes,v=>engine.Settings.CleanIntervalMinutes=v);
            Number(tuning,"最少无操作时间（秒）",18,66,15,600,()=>engine.Settings.IdleSeconds,v=>engine.Settings.IdleSeconds=v);
            Number(tuning,"每轮最多清理进程数",403,66,1,60,()=>engine.Settings.MaxCleanProcesses,v=>engine.Settings.MaxCleanProcesses=v);
            Number(tuning,"目标最低工作集（MB）",18,114,16,1024,()=>engine.Settings.MinWorkingSetMB,v=>engine.Settings.MinWorkingSetMB=v);
            Number(tuning,"后台检测间隔（秒）",403,114,3,30,()=>engine.Settings.CheckSeconds,v=>engine.Settings.CheckSeconds=v);
            Bool(tuning,"将 Steam 网页进程 steamwebhelper 加入清理范围",18,170,()=>engine.Settings.CleanSteam,v=>engine.Settings.CleanSteam=v);
            Bool(tuning,"手动清理也包含未最小化的目标窗口（仍保护前台与游戏）",18,208,()=>engine.Settings.ManualIncludeVisible,v=>engine.Settings.ManualIncludeVisible=v);
            Label(tuning,"自动清理需连续 3 次检测超过阈值；游戏结束后等待 2 分钟。\n手动清理间隔至少 30 秒。工作集收缩后，应用再次使用时可能重新加载页面。",18,252,742,59,10,Muted);
            Button(tuning,"恢复推荐清理参数",18,327,210,38,delegate {
                engine.Settings.MemoryThreshold=80; engine.Settings.CleanIntervalMinutes=5; engine.Settings.IdleSeconds=45;
                engine.Settings.MaxCleanProcesses=12; engine.Settings.MinWorkingSetMB=64; engine.Settings.CheckSeconds=5;
                engine.Settings.CleanSteam=true; engine.Settings.ManualIncludeVisible=false; Save();
                RefreshBindings(); ShowResult("已恢复推荐清理参数。");
            });
            Entry(lists,"额外游戏（识别并禁止清理）",18,()=>engine.Settings.ExtraGames,v=>engine.Settings.ExtraGames=v);
            Entry(lists,"额外清理程序（仅清理名单中的程序）",100,()=>engine.Settings.CleanList,v=>engine.Settings.CleanList=v);
            Entry(lists,"游戏时降低优先级的额外程序",182,()=>engine.Settings.BackgroundList,v=>engine.Settings.BackgroundList=v);
            Entry(lists,"自定义保护名单（高于清理和降优先级名单）",264,()=>engine.Settings.ProtectedList,v=>engine.Settings.ProtectedList=v);
            Label(lists,"填进程名，逗号分隔，可带 .exe。系统、游戏、语音、录屏及反作弊进程内置保护。\n也可在「进程管理」选中程序，再加入名单。",18,350,748,48,9,Muted);
            processes = new ListView { Bounds=new Rectangle(18,45,748,263),View=View.Details,FullRowSelect=true,MultiSelect=false,HideSelection=false,BackColor=Bg,ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle };
            processes.Columns.Add("程序",220); processes.Columns.Add("PID",75); processes.Columns.Add("工作集 MB",110); processes.Columns.Add("状态",300); task.Controls.Add(processes);
            Label(task,"当前用户的应用按占用排序；受限进程不操作。名单按进程名匹配。",18,12,748,25,9,Muted);
            Button(task,"加入清理名单",18,322,140,35,delegate { AddSelected(0); });
            Button(task,"加入后台名单",170,322,140,35,delegate { AddSelected(1); });
            Button(task,"加入保护名单",322,322,140,35,delegate { AddSelected(2); });
            Button(task,"清理所选进程",474,322,140,35,delegate {
                var p=Selected(); if(p==null)return;
                ShowResult(engine.Clean(DateTime.UtcNow,true,p.Key)); UpdateStatus();
            });
            Button(task,"正常关闭所选程序",18,366,185,35,delegate { var p=Selected(); if(p!=null)ShowResult(engine.CloseSelected(p.Key)); });
            Label(task,"关闭前请保存内容；发送普通关窗请求，程序可能缩到托盘。",220,374,535,25,9,Muted);
            Label(tools,"对局前控制",18,16,730,28,14,Accent);
            Button(tools,"停止动态壁纸",18,60,190,38,delegate { Wallpaper("stop"); });
            Button(tools,"恢复动态壁纸",225,60,190,38,delegate { Wallpaper("play"); });
            Label(tools,"调用 Wallpaper Engine 官方控制命令，作用于所有壁纸。\n手动停止后可点恢复；启用自动控制时，结束对局或退出工具会播放壁纸。",18,112,746,55,10,Muted);
            Button(tools,"Windows 游戏模式",18,188,190,38,delegate { Launch("ms-settings:gaming-gamemode"); });
            Button(tools,"后台录制设置",225,188,190,38,delegate { Launch("ms-settings:gaming-gamedvr"); });
            Button(tools,"开机启动应用",432,188,190,38,delegate { Launch("ms-settings:startupapps"); });
            Button(tools,"任务管理器",18,241,190,38,delegate { Launch("taskmgr.exe"); });
            Button(tools,"查看活动日志",225,241,190,38,delegate { Launch(Path.Combine(Store.Root,"activity.log")); });
            Button(tools,"打开设置文件夹",432,241,190,38,delegate { Launch(Store.Root); });
            Label(tools,"更多清理通过扩大白名单和调整参数实现；游戏期间仍禁止内存清理。\n程序实际退出通常能释放更多内存，工作集收缩并不保证提升 FPS。\n完全本地运行，无常驻服务。",18,310,746,82,10,Muted);
            Label(stability,"减少后台干扰，保留游戏正在使用的数据",18,16,748,29,14,Accent);
            Bool(stability,"游戏时为后台编辑器与云同步让出 CPU（Code / OneDrive）",18,55,()=>engine.Settings.LowerEditors,v=>engine.Settings.LowerEditors=v);
            Bool(stability,"自动识别 Steam 已安装目录中的游戏程序（启动器与常见工具排除）",18,94,()=>engine.Settings.DetectSteamGames,v=>engine.Settings.DetectSteamGames=v);
            var manual=Button(stability,engine.ManualSession?"结束手动游戏守护":"手动开启游戏守护",18,140,220,36,delegate{});
            manual.Click+=delegate {engine.ManualSession=!engine.ManualSession;manual.Text=engine.ManualSession?"结束手动游戏守护":"手动开启游戏守护";if(app!=null)app.RequestRefresh();ShowResult(engine.ManualSession?"手动守护开启：禁止内存清理，后台名单让出 CPU。":"手动守护已结束；仍检测到游戏则继续自动守护。");};
            Label(stability,"其他平台、模拟器或未识别游戏均可手动守护。结束后记得关闭。",253,146,510,38,9,Muted);
            Label(stability,"额外游戏安装目录（每行一个具体游戏文件夹）",18,193,746,24,10,Color.White);
            var folders=new TextBox {Bounds=new Rectangle(18,223,595,67),Multiline=true,ScrollBars=ScrollBars.Vertical,Text=engine.Settings.GameFolders,BackColor=Bg,ForeColor=Color.White};stability.Controls.Add(folders);
            Button(stability,"选择目录",630,222,135,32,delegate {
                using(var pick=new FolderBrowserDialog {Description="选择具体游戏安装文件夹，避免选择整个磁盘或公共程序目录"})if(pick.ShowDialog()==DialogResult.OK){folders.Text=(folders.Text+Environment.NewLine+pick.SelectedPath).Trim();engine.Settings.GameFolders=folders.Text;Save();ShowResult("游戏目录已保存。");}
            });
            Button(stability,"保存目录",630,260,135,32,delegate {engine.Settings.GameFolders=folders.Text;Save();ShowResult("游戏目录已保存。");});
            Label(stability,"支持内置常见游戏、Steam 目录、额外进程名和自定义目录。目录识别是启发式规则，\n可在守护页确认识别结果；访问受限时，用额外游戏进程名补充。\n统一使用后台降优先级和可选壁纸控制；实际 1% low 需游戏内对比。",18,312,746,83,9,Muted);
            Label(analysis,"用实测帧时间比较优化前后",18,16,746,27,14,Accent);
            Label(analysis,"导入单一游戏进程、单一交换链的毫秒帧时间 CSV。\n支持 MsBetweenPresents / FrameTime / FrameTimeMs；不在后台采集或注入游戏。",18,56,746,49,9,Muted);
            var output=new TextBox {Bounds=new Rectangle(18,157,748,153),Multiline=true,ReadOnly=true,BackColor=Bg,ForeColor=Color.White,ScrollBars=ScrollBars.Vertical,Text="尚未导入实测数据；不显示推测的 1% low。"};analysis.Controls.Add(output);
            Button(analysis,"导入帧时间 CSV",18,110,210,35,delegate {
                using(var picker=new OpenFileDialog {Filter="帧时间 CSV|*.csv|所有文件|*.*"})if(picker.ShowDialog()==DialogResult.OK){output.Text=FrameAnalysis.Load(picker.FileName).ToString();ShowResult("分析完成；请使用相同游戏场景、采样区间与画质做对比。");}
            });
            Label(analysis,"目标帧率",18,330,110,25,10,Color.White);
            var target=new NumericUpDown {AutoScaleMode=AutoScaleMode.None,Bounds=new Rectangle(133,327,85,29),Minimum=30,Maximum=1000,Value=180,BackColor=Bg,ForeColor=Color.White};analysis.Controls.Add(target);
            var budget=Label(analysis,"每帧预算 5.56 ms",238,330,480,25,10,Accent);
            target.ValueChanged+=delegate {budget.Text="每帧预算 "+(1000.0/(double)target.Value).ToString("0.00")+" ms（仅计算，不修改游戏设置）";};
            Label(analysis,"激战时掉帧：试用游戏内限帧，让上限接近激战可持续帧率，再比较慢帧。\n不同游戏应分别测量；降低画质前先区分 CPU 与 GPU 限制。",18,369,748,43,9,Muted);
            Button(this,"清理后台内存",25,650,215,40,delegate { app.Clean(); },true);
            toggle=Button(this,"暂停守护",258,650,150,40,delegate { app.SetEnabled(!engine.Settings.Enabled); });
            Button(this,"收起到托盘",650,650,175,40,delegate { Hide(); });
            result=Label(this,"设置自动保存；关闭窗口后继续驻留。",25,704,800,30,9,Muted);
            tabs.SelectedIndexChanged += delegate { if(tabs.SelectedIndex==3) {if(app!=null)app.RequestRefresh();FillProcesses();} };
            Redesign();
            InitializeDpiLayout();
            FormClosing += delegate(object sender,FormClosingEventArgs e) { if(!AllowClose && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();} };
        }
        void Save() { if(app!=null)app.Save(); }
        void RefreshBindings() { updating=true; try { foreach(var bind in bindings)bind(); } finally { updating=false; } }
        void Entry(Control parent,string title,int y,Func<string> read,Action<string> write) {
            Label(parent,title,18,y,740,23,10,Color.White);
            var input=new TextBox { Bounds=new Rectangle(18,y+29,595,29),Text=read(),BackColor=Bg,ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle }; parent.Controls.Add(input);
            bindings.Add(delegate { input.Text=read(); });
            Button(parent,"保存",630,y+27,135,33,delegate {
                string names=String.Join(",",Policy.ExtraNames(input.Text)); write(names); input.Text=names; Save(); ShowResult("名单已保存。");
            });
        }
        void Number(Control parent,string title,int x,int y,int min,int max,Func<int> read,Action<int> write) {
            Label(parent,title,x,y+3,245,26,9,Color.White);
            var value=new NumericUpDown {AutoScaleMode=AutoScaleMode.None, Bounds=new Rectangle(x+253,y,100,29),Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,read())),BackColor=Bg,ForeColor=Color.White }; parent.Controls.Add(value);
            bindings.Add(delegate { value.Value=read(); });
            value.ValueChanged += delegate {if(updating)return;write((int)value.Value);Save();};
        }
        void Bool(Control parent,string title,int x,int y,Func<bool> read,Action<bool> write) {
            var box=new SlideSwitch { Text=title,Bounds=new Rectangle(x,y,750,30),Checked=read(),ForeColor=Color.White }; parent.Controls.Add(box);
            bindings.Add(delegate { box.Checked=read(); });
            box.CheckedChanged += delegate {if(updating)return;write(box.Checked);Save();};
        }
        ProcessInfo Selected() {
            if(processes.SelectedItems.Count==0){ShowResult("先选中一个进程。");return null;}
            return (ProcessInfo)processes.SelectedItems[0].Tag;
        }
        void AddSelected(int list) {
            var p=Selected(); if(p==null)return;
            if(list!=2 && Policy.IsProtected(p,engine.Settings)){ShowResult("该进程受保护，不能加入操作名单。");return;}
            string old=list==0?engine.Settings.CleanList:list==1?engine.Settings.BackgroundList:engine.Settings.ProtectedList;
            string value=String.Join(",",Policy.ExtraNames(old+","+p.Name));
            if(list==0)engine.Settings.CleanList=value; else if(list==1)engine.Settings.BackgroundList=value; else engine.Settings.ProtectedList=value;
            Save(); RefreshBindings(); ShowResult(p.Name+" 已加入名单。"); FillProcesses();
        }
        void FillProcesses() {
            string selected=processes.SelectedItems.Count>0?((ProcessInfo)processes.SelectedItems[0].Tag).Key:null;
            processes.BeginUpdate(); processes.Items.Clear();
            foreach(var p in engine.Latest.Processes.Where(p=>p.UserProcess && p.Started>0).OrderByDescending(p=>p.WorkingSet)) {
                string state=Policy.IsProtected(p,engine.Settings)?"游戏 / 受保护":Policy.IsCleanTarget(p.Name,engine.Settings)?"清理名单":"未加入清理名单";
                if(p.Name.Equals(engine.Latest.ForegroundName,StringComparison.OrdinalIgnoreCase))state+=" / 前台";
                else if(engine.Latest.VisibleNames.Contains(p.Name))state+=" / 窗口可见";
                var row=new ListViewItem(new[]{p.Name,p.Id.ToString(),p.WorkingSet>0?(p.WorkingSet/1048576.0).ToString("0"):"—",state}){Tag=p};
                processes.Items.Add(row); if(p.Key==selected)row.Selected=true;
            }
            processes.EndUpdate();
        }
        void Wallpaper(string command) {
            try {
                foreach(string name in new[]{"wallpaper64","wallpaper32"}) {
                    var running=Process.GetProcessesByName(name);
                    try {
                        foreach(var process in running) {
                            if(process.SessionId!=Process.GetCurrentProcess().SessionId)continue;
                            string exe=process.MainModule.FileName;
                            using(var sent=Process.Start(new ProcessStartInfo(exe,"-control "+command){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) { }
                            Store.Log("壁纸控制命令已发送："+command); ShowResult("已发送壁纸"+(command=="stop"?"停止":"恢复")+"命令；以实际播放状态为准。"); return;
                        }
                    } finally {foreach(var process in running)process.Dispose();}
                }
                ShowResult("未找到正在运行的 Wallpaper Engine。");
            } catch(Exception ex){ShowResult("壁纸控制失败："+ex.Message);}
        }
        public void UpdateStatus() {
            var s=engine.Latest;
            using(var self=Process.GetCurrentProcess())stats.Text=String.Format("内存 {0}%     可用 {1:0.0} GB     轻守自身 {2:0} MB",s.MemoryLoad,s.AvailableMemory/1073741824.0,self.WorkingSet64/1048576.0);
            status.Text=!engine.Settings.Enabled?"● 已暂停":!s.Complete?"● 检测不可用":engine.Games.Length>0?"● 游戏守护中":"● 待机守护中";
            details.Text=(engine.Games.Length>0?engine.Games:"未检测到游戏")+"\n"+"已调整进程："+engine.ManagedCount+" · 检测间隔 "+engine.CheckInterval+" 秒 · 电源方案保持原样\n"+engine.LastClean;
            UpdateModernMetrics();
            toggle.Text=engine.Settings.Enabled?"暂停守护":"恢复守护";
            if(tabs.SelectedIndex==3)FillProcesses();
        }
        public void ShowResult(string text){result.Text=text;}
        TabPage Page(string title){var page=new TabPage(title){BackColor=Card,ForeColor=Color.White};tabs.TabPages.Add(page);return page;}
        static Label Label(Control parent,string text,int x,int y,int w,int h,float size,Color color){var label=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font("Microsoft YaHei UI",size),ForeColor=color,BackColor=Color.Transparent};parent.Controls.Add(label);return label;}
        static SoftButton Button(Control parent,string title,int x,int y,int w,int h,Action action,bool primary=false){var button=new SoftButton{Text=title,Bounds=new Rectangle(x,y,w,h),BackColor=primary?Accent:Color.FromArgb(42,51,62),ForeColor=primary?Bg:Color.White,Cursor=Cursors.Hand};button.Click+=delegate{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"FPS 轻守");}};parent.Controls.Add(button);return button;}
        void Launch(string target){try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}catch(Exception ex){ShowResult(ex.Message);}}
    }
}
