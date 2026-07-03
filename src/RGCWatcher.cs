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
                SaveIniValues(f.GetResult());
                Log("hotkeys/options saved to " + GetDotaIniPath());
                if (optBalloon) trayIcon.ShowBalloonTip(4000, "Custom hotkeys",
                    "Saved. Reopen the in-game settings menu (or restart the game) to apply.", ToolTipIcon.Info);
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
        ClientSize = new Size(320, 300);
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
        lblWinPct.SetBounds(20, 116, 150, 18);
        Add(lblWinPct);

        lblGames = new Label();
        lblGames.Font = new Font("Segoe UI", 9f);
        lblGames.ForeColor = Color.FromArgb(120, 120, 116);
        lblGames.TextAlign = ContentAlignment.MiddleRight;
        lblGames.SetBounds(150, 116, 150, 18);
        Add(lblGames);

        lblKillsVal   = AddTile("KILLS", 20);
        lblDeathsVal  = AddTile("DEATHS", 113);
        lblAssistsVal = AddTile("ASSISTS", 206);

        lblScore = new Label();
        lblScore.Font = new Font("JetBrains Mono", 11f, FontStyle.Bold);
        lblScore.ForeColor = Color.FromArgb(39, 173, 75);
        lblScore.SetBounds(20, 204, 280, 22);
        Add(lblScore);

        lblExtra = new Label();
        lblExtra.Font = new Font("Segoe UI", 8.5f);
        lblExtra.ForeColor = Color.FromArgb(120, 120, 116);
        lblExtra.SetBounds(20, 226, 280, 18);
        Add(lblExtra);

        btnProfile = new Button();
        btnProfile.Text = "VIEW FULL PROFILE";
        btnProfile.FlatStyle = FlatStyle.Flat;
        btnProfile.FlatAppearance.BorderColor = Color.FromArgb(198, 198, 193);
        btnProfile.BackColor = Color.White;
        btnProfile.ForeColor = Color.FromArgb(30, 30, 28);
        btnProfile.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnProfile.SetBounds(20, 250, 280, 32);
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
        cap.SetBounds(x, 146, 86, 16);
        Add(cap);

        var val = new Label();
        val.Font = new Font("JetBrains Mono", 16f, FontStyle.Bold);
        val.ForeColor = Color.FromArgb(30, 30, 28);
        val.SetBounds(x, 164, 86, 30);
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

// ---------- Custom hotkeys: Dota-stil grid editor + on/off options ----------
class BindInfo {
    public string IniKey;   // "[SECTION]|KeyName"
    public string Token = "";
}

class HotkeysForm : Form {
    static readonly Color BG        = Color.FromArgb(23, 20, 16);
    static readonly Color PANEL_BG  = Color.FromArgb(23, 20, 16);
    static readonly Color TILE_BG   = Color.FromArgb(15, 13, 10);
    static readonly Color GOLD      = Color.FromArgb(224, 188, 118);
    static readonly Color GOLD_DIM  = Color.FromArgb(120, 102, 66);
    static readonly Color GOLD_BORDER = Color.FromArgb(140, 111, 60);
    static readonly Color GREEN     = Color.FromArgb(90, 230, 150);
    static readonly Color GREEN_BORDER = Color.FromArgb(63, 217, 104);
    static readonly Color TEXT_DIM  = Color.FromArgb(150, 150, 145);
    static readonly Color TEXT      = Color.FromArgb(215, 215, 210);
    static readonly Color ARMED_BG  = Color.FromArgb(40, 70, 45);

    Panel hotkeysPanel, optionsPanel;
    Label tabHotkeys, tabOptions;
    List<Button> bindButtons = new List<Button>();
    List<CheckBox> checkBoxes = new List<CheckBox>();
    Button armedButton = null;

