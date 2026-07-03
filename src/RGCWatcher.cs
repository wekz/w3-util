// =====================================================================
// Wekz App v1.3
//  - war3 start -> borderless fullscreen + fokus u lobi + zvuk + notifikacija
//    (zahteva "-window" u RGC WC3 launch opcijama)
//  - Alt+`  = klik na SIGN (pozicija iz sign-offset.txt)
//  - Alt+F2 = kalibracija
//  - W3 pozadina: menja glavni meni Warcrafta (lokalni fajl override,
//    modeli u backgrounds\ folderu, "Allow Local Files" registry)
//  - tray meni + podesavanja (settings.ini)
// Upgrade: izmeni ovaj fajl -> build.ps1 -> install.ps1 (kao admin)
// =====================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
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
    static string optBg = "";   // ime fajla pozadine ili "" za original

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
                "bg="         + optBg + "\r\n");
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

    // ---------- W3 pozadina ----------
    static string GetW3Dir() {
        try {
            if (File.Exists(rgcPrefs)) {
                foreach (var line in File.ReadAllLines(rgcPrefs)) {
                    if (!line.StartsWith("WC3_LOCATION=")) continue;
                    string hex = line.Substring("WC3_LOCATION=".Length).Trim();
                    if (hex.Length < 4) break;
                    var sb = new StringBuilder();
                    for (int i = 0; i + 1 < hex.Length; i += 2)
                        sb.Append((char)Convert.ToInt32(hex.Substring(i, 2), 16));
                    string exe = sb.ToString().Replace('/', '\\');
                    string dir = Path.GetDirectoryName(exe);
                    if (Directory.Exists(dir)) return dir;
                }
            }
        } catch { }
        return @"C:\Program Files (x86)\Warcraft III";
    }

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

    [STAThread]
    static void Main() {
        try { File.WriteAllText(logFile, ""); } catch { }
        LoadSettings();
        Log("start (Wekz App v1.4) | elevated=" + IsElevated());
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

        // ----- Warcraft III section -----
        menu.Items.Add(new ToolStripSeparator());
        var w3Header = new ToolStripMenuItem("WARCRAFT III");
        w3Header.Enabled = false;
        w3Header.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        menu.Items.Add(w3Header);
        bgMenuRoot = new ToolStripMenuItem("    Menu background");
        BuildBgMenu();
        menu.Items.Add(bgMenuRoot);

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

    // ---------- hotkeys ----------
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
}
