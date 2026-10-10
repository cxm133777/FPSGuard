using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FpsGuard {
    public sealed class GameDiscovery {
        DateTime refresh = DateTime.MinValue;
        readonly List<string> steamFolders = new List<string>();
        public string[] Folders(Settings s, bool allowRefresh = true) {
            if (s.DetectSteamGames && (refresh == DateTime.MinValue || allowRefresh && DateTime.UtcNow >= refresh)) {
                refresh = DateTime.UtcNow.AddMinutes(5); steamFolders.Clear();
                var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try {
                    using (var key = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam")) {
                        string steam = key == null ? "" : Convert.ToString(key.GetValue("SteamPath"));
                        if (Directory.Exists(steam)) libraries.Add(steam);
                    }
                    libraries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
                    foreach (var library in libraries.ToArray()) {
                        string vdf = Path.Combine(library, "steamapps", "libraryfolders.vdf");
                        if (!File.Exists(vdf)) continue;
                        foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
                    }
                    foreach (var library in libraries) {
                        string apps = Path.Combine(library, "steamapps"), common = Path.Combine(apps, "common");
                        if (!Directory.Exists(apps)) continue;
                        foreach (string file in Directory.GetFiles(apps, "appmanifest_*.acf")) {
                            try {
                                var match = Regex.Match(File.ReadAllText(file), "\"installdir\"\\s+\"([^\"]+)\"");
                                if (!match.Success) continue;
                                string dir = match.Groups[1].Value;
                                if (new[] { "wallpaper_engine", "soundpad", "steamworks shared" }.Contains(dir, StringComparer.OrdinalIgnoreCase)) continue;
                                string folder = Path.GetFullPath(Path.Combine(common, dir));
                                if (Inside(folder + "\\placeholder.exe", common) && Directory.Exists(folder)) steamFolders.Add(folder);
                            } catch { }
                        }
                    }
                } catch { }
            }
            return (s.DetectSteamGames ? steamFolders : Enumerable.Empty<string>()).Concat((s.GameFolders ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static bool Inside(string file, string folder) {
            try {
                string root = Path.GetFullPath(folder.Trim()).TrimEnd('\\', '/') + "\\";
                // A whole drive cannot be a game-specific root.
                if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)) return false;
                return Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
        }
        public static bool Matches(string name, string path, string[] roots) {
            if (String.IsNullOrEmpty(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (new[] { "crash", "launcher", "update", "uninstall", "setup", "report", "bootstrap", "anticheat", "webhelper" }.Any(x => name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            return roots.Any(root => Inside(path, root));
        }
    }
}
