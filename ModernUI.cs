using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FpsGuard {
    static class Theme {
        public static bool Dark=SystemDark();
        public static bool SystemDark(){try{using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))return key!=null && Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;}catch{return false;}}
        public static Color Canvas {get{return Dark?Color.FromArgb(24,24,27):Color.FromArgb(246,246,248);}}
        public static Color Paper {get{return Dark?Color.FromArgb(36,36,40):Color.White;}}
        public static Color Ink {get{return Dark?Color.FromArgb(242,242,247):Color.FromArgb(29,29,31);}}
        public static Color Secondary {get{return Dark?Color.FromArgb(174,174,183):Color.FromArgb(113,113,122);}}
        public static Color Blue {get{return Dark?Color.FromArgb(10,132,255):Color.FromArgb(0,113,227);}}
        public static Color Line {get{return Dark?Color.FromArgb(64,64,70):Color.FromArgb(228,228,233);}}
        public static Color Sidebar {get{return Dark?Color.FromArgb(30,30,34):Color.FromArgb(237,238,242);}}
        public static Color Button {get{return Dark?Color.FromArgb(52,52,58):Color.FromArgb(239,240,243);}}
        public static Color SwitchOff {get{return Dark?Color.FromArgb(89,89,97):Color.FromArgb(211,213,219);}}
        public static void PaintParent(Control control,Graphics graphics){using(var brush=new SolidBrush(control.Parent==null?Canvas:control.Parent.BackColor))graphics.FillRectangle(brush,control.ClientRectangle);}
        public static GraphicsPath Round(RectangleF r,float radius) {
            var path=new GraphicsPath();float d=radius*2;
            path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
    }
    sealed class SoftButton : Control {
        readonly Timer motion=new Timer {Interval=15};
        float hover; bool inside,down;
        public bool SelectedStyle;
        public void PerformClick(){if(Enabled)OnClick(EventArgs.Empty);}
        public SoftButton() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            Cursor=Cursors.Hand;TabStop=true;AccessibleRole=AccessibleRole.PushButton;
            motion.Tick+=delegate {float target=inside?1:0;hover+=(target-hover)*0.35f;if(Math.Abs(hover-target)<0.02f){hover=target;motion.Stop();}Invalidate();};
        }
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);inside=true;motion.Start();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);inside=false;down=false;motion.Start();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);down=true;Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);down=false;Invalidate();}
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){e.Handled=true;OnClick(EventArgs.Empty);}}
        protected override void OnPaintBackground(PaintEventArgs e){Theme.PaintParent(this,e.Graphics);}
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=SelectedStyle?Theme.Blue:BackColor;
            int diff=(int)(hover*(fill.GetBrightness()<0.5f?10:-8))-(down?12:0);
            fill=Color.FromArgb(Math.Max(0,Math.Min(255,fill.R+diff)),Math.Max(0,Math.Min(255,fill.G+diff)),Math.Max(0,Math.Min(255,fill.B+diff)));
            using(var shape=Theme.Round(new RectangleF(0.5f,0.5f,Width-1,Height-1),9*DrawingScale.Of(this)))using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?(SelectedStyle?Color.White:ForeColor):Theme.Secondary,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Focused&&ShowFocusCues)using(var pen=new Pen(Color.FromArgb(95,Theme.Blue),1))using(var shape=Theme.Round(new RectangleF(2,2,Width-5,Height-5),7*DrawingScale.Of(this)))e.Graphics.DrawPath(pen,shape);
        }
        protected override void Dispose(bool disposing){if(disposing)motion.Dispose();base.Dispose(disposing);}
    }
    sealed class SlideSwitch : Control {
        readonly Timer motion=new Timer {Interval=15}; float position;
        bool isChecked;
        public event EventHandler CheckedChanged;
        public bool Checked {get{return isChecked;}set{if(isChecked==value)return;isChecked=value;OnCheckedChanged(EventArgs.Empty);}}
        public SlideSwitch(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Theme.Paper;Cursor=Cursors.Hand;TabStop=true;AccessibleRole=AccessibleRole.CheckButton;motion.Tick+=delegate{float target=Checked?1:0;position+=(target-position)*0.4f;if(Math.Abs(position-target)<0.02f){position=target;motion.Stop();}Invalidate();};}
        protected override void OnPaintBackground(PaintEventArgs e){Theme.PaintParent(this,e.Graphics);}
        void OnCheckedChanged(EventArgs e){if(IsHandleCreated&&Visible)motion.Start();else position=Checked?1:0;Invalidate();if(CheckedChanged!=null)CheckedChanged(this,e);}
        protected override void OnClick(EventArgs e){Checked=!Checked;base.OnClick(e);}
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Space){e.Handled=true;Checked=!Checked;}}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(0,0,Math.Max(0,Width-(int)(64*DrawingScale.Of(this))),Height),Theme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            float scale=DrawingScale.Of(this),x=Width-48*scale,y=(Height-24*scale)/2f;
            var off=Theme.SwitchOff;var on=Color.FromArgb(52,199,89);
            Color fill=Color.FromArgb((int)(off.R+(on.R-off.R)*position),(int)(off.G+(on.G-off.G)*position),(int)(off.B+(on.B-off.B)*position));
            using(var shape=Theme.Round(new RectangleF(x,y,44*scale,24*scale),12*scale))using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
            using(var shadow=new SolidBrush(Color.FromArgb(22,0,0,0)))e.Graphics.FillEllipse(shadow,x+(2+20*position)*scale,y+3*scale,21*scale,21*scale);
            using(var thumb=new SolidBrush(Color.White))e.Graphics.FillEllipse(thumb,x+(2+20*position)*scale,y+2*scale,20*scale,20*scale);
            if(Focused&&ShowFocusCues)using(var pen=new Pen(Theme.Blue))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
        }
        protected override void Dispose(bool disposing){if(disposing)motion.Dispose();base.Dispose(disposing);}
    }
    sealed class PaperPanel : Panel {
        public int Corner=16;
        public PaperPanel(){DoubleBuffered=true;BackColor=Theme.Canvas;}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Theme.Round(new RectangleF(0.5f,0.5f,Width-1,Height-1),Corner*DrawingScale.Of(this))){using(var brush=new SolidBrush(Theme.Paper))e.Graphics.FillPath(brush,path);using(var pen=new Pen(Theme.Line,1))e.Graphics.DrawPath(pen,path);}}
    }
    // Paint corner masks over the native tab pages; no binary window-region edges.
    sealed class CardCorner : Control {
        readonly int corner;
        public CardCorner(int index){corner=index;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Size=new Size(18,18);Enabled=false;TabStop=false;}
        protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Theme.Canvas);}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float x=(corner%2==0)?0:Width-Parent.Width,y=(corner<2)?0:Height-Parent.Height;
            using(var path=Theme.Round(new RectangleF(x+0.5f,y+0.5f,Parent.Width-1,Parent.Height-1),16*DrawingScale.Of(this))){
                using(var brush=new SolidBrush(Theme.Paper))e.Graphics.FillPath(brush,path);
                using(var pen=new Pen(Theme.Line))e.Graphics.DrawPath(pen,path);
            }
        }
    }
    sealed class WindowDot : Control {
        public WindowDot(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);Cursor=Cursors.Hand;}
        protected override void OnPaintBackground(PaintEventArgs e){using(var brush=new SolidBrush(Parent.BackColor))e.Graphics.FillRectangle(brush,ClientRectangle);}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var brush=new SolidBrush(BackColor))e.Graphics.FillEllipse(brush,0.5f,0.5f,Width-1,Height-1);}
    }
    sealed partial class Dashboard {
        Label modernTitle,modernSubtitle,sideState;
        readonly Label[] metricValues=new Label[3];
        readonly List<SoftButton> navigation=new List<SoftButton>();
        readonly List<Panel> pageViews=new List<Panel>();
        public TabControl PreviewTabs {get{return tabs;}}
        public int PreviewPageIndex {get{return pageViews.FindIndex(p=>p.Visible);}}
        public void PreviewNavigate(int index){navigation[index].PerformClick();}
        [DllImport("user32.dll")]static extern bool ReleaseCapture();
        [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,IntPtr l);
        [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
        void Redesign() {
            SuspendLayout();DoubleBuffered=true;ClientSize=new Size(1050,780);Text="FPS 轻守";FormBorderStyle=FormBorderStyle.None;BackColor=Theme.Canvas;ForeColor=Theme.Ink;
            ApplyTheme(this);
            processes.OwnerDraw=true;
            processes.DrawColumnHeader+=delegate(object sender,DrawListViewColumnHeaderEventArgs e){using(var brush=new SolidBrush(Theme.Button))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,e.Header.Text,processes.Font,new Rectangle(e.Bounds.X+6,e.Bounds.Y,e.Bounds.Width-6,e.Bounds.Height),Theme.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
            processes.DrawItem+=delegate(object sender,DrawListViewItemEventArgs e){e.DrawDefault=true;};
            processes.DrawSubItem+=delegate(object sender,DrawListViewSubItemEventArgs e){e.DrawDefault=true;};
            foreach(Control control in Controls.Cast<Control>().ToArray()) {
                if(control==tabs)continue;
                control.Left+=210;
                if(control.Top>=590&&control.Top<700)control.Top=680;
                if(control.Top>=700)control.Top=732;
            }
            modernTitle=Controls.OfType<Label>().First(x=>x.Text=="FPS 轻守");modernTitle.Text="概览";modernTitle.SetBounds(235,27,410,43);modernTitle.Font=new Font("Microsoft YaHei UI",23,FontStyle.Bold);
            modernSubtitle=Controls.OfType<Label>().First(x=>x.Text.StartsWith("3.0"));modernSubtitle.Text="把资源留给游戏，把控制留给你。";modernSubtitle.SetBounds(237,78,650,24);
            status.SetBounds(725,39,310,27);status.ForeColor=Theme.Secondary;
            stats.Visible=false;
            string[] captions={"内存使用","可用内存","轻守占用"};
            for(int i=0;i<3;i++){
                var card=new PaperPanel {Bounds=new Rectangle(235+i*271,112,258,84)};Controls.Add(card);
                var caption=Label(card,captions[i],16,11,220,19,9,Theme.Secondary);
                metricValues[i]=Label(card,"—",16,31,220,49,23,Theme.Ink);metricValues[i].Font=new Font("Segoe UI",23,FontStyle.Regular);
            }
            var host=new PaperPanel {Bounds=new Rectangle(235,212,800,450),Corner=16};Controls.Add(host);
            // Keep the tab selection model, but render plain panels instead of native tab surfaces.
            var selectionHandle=tabs.Handle;
            tabs.Visible=false;tabs.TabStop=false;
            var pages=pageViews;
            foreach(TabPage tab in tabs.TabPages){var page=new Panel {Bounds=new Rectangle(0,0,800,450),BackColor=Theme.Paper,Visible=false};foreach(Control child in tab.Controls.Cast<Control>().ToArray()){tab.Controls.Remove(child);page.Controls.Add(child);}host.Controls.Add(page);pages.Add(page);}
            if(tabs.SelectedIndex<0)tabs.SelectedIndex=0;
            pages[tabs.SelectedIndex].Visible=true;
            tabs.SelectedIndexChanged+=delegate{for(int i=0;i<pages.Count;i++)pages[i].Visible=i==tabs.SelectedIndex;host.Invalidate(true);};
            for(int i=0;i<4;i++){var corner=new CardCorner(i);corner.Location=new Point(i%2==0?0:host.Width-18,i<2?0:host.Height-18);host.Controls.Add(corner);corner.BringToFront();}
            var sidebar=new Panel {Bounds=new Rectangle(0,0,210,780),BackColor=Theme.Sidebar};Controls.Add(sidebar);sidebar.SendToBack();
            var brand=Label(sidebar,"轻守",22,66,166,44,23,Theme.Ink);brand.Font=new Font("Microsoft YaHei UI",23,FontStyle.Bold);
            Label(sidebar,"FPS GUARD",24,110,166,23,8,Theme.Secondary);
            string[] titles={"概览","内存管理","自定义名单","应用管理","实用工具","游戏识别","帧时间分析"};
            string[] icons={"◉","▤","≡","▦","◇","⊕","∿"};
            for(int i=0;i<titles.Length;i++){
                int index=i;
                var button=new SoftButton {Bounds=new Rectangle(14,163+i*48,182,40),Text=icons[i]+"    "+titles[i],BackColor=sidebar.BackColor,ForeColor=Theme.Ink,Font=new Font("Microsoft YaHei UI",10)};
                button.Click+=delegate{tabs.SelectedIndex=index;};sidebar.Controls.Add(button);navigation.Add(button);
            }
            sideState=Label(sidebar,"●  后台守护已开启",23,663,173,24,9,Theme.Secondary);
            Label(sidebar,"本地运行 · 3.1.0",23,703,170,23,8,Theme.Secondary);
            Label(sidebar,"关闭窗口后继续驻留",23,728,174,24,8,Theme.Secondary);
            Color[] lights={Color.FromArgb(255,95,87),Color.FromArgb(255,189,46),Color.FromArgb(40,201,64)};
            string[] tips={"关闭窗口并驻留托盘","最小化窗口","收起到托盘"};
            var tooltip=new ToolTip();componentsForTheme=tooltip;
            for(int i=0;i<3;i++){
                int index=i;var light=new WindowDot {Bounds=new Rectangle(23+i*24,24,14,14),BackColor=lights[i],TabStop=false};tooltip.SetToolTip(light,tips[i]);
                light.Click+=delegate{if(index==0)Close();else if(index==1)WindowState=FormWindowState.Minimized;else Hide();};sidebar.Controls.Add(light);
            }
            MouseEventHandler drag=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
            modernTitle.MouseDown+=drag;modernSubtitle.MouseDown+=drag;sidebar.MouseDown+=drag;brand.MouseDown+=drag;MouseDown+=drag;
            tabs.SelectedIndexChanged+=delegate{
                modernTitle.Text=titles[tabs.SelectedIndex];modernSubtitle.Text=Subtitles[tabs.SelectedIndex];UpdateNavigation();
            };
            Shown+=delegate{UpdateNavigation();try{int corners=2;DwmSetWindowAttribute(Handle,33,ref corners,4);int dark=Theme.Dark?1:0;DwmSetWindowAttribute(Handle,20,ref dark,4);}catch{};};
            FormClosed+=delegate{if(componentsForTheme!=null)componentsForTheme.Dispose();};
            UpdateNavigation();ResumeLayout(true);
        }
        ToolTip componentsForTheme;
        static readonly string[] Subtitles={"把资源留给游戏，把控制留给你。","仅在需要时整理后台应用的内存。","每个名单，都由你决定。","找到占用资源的应用，再决定如何处理。","对局开始前，少一些后台干扰。","自动发现游戏，也支持你手动补充。","用真实数据，观察每一帧的表现。"};
        void UpdateNavigation(){for(int i=0;i<navigation.Count;i++){navigation[i].SelectedStyle=i==tabs.SelectedIndex;navigation[i].Invalidate();}}
        void UpdateModernMetrics(){if(metricValues[0]==null)return;var s=engine.Latest;metricValues[0].Text=s.MemoryLoad+"%";metricValues[1].Text=(s.AvailableMemory/1073741824.0).ToString("0.0")+" GB";using(var self=System.Diagnostics.Process.GetCurrentProcess())metricValues[2].Text=(self.WorkingSet64/1048576.0).ToString("0")+" MB";sideState.Text=engine.Settings.Enabled?"●  后台守护已开启":"○  后台守护已暂停";}
        static void ApplyTheme(Control parent){
            foreach(Control control in parent.Controls){
                if(control is Label){control.ForeColor=control.ForeColor==Muted?Theme.Secondary:control.ForeColor==Accent&&control.Font.Size<12?Theme.Blue:Theme.Ink;control.BackColor=parent.BackColor;}
                else if(control is SoftButton){control.BackColor=control.BackColor==Accent?Theme.Blue:Theme.Button;control.ForeColor=control.BackColor==Theme.Blue?Color.White:Theme.Ink;}
                else if(control is TextBox||control is NumericUpDown||control is ListView){control.BackColor=Theme.Paper;control.ForeColor=Theme.Ink;}
                else if(control is TabPage){control.BackColor=Theme.Paper;control.ForeColor=Theme.Ink;((TabPage)control).UseVisualStyleBackColor=false;}
                else if(control is SlideSwitch){control.ForeColor=Theme.Ink;control.BackColor=parent.BackColor;}
                ApplyTheme(control);
            }
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys key){if(key==Keys.Escape){Hide();return true;}if((key&Keys.Control)==Keys.Control){int digit=(int)(key&Keys.KeyCode)-(int)Keys.D1;if(digit>=0&&digit<tabs.TabPages.Count){tabs.SelectedIndex=digit;return true;}}return base.ProcessCmdKey(ref msg,key);}
    }
}
