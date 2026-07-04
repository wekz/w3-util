// =====================================================================
// Wekz App v1.6
//  - war3 start -> borderless fullscreen + fokus u lobi + zvuk + notifikacija
//    (zahteva "-window" u RGC WC3 launch opcijama)
//  - Alt+`  = klik na SIGN (pozicija iz sign-offset.txt)
//  - Alt+F2 = kalibracija
//  - W3 pozadina: menja glavni meni Warcrafta (lokalni fajl override,
//    modeli u backgrounds\ folderu, "Allow Local Files" registry)
//  - RGC stats: cita username iz RGC Preferences, scrapuje
//    ladder.rankedgaming.com i prikazuje mali popup (+ search)
//  - tray meni + podesavanja (settings.ini)
//  (Custom hotkeys editor uklonjen u v1.6 - nije radio pouzdano)
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
    static ToolStripMenuItem fixLatency, fixGfx;
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

    // ---------- war3 4GB (LAA) patch ----------
    static void ApplyLaaPatch() {
        try {
            string exe = Path.Combine(GetW3Dir(), "war3.exe");
            if (!File.Exists(exe)) {
                MessageBox.Show("war3.exe not found:\n" + exe, "4GB patch",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Process.GetProcessesByName("war3").Length > 0) {
                MessageBox.Show("Warcraft III is running.\nClose the game first, then apply the patch.",
                    "4GB patch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            byte[] bytes = File.ReadAllBytes(exe);
            int peOff = BitConverter.ToInt32(bytes, 0x3C);
            int charOff = peOff + 4 + 18;
            ushort chars = BitConverter.ToUInt16(bytes, charOff);
            if ((chars & 0x20) != 0) {
                MessageBox.Show("Already patched - war3.exe can use 4 GB of memory.",
                    "4GB patch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string backup = exe + ".pre-LAA-backup";
            if (!File.Exists(backup)) File.Copy(exe, backup);
            ushort patched = (ushort)(chars | 0x20);
            bytes[charOff] = (byte)(patched & 0xFF);
            bytes[charOff + 1] = (byte)(patched >> 8);
            File.WriteAllBytes(exe, bytes);
            Log("LAA patch applied to " + exe + ": 0x" + chars.ToString("X4") + " -> 0x" + patched.ToString("X4"));
            MessageBox.Show("Done! war3.exe can now use 4 GB of memory.\n" +
                "This fixes the \"Not enough memory / SFile.cpp\" crash.\n\n" +
                "Backup saved as:\n" + backup,
                "4GB patch", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (UnauthorizedAccessException) {
            MessageBox.Show("Access denied writing to war3.exe.\n" +
                "Start Wekz App as administrator (or via the scheduled task) and try again.",
                "4GB patch", MessageBoxButtons.OK, MessageBoxIcon.Error);
        } catch (Exception ex) {
            Log("LAA patch error: " + ex.Message);
            MessageBox.Show("Patch failed: " + ex.Message, "4GB patch",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------- W3 fixes: undo LAA, latency, graphics, mod cleanup ----------
    const string LAYERS_KEY = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    const string GFX_FLAGS = "~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE";
    const string TCP_IF_KEY = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    static string War3ExePath() { return Path.Combine(GetW3Dir(), "war3.exe"); }

    static void UpdateFixChecks() {
        try { fixLatency.Checked = IsLatencyFixApplied(); } catch { }
        try { fixGfx.Checked = IsGfxFixApplied(); } catch { }
    }

    static void UndoLaaPatch() {
        try {
            string exe = War3ExePath();
            string backup = exe + ".pre-LAA-backup";
            if (!File.Exists(backup)) {
                MessageBox.Show("No backup found:\n" + backup, "4GB patch",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Process.GetProcessesByName("war3").Length > 0) {
                MessageBox.Show("Warcraft III is running.\nClose the game first.",
                    "4GB patch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            File.Copy(backup, exe, true);
            Log("LAA patch reverted, original war3.exe restored");
            MessageBox.Show("Original war3.exe restored from backup.", "4GB patch",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) {
            MessageBox.Show("Restore failed: " + ex.Message, "4GB patch",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static bool IsLatencyFixApplied() {
        using (var ifs = Registry.LocalMachine.OpenSubKey(TCP_IF_KEY)) {
            if (ifs == null) return false;
            foreach (var name in ifs.GetSubKeyNames()) {
                using (var k = ifs.OpenSubKey(name)) {
                    if (k == null) continue;
                    object v = k.GetValue("TcpAckFrequency");
                    if (v is int && (int)v == 1) return true;
                }
            }
        }
        return false;
    }

    static void ToggleLatencyFix() {
        try {
            bool applied = IsLatencyFixApplied();
            using (var ifs = Registry.LocalMachine.OpenSubKey(TCP_IF_KEY, true)) {
                if (ifs == null) throw new Exception("TCP interfaces key not found");
                foreach (var name in ifs.GetSubKeyNames()) {
                    using (var k = ifs.OpenSubKey(name, true)) {
                        if (k == null) continue;
                        if (applied) {
                            k.DeleteValue("TcpAckFrequency", false);
                            k.DeleteValue("TCPNoDelay", false);
                        } else {
                            k.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                            k.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            Log("latency fix " + (applied ? "removed" : "applied"));
            MessageBox.Show(applied
                ? "Latency fix removed.\nRestart Windows for it to take effect."
                : "Latency fix applied (TcpAckFrequency / TCPNoDelay).\n" +
                  "Lowers in-game delay on RGC/LAN.\n\n" +
                  "Restart Windows (or disable/enable your network adapter) for it to take effect.",
                "Latency fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (UnauthorizedAccessException) {
            MessageBox.Show("Access denied - administrator rights required.\n" +
                "Start Wekz App via the scheduled task and try again.",
                "Latency fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
        } catch (Exception ex) {
            MessageBox.Show("Failed: " + ex.Message, "Latency fix",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static bool IsGfxFixApplied() {
        using (var k = Registry.CurrentUser.OpenSubKey(LAYERS_KEY)) {
            return k != null && k.GetValue(War3ExePath()) != null;
        }
    }

    static void ToggleGfxFix() {
        try {
            bool applied = IsGfxFixApplied();
            using (var k = Registry.CurrentUser.CreateSubKey(LAYERS_KEY)) {
                if (applied) k.DeleteValue(War3ExePath(), false);
                else k.SetValue(War3ExePath(), GFX_FLAGS, RegistryValueKind.String);
            }
            Log("gfx fix " + (applied ? "removed" : "applied") + " for " + War3ExePath());
            MessageBox.Show(applied
                ? "Smooth graphics fix removed.\nTakes effect on next game launch."
                : "Smooth graphics fix applied:\n" +
                  "- DPI scaling override (no blurry picture on 125%/150% scaling)\n" +
                  "- fullscreen optimizations disabled (less stutter)\n\n" +
                  "Takes effect on next game launch.",
                "Smooth graphics", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) {
            MessageBox.Show("Failed: " + ex.Message, "Smooth graphics",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void CleanModFiles() {
        try {
            string w3 = GetW3Dir();
            string dest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "W3-removed-mods");
            string[] known = { "audiere.dll" };
            var moved = new List<string>();
            foreach (var f in known) {
                string src = Path.Combine(w3, f);
                if (!File.Exists(src)) continue;
                Directory.CreateDirectory(dest);
                string target = Path.Combine(dest, f);
                if (File.Exists(target)) File.Delete(target);
                File.Move(src, target);
                moved.Add(f);
            }
            Log("clean mod files: " + (moved.Count > 0 ? string.Join(", ", moved.ToArray()) : "nothing found"));
            MessageBox.Show(moved.Count > 0
                ? "Moved out of the game folder:\n" + string.Join("\n", moved.ToArray()) +
                  "\n\nSaved to: " + dest + "\nRGC should start the game normally now."
                : "No known problematic files found - game folder looks clean.",
                "Clean mod files", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) {
            MessageBox.Show("Failed: " + ex.Message, "Clean mod files",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        Log("start (Wekz App v1.6) | elevated=" + IsElevated());
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
        var fixesRoot = new ToolStripMenuItem("    Fixes");
        fixesRoot.DropDownItems.Add("4GB memory patch (SFile.cpp crash)", null, delegate { ApplyLaaPatch(); });
        fixesRoot.DropDownItems.Add("Undo 4GB patch (restore backup)", null, delegate { UndoLaaPatch(); });
        fixesRoot.DropDownItems.Add(new ToolStripSeparator());
        fixLatency = new ToolStripMenuItem("Latency fix (lower in-game delay)");
        fixLatency.Click += delegate { ToggleLatencyFix(); };
        fixesRoot.DropDownItems.Add(fixLatency);
        fixGfx = new ToolStripMenuItem("Smooth graphics (DPI / fullscreen fix)");
        fixGfx.Click += delegate { ToggleGfxFix(); };
        fixesRoot.DropDownItems.Add(fixGfx);
        fixesRoot.DropDownItems.Add(new ToolStripSeparator());
        fixesRoot.DropDownItems.Add("Clean mod files (RGC start error)", null, delegate { CleanModFiles(); });
        fixesRoot.DropDownOpening += delegate { UpdateFixChecks(); };
        menu.Items.Add(fixesRoot);

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

}