    // ----- [HOTKEYS] fields (bare key names, "[HOTKEYS]|" prefix added when built) -----
    static readonly string[][] ITEMS = {
        new[]{"ItemSlot1","Item 1"}, new[]{"ItemSlot2","Item 2"},
        new[]{"ItemSlot3","Item 3"}, new[]{"ItemSlot4","Item 4"},
        new[]{"ItemSlot5","Item 5"}, new[]{"ItemSlot6","Item 6"}
    };
    static readonly string[][] ABILITIES = {
        new[]{"SkillSlot1","Q"}, new[]{"SkillSlot2","W"}, new[]{"SkillSlot3","E"}, new[]{"SkillSlot4","R"},
        new[]{"SkillSlot5","Slot 5"}, null, null, new[]{"SkillSlot6","Ult / 6"}
    };
    static readonly string[][] AUTOCAST = {
        new[]{"ASkillSlot1","Auto Q"}, new[]{"ASkillSlot2","Auto W"}, new[]{"ASkillSlot3","Auto E"}, new[]{"ASkillSlot4","Auto R"},
        new[]{"ASkillSlot5","Auto 5"}, null, null, new[]{"ASkillSlot6","Auto 6"}
    };
    static readonly string[][] QUICKCAST_ABILITY = {
        new[]{"QuickCastSlot1","QC Q"}, new[]{"QuickCastSlot2","QC W"}, new[]{"QuickCastSlot3","QC E"}, new[]{"QuickCastSlot4","QC R"},
        new[]{"QuickCastSlot5","QC 5"}, null, null, new[]{"QuickCastSlot6","QC 6"}
    };
    static readonly string[][] QUICKCAST_ITEM = {
        new[]{"QuickCastInventorySlot1","QC Item1"}, new[]{"QuickCastInventorySlot2","QC Item2"},
        new[]{"QuickCastInventorySlot3","QC Item3"}, new[]{"QuickCastInventorySlot4","QC Item4"},
        new[]{"QuickCastInventorySlot5","QC Item5"}, new[]{"QuickCastInventorySlot6","QC Item6"}
    };
    static readonly string[,] UNITACTIONS = {
        {"BindMove","Move"}, {"BindStop","Stop"}, {"BindHold","Hold Position"},
        {"BindPatrol","Patrol"}, {"BindAttack","Attack"}, {"BindOpenHeroSkills","Open Hero Skills"},
        {"TalentsMenuHotkey","Open Talents Menu"}, {"TeleportScrollHotkey","Use Teleport Scroll"},
        {"QuickCastAttack","Quick Attack (needs -aat)"}
    };
    static readonly string[,] SELECTION = {
        {"SelectYourHero","Select Main Hero"}, {"SelectAllUnits","Select All Units"},
        {"SelectAllOtherUnits","Select All Other Units"}, {"SelectBestCourier","Select Courier"},
        {"SelectCircleOfPower","Select Circle of Power"}
    };
    static readonly string[,] INTERFACE = {
        {"DisplayScoreboard","Scoreboard"}
    };
    static readonly string[,] OTHER = {
        {"OrderToAllControlledUnitsHotkey","Order All Units (hold)"},
        {"DisplayNeutralsSpawnAreaHotkey","Show Neutral Spawns"},
        {"DisplayTowerRangeHotkey","Show Tower Range"}
    };

