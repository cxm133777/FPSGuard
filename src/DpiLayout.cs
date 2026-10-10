using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FpsGuard {
    // All layout coordinates are captured once at 96 DPI. Never scale the previous result.
    sealed partial class Dashboard {
        sealed class LayoutEntry {
            public Control Control; public Rectangle Bounds; public string Family;
            public float Points; public FontStyle Style; public Padding Padding;
            public int[] Columns;
        }
        readonly List<LayoutEntry> dpiLayout=new List<LayoutEntry>();
        List<Font> dpiFonts=new List<Font>();
        readonly Dictionary<string,Font> dpiFontCache=new Dictionary<string,Font>();
        int layoutDpi=96;
        bool applyingDpi;
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [StructLayout(LayoutKind.Sequential)] struct DpiRect {public int Left,Top,Right,Bottom;}
        public int LayoutDpi {get{return layoutDpi;}}
        static int Scaled(int value,int dpi){return (int)Math.Round(value*dpi/96.0,MidpointRounding.AwayFromZero);}
        void CaptureLayout(Control parent){
            foreach(Control c in parent.Controls){
                if(c is TabControl)continue;
                var container=c as ContainerControl;if(container!=null)container.AutoScaleMode=AutoScaleMode.None;
                var entry=new LayoutEntry {Control=c,Bounds=c.Bounds,Family=c.Font.FontFamily.Name,Points=c.Font.SizeInPoints,Style=c.Font.Style,Padding=c.Padding};
                var list=c as ListView;if(list!=null)entry.Columns=list.Columns.Cast<ColumnHeader>().Select(x=>x.Width).ToArray();
                dpiLayout.Add(entry);if(!(c is NumericUpDown)&&!(c is TextBox)&&!(c is ListView))CaptureLayout(c);
            }
        }
        void InitializeDpiLayout(){
            CaptureLayout(this);
            int dpi=96;try{dpi=(int)GetDpiForWindow(Handle);}catch{using(var g=CreateGraphics())dpi=(int)g.DpiX;}
            ApplyDpiLayout(dpi,Screen.FromHandle(Handle).WorkingArea);
            FormClosed+=delegate{foreach(var font in dpiFonts)font.Dispose();};
        }
        public void PreviewDpi(int dpi,Rectangle workArea){ApplyDpiLayout(dpi,workArea);}
        public void PreviewDpiMessage(int dpi){
            var rect=new DpiRect {Left=100,Top=60,Right=100+Scaled(1050,dpi),Bottom=60+Scaled(780,dpi)};
            IntPtr ptr=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DpiRect)));
            try{Marshal.StructureToPtr(rect,ptr,false);var message=Message.Create(Handle,0x02E0,new IntPtr(dpi|(dpi<<16)),ptr);WndProc(ref message);if(layoutDpi!=dpi)throw new InvalidOperationException("DPI change message ignored");}
            finally{Marshal.FreeHGlobal(ptr);}
        }
        public void PreviewScrollBottom(){
            if(AutoScrollMinSize.Height<=ClientSize.Height)return;
            AutoScrollPosition=new Point(AutoScrollMinSize.Width,AutoScrollMinSize.Height);PerformLayout();
            if(toggle.Top<0||toggle.Bottom>ClientSize.Height)throw new InvalidOperationException("Footer inaccessible on small screen");
            AutoScrollPosition=Point.Empty;
        }
        public void PreviewValidateLayout(){
            foreach(var entry in dpiLayout){
                var c=entry.Control;if(!c.Visible)continue;
                var expected=new Point(Scaled(entry.Bounds.X,layoutDpi),Scaled(entry.Bounds.Y,layoutDpi));
                if(c.Parent==this)expected.Offset(AutoScrollPosition);
                if(c.Location!=expected)throw new InvalidOperationException("DPI position mismatch: "+c.GetType().Name+" / "+c.Text+" / "+c.Location+" expected "+expected);
                if(c is Label && c.Font.Height>c.Height)throw new InvalidOperationException("DPI text clipping: "+c.Text);
            }
        }
        void ApplyDpiLayout(int dpi,Rectangle workArea){
            if(applyingDpi||dpiLayout.Count==0)return;
            dpi=Math.Max(96,Math.Min(384,dpi));applyingDpi=true;
            SuspendLayout();
            foreach(var entry in dpiLayout)entry.Control.SuspendLayout();
            try{
                AutoScrollPosition=Point.Empty;AutoScrollMinSize=Size.Empty;layoutDpi=dpi;
                foreach(var entry in dpiLayout){
                    var c=entry.Control;
                    string fontKey=entry.Family+"|"+entry.Points.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+(int)entry.Style+"|"+dpi;
                    Font font;if(!dpiFontCache.TryGetValue(fontKey,out font)){font=new Font(entry.Family,entry.Points*dpi/72f,entry.Style,GraphicsUnit.Pixel);dpiFonts.Add(font);dpiFontCache[fontKey]=font;}c.Font=font;
                    var b=entry.Bounds;c.Bounds=new Rectangle(Scaled(b.X,dpi),Scaled(b.Y,dpi),Scaled(b.Width,dpi),Scaled(b.Height,dpi));
                    var p=entry.Padding;c.Padding=new Padding(Scaled(p.Left,dpi),Scaled(p.Top,dpi),Scaled(p.Right,dpi),Scaled(p.Bottom,dpi));
                    var list=c as ListView;if(list!=null)for(int i=0;i<entry.Columns.Length;i++)list.Columns[i].Width=Scaled(entry.Columns[i],dpi);
                }
                int width=Scaled(1050,dpi),height=Scaled(780,dpi);
                // Keep the full-size readable layout; add scrollbars on smaller work areas.
                AutoScroll=true;
                ClientSize=new Size(Math.Min(width,Math.Max(320,workArea.Width-24)),Math.Min(height,Math.Max(240,workArea.Height-24)));
                AutoScrollMinSize=new Size(width,height);
            }finally{
                foreach(var entry in dpiLayout)entry.Control.ResumeLayout(false);
                ResumeLayout(true);applyingDpi=false;Invalidate(true);
            }
        }
        protected override void WndProc(ref Message message){
            if(message.Msg==0x02E0 && dpiLayout.Count>0 && !applyingDpi){
                int dpi=(int)(message.WParam.ToInt64()&0xffff);
                var rect=(DpiRect)Marshal.PtrToStructure(message.LParam,typeof(DpiRect));
                var area=Screen.FromRectangle(Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom)).WorkingArea;
                ApplyDpiLayout(dpi,area);Location=new Point(rect.Left,rect.Top);message.Result=IntPtr.Zero;return;
            }
            base.WndProc(ref message);
        }
    }
    static class DrawingScale {
        public static float Of(Control control){var form=control.FindForm() as Dashboard;return form==null?1:form.LayoutDpi/96f;}
    }
}
