using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FpsGuard {
    public sealed class FrameResult {
        public int Frames;
        public double AverageFps, Low1Fps, P99Ms, P999Ms;
        public int Above16, Above33;
        public override string ToString() {
            return String.Format(CultureInfo.InvariantCulture,"有效帧数 {0}\r\n平均 FPS  {1:0.0}\r\n1% low  {2:0.0} FPS（最慢 1% 帧时间平均值的倒数）\r\nP99 帧时间  {3:0.00} ms\r\nP99.9 帧时间  {4:0.00} ms\r\n超过 16.67 ms：{5} 帧   超过 33.33 ms：{6} 帧",Frames,AverageFps,Low1Fps,P99Ms,P999Ms,Above16,Above33);
        }
    }
    public static class FrameAnalysis {
        public static FrameResult Calculate(IEnumerable<double> source) {
            var data=source.Where(x=>x>0&&!Double.IsNaN(x)&&!Double.IsInfinity(x)).OrderBy(x=>x).ToArray();
            if(data.Length<100)throw new InvalidDataException("至少需要 100 个有效帧；建议采集同一游戏进程 60 秒以上。 ");
            int count=Math.Max(1,(int)Math.Ceiling(data.Length*0.01));
            return new FrameResult {Frames=data.Length,AverageFps=1000/data.Average(),Low1Fps=1000/data.Skip(data.Length-count).Average(),P99Ms=data[(int)Math.Ceiling(data.Length*0.99)-1],P999Ms=data[(int)Math.Ceiling(data.Length*0.999)-1],Above16=data.Count(x=>x>1000.0/60),Above33=data.Count(x=>x>1000.0/30)};
        }
        public static FrameResult Load(string file) {
            if(new FileInfo(file).Length>64L*1024*1024)throw new InvalidDataException("CSV 超过 64MB，请先导出单个游戏进程的一段采样。");
            using(var reader=new StreamReader(file,Encoding.UTF8,true)) {
                string header=reader.ReadLine();if(header==null)throw new InvalidDataException("CSV 为空。");
                char separator=header.Contains("\t")?'\t':header.Contains(";")?';':',';
                var fields=Split(header,separator).Select(x=>x.Trim().Trim('\uFEFF')).ToArray();
                string[] accepted={"MsBetweenPresents","FrameTime","FrameTimeMs","FrameTime (ms)"};
                int column=Array.FindIndex(fields,x=>accepted.Contains(x,StringComparer.OrdinalIgnoreCase));
                if(column<0)throw new InvalidDataException("需要毫秒帧时间列：MsBetweenPresents、FrameTime 或 FrameTimeMs。不能用 FPS 或 CPUFrameTime 列替代。");
                int app=Array.FindIndex(fields,x=>x.Equals("Application",StringComparison.OrdinalIgnoreCase));
                int pid=Array.FindIndex(fields,x=>x.Equals("ProcessID",StringComparison.OrdinalIgnoreCase));
                int swap=Array.FindIndex(fields,x=>x.Equals("SwapChainAddress",StringComparison.OrdinalIgnoreCase));
                var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var values=new List<double>();
                string line;while((line=reader.ReadLine())!=null) {
                    var row=Split(line,separator);if(row.Length<=column)continue;
                    double value;if(!Double.TryParse(row[column],NumberStyles.Float,CultureInfo.InvariantCulture,out value)||value<=0||Double.IsNaN(value)||Double.IsInfinity(value))continue;
                    string identity="";foreach(int index in new[]{app,pid,swap})if(index>=0&&index<row.Length)identity+="|"+row[index];
                    identities.Add(identity);if(identities.Count>1)throw new InvalidDataException("文件混有多个应用、进程或交换链，请先导出单一帧流再分析，避免错误的 1% low。");
                    values.Add(value);if(values.Count>2000000)throw new InvalidDataException("采样超过 200 万帧，请缩短区间。");
                }
                return Calculate(values);
            }
        }
        static string[] Split(string line,char separator) {
            var fields=new List<string>();var value=new StringBuilder();bool quoted=false;
            for(int i=0;i<line.Length;i++) {
                char c=line[i];if(c=='"') {if(quoted&&i+1<line.Length&&line[i+1]=='"'){value.Append('"');i++;}else quoted=!quoted;}
                else if(c==separator&&!quoted){fields.Add(value.ToString());value.Clear();}else value.Append(c);
            }
            fields.Add(value.ToString());return fields.ToArray();
        }
    }
}