    // ----- on/off toggles: {section, key, label} -----
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
    static readonly string[,] OPT_GAMEPLAY = {
        {"[GAMEOPTIONS]","AutoattackEnabled","Auto Attack Enabled"},
        {"[GAMEOPTIONS]","AutoattackDisabledByStopOnly","Auto-attack disable: Stop only"},
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
    static readonly string[,] OPT_HERO = {
        {"[HEROOPTIONS]","Juggernaut_HealingWardDoNotFollow","Juggernaut: Ward Doesn't Follow"},
        {"[HEROOPTIONS]","Meepo_NumbersOverheadClones","Meepo: Numbers Over Clones"}
    };

    public HotkeysForm(Dictionary<string, string> current) {
        Text = "Wekz App - Custom Hotkeys";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 600);
        BackColor = BG;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        var hint = new Label();
        hint.Text = "Click a slot, press your key combo. Esc clears it.";
        hint.ForeColor = TEXT_DIM;
        hint.SetBounds(16, 8, 300, 18);
        Controls.Add(hint);

        tabHotkeys = MakeTab("HOTKEYS", 320);
        tabOptions = MakeTab("OPTIONS", 410);
        tabHotkeys.Click += delegate { ShowTab(true); };
        tabOptions.Click += delegate { ShowTab(false); };

        hotkeysPanel = new Panel();
        hotkeysPanel.SetBounds(0, 34, 500, 510);
        hotkeysPanel.AutoScroll = true;
        hotkeysPanel.BackColor = PANEL_BG;
        Controls.Add(hotkeysPanel);

        optionsPanel = new Panel();
        optionsPanel.SetBounds(0, 34, 500, 510);
        optionsPanel.AutoScroll = true;
        optionsPanel.BackColor = PANEL_BG;
        optionsPanel.Visible = false;
        Controls.Add(optionsPanel);

        BuildHotkeysPanel(current);
        BuildOptionsPanel(current);
        ShowTab(true);

        var reset = new Button();
        reset.Text = "RESET TAB";
        reset.FlatStyle = FlatStyle.Flat;
        reset.FlatAppearance.BorderColor = GOLD_BORDER;
        reset.BackColor = TILE_BG;
        reset.ForeColor = GOLD;
        reset.SetBounds(16, 552, 150, 36);
        reset.Click += delegate {
            if (hotkeysPanel.Visible) foreach (var b in bindButtons) SetButtonToken(b, "");
            else foreach (var c in checkBoxes) c.Checked = false;
        };
        Controls.Add(reset);

        var save = new Button();
        save.Text = "SAVE";
        save.FlatStyle = FlatStyle.Flat;
        save.FlatAppearance.BorderSize = 0;
        save.BackColor = Color.FromArgb(63, 217, 104);
        save.ForeColor = Color.FromArgb(13, 43, 22);
        save.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        save.SetBounds(334, 552, 150, 36);
        save.Click += delegate { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(save);
        AcceptButton = save;
    }

    Label MakeTab(string text, int x) {
        var l = new Label();
        l.Text = text;
        l.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        l.SetBounds(x, 6, 90, 22);
        l.Cursor = Cursors.Hand;
        Controls.Add(l);
        return l;
    }

    void ShowTab(bool hotkeys) {
        hotkeysPanel.Visible = hotkeys;
        optionsPanel.Visible = !hotkeys;
        tabHotkeys.ForeColor = hotkeys ? GOLD : GOLD_DIM;
        tabOptions.ForeColor = !hotkeys ? GOLD : GOLD_DIM;
    }

    void BuildHotkeysPanel(Dictionary<string, string> current) {
        int y = 6;
        y = AddGridSection(hotkeysPanel, "ITEMS", ITEMS, 2, current, y);
        y = AddGridSection(hotkeysPanel, "ABILITIES (CAST)", ABILITIES, 4, current, y);
        y = AddGridSection(hotkeysPanel, "ABILITIES (AUTOCAST TOGGLE)", AUTOCAST, 4, current, y);
        y = AddGridSection(hotkeysPanel, "QUICK CAST - ABILITIES", QUICKCAST_ABILITY, 4, current, y);
        y = AddGridSection(hotkeysPanel, "QUICK CAST - ITEMS", QUICKCAST_ITEM, 2, current, y);
        y = AddListSection(hotkeysPanel, "UNIT ACTIONS", UNITACTIONS, current, y);
        y = AddListSection(hotkeysPanel, "SELECTION", SELECTION, current, y);
        y = AddListSection(hotkeysPanel, "INTERFACE", INTERFACE, current, y);
        AddListSection(hotkeysPanel, "OTHER", OTHER, current, y);
    }

    void BuildOptionsPanel(Dictionary<string, string> current) {
        int y = 6;
        y = AddCheckSection(optionsPanel, "HOTKEY BEHAVIOR", OPT_HOTKEY_BEHAVIOR, current, y);
        y = AddCheckSection(optionsPanel, "GAMEPLAY", OPT_GAMEPLAY, current, y);
        y = AddCheckSection(optionsPanel, "VISUALS", OPT_VISUALS, current, y);
        AddCheckSection(optionsPanel, "HERO OPTIONS", OPT_HERO, current, y);
    }

    Label SectionHeader(string title) {
        var lbl = new Label();
        lbl.Text = title;
        lbl.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        lbl.ForeColor = GOLD;
        return lbl;
    }

    // grid of square tiles; a null cell in fields leaves a visual gap (no control)
    int AddGridSection(Panel p, string title, string[][] fields, int cols, Dictionary<string, string> current, int y) {
        var lbl = SectionHeader(title);
        lbl.SetBounds(6, y, 460, 18);
        p.Controls.Add(lbl);
        y += 24;

        int tileW = 66, tileH = 40, gap = 6;
        int startY = y;
        for (int i = 0; i < fields.Length; i++) {
            int col = i % cols, row = i / cols;
            int x = 10 + col * (tileW + gap);
            int cy = startY + row * (tileH + gap + 14);
            if (fields[i] == null) continue;

            string iniKey = "[HOTKEYS]|" + fields[i][0];
            string shortLabel = fields[i][1];

            var btn = new Button();
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 2;
            btn.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btn.SetBounds(x, cy, tileW, tileH);

            var bi = new BindInfo();
            bi.IniKey = iniKey;
            btn.Tag = bi;
            string cur;
            SetButtonToken(btn, current.TryGetValue(iniKey, out cur) ? cur : "");
            btn.Click += delegate { ArmButton(btn); };
            p.Controls.Add(btn);
            bindButtons.Add(btn);

            var cap = new Label();
            cap.Text = shortLabel;
            cap.ForeColor = TEXT_DIM;
            cap.Font = new Font("Segoe UI", 7f);
            cap.TextAlign = ContentAlignment.MiddleCenter;
            cap.SetBounds(x, cy + tileH + 1, tileW, 13);
            p.Controls.Add(cap);
        }
        int rows = (fields.Length + cols - 1) / cols;
        y = startY + rows * (tileH + gap + 14) + 10;
        return y;
    }

    int AddListSection(Panel p, string title, string[,] fields, Dictionary<string, string> current, int y) {
        var lbl = SectionHeader(title);
        lbl.SetBounds(6, y, 460, 18);
        p.Controls.Add(lbl);
        y += 24;

        int n = fields.GetLength(0);
        for (int i = 0; i < n; i++) {
            string iniKey = "[HOTKEYS]|" + fields[i, 0];
            string label = fields[i, 1];

            var btn = new Button();
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 2;
            btn.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btn.SetBounds(16, y, 96, 24);

            var bi = new BindInfo();
            bi.IniKey = iniKey;
            btn.Tag = bi;
            string cur;
            SetButtonToken(btn, current.TryGetValue(iniKey, out cur) ? cur : "");
            btn.Click += delegate { ArmButton(btn); };
            p.Controls.Add(btn);
            bindButtons.Add(btn);

            var nameLbl = new Label();
            nameLbl.Text = label;
            nameLbl.ForeColor = TEXT;
            nameLbl.Font = new Font("Segoe UI", 9f);
            nameLbl.SetBounds(122, y + 4, 340, 20);
            p.Controls.Add(nameLbl);

            y += 30;
        }
        y += 8;
        return y;
    }

    int AddCheckSection(Panel p, string title, string[,] fields, Dictionary<string, string> current, int y) {
        var lbl = SectionHeader(title);
        lbl.SetBounds(6, y, 460, 18);
        p.Controls.Add(lbl);
        y += 24;

        int n = fields.GetLength(0);
        int colW = 230;
        int startY = y;
        for (int i = 0; i < n; i++) {
            string section = fields[i, 0], key = fields[i, 1], label = fields[i, 2];
            int col = i % 2, row = i / 2;

            var cb = new CheckBox();
            cb.Text = label;
            cb.ForeColor = TEXT;
            cb.Font = new Font("Segoe UI", 8.5f);
            cb.FlatStyle = FlatStyle.Flat;
            cb.SetBounds(16 + col * colW, startY + row * 22, colW - 10, 20);
            string fullKey = section + "|" + key;
            string cur;
            cb.Checked = current.TryGetValue(fullKey, out cur) && cur.Trim().ToLower() == "true";
            cb.Tag = fullKey;
            p.Controls.Add(cb);
            checkBoxes.Add(cb);
        }
        int rows = (n + 1) / 2;
        y = startY + rows * 22 + 14;
        return y;
    }

    void SetButtonToken(Button b, string token) {
        var bi = (BindInfo)b.Tag;
        bi.Token = token ?? "";
        b.BackColor = TILE_BG;
        if (string.IsNullOrEmpty(bi.Token)) {
            b.Text = "NOT USED";
            b.ForeColor = TEXT_DIM;
            b.FlatAppearance.BorderColor = GOLD_BORDER;
        } else {
            b.Text = bi.Token;
            b.ForeColor = GREEN;
            b.FlatAppearance.BorderColor = GREEN_BORDER;
        }
    }

    void ArmButton(Button b) {
        if (armedButton != null && armedButton != b) SetButtonToken(armedButton, ((BindInfo)armedButton.Tag).Token);
        armedButton = b;
        b.BackColor = ARMED_BG;
        b.ForeColor = GREEN;
        b.Text = "...";
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (armedButton != null) {
            Button b = armedButton;
            var bi = (BindInfo)b.Tag;
            if (e.KeyCode == Keys.Escape) {
                bi.Token = "";
            } else if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) {
                e.Handled = true; e.SuppressKeyPress = true;
                return; // cekaj pravi taster posle modifikatora
            } else {
                bi.Token = KeyEventToToken(e);
            }
            SetButtonToken(b, bi.Token);
            armedButton = null;
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
        string baseTok;
        if (k >= Keys.A && k <= Keys.Z) baseTok = k.ToString();
        else if (k >= Keys.D0 && k <= Keys.D9) baseTok = k.ToString().Substring(1);
        else if (k >= Keys.F1 && k <= Keys.F24) baseTok = "0x" + ((int)k).ToString("x2");
        else if (k == Keys.Space) baseTok = "space";
        else baseTok = k.ToString();

        return prefix + baseTok;
    }

    public Dictionary<string, string> GetResult() {
        var d = new Dictionary<string, string>();
        foreach (var b in bindButtons) {
            var bi = (BindInfo)b.Tag;
            d[bi.IniKey] = bi.Token;
        }
        foreach (var cb in checkBoxes) d[(string)cb.Tag] = cb.Checked ? "true" : "false";
        return d;
    }
}
}
