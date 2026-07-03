// =====================================================================
// Wekz App v1.5
//  - war3 start -> borderless fullscreen + fokus u lobi + zvuk + notifikacija
//    (zahteva "-window" u RGC WC3 launch opcijama)
//  - Alt+`  = klik na SIGN (pozicija iz sign-offset.txt)
//  - Alt+F2 = kalibracija
//  - W3 pozadina: menja glavni meni Warcrafta (lokalni fajl override,
//    modeli u backgrounds\ folderu, "Allow Local Files" registry)
//  - Custom hotkeys: Dota-2-stil grid editor koji pise u <W3>\config.dota.ini
//    (dokumentovan format, d1stats.ru/configdota - obican tekst, ne binarni cache)
//  - RGC stats: cita username iz RGC Preferences, scrapuje
//    ladder.rankedgaming.com i prikazuje mali popup
//  - tray meni + podesavanja (settings.ini)
// Upgrade: izmeni ovaj fajl -> build.ps1 -> install.ps1 (kao admin)
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WekzApp {
static class Program {
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern int  GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int  SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint c, uint t);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    const int  GWL_STYLE       = -16;
    const int  WS_CAPTION      = 0x00C00000;
    const int  WS_THICKFRAME   = 0x00040000;
    const int  WS_MINIMIZEBOX  = 0x00020000;
    const int  WS_MAXIMIZEBOX  = 0x00010000;
    const int  WS_SYSMENU      = 0x00080000;
    const uint SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040, SWP_NOZORDER = 0x0004,
               SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002;

    static string baseDir      = AppDomain.CurrentDomain.BaseDirectory;
    static string logFile      = Path.Combine(baseDir, "watcher.log");
    static string offsetFile   = Path.Combine(baseDir, "sign-offset.txt");
    static string alertWav     = Path.Combine(baseDir, "game-alert.wav");
    static string settingsFile = Path.Combine(baseDir, "settings.ini");
    static string bgDir        = Path.Combine(baseDir, "backgrounds");
    static string rgcPrefs     = @"C:\Program Files (x86)\Warcraft III\Ranked Gaming Client\Preferences";

    static NotifyIcon trayIcon;
    static ToolStripMenuItem bgMenuRoot;
    static bool handled = false, notified = false, hkHeld = false, hk2Held = false;
    static DateTime hkLast = DateTime.MinValue;
    static uint vkGrave;

    // podesavanja (settings.ini)
    static bool optBorderless = true, optSound = true, optBalloon = true, optHotkey = true;
    static string optBg = "";        // ime fajla pozadine ili "" za original
    static string optRgcRoom = "19"; // (EU) Public na ladder.rankedgaming.com

    static void Log(string m) {
        try { File.AppendAllText(logFile, DateTime.Now.ToString("HH:mm:ss") + " " + m + "\r\n"); } catch { }
    }

    static bool IsElevated() {
        using (var id = WindowsIdentity.GetCurrent())
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    static Process FindWithWindow(string name) {
        foreach (var p in Process.GetProcessesByName(name))
            if (p.MainWindowHandle != IntPtr.Zero) return p;
        return null;
    }

    // ---------- podesavanja ----------
    static void LoadSettings() {
        try {
            if (!File.Exists(settingsFile)) return;
            foreach (var line in File.ReadAllLines(settingsFile)) {
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                switch (k) {
                    case "borderless": optBorderless = v == "1"; break;
                    case "sound":      optSound = v == "1"; break;
                    case "balloon":    optBalloon = v == "1"; break;
                    case "hotkey":     optHotkey = v == "1"; break;
                    case "bg":         optBg = v; break;
                    case "rgcRoom":    if (v.Length > 0) optRgcRoom = v; break;
                }
            }
        } catch { }
    }

    static void SaveSettings() {
        try {
            File.WriteAllText(settingsFile,
                "borderless=" + (optBorderless ? "1" : "0") + "\r\n" +
                "sound="      + (optSound ? "1" : "0") + "\r\n" +
                "balloon="    + (optBalloon ? "1" : "0") + "\r\n" +
                "hotkey="     + (optHotkey ? "1" : "0") + "\r\n" +
                "bg="         + optBg + "\r\n" +
                "rgcRoom="    + optRgcRoom + "\r\n");
        } catch { }
    }

    static SettingsForm settingsOpen = null;
    static void OpenSettings() {
        if (settingsOpen != null) { settingsOpen.Activate(); return; }
        var f = new SettingsForm();
        settingsOpen = f;
        f.Icon = trayIcon.Icon;
        f.cbB.Checked = optBorderless;
        f.cbS.Checked = optSound;
        f.cbN.Checked = optBalloon;
        f.cbH.Checked = optHotkey;
        f.TopMost = true;
        f.FormClosed += delegate { settingsOpen = null; };
        if (f.ShowDialog() == DialogResult.OK) {
            optBorderless = f.cbB.Checked;
            optSound      = f.cbS.Checked;
            optBalloon    = f.cbN.Checked;
            optHotkey     = f.cbH.Checked;
            SaveSettings();
            Log("settings: borderless=" + optBorderless + " sound=" + optSound +
                " balloon=" + optBalloon + " hotkey=" + optHotkey);
        }
    }

    // ---------- RGC / Warcraft III paths (read from RGC Preferences, hex-encoded) ----------
    static string DecodeHexPref(string key) {
        try {
            if (!File.Exists(rgcPrefs)) return null;
            foreach (var line in File.ReadAllLines(rgcPrefs)) {
                if (!line.StartsWith(key + "=")) continue;
                string hex = line.Substring(key.Length + 1).Trim();
                if (hex.Length < 2) return null;
                var sb = new StringBuilder();
                for (int i = 0; i + 1 < hex.Length; i += 2)
                    sb.Append((char)Convert.ToInt32(hex.Substring(i, 2), 16));
                return sb.ToString();
            }
        } catch { }
        return null;
    }

    static string GetW3Dir() {
        try {
            string exe = DecodeHexPref("WC3_LOCATION");
            if (exe != null) {
                string dir = Path.GetDirectoryName(exe.Replace('/', '\\'));
                if (Directory.Exists(dir)) return dir;
            }
        } catch { }
        return @"C:\Program Files (x86)\Warcraft III";
    }

    static string GetRgcUser() {
        return DecodeHexPref("USER");
    }

    // ---------- W3 pozadina ----------
    static void BuildBgMenu() {
        bgMenuRoot.DropDownItems.Clear();
        var def = new ToolStripMenuItem("Original (TFT menu)");
        def.Checked = string.IsNullOrEmpty(optBg);
        def.Click += delegate { ApplyBackground(null); };
        bgMenuRoot.DropDownItems.Add(def);
        if (Directory.Exists(bgDir)) {
            bgMenuRoot.DropDownItems.Add(new ToolStripSeparator());
            foreach (var f in Directory.GetFiles(bgDir, "*.mdx")) {
                var it = new ToolStripMenuItem(Path.GetFileNameWithoutExtension(f).Replace('-', ' '));
                it.Tag = f;
                it.Checked = string.Equals(optBg, Path.GetFileName(f), StringComparison.OrdinalIgnoreCase);
                it.Click += delegate(object s, EventArgs e) {
                    ApplyBackground((string)((ToolStripMenuItem)s).Tag);
                };
                bgMenuRoot.DropDownItems.Add(it);
            }
        }
    }

    static void ApplyBackground(string srcFile) {
        try {
            string w3 = GetW3Dir();
            string target = Path.Combine(w3, @"UI\Glues\MainMenu\MainMenu3d_exp\MainMenu3d_exp.mdx");
            if (srcFile == null) {
                if (File.Exists(target)) File.Delete(target);
                optBg = "";
                Log("w3 background: original restored");
            } else {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(srcFile, target, true);
                optBg = Path.GetFileName(srcFile);
                Log("w3 background: " + optBg + " -> " + target);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Blizzard Entertainment\Warcraft III"))
                k.SetValue("Allow Local Files", 1, RegistryValueKind.DWord);
            SaveSettings();
            BuildBgMenu();
            if (optBalloon) trayIcon.ShowBalloonTip(4000, "W3 background",
                srcFile == null
                    ? "Original restored. Restart Warcraft to see it."
                    : "Applied: " + Path.GetFileNameWithoutExtension(srcFile).Replace('-', ' ') + ". Restart Warcraft to see it.",
                ToolTipIcon.Info);
        } catch (Exception ex) { Log("w3 background error: " + ex.Message); }
    }

    // ---------- config.dota.ini: generic multi-section read/write ----------
    // keys are stored as "[SECTION]|KeyName" so hotkeys and on/off toggles
    // (which live in different ini sections) can be edited and saved together
    static string GetDotaIniPath() {
        return Path.Combine(GetW3Dir(), "config.dota.ini");
    }

    static Dictionary<string, string> LoadIniAll() {
        var dict = new Dictionary<string, string>();
        string path = GetDotaIniPath();
        if (!File.Exists(path)) return dict;
        string section = "";
        foreach (var raw in File.ReadAllLines(path)) {
            string line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line; continue; }
            if (line.Length == 0 || line.StartsWith(";")) continue;
            int eq = line.IndexOf('=');
            if (eq < 1) continue;
            dict[section + "|" + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return dict;
    }

    static void SaveIniValues(Dictionary<string, string> newValues) {
        string path = GetDotaIniPath();
        List<string> lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();

        var remaining = new Dictionary<string, string>(newValues);
        string section = "";
        var sectionEndIndex = new Dictionary<string, int>();

        for (int i = 0; i < lines.Count; i++) {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) {
                if (section != "" && !sectionEndIndex.ContainsKey(section)) sectionEndIndex[section] = i;
                section = trimmed;
                continue;
            }
            int eq = trimmed.IndexOf('=');
            if (eq < 1) continue;
            string key = trimmed.Substring(0, eq).Trim();
            string fullKey = section + "|" + key;
            if (remaining.ContainsKey(fullKey)) {
                lines[i] = key + " = " + remaining[fullKey];
                remaining.Remove(fullKey);
            }
        }
        if (section != "" && !sectionEndIndex.ContainsKey(section)) sectionEndIndex[section] = lines.Count;

        if (remaining.Count > 0) {
            var bySection = new Dictionary<string, List<string>>();
            foreach (var kv in remaining) {
                int bar = kv.Key.IndexOf('|');
                string sec = kv.Key.Substring(0, bar);
                string key = kv.Key.Substring(bar + 1);
                if (!bySection.ContainsKey(sec)) bySection[sec] = new List<string>();
                bySection[sec].Add(key + " = " + kv.Value);
            }
            var order = new List<KeyValuePair<string, int>>();
            foreach (var sec in bySection.Keys)
                order.Add(new KeyValuePair<string, int>(sec, sectionEndIndex.ContainsKey(sec) ? sectionEndIndex[sec] : -1));
            order.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            foreach (var kv in order) {
                if (kv.Value >= 0) lines.InsertRange(kv.Value, bySection[kv.Key]);
                else { lines.Add(kv.Key); lines.AddRange(bySection[kv.Key]); }
            }
        }

        File.WriteAllLines(path, lines.ToArray());
    }

    static void OpenHotkeysEditor() {
        var current = LoadIniAll();
        var f = new HotkeysForm(current);
        f.Icon = trayIcon.Icon;
        if (f.ShowDialog() == DialogResult.OK) {
            try {
                var changes = f.GetResult();
                SaveIniValues(changes);
                Log((f.OverrideAll ? "hotkeys/options OVERRIDE (" : "hotkeys/options saved (")
                    + changes.Count + " fields) to " + GetDotaIniPath());
                if (optBalloon) trayIcon.ShowBalloonTip(4000, "Custom hotkeys",
                    changes.Count + " field(s) written. Reopen the in-game settings menu (or restart the game) to apply.", ToolTipIcon.Info);
            } catch (Exception ex) {
                Log("hotkeys save error: " + ex.Message);
                if (optBalloon) trayIcon.ShowBalloonTip(4000, "Custom hotkeys", "Could not save: " + ex.Message, ToolTipIcon.Error);
            }
        }
    }

    // ---------- RGC stats ----------
    static RgcStats ParseStatsHtml(string html) {
        int rowIdx = html.IndexOf("class=\"row animate\"", StringComparison.Ordinal);
        if (rowIdx < 0) return null;
        int len = Math.Min(4500, html.Length - rowIdx);
        string block = html.Substring(rowIdx, len);

        var mUid = Regex.Match(block, @"achievements\.php\?uid=(\d+)");
        if (!mUid.Success) return null;

        var s = new RgcStats();
        s.Uid = mUid.Groups[1].Value;

        var mRank = Regex.Match(block, @"font-size:\s*16px;.*?>(\d+)<", RegexOptions.Singleline);
        if (mRank.Success) s.Rank = mRank.Groups[1].Value;

        var mName = Regex.Match(block, @"countries\['[A-Za-z]{2}'\]\);.*?>\s*([^<]+?)\s*</div>", RegexOptions.Singleline);
        if (mName.Success) s.Name = mName.Groups[1].Value.Trim();

        var mWins = Regex.Match(block, @"(\d+)\s+WINS");
        if (mWins.Success) s.Wins = mWins.Groups[1].Value;

        var mLoss = Regex.Match(block, @"(\d+)\s+LOSSES");
        if (mLoss.Success) s.Losses = mLoss.Groups[1].Value;

        var mGames = Regex.Match(block, @"&nbsp;&nbsp;(\d+)</div>");
        if (mGames.Success) s.Games = mGames.Groups[1].Value;

        var kda = Regex.Matches(block, @"text-align:\s*right;.*?>(\d+)</div>", RegexOptions.Singleline);
        if (kda.Count >= 3) {
            s.Kills = kda[0].Groups[1].Value;
            s.Deaths = kda[1].Groups[1].Value;
            s.Assists = kda[2].Groups[1].Value;
        }

        var mScore = Regex.Match(block, @"#37a;.*?Roboto Condensed.*?bold;.*?>(\d+)<", RegexOptions.Singleline);
        if (mScore.Success) s.Score = mScore.Groups[1].Value;

        var mReli = Regex.Match(block, @"(\d+)%\s*reliability");
        if (mReli.Success) s.Reliability = mReli.Groups[1].Value;

        int w, l;
        if (int.TryParse(s.Wins, out w) && int.TryParse(s.Losses, out l) && (w + l) > 0)
            s.WinPct = ((int)Math.Round(100.0 * w / (w + l))).ToString();

        int k, d, a;
        if (int.TryParse(s.Kills, out k) && int.TryParse(s.Deaths, out d) && int.TryParse(s.Assists, out a))
            s.KdaRatio = ((k + a) / (double)Math.Max(1, d)).ToString("0.00");

        return s;
    }

    static RgcStats FetchStats(string user, string room) {
        ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
        string url = "https://ladder.rankedgaming.com/index.php?room=" + Uri.EscapeDataString(room) +
                     "&s=" + Uri.EscapeDataString(user);
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WekzApp/1.5";
        req.Timeout = 8000;
        string html;
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var stream = resp.GetResponseStream())
        using (var sr = new StreamReader(stream, Encoding.UTF8))
            html = sr.ReadToEnd();
        return ParseStatsHtml(html);
    }

    static void LoadStats(StatsForm f, string user) {
        string room = optRgcRoom;
        f.SetLoading(user);
        Task.Factory.StartNew(delegate { return FetchStats(user, room); })
            .ContinueWith(delegate(Task<RgcStats> t) {
                if (t.IsFaulted) {
                    f.SetError("Could not load stats (network error).");
                    Log("stats error: " + (t.Exception != null ? t.Exception.Message : "unknown"));
                } else if (t.Result == null) {
                    f.SetError("Player '" + user + "' not found on the ladder.");
                } else {
                    f.SetData(t.Result, room);
                    Log("stats loaded for " + user + " (uid " + t.Result.Uid + ")");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    static void OpenStats() {
        string user = GetRgcUser();
        if (string.IsNullOrEmpty(user)) {
            if (optBalloon) trayIcon.ShowBalloonTip(4000, "RGC stats", "Could not read your RGC username from Preferences.", ToolTipIcon.Warning);
            Log("stats: could not read RGC username");
            return;
        }

        var f = new StatsForm();
        f.Icon = trayIcon.Icon;
        f.SearchRequested += delegate(object s, string name) { LoadStats(f, name); };
        f.Show();
        LoadStats(f, user);
    }

    [STAThread]
    static void Main() {
        try { File.WriteAllText(logFile, ""); } catch { }
        LoadSettings();
        Log("start (Wekz App v1.5) | elevated=" + IsElevated());
        vkGrave = MapVirtualKey(0x29, 3);
        if (vkGrave == 0) vkGrave = 0xC0;

        trayIcon = new NotifyIcon();
        try { trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { trayIcon.Icon = SystemIcons.Information; }
        trayIcon.Text = "Wekz App";

        var menu = new ContextMenuStrip();

        // ----- RGC section -----
        var rgcHeader = new ToolStripMenuItem("RGC");
        rgcHeader.Enabled = false;
        rgcHeader.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        menu.Items.Add(rgcHeader);
        menu.Items.Add("    Sign now  (Alt+`)", null, delegate { SignClick(); });
        menu.Items.Add("    Calibrate sign in 3s  (Alt+F2)", null, delegate { StartDelayedCalibration(); });
        menu.Items.Add("    My RGC stats", null, delegate { OpenStats(); });

        // ----- Warcraft III section -----
        menu.Items.Add(new ToolStripSeparator());
        var w3Header = new ToolStripMenuItem("WARCRAFT III");
        w3Header.Enabled = false;
        w3Header.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        menu.Items.Add(w3Header);
        bgMenuRoot = new ToolStripMenuItem("    Menu background");
        BuildBgMenu();
        menu.Items.Add(bgMenuRoot);
        menu.Items.Add("    Custom hotkeys...", null, delegate { OpenHotkeysEditor(); });

        // ----- app -----
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, delegate { OpenSettings(); });
        menu.Items.Add("Open log", null, delegate { try { Process.Start("notepad.exe", logFile); } catch { } });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { trayIcon.Visible = false; Application.Exit(); });
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += delegate { OpenSettings(); };
        trayIcon.Visible = true;

        RestoreRgcTitlebar();

        var mainTimer = new Timer(); mainTimer.Interval = 1500;
        mainTimer.Tick += delegate { MainTick(); };
        mainTimer.Start();

        var hkTimer = new Timer(); hkTimer.Interval = 75;
        hkTimer.Tick += delegate { HotkeyTick(); };
        hkTimer.Start();
        Log("hotkeys: Alt+` = sign klik, Alt+F2 = kalibracija | vkGrave=0x" + vkGrave.ToString("X"));

        Application.Run();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }

    // ---------- war3: borderless + fokus + notifikacija ----------
    static void MainTick() {
        Process war3 = null;
        var procs = Process.GetProcessesByName("war3");
        if (procs.Length > 0) war3 = procs[0];

        if (war3 != null) {
            if (!notified) {
                notified = true;
                if (optSound) { try { if (File.Exists(alertWav)) new SoundPlayer(alertWav).Play(); } catch { } }
                if (optBalloon) trayIcon.ShowBalloonTip(8000, "Game is starting!",
                    "Warcraft III is launching - entering the lobby. GLHF!", ToolTipIcon.Info);
            }
            if (!handled) {
                if (optBorderless) {
                    try { handled = SetBorderless(war3); if (handled) Log("war3 borderless applied"); }
                    catch (Exception ex) { Log("war3 borderless error: " + ex.Message); }
                } else handled = true;
            }
        } else {
            handled = false;
            notified = false;
        }
    }

    static bool SetBorderless(Process proc) {
        proc.Refresh();
        IntPtr h = proc.MainWindowHandle;
        if (h == IntPtr.Zero) return false;

        int style = GetWindowLong(h, GWL_STYLE);
        int newStyle = style & ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        if (newStyle != style) SetWindowLong(h, GWL_STYLE, newStyle);

        Rectangle b = Screen.PrimaryScreen.Bounds;
        SetWindowPos(h, IntPtr.Zero, b.X, b.Y, b.Width, b.Height,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOZORDER);
        ShowWindow(h, 9);
        SetForegroundWindow(h);
        return true;
    }

    static void RestoreRgcTitlebar() {
        var rgc = FindWithWindow("rgc");
        if (rgc == null) return;
        IntPtr h = rgc.MainWindowHandle;
        int style = GetWindowLong(h, GWL_STYLE);
        if ((style & WS_CAPTION) == 0) {
            SetWindowLong(h, GWL_STYLE, style | WS_CAPTION);
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER);
            Log("RGC titlebar restored");
        }
    }

    // ---------- hotkeys (Alt+` sign / Alt+F2 calibrate) ----------
    static void HotkeyTick() {
        bool alt   = (GetAsyncKeyState(0x12) & 0x8000) != 0;
        bool grave = (GetAsyncKeyState((int)vkGrave) & 0x8000) != 0;
        bool f2    = (GetAsyncKeyState(0x71) & 0x8000) != 0;

        if (alt && grave && optHotkey) {
            if (!hkHeld && (DateTime.Now - hkLast).TotalMilliseconds > 800) {
                hkHeld = true;
                hkLast = DateTime.Now;
                SignClick();
            }
        } else hkHeld = false;

        if (alt && f2) {
            if (!hk2Held) { hk2Held = true; Calibrate(); }
        } else hk2Held = false;
    }

    static void StartDelayedCalibration() {
        if (optBalloon) trayIcon.ShowBalloonTip(3000, "Calibration",
            "Place the cursor on the SIGN button - capturing in 3 seconds...", ToolTipIcon.Info);
        var t = new Timer(); t.Interval = 3000;
        t.Tick += delegate { t.Stop(); t.Dispose(); Calibrate(); };
        t.Start();
    }

    static void SignClick() {
        var rgc = FindWithWindow("rgc");
        if (rgc == null) { Log("sign: RGC window not found"); return; }
        if (!File.Exists(offsetFile)) {
            Log("sign: not calibrated - place cursor on SIGN and press Alt+F2");
            Console.Beep(400, 300);
            return;
        }
        try {
            string[] parts = File.ReadAllText(offsetFile).Trim().Split(',');
            int dx = int.Parse(parts[0]), dy = int.Parse(parts[1]);

            IntPtr h = rgc.MainWindowHandle;
            RECT wr; GetWindowRect(h, out wr);
            int x = wr.R - dx, y = wr.B - dy;

            ShowWindow(h, 9);
            SetForegroundWindow(h);
            System.Threading.Thread.Sleep(120);

            POINT old; GetCursorPos(out old);
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(40);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(60);
            SetCursorPos(old.X, old.Y);

            Log(string.Format("sign: click at ({0},{1}) [offset right-{2} bottom-{3}]", x, y, dx, dy));
            Console.Beep(1318, 70);
        } catch (Exception ex) { Log("sign error: " + ex.Message); }
    }

    static void Calibrate() {
        var rgc = FindWithWindow("rgc");
        if (rgc == null) { Log("calibrate: RGC window not found"); return; }
        RECT wr; GetWindowRect(rgc.MainWindowHandle, out wr);
        POINT cur; GetCursorPos(out cur);
        if (cur.X < wr.L || cur.X > wr.R || cur.Y < wr.T || cur.Y > wr.B) {
            Log(string.Format("calibrate: cursor ({0},{1}) is outside the RGC window", cur.X, cur.Y));
            Console.Beep(400, 300);
            return;
        }
        int dx = wr.R - cur.X, dy = wr.B - cur.Y;
        File.WriteAllText(offsetFile, dx + "," + dy);
        Log(string.Format("calibrate OK: SIGN at (right-{0}, bottom-{1})", dx, dy));
        Console.Beep(1046, 80);
        System.Threading.Thread.Sleep(60);
        Console.Beep(1568, 110);
    }
}

// ---------- Settings prozor ----------
class SettingsForm : Form {
    public CheckBox cbB, cbS, cbN, cbH;

    public SettingsForm() {
        Text = "Wekz App - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(340, 226);
        BackColor = Color.FromArgb(240, 240, 238);
        Font = new Font("Segoe UI", 9f);

        cbB = Mk("Borderless Warcraft (auto-lobby)", 22);
        cbS = Mk("Sound when a game starts", 52);
        cbN = Mk("Windows notification", 82);
        cbH = Mk("SIGN hotkey (Alt+`)", 112);

        var save = new Button();
        save.Text = "SAVE";
        save.FlatStyle = FlatStyle.Flat;
        save.FlatAppearance.BorderSize = 0;
        save.BackColor = Color.FromArgb(63, 217, 104);
        save.ForeColor = Color.FromArgb(13, 43, 22);
        save.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        save.SetBounds(20, 162, 300, 40);
        save.Click += delegate { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(save);
        AcceptButton = save;
    }

    CheckBox Mk(string t, int y) {
        var c = new CheckBox();
        c.Text = t;
        c.ForeColor = Color.FromArgb(30, 30, 28);
        c.SetBounds(24, y, 300, 24);
        Controls.Add(c);
        return c;
    }
}

// ---------- RGC stats: podaci + popup ----------
class RgcStats {
    public string Uid = "", Rank = "?", Name = "", Wins = "?", Losses = "?", WinPct = "?",
                  Games = "?", Kills = "?", Deaths = "?", Assists = "?", Score = "?",
                  Reliability = "?", KdaRatio = "?";
}

class StatsForm : Form {
    public event EventHandler<string> SearchRequested;

    TextBox searchBox;
    Label lblLoading;
    Label lblName, lblRank, lblWinPct, lblGames, lblScore, lblExtra;
    Panel barPanel;
    Label lblKillsVal, lblDeathsVal, lblAssistsVal;
    Button btnProfile;
    List<Control> dataControls = new List<Control>();
    int winPctValue = 0;
    string profileUrl = null;

    public StatsForm() {
        Text = "Wekz App - RGC Stats";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 320);
        BackColor = Color.FromArgb(240, 240, 238);
        Font = new Font("Segoe UI", 9f);

        searchBox = new TextBox();
        searchBox.Font = new Font("Segoe UI", 9f);
        searchBox.SetBounds(20, 14, 190, 24);
        searchBox.KeyDown += delegate(object s, KeyEventArgs e) {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); }
        };
        Controls.Add(searchBox);

        var searchBtn = new Button();
        searchBtn.Text = "SEARCH";
        searchBtn.FlatStyle = FlatStyle.Flat;
        searchBtn.FlatAppearance.BorderSize = 0;
        searchBtn.BackColor = Color.FromArgb(63, 217, 104);
        searchBtn.ForeColor = Color.FromArgb(13, 43, 22);
        searchBtn.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        searchBtn.SetBounds(216, 14, 84, 24);
        searchBtn.Click += delegate { DoSearch(); };
        Controls.Add(searchBtn);

        lblLoading = new Label();
        lblLoading.Font = new Font("Segoe UI", 10f);
        lblLoading.ForeColor = Color.FromArgb(90, 90, 86);
        lblLoading.SetBounds(20, 52, 280, 60);
        Controls.Add(lblLoading);

        lblName = new Label();
        lblName.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        lblName.ForeColor = Color.FromArgb(30, 30, 28);
        lblName.AutoEllipsis = true;
        lblName.SetBounds(20, 50, 190, 28);
        Add(lblName);

        lblRank = new Label();
        lblRank.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        lblRank.ForeColor = Color.White;
        lblRank.BackColor = Color.FromArgb(63, 217, 104);
        lblRank.TextAlign = ContentAlignment.MiddleCenter;
        lblRank.SetBounds(216, 50, 84, 28);
        lblRank.Region = RoundedRegion(new Rectangle(0, 0, 84, 28), 14);
        Add(lblRank);

        barPanel = new Panel();
        barPanel.SetBounds(20, 90, 280, 20);
        barPanel.Paint += BarPanel_Paint;
        Add(barPanel);

        lblWinPct = new Label();
        lblWinPct.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        lblWinPct.ForeColor = Color.FromArgb(39, 173, 75);
        lblWinPct.SetBounds(20, 116, 280, 18);
        Add(lblWinPct);

        lblGames = new Label();
        lblGames.Font = new Font("Segoe UI", 9f);
        lblGames.ForeColor = Color.FromArgb(120, 120, 116);
        lblGames.SetBounds(20, 136, 280, 18);
        Add(lblGames);

        lblKillsVal   = AddTile("KILLS", 20);
        lblDeathsVal  = AddTile("DEATHS", 113);
        lblAssistsVal = AddTile("ASSISTS", 206);

        lblScore = new Label();
        lblScore.Font = new Font("JetBrains Mono", 11f, FontStyle.Bold);
        lblScore.ForeColor = Color.FromArgb(39, 173, 75);
        lblScore.SetBounds(20, 224, 280, 22);
        Add(lblScore);

        lblExtra = new Label();
        lblExtra.Font = new Font("Segoe UI", 8.5f);
        lblExtra.ForeColor = Color.FromArgb(120, 120, 116);
        lblExtra.SetBounds(20, 246, 280, 18);
        Add(lblExtra);

        btnProfile = new Button();
        btnProfile.Text = "VIEW FULL PROFILE";
        btnProfile.FlatStyle = FlatStyle.Flat;
        btnProfile.FlatAppearance.BorderColor = Color.FromArgb(198, 198, 193);
        btnProfile.BackColor = Color.White;
        btnProfile.ForeColor = Color.FromArgb(30, 30, 28);
        btnProfile.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnProfile.SetBounds(20, 270, 280, 32);
        btnProfile.Click += delegate {
            if (profileUrl != null) { try { Process.Start(profileUrl); } catch { } }
        };
        Add(btnProfile);
    }

    void DoSearch() {
        string name = (searchBox.Text ?? "").Trim();
        if (name.Length == 0) return;
        if (SearchRequested != null) SearchRequested(this, name);
    }

    void Add(Control c) {
        c.Visible = false;
        Controls.Add(c);
        dataControls.Add(c);
    }

    Label AddTile(string caption, int x) {
        var cap = new Label();
        cap.Text = caption;
        cap.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        cap.ForeColor = Color.FromArgb(140, 140, 136);
        cap.SetBounds(x, 166, 86, 16);
        Add(cap);

        var val = new Label();
        val.Font = new Font("JetBrains Mono", 16f, FontStyle.Bold);
        val.ForeColor = Color.FromArgb(30, 30, 28);
        val.SetBounds(x, 184, 86, 30);
        Add(val);
        return val;
    }

    static Region RoundedRegion(Rectangle rect, int radius) {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }

    void BarPanel_Paint(object sender, PaintEventArgs e) {
        int w = barPanel.Width, h = barPanel.Height;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var pillPath = new GraphicsPath();
        pillPath.AddArc(0, 0, h, h, 90, 180);
        pillPath.AddArc(w - h, 0, h, h, 270, 180);
        pillPath.CloseFigure();
        e.Graphics.SetClip(pillPath);

        int winW = (int)(w * (winPctValue / 100.0));
        using (var gBrush = new SolidBrush(Color.FromArgb(63, 217, 104)))
            e.Graphics.FillRectangle(gBrush, 0, 0, winW, h);
        using (var rBrush = new SolidBrush(Color.FromArgb(207, 68, 68)))
            e.Graphics.FillRectangle(rBrush, winW, 0, w - winW, h);
        e.Graphics.ResetClip();
    }

    void ShowData(bool show) {
        lblLoading.Visible = !show;
        foreach (var c in dataControls) c.Visible = show;
    }

    public void SetLoading(string user) {
        lblLoading.Text = "Loading stats for " + user + "...";
        ShowData(false);
    }

    public void SetError(string msg) {
        lblLoading.Text = msg;
        ShowData(false);
    }

    public void SetData(RgcStats s, string room) {
        lblName.Text = string.IsNullOrEmpty(s.Name) ? "?" : s.Name;
        lblRank.Text = "#" + s.Rank;

        int pct;
        if (!int.TryParse(s.WinPct, out pct)) pct = 0;
        winPctValue = pct;

        lblWinPct.Text = s.WinPct + "% WIN RATE";
        lblGames.Text = s.Wins + "W - " + s.Losses + "L  |  " + s.Games + " games";

        lblKillsVal.Text = s.Kills;
        lblDeathsVal.Text = s.Deaths;
        lblAssistsVal.Text = s.Assists;

        lblScore.Text = "SCORE  " + s.Score;
        lblExtra.Text = "KDA " + s.KdaRatio + (s.Reliability != "?" ? "   |   " + s.Reliability + "% reliable" : "");

        profileUrl = "https://ladder.rankedgaming.com/achievements.php?uid=" + s.Uid + "&room=" + room;

        ShowData(true);
        barPanel.Invalidate();
    }
}

// ---------- Custom hotkeys: faithful in-game layout, CAST/QUICKCAST toggle,
//            diff-save (only changed fields written) + explicit OVERRIDE ALL ----------
class BindSlot {
    public string CastKey;      // "[SECTION]|KeyName" active in CAST mode
    public string QuickKey;     // key active in QUICKCAST mode (null if not toggleable)
    public bool Toggleable;
    public Button Btn;
}

class HotkeysForm : Form {
    static readonly Color BG          = Color.FromArgb(12, 15, 20);
    static readonly Color SLOT_BG     = Color.FromArgb(16, 14, 11);
    static readonly Color GOLD        = Color.FromArgb(201, 162, 75);
    static readonly Color GOLD_BORDER = Color.FromArgb(140, 111, 60);
    static readonly Color GOLD_DIM    = Color.FromArgb(96, 80, 46);
    static readonly Color BLUE_DIM    = Color.FromArgb(45, 74, 108);
    static readonly Color GREEN       = Color.FromArgb(95, 235, 150);
    static readonly Color GREEN_BORDER= Color.FromArgb(63, 217, 104);
    static readonly Color TEXT        = Color.FromArgb(216, 216, 210);
    static readonly Color TEXT_DIM    = Color.FromArgb(130, 130, 124);
    static readonly Color ARMED_BG    = Color.FromArgb(36, 66, 44);

    Dictionary<string, string> originalValues;
    Dictionary<string, string> values = new Dictionary<string, string>();   // current bind tokens by full key
    List<BindSlot> slots = new List<BindSlot>();
    List<CheckBox> checkBoxes = new List<CheckBox>();
    BindSlot armed = null;
    bool quickMode = false;

    Panel pControl, pGame, pVisuals;
    Label tabControl, tabGame, tabVisuals;
    Label lblCast, lblQuick;

    public bool OverrideAll = false;

    // ability command-card map (4 cols x 3 rows). -1 = non-editable placeholder,
    // otherwise the slot number. Matches the in-game layout (QWER bottom, 5/6 mid).
    static readonly int[] ABILITY_MAP = { -1, -1, -1, -1,   -1, 5, 6, -1,   1, 2, 3, 4 };

    static readonly string[,] UNIT_ACTIONS = {
        {"BindMove","Move"}, {"BindHold","Hold Position"}, {"BindStop","Stop"},
        {"BindPatrol","Patrol"}, {"BindAttack","Attack"}, {"BindOpenHeroSkills","Ability Learn"},
        {"TalentsMenuHotkey","Talent Learn"}, {"SelectYourHero","Select Main Hero"},
        {"TeleportScrollHotkey","Use TP Scroll"}, {"QuickCastAttack","Quick Attack (-aat)"}
    };
    static readonly string[,] SELECTION = {
        {"SelectBestCourier","Select Courier"}, {"SelectAllUnits","Select All Units"},
        {"SelectAllOtherUnits","Select All Other Units"}, {"SelectCircleOfPower","Select Circle of Power"},
        {"OrderToAllControlledUnitsHotkey","Order All Units (hold)"}
    };
    static readonly string[,] INTERFACE = {
        {"DisplayScoreboard","Scoreboard"}, {"DisplayNeutralsSpawnAreaHotkey","Show Neutral Spawns"},
        {"DisplayTowerRangeHotkey","Show Tower Range"}
    };

    static readonly string[,] OPT_GAMEPLAY = {
        {"[GAMEOPTIONS]","AutoattackEnabled","Auto Attack Enabled"},
        {"[GAMEOPTIONS]","AutoattackDisabledByStopOnly","Auto-attack: Stop only"},
        {"[GAMEOPTIONS]","SmartAttackEnabled","Smart Attack"},
        {"[GAMEOPTIONS]","RightClickDeny","Right Click Deny"},
        {"[GAMEOPTIONS]","SelectionHelperEnabled","Selection Helper"},
        {"[GAMEOPTIONS]","DoubleClickHelperEnabled","Double Click Support"},
        {"[GAMEOPTIONS]","KeepLegacyCourierButtonsLayout","Legacy Courier Buttons"},
        {"[GAMEOPTIONS]","AutoselectHero","Auto-select Hero"},
        {"[GAMEOPTIONS]","DotA2HPBars","Dota 2 HP Bars"},
        {"[GAMEOPTIONS]","DisplayManabars","Display Mana Bars"},
        {"[GAMEOPTIONS]","WideScreen","Wide Screen"},
        {"[GAMEOPTIONS]","AutoFPSLimit","Auto FPS Limit"},
        {"[GAMEOPTIONS]","DisplayFPSCounter","Display FPS Counter"},
        {"[GAMEOPTIONS]","LockMouseAtWindow","Lock Mouse At Window"},
        {"[GAMEOPTIONS]","TeleportationCanOnlyBeStoppedSoft","TP: Soft-stop only"},
        {"[GAMEOPTIONS]","TeleportationCanOnlyBeStopped","TP: Can only be stopped"},
        {"[GAMEOPTIONS]","CloseWC3EveryGame","Close WC3 Every Game"},
        {"[GAMEOPTIONS]","BlinkAutoShifting","Blink Auto-Shifting"}
    };
    static readonly string[,] OPT_HOTKEY_BEHAVIOR = {
        {"[HOTKEYS]","ShopsQWERTY","Auto-bind shops to QWERTY"},
        {"[HOTKEYS]","DisableAllDefaultHotkeys","Disable ALL default hotkeys"},
        {"[HOTKEYS]","DisableDefaultAltHotkeys","Disable all Alt+menu hotkeys"},
        {"[HOTKEYS]","DisableAltS","Disable Alt+S"}, {"[HOTKEYS]","DisableAltL","Disable Alt+L"},
        {"[HOTKEYS]","DisableAltH","Disable Alt+H"}, {"[HOTKEYS]","DisableAltO","Disable Alt+O"},
        {"[HOTKEYS]","DisableAltQ","Disable Alt+Q"}, {"[HOTKEYS]","DisableAltG","Disable Alt+G"},
        {"[HOTKEYS]","DisableAltT","Disable Alt+T"}, {"[HOTKEYS]","DisableAltA","Disable Alt+A"},
        {"[HOTKEYS]","DisableAltR","Disable Alt+R"}, {"[HOTKEYS]","DisableAltF","Disable Alt+F"}
    };
    static readonly string[,] OPT_HERO = {
        {"[HEROOPTIONS]","Juggernaut_HealingWardDoNotFollow","Juggernaut: Ward doesn't follow"},
        {"[HEROOPTIONS]","Meepo_NumbersOverheadClones","Meepo: numbers over clones"}
    };
    static readonly string[,] OPT_VISUALS = {
        {"[VISUALS]","AlwaysDisplayRangeMarkers","Always Show Range Markers"},
        {"[VISUALS]","AlwaysDisplayHPRegen","Always Show HP Regen"},
        {"[VISUALS]","SameSelectionCircleForEveryone","Same Selection Circle"},
        {"[VISUALS]","AdvancedTooltips","Advanced Tooltips"},
        {"[VISUALS]","DisplayRegeneration","Display Regeneration"},
        {"[VISUALS]","CustomFPSInfo","Custom FPS Info"},
        {"[VISUALS]","EscClearsChat","Esc Clears Chat"},
        {"[VISUALS]","EscClearsPlayersChat","Esc Clears Players Chat"},
        {"[VISUALS]","GoodMinimap","Improved Minimap"},
        {"[VISUALS]","ProperColorsForCreeps","Proper Colors For Creeps"},
        {"[VISUALS]","AlliesAlwaysGreen","Allies Always Green"},
        {"[VISUALS]","BetterFPS","Better FPS"}, {"[VISUALS]","BetterFPS2","Better FPS 2"}, {"[VISUALS]","BetterFPS3","Better FPS 3"},
        {"[VISUALS]","DisableDefaultSpace","Disable Default Space"},
        {"[VISUALS]","DisableDefaultMouseWheel","Disable Default Mouse Wheel"},
        {"[VISUALS]","DisableDefaultTilde","Disable Default Tilde"},
        {"[VISUALS]","ShowTipsWhileDead","Show Tips While Dead"},
        {"[VISUALS]","ShowItemsInMultiboard","Show Items In Multiboard"},
        {"[VISUALS]","UseAdvancedHUD","Use Advanced HUD"},
        {"[VISUALS]","DisableAltTogglingHPBars","Disable Alt-Toggle HP Bars"},
        {"[VISUALS]","IgnoreAllChat","Ignore All Chat"},
        {"[VISUALS]","HideHeroNames","Hide Hero Names"},
        {"[VISUALS]","RepeatGameMessagesIntoChatLog","Repeat Msgs Into Chat Log"},
        {"[VISUALS]","AlwaysShowCourierButton","Always Show Courier Button"},
        {"[VISUALS]","HideMinimapSignals","Hide Minimap Signals"},
        {"[VISUALS]","ColorblindMode","Colorblind Mode"},
        {"[VISUALS]","AdvancedStatsIconDisabled","Disable Advanced Stats Icon"},
        {"[VISUALS]","ClassicIngameTime","Classic In-game Time"},
        {"[VISUALS]","SmoothFogReveal","Smooth Fog Reveal"},
        {"[VISUALS]","HealingDisplaysAmount","Healing Displays Amount"},
        {"[VISUALS]","KeepStopHoldButtons","Keep Stop/Hold Buttons"},
        {"[VISUALS]","DisableHeroCornerButton","Disable Hero Corner Button"},
        {"[VISUALS]","HideHeroIcon","Hide Hero Icon"},
        {"[VISUALS]","DisplayAllyGoldOnSelection","Display Ally Gold On Selection"},
        {"[VISUALS]","UIDisableManacostDisplay","Disable Manacost Display"},
        {"[VISUALS]","UIDisableHotkeyDisplay","Disable Hotkey Display"},
        {"[VISUALS]","EnableSoundOfGoldCoins","Gold Coin Sound"}
    };

    string Orig(string key) { string v; return originalValues.TryGetValue(key, out v) ? v : ""; }

    public HotkeysForm(Dictionary<string, string> current) {
        originalValues = current;
        Text = "Wekz App - Custom Hotkeys";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(740, 660);
        BackColor = BG;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        var note = new Label();
        note.Text = "Click a slot, press your key combo (Esc clears). SAVE writes only what you changed - "
                  + "your other in-game settings are kept. Use OVERRIDE to force-write everything.";
        note.ForeColor = TEXT_DIM;
        note.SetBounds(14, 4, 712, 30);
        Controls.Add(note);

        tabControl = MakeTab("CONTROL", 16, 36);
        tabGame    = MakeTab("GAME OPTIONS", 130, 36);
        tabVisuals = MakeTab("VISUALS", 290, 36);
        tabControl.Click += delegate { ShowTab(0); };
        tabGame.Click    += delegate { ShowTab(1); };
        tabVisuals.Click += delegate { ShowTab(2); };

        int py = 62, ph = 540;
        pControl = MakePanel(py, ph);
        pGame    = MakePanel(py, ph);
        pVisuals = MakePanel(py, ph);

        BuildControl();
        BuildGame();
        BuildVisuals();
        ShowTab(0);

        var reset = MakeBtn("RESET TAB", 14, 614, 130, SLOT_BG, GOLD);
        reset.FlatAppearance.BorderColor = GOLD_BORDER;
        reset.Click += delegate {
            if (pControl.Visible) { foreach (var s in slots) { values[ActiveKey(s)] = ""; Refresh(s); } }
            else foreach (var c in checkBoxes) if (c.Parent == (pGame.Visible ? (Control)pGame : pVisuals)) c.Checked = false;
        };

        var over = MakeBtn("OVERRIDE ALL", 470, 614, 130, Color.FromArgb(60, 30, 30), Color.FromArgb(240, 150, 150));
        over.FlatAppearance.BorderColor = Color.FromArgb(150, 70, 70);
        over.Click += delegate {
            if (MessageBox.Show(this,
                "OVERRIDE ALL will force-write every hotkey and option shown here into config.dota.ini,\n"
              + "replacing whatever is currently set in-game for those fields.\n\nContinue?",
                "Override all settings", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                OverrideAll = true; DialogResult = DialogResult.OK; Close();
            }
        };

        var save = MakeBtn("SAVE", 610, 614, 118, Color.FromArgb(63, 217, 104), Color.FromArgb(13, 43, 22));
        save.FlatAppearance.BorderSize = 0;
        save.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        save.Click += delegate { DialogResult = DialogResult.OK; Close(); };
        AcceptButton = save;
    }

    Label MakeTab(string text, int x, int y) {
        var l = new Label();
        l.Text = text;
        l.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        l.SetBounds(x, y, 155, 22);
        l.Cursor = Cursors.Hand;
        Controls.Add(l);
        return l;
    }

    Panel MakePanel(int y, int h) {
        var p = new Panel();
        p.SetBounds(0, y, 740, h);
        p.BackColor = BG;
        p.AutoScroll = true;
        p.Visible = false;
        Controls.Add(p);
        return p;
    }

    Button MakeBtn(string text, int x, int y, int w, Color bg, Color fg) {
        var b = new Button();
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = bg;
        b.ForeColor = fg;
        b.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        b.SetBounds(x, y, w, 34);
        Controls.Add(b);
        return b;
    }

    void ShowTab(int idx) {
        pControl.Visible = idx == 0;
        pGame.Visible = idx == 1;
        pVisuals.Visible = idx == 2;
        tabControl.ForeColor = idx == 0 ? GOLD : GOLD_DIM;
        tabGame.ForeColor    = idx == 1 ? GOLD : GOLD_DIM;
        tabVisuals.ForeColor = idx == 2 ? GOLD : GOLD_DIM;
    }

    Label Header(Panel p, string text, int x, int y, int w) {
        var l = new Label();
        l.Text = text;
        l.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        l.ForeColor = TEXT;
        l.SetBounds(x, y, w, 18);
        var line = new Panel();
        line.SetBounds(x, y + 20, w, 1);
        line.BackColor = BLUE_DIM;
        p.Controls.Add(l);
        p.Controls.Add(line);
        return l;
    }

    // ---- CONTROL tab ----
    void BuildControl() {
        var p = pControl;

        Header(p, "ITEMS", 14, 6, 150);
        for (int i = 0; i < 6; i++) {
            int col = i % 2, row = i / 2, n = i + 1;
            MakeSlot(p, 14 + col * 48, 30 + row * 48, 44, 44,
                     "[HOTKEYS]|ItemSlot" + n, "[HOTKEYS]|QuickCastInventorySlot" + n, true);
        }

        Header(p, "ABILITIES CAST", 178, 6, 216);
        BuildAbilityGrid(p, 178, 30, "SkillSlot", "QuickCastSlot", true);

        lblCast = new Label();
        lblCast.Text = "CAST";
        lblCast.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        lblCast.SetBounds(200, 190, 60, 22);
        lblCast.Cursor = Cursors.Hand;
        lblCast.TextAlign = ContentAlignment.MiddleCenter;
        lblCast.Click += delegate { SetQuickMode(false); };
        p.Controls.Add(lblCast);

        var sep = new Label();
        sep.Text = "|"; sep.ForeColor = TEXT_DIM;
        sep.Font = new Font("Segoe UI", 10f);
        sep.SetBounds(262, 190, 10, 22);
        p.Controls.Add(sep);

        lblQuick = new Label();
        lblQuick.Text = "QUICKCAST";
        lblQuick.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        lblQuick.SetBounds(274, 190, 110, 22);
        lblQuick.Cursor = Cursors.Hand;
        lblQuick.TextAlign = ContentAlignment.MiddleCenter;
        lblQuick.Click += delegate { SetQuickMode(true); };
        p.Controls.Add(lblQuick);

        Header(p, "ABILITIES AUTO CAST", 178, 220, 216);
        BuildAbilityGrid(p, 178, 244, "ASkillSlot", null, false);

        MakeCheck(p, "[GAMEOPTIONS]|DoubleClickHelperEnabled", "Double Click Support", 14, 174, 160);

        int rx = 410, ry = 6;
        Header(p, "UNIT ACTIONS", rx, ry, 316); ry += 26;
        ry = BuildBindList(p, UNIT_ACTIONS, rx, ry);
        ry += 8;
        Header(p, "SELECTION", rx, ry, 316); ry += 26;
        ry = BuildBindList(p, SELECTION, rx, ry);
        ry += 8;
        Header(p, "INTERFACE", rx, ry, 316); ry += 26;
        BuildBindList(p, INTERFACE, rx, ry);

        SetQuickMode(false);
    }

    void BuildAbilityGrid(Panel p, int x0, int y0, string castPrefix, string quickPrefix, bool toggle) {
        for (int i = 0; i < 12; i++) {
            int col = i % 4, row = i / 4;
            int x = x0 + col * 50, y = y0 + row * 50;
            int slotNum = ABILITY_MAP[i];
            if (slotNum < 0) { MakePlaceholder(p, x, y, 44, 44); continue; }
            string cast = "[HOTKEYS]|" + castPrefix + slotNum;
            string quick = quickPrefix != null ? "[HOTKEYS]|" + quickPrefix + slotNum : null;
            MakeSlot(p, x, y, 44, 44, cast, quick, toggle);
        }
    }

    int BuildBindList(Panel p, string[,] fields, int x, int y) {
        int n = fields.GetLength(0);
        for (int i = 0; i < n; i++) {
            string key = "[HOTKEYS]|" + fields[i, 0];
            string label = fields[i, 1];
            MakeSlot(p, x, y, 60, 22, key, null, false);
            var lbl = new Label();
            lbl.Text = label;
            lbl.ForeColor = TEXT;
            lbl.Font = new Font("Segoe UI", 9f);
            lbl.SetBounds(x + 68, y + 3, 244, 18);
            p.Controls.Add(lbl);
            y += 26;
        }
        return y;
    }

    void MakePlaceholder(Panel p, int x, int y, int w, int h) {
        var lbl = new Label();
        lbl.Text = "NOT\nUSED";
        lbl.Font = new Font("Segoe UI", 6.5f, FontStyle.Bold);
        lbl.ForeColor = GOLD_DIM;
        lbl.BackColor = SLOT_BG;
        lbl.TextAlign = ContentAlignment.MiddleCenter;
        lbl.BorderStyle = BorderStyle.FixedSingle;
        lbl.SetBounds(x, y, w, h);
        p.Controls.Add(lbl);
    }

    void MakeSlot(Panel p, int x, int y, int w, int h, string castKey, string quickKey, bool toggleable) {
        var b = new Button();
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 2;
        b.Font = new Font("Segoe UI", h >= 40 ? 8f : 8.5f, FontStyle.Bold);
        b.SetBounds(x, y, w, h);

        var s = new BindSlot();
        s.CastKey = castKey; s.QuickKey = quickKey; s.Toggleable = toggleable; s.Btn = b;
        b.Tag = s;

        if (!values.ContainsKey(castKey)) values[castKey] = Orig(castKey);
        if (quickKey != null && !values.ContainsKey(quickKey)) values[quickKey] = Orig(quickKey);

        b.Click += delegate { Arm(s); };
        p.Controls.Add(b);
        slots.Add(s);
        Refresh(s);
    }

    void MakeCheck(Panel p, string fullKey, string label, int x, int y, int w) {
        var c = new CheckBox();
        c.Text = label;
        c.ForeColor = TEXT;
        c.Font = new Font("Segoe UI", 8.5f);
        c.FlatStyle = FlatStyle.Flat;
        c.SetBounds(x, y, w, 20);
        c.Checked = Orig(fullKey).Trim().ToLower() == "true";
        c.Tag = fullKey;
        p.Controls.Add(c);
        checkBoxes.Add(c);
    }

    string ActiveKey(BindSlot s) {
        return (quickMode && s.Toggleable && s.QuickKey != null) ? s.QuickKey : s.CastKey;
    }

    void Refresh(BindSlot s) {
        string tok = values[ActiveKey(s)];
        Button b = s.Btn;
        b.BackColor = SLOT_BG;
        if (string.IsNullOrEmpty(tok)) {
            b.Text = b.Height >= 40 ? "NOT\nUSED" : "";
            b.ForeColor = TEXT_DIM;
            b.FlatAppearance.BorderColor = GOLD_BORDER;
        } else {
            b.Text = tok;
            b.ForeColor = GREEN;
            b.FlatAppearance.BorderColor = GREEN_BORDER;
        }
    }

    void Arm(BindSlot s) {
        if (armed != null && armed != s) Refresh(armed);
        armed = s;
        s.Btn.BackColor = ARMED_BG;
        s.Btn.ForeColor = GREEN;
        s.Btn.Text = "...";
    }

    void SetQuickMode(bool q) {
        quickMode = q;
        lblCast.ForeColor = q ? TEXT_DIM : GOLD;
        lblQuick.ForeColor = q ? GREEN : TEXT_DIM;
        foreach (var s in slots) if (s.Toggleable) Refresh(s);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (armed != null) {
            var s = armed;
            if (e.KeyCode == Keys.Escape) values[ActiveKey(s)] = "";
            else if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) {
                e.Handled = true; e.SuppressKeyPress = true; return;
            } else values[ActiveKey(s)] = KeyEventToToken(e);
            armed = null;
            Refresh(s);
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    static string KeyEventToToken(KeyEventArgs e) {
        string prefix = "";
        if (e.Shift) prefix += "Shift";
        if (e.Control) prefix += "Ctrl";
        if (e.Alt) prefix += "Alt";
        Keys k = e.KeyCode;
        string t;
        if (k >= Keys.A && k <= Keys.Z) t = k.ToString();
        else if (k >= Keys.D0 && k <= Keys.D9) t = k.ToString().Substring(1);
        else if (k >= Keys.F1 && k <= Keys.F24) t = "0x" + ((int)k).ToString("x2");
        else if (k == Keys.Space) t = "space";
        else t = k.ToString();
        return prefix + t;
    }

    // ---- GAME OPTIONS tab ----
    void BuildGame() {
        var p = pGame;
        int y = 6;
        y = CheckSection(p, "GAMEPLAY", OPT_GAMEPLAY, y);
        y = CheckSection(p, "HOTKEY BEHAVIOR", OPT_HOTKEY_BEHAVIOR, y);
        CheckSection(p, "HERO OPTIONS", OPT_HERO, y);
    }

    void BuildVisuals() {
        CheckSection(pVisuals, "VISUALS", OPT_VISUALS, 6);
    }

    int CheckSection(Panel p, string title, string[,] fields, int y) {
        Header(p, title, 14, y, 700); y += 26;
        int n = fields.GetLength(0), colW = 350;
        int startY = y;
        for (int i = 0; i < n; i++) {
            int col = i % 2, row = i / 2;
            MakeCheck(p, fields[i, 0] + "|" + fields[i, 1], fields[i, 2], 20 + col * colW, startY + row * 22, colW - 12);
        }
        y = startY + ((n + 1) / 2) * 22 + 14;
        return y;
    }

    public Dictionary<string, string> GetResult() {
        var result = new Dictionary<string, string>();
        foreach (var kv in values) {
            string cur = kv.Value, orig = Orig(kv.Key);
            if (OverrideAll || cur != orig) result[kv.Key] = cur;
        }
        foreach (var cb in checkBoxes) {
            string k = (string)cb.Tag;
            bool origBool = Orig(k).Trim().ToLower() == "true";
            if (OverrideAll || cb.Checked != origBool) result[k] = cb.Checked ? "true" : "false";
        }
        return result;
    }
}
}
