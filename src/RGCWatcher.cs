// =====================================================================
// Wekz App v2.1
//  - war3 start -> borderless fullscreen + fokus u lobi + zvuk + notifikacija
//    (zahteva "-window" u RGC WC3 launch opcijama)
//  - SIGN hotkey (podesiv u Settings, default Alt+`) = klik u pozadini
//    (PostMessage - ne pomera misa, ne krade fokus; pozicija iz sign-offset.txt)
//    zvuk po stanju dugmeta: BitBlt boja vs reference iz sign-states.txt
//    (IN = rastuci ton, OUT = opadajuci; reference se uce kalibracijom)
//  - Alt+F2 = kalibracija (pozicija + IN boja dugmeta)
//  - In-game UI skinovi (uiskins\ folder, izvuceni iz Wc3styler UI.mpq)
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
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int dx, int dy, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint acc, bool inh, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder sb, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

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
    static string stateFile    = Path.Combine(baseDir, "sign-states.txt");
    static string uiskinDir    = Path.Combine(baseDir, "uiskins");
    static string uiManifest   = Path.Combine(baseDir, "uiskin-installed.txt");

    // RGC folder se trazi dinamicki (razliciti racunari = razlicite putanje):
    // 1) putanja pokrenutog rgc.exe procesa, 2) standardna instalacija,
    // 3) "Ranked Gaming Client" unutar W3 foldera
    static string rgcDirCached = null;
    static string GetRgcDir() {
        if (rgcDirCached != null && File.Exists(Path.Combine(rgcDirCached, "rgc.exe")))
            return rgcDirCached;
        try {
            foreach (var p in Process.GetProcessesByName("rgc")) {
                IntPtr h = OpenProcess(0x1000 /*QUERY_LIMITED_INFO*/, false, p.Id);
                if (h == IntPtr.Zero) continue;
                var sb = new StringBuilder(1024); int len = sb.Capacity;
                bool ok = QueryFullProcessImageName(h, 0, sb, ref len);
                CloseHandle(h);
                if (ok) { rgcDirCached = Path.GetDirectoryName(sb.ToString()); return rgcDirCached; }
            }
        } catch { }
        // (ne sme da zove GetW3Dir - taj cita RGC Preferences pa bi se vrteli ukrug)
        string std = @"C:\Program Files (x86)\Warcraft III\Ranked Gaming Client";
        if (File.Exists(Path.Combine(std, "rgc.exe"))) { rgcDirCached = std; return std; }
        return std; // poslednja nada - standardna putanja
    }
    static string RgcPrefsPath() { return Path.Combine(GetRgcDir(), "Preferences"); }

    static NotifyIcon trayIcon;
    static ToolStripMenuItem bgMenuRoot, signMenuItem;
    static ToolStripMenuItem fixLatency, fixGfx;
    static bool handled = false, notified = false, hkHeld = false, hk2Held = false;
    static DateTime hkLast = DateTime.MinValue;
    static uint vkGrave;

    // podesavanja (settings.ini)
    static bool optBorderless = true, optSound = true, optBalloon = true, optHotkey = true;
    static bool optSignMouse = false;   // fallback: stari sign metod sa pravim misem
    static string optUiSkin = "";       // in-game UI skin (folder u uiskins\) ili "" za original
    // SIGN hotkey (podesivo u Settings): default Alt+` (vk se postavlja u Main)
    static int  optSignVk = 0;
    static bool optSignAlt = true, optSignCtrl = false, optSignShift = false;

    static string HotkeyText() {
        string s = "";
        if (optSignCtrl)  s += "Ctrl+";
        if (optSignAlt)   s += "Alt+";
        if (optSignShift) s += "Shift+";
        Keys k = (Keys)optSignVk;
        string name = k == Keys.Oemtilde ? "`" : k.ToString();
        return s + name;
    }
    static string optBg = "";        // ime fajla pozadine ili "" za original
    static string optRgcRoom = "19"; // (EU) Public na ladder.rankedgaming.com

    static void Log(string m) {
        try { File.AppendAllText(logFile, DateTime.Now.ToString("HH:mm:ss") + " " + m + "\r\n"); } catch { }
    }

    // ---------- zvukovi (sintetizovani chime umesto Console.Beep) ----------
    static SoundPlayer sfxSign, sfxSignOut, sfxTick, sfxOk, sfxErr;

    // notes: { frekvencija Hz, start ms, trajanje ms, jacina 0..1 }
    static SoundPlayer BuildSound(double[][] notes) {
        const int sr = 44100;
        double totalMs = 0;
        foreach (var n in notes) totalMs = Math.Max(totalMs, n[1] + n[2]);
        int len = (int)(sr * (totalMs + 80) / 1000.0);
        double[] mix = new double[len];
        foreach (var n in notes) {
            double f = n[0], vol = n[3];
            int start = (int)(sr * n[1] / 1000.0), dur = (int)(sr * n[2] / 1000.0);
            for (int i = 0; i < dur && start + i < len; i++) {
                double t = i / (double)sr;
                double attack = i < sr * 0.003 ? i / (sr * 0.003) : 1.0;
                double env = attack * Math.Exp(-t * 11.0);
                double s = Math.Sin(2 * Math.PI * f * t)
                         + 0.30 * Math.Exp(-t * 22.0) * Math.Sin(2 * Math.PI * f * 2.0 * t)
                         + 0.12 * Math.Exp(-t * 30.0) * Math.Sin(2 * Math.PI * f * 3.0 * t);
                mix[start + i] += s * env * vol;
            }
        }
        double peak = 0.0001;
        for (int i = 0; i < len; i++) if (Math.Abs(mix[i]) > peak) peak = Math.Abs(mix[i]);
        double g = 0.55 / peak;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + len * 2);
        bw.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); bw.Write(16);
        bw.Write((short)1); bw.Write((short)1); bw.Write(sr); bw.Write(sr * 2);
        bw.Write((short)2); bw.Write((short)16);
        bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(len * 2);
        for (int i = 0; i < len; i++) bw.Write((short)(mix[i] * g * 32767.0));
        ms.Position = 0;
        var p = new SoundPlayer(ms);
        p.Load();
        return p;
    }

    static void InitSfx() {
        try {
            // sign IN: dva zvonka tona navise (B5 -> G6), kratko i cisto
            sfxSign = BuildSound(new double[][] {
                new double[] { 987.77, 0, 240, 0.9 },
                new double[] { 1567.98, 90, 300, 1.0 } });
            // sign OUT: isti timbre, obrnut smer (G6 -> B5) - jasno "odjava"
            sfxSignOut = BuildSound(new double[][] {
                new double[] { 1567.98, 0, 240, 0.9 },
                new double[] { 987.77, 90, 300, 1.0 } });
            // neutralni tik: klik je poslat ali stanje dugmeta nije prepoznato
            sfxTick = BuildSound(new double[][] {
                new double[] { 523.25, 0, 140, 1.0 } });
            // kalibracija OK: arpeggio navise C6-E6-G6
            sfxOk = BuildSound(new double[][] {
                new double[] { 1046.50, 0, 220, 0.8 },
                new double[] { 1318.51, 80, 220, 0.9 },
                new double[] { 1567.98, 160, 320, 1.0 } });
            // greska: dva tiha duboka tona nanize
            sfxErr = BuildSound(new double[][] {
                new double[] { 233.08, 0, 260, 1.0 },
                new double[] { 185.00, 120, 320, 0.9 } });
        } catch (Exception ex) { Log("sfx init error: " + ex.Message); }
    }

    static void Sfx(SoundPlayer p) {
        try { if (p != null) p.Play(); } catch { }
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
                    case "signMouse":  optSignMouse = v == "1"; break;
                    case "uiskin":     optUiSkin = v; break;
                    case "signVk":     { int vk; if (int.TryParse(v, out vk) && vk > 0) optSignVk = vk; } break;
                    case "signAlt":    optSignAlt = v == "1"; break;
                    case "signCtrl":   optSignCtrl = v == "1"; break;
                    case "signShift":  optSignShift = v == "1"; break;
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
                "signMouse="  + (optSignMouse ? "1" : "0") + "\r\n" +
                "uiskin="     + optUiSkin + "\r\n" +
                "signVk="     + optSignVk + "\r\n" +
                "signAlt="    + (optSignAlt ? "1" : "0") + "\r\n" +
                "signCtrl="   + (optSignCtrl ? "1" : "0") + "\r\n" +
                "signShift="  + (optSignShift ? "1" : "0") + "\r\n" +
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
        f.cbM.Checked = optSignMouse;
        f.SetHotkey(optSignVk, optSignAlt, optSignCtrl, optSignShift);
        f.TopMost = true;
        f.FormClosed += delegate { settingsOpen = null; };
        if (f.ShowDialog() == DialogResult.OK) {
            optBorderless = f.cbB.Checked;
            optSound      = f.cbS.Checked;
            optBalloon    = f.cbN.Checked;
            optHotkey     = f.cbH.Checked;
            optSignMouse  = f.cbM.Checked;
            optSignVk     = f.hkVk;
            optSignAlt    = f.hkAlt;
            optSignCtrl   = f.hkCtrl;
            optSignShift  = f.hkShift;
            SaveSettings();
            if (signMenuItem != null) signMenuItem.Text = "    Sign now  (" + HotkeyText() + ")";
            Log("settings: borderless=" + optBorderless + " sound=" + optSound +
                " balloon=" + optBalloon + " hotkey=" + optHotkey + " signMouse=" + optSignMouse +
                " signKey=" + HotkeyText());
        }
    }

    // ---------- RGC / Warcraft III paths (read from RGC Preferences, hex-encoded) ----------
    static string DecodeHexPref(string key) {
        try {
            string rgcPrefs = RgcPrefsPath();
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

    // ---------- in-game UI skinovi (izvuceno iz Wc3styler UI.mpq) ----------
    // Svaki skin u uiskins\<Ime>\ ima genericka imena fajlova (UITile01.blp,
    // Cursor.blp, tooltip-border.blp...). Primena = kopiranje na prave lokalne
    // putanje u W3 folderu za sve 4 rase ("Allow Local Files" ih ucitava umesto
    // MPQ verzija). uiskin-installed.txt pamti sta je kopirano radi ciscenja.
    static ToolStripMenuItem uiMenuRoot;
    static readonly string[] Races  = { "Human", "Orc", "NightElf", "Undead" };
    static readonly string[] RacesL = { "human", "orc", "nightelf", "undead" };

    static List<string> UiTargets(string generic) {
        var t = new List<string>();
        string g = generic.ToLower();
        for (int i = 0; i < 4; i++) {
            string R = Races[i], r = RacesL[i];
            switch (g) {
                case "uitile01.blp": case "uitile02.blp": case "uitile03.blp": case "uitile04.blp":
                    t.Add(@"UI\Console\" + R + @"\" + R + "UITile" + g.Substring(6, 2) + ".blp"); break;
                case "uitile-inventorycover.blp":
                    t.Add(@"UI\Console\" + R + @"\" + R + "UITile-InventoryCover.blp"); break;
                case "uitile-timeindicatorframe.blp":
                    t.Add(@"UI\Console\" + R + @"\" + R + "UITile-TimeIndicatorFrame.blp"); break;
                case "cursor.blp":
                    t.Add(@"UI\Cursor\" + R + "Cursor.blp"); break;
                case "options-menu-border.blp":
                    t.Add(@"UI\Widgets\EscMenu\" + R + @"\" + r + "-options-menu-border.blp"); break;
                case "options-menu-background.blp":
                    t.Add(@"UI\Widgets\EscMenu\" + R + @"\" + r + "-options-menu-background.blp"); break;
                case "options-button-highlight.blp":
                    t.Add(@"UI\Widgets\EscMenu\" + R + @"\" + r + "-options-button-highlight.blp"); break;
                case "cinematic-border.blp":
                    t.Add(@"UI\Widgets\EscMenu\" + R + @"\" + r + "-cinematic-border.blp"); break;
                case "console-buttonstates2.blp":
                    t.Add(@"UI\Widgets\Console\" + R + @"\" + r + "-console-buttonstates2.blp"); break;
            }
        }
        switch (g) {
            case "tooltip-border.blp":       t.Add(@"UI\Widgets\ToolTips\Human\human-tooltip-border.blp"); break;
            case "tooltipgoldicon.blp":      t.Add(@"UI\Widgets\ToolTips\Human\ToolTipGoldIcon.blp"); break;
            case "tooltiplumbericon.blp":    t.Add(@"UI\Widgets\ToolTips\Human\ToolTipLumberIcon.blp"); break;
            case "tooltipsupplyicon.blp":    t.Add(@"UI\Widgets\ToolTips\Human\ToolTipSupplyIcon.blp"); break;
            case "options-button-border-up.blp":
                t.Add(@"UI\Widgets\EscMenu\Human\human-options-button-border-up.blp"); break;
            case "options-button-border-down.blp":
                t.Add(@"UI\Widgets\EscMenu\Human\human-options-button-border-down.blp"); break;
            case "options-button-background-disabled.blp":
                t.Add(@"UI\Widgets\EscMenu\Human\human-options-button-background-disabled.blp");
                t.Add(@"UI\Widgets\EscMenu\Undead\undead-options-button-background-disabled.blp"); break;
            case "console-button-up.blp":    t.Add(@"UI\Widgets\Console\Human\human-console-button-up.blp"); break;
            case "console-button-down.blp":  t.Add(@"UI\Widgets\Console\Human\human-console-button-down.blp"); break;
            case "console-button-highlight.blp":
                t.Add(@"UI\Widgets\Console\Human\human-console-button-highlight.blp"); break;
            case "console-button-back-active.blp":
                t.Add(@"UI\Widgets\Console\Human\human-console-button-back-active.blp"); break;
            case "console-button-back-disabled.blp":
                t.Add(@"UI\Widgets\Console\Human\human-console-button-back-disabled.blp"); break;
            case "transport-slot.blp":
                t.Add(@"UI\Console\Human\human-transport-slot.blp");
                t.Add(@"UI\Widgets\Console\Human\human-transport-slot.blp"); break;
            case "unitqueue-border.blp":
                t.Add(@"UI\Widgets\Console\Human\human-unitqueue-border.blp"); break;
            case "multipleselection-border.blp":
                t.Add(@"UI\Widgets\Console\Human\CommandButton\human-multipleselection-border.blp"); break;
            case "spellareaofeffect.blp":
                t.Add(@"ReplaceableTextures\Selection\SpellAreaOfEffect.blp");
                t.Add(@"ReplaceableTextures\Selection\SpellAreaOfEffect_NE.blp");
                t.Add(@"ReplaceableTextures\Selection\SpellAreaOfEffect_Orc.blp");
                t.Add(@"ReplaceableTextures\Selection\SpellAreaOfEffect_Undead.blp");
                t.Add(@"ReplaceableTextures\Selection\SpellAreaOfEffect_basic.blp"); break;
        }
        return t;
    }

    static void RemoveInstalledUiFiles() {
        try {
            if (!File.Exists(uiManifest)) return;
            foreach (var p in File.ReadAllLines(uiManifest)) {
                try { if (p.Trim().Length > 3 && File.Exists(p)) File.Delete(p); } catch { }
            }
            File.Delete(uiManifest);
        } catch { }
    }

    static void ApplyUiSkin(string name) {  // null/"" = original
        try {
            RemoveInstalledUiFiles();
            optUiSkin = "";
            if (!string.IsNullOrEmpty(name)) {
                string skinDir = Path.Combine(uiskinDir, name);
                string w3 = GetW3Dir();
                var installed = new List<string>();
                foreach (var f in Directory.GetFiles(skinDir)) {
                    string generic = Path.GetFileName(f);
                    if (generic.Equals("preview.jpg", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var rel in UiTargets(generic)) {
                        string target = Path.Combine(w3, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(f, target, true);
                        installed.Add(target);
                    }
                }
                File.WriteAllLines(uiManifest, installed.ToArray());
                optUiSkin = name;
                Log("ui skin: " + name + " (" + installed.Count + " fajlova)");
            } else Log("ui skin: original restored");
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Blizzard Entertainment\Warcraft III"))
                k.SetValue("Allow Local Files", 1, RegistryValueKind.DWord);
            SaveSettings();
            BuildUiMenu();
            if (optBalloon) trayIcon.ShowBalloonTip(4000, "In-game UI",
                string.IsNullOrEmpty(name)
                    ? "Original restored. Restart Warcraft to see it."
                    : "Applied: " + name.Replace('-', ' ') + ". Restart Warcraft to see it.",
                ToolTipIcon.Info);
        } catch (Exception ex) { Log("ui skin error: " + ex.Message); }
    }

    static void BuildUiMenu() {
        uiMenuRoot.DropDownItems.Clear();
        var def = new ToolStripMenuItem("Original (W3 default)");
        def.Checked = string.IsNullOrEmpty(optUiSkin);
        def.Click += delegate { ApplyUiSkin(null); };
        uiMenuRoot.DropDownItems.Add(def);
        if (Directory.Exists(uiskinDir)) {
            uiMenuRoot.DropDownItems.Add(new ToolStripSeparator());
            foreach (var d in Directory.GetDirectories(uiskinDir)) {
                string name = Path.GetFileName(d);
                var it = new ToolStripMenuItem(name.Replace('-', ' '));
                it.Tag = name;
                it.Checked = string.Equals(optUiSkin, name, StringComparison.OrdinalIgnoreCase);
                it.Click += delegate(object s, EventArgs e) {
                    ApplyUiSkin((string)((ToolStripMenuItem)s).Tag);
                };
                uiMenuRoot.DropDownItems.Add(it);
            }
        }
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
        try { SetProcessDPIAware(); } catch { }  // tacne koordinate i na 125%/150% skaliranju
        try { File.WriteAllText(logFile, ""); } catch { }
        LoadSettings();
        LoadSignStates();
        Log("start (Wekz App v2.1) | elevated=" + IsElevated());
        InitSfx();
        vkGrave = MapVirtualKey(0x29, 3);
        if (vkGrave == 0) vkGrave = 0xC0;
        if (optSignVk == 0) optSignVk = (int)vkGrave;  // default: Alt+`

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
        signMenuItem = new ToolStripMenuItem("    Sign now  (" + HotkeyText() + ")");
        signMenuItem.Click += delegate { SignClick(); };
        menu.Items.Add(signMenuItem);
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
        uiMenuRoot = new ToolStripMenuItem("    In-game UI skin");
        BuildUiMenu();
        menu.Items.Add(uiMenuRoot);
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

        var hkTimer = new Timer(); hkTimer.Interval = 30;
        hkTimer.Tick += delegate { HotkeyTick(); };
        hkTimer.Start();
        Log("hotkeys: " + HotkeyText() + " = sign klik, Alt+F2 = kalibracija | vk=0x" + optSignVk.ToString("X"));

        // prvi start na novom racunaru: objasni kalibraciju
        if (!File.Exists(offsetFile) && optBalloon)
            trayIcon.ShowBalloonTip(10000, "Wekz App - first run",
                "Open RGC, place the mouse cursor on the SIGN button and press Alt+F2 to calibrate. " +
                "After that " + HotkeyText() + " signs from anywhere - even in-game.",
                ToolTipIcon.Info);

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
        bool ctrl  = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        bool key   = (GetAsyncKeyState(optSignVk) & 0x8000) != 0;
        bool f2    = (GetAsyncKeyState(0x71) & 0x8000) != 0;

        bool mods = (!optSignAlt || alt) && (!optSignCtrl || ctrl) && (!optSignShift || shift);
        if (mods && key && optHotkey) {
            if (!hkHeld && (DateTime.Now - hkLast).TotalMilliseconds > 250) {
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

    const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    const uint CWP_SKIPINVISIBLE = 0x0001, CWP_SKIPDISABLED = 0x0002;
    const int  SW_SHOWNOACTIVATE = 4;
    const uint SRCCOPY = 0x00CC0020;

    // prosecna boja malog regiona oko tacke (screen koordinate) - cita se iz
    // window DC-a preko BitBlt, pa radi i dok je RGC prekriven igrom
    // (PrintWindow i UIA ne rade na RGC-ovom Qt5 prozoru)
    static Color GetButtonColor(IntPtr h, RECT wr, int x, int y) {
        const int PW = 40, PH = 16;
        int rx = x - wr.L - PW / 2, ry = y - wr.T - PH / 2;
        using (var bmp = new Bitmap(PW, PH))
        {
            using (var g = Graphics.FromImage(bmp)) {
                IntPtr dest = g.GetHdc();
                IntPtr src = GetWindowDC(h);
                bool ok = BitBlt(dest, 0, 0, PW, PH, src, rx, ry, SRCCOPY);
                ReleaseDC(h, src);
                g.ReleaseHdc(dest);
                if (!ok) return Color.Empty;
            }
            long r = 0, gr = 0, b = 0;
            int n = 0;
            for (int py = 0; py < PH; py += 2)
                for (int px = 0; px < PW; px += 2) {
                    Color c = bmp.GetPixel(px, py);
                    r += c.R; gr += c.G; b += c.B; n++;
                }
            return Color.FromArgb((int)(r / n), (int)(gr / n), (int)(b / n));
        }
    }

    // ---------- sign stanje: referentne boje dugmeta (sign-states.txt) ----------
    // Neki skinovi ne menjaju boju dugmeta (SIGN i OUT su oba zelena, razlika je
    // samo u tekstu), pa se stanje prepoznaje poredjenjem sa naucenim referentnim
    // prosecima: "in" = boja dok pise SIGN, "out" = boja dok pise OUT.
    static Color refIn = Color.Empty, refOut = Color.Empty;
    static bool hasIn = false, hasOut = false;

    static void LoadSignStates() {
        try {
            hasIn = hasOut = false;
            if (!File.Exists(stateFile)) return;
            foreach (var line in File.ReadAllLines(stateFile)) {
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                string k = line.Substring(0, eq).Trim();
                string[] v = line.Substring(eq + 1).Split(',');
                if (v.Length != 3) continue;
                var c = Color.FromArgb(int.Parse(v[0].Trim()), int.Parse(v[1].Trim()), int.Parse(v[2].Trim()));
                if (k == "in")  { refIn = c;  hasIn = true; }
                if (k == "out") { refOut = c; hasOut = true; }
            }
        } catch { }
    }

    static void SaveSignStates() {
        try {
            var sb = new StringBuilder();
            if (hasIn)  sb.Append("in="  + refIn.R  + "," + refIn.G  + "," + refIn.B  + "\r\n");
            if (hasOut) sb.Append("out=" + refOut.R + "," + refOut.G + "," + refOut.B + "\r\n");
            File.WriteAllText(stateFile, sb.ToString());
        } catch { }
    }

    static int ColorDist(Color a, Color b) {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    // 1 = klik ce potpisati (dugme u SIGN stanju), 2 = odjaviti (OUT stanje), 0 = nepoznato
    static int ClassifyButton(Color avg) {
        if (avg.IsEmpty || (avg.R == 0 && avg.G == 0 && avg.B == 0)) return 0;
        if (hasIn && hasOut) {
            int dIn = ColorDist(avg, refIn), dOut = ColorDist(avg, refOut);
            if (Math.Min(dIn, dOut) <= 25 && dIn != dOut) return dIn < dOut ? 1 : 2;
        } else if (hasIn) {
            int dIn = ColorDist(avg, refIn);
            if (dIn <= 8) return 1;
            if (dIn <= 60) {
                // drugo stanje istog dugmeta - nauci ga kao OUT
                refOut = avg; hasOut = true; SaveSignStates();
                Log("sign: learned OUT color R" + avg.R + " G" + avg.G + " B" + avg.B);
                return 2;
            }
        }
        // fallback za skinove gde je SIGN zeleno a OUT crveno
        if (avg.G > avg.R + 30 && avg.G > avg.B + 30) return 1;
        if (avg.R > avg.G + 30) return 2;
        return 0;
    }

    static void SignClick() {
        var rgc = FindWithWindow("rgc");
        if (rgc == null) { Log("sign: RGC window not found"); return; }
        if (!File.Exists(offsetFile)) {
            Log("sign: not calibrated - place cursor on SIGN and press Alt+F2");
            Sfx(sfxErr);
            return;
        }
        try {
            string[] parts = File.ReadAllText(offsetFile).Trim().Split(',');
            int dx = int.Parse(parts[0]), dy = int.Parse(parts[1]);

            IntPtr h = rgc.MainWindowHandle;
            if (IsIconic(h)) {
                // vrati prozor bez otimanja fokusa da koordinate budu validne
                ShowWindow(h, SW_SHOWNOACTIVATE);
                System.Threading.Thread.Sleep(150);
            }
            RECT wr; GetWindowRect(h, out wr);
            int x = wr.R - dx, y = wr.B - dy;

            // stanje dugmeta PRE klika odredjuje sta klik radi (i koji zvuk ide)
            Color avg = GetButtonColor(h, wr, x, y);
            int action = ClassifyButton(avg);

            if (optSignMouse) SignClickMouse(h, x, y, dx, dy);
            else SignClickBackground(h, x, y, dx, dy);

            Log(string.Format("sign: {0} (avg R{1} G{2} B{3})",
                action == 1 ? "IN" : action == 2 ? "OUT" : "UNKNOWN", avg.R, avg.G, avg.B));
            Sfx(action == 1 ? sfxSign : action == 2 ? sfxSignOut : sfxTick);

            // dijagnostika: boja dugmeta ~400ms posle klika (da se vidi preokret u logu)
            var vt = new Timer(); vt.Interval = 400;
            vt.Tick += delegate {
                vt.Stop(); vt.Dispose();
                try {
                    RECT wr2; GetWindowRect(h, out wr2);
                    Color after = GetButtonColor(h, wr2, wr2.R - dx, wr2.B - dy);
                    // posle IN klika dugme je u OUT stanju - iskoristi za ucenje
                    if (action == 1 && hasIn && !hasOut && !after.IsEmpty &&
                        ColorDist(after, refIn) > 8 && ColorDist(after, refIn) <= 60) {
                        refOut = after; hasOut = true; SaveSignStates();
                        Log("sign: learned OUT color R" + after.R + " G" + after.G + " B" + after.B);
                    }
                    int ac = ClassifyButton(after);
                    Log(string.Format("sign: after-click avg R{0} G{1} B{2} -> {3}",
                        after.R, after.G, after.B,
                        ac == 1 ? "SIGN state" : ac == 2 ? "OUT state" : "unknown"));
                } catch { }
            };
            vt.Start();
        } catch (Exception ex) { Log("sign error: " + ex.Message); }
    }

    // klik bez fokusa i bez pomeranja misa: PostMessage direktno RGC prozoru
    static void SignClickBackground(IntPtr top, int x, int y, int dx, int dy) {
        // spusti se do najdubljeg child prozora ispod tacke (VCL kontrole su child prozori)
        IntPtr target = top;
        var pt = new POINT { X = x, Y = y };
        for (int i = 0; i < 16; i++) {
            var cp = pt;
            ScreenToClient(target, ref cp);
            IntPtr child = ChildWindowFromPointEx(target, cp, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED);
            if (child == IntPtr.Zero || child == target) break;
            target = child;
        }
        var client = pt;
        ScreenToClient(target, ref client);
        IntPtr lp = (IntPtr)(((client.Y & 0xFFFF) << 16) | (client.X & 0xFFFF));

        PostMessage(target, WM_MOUSEMOVE, IntPtr.Zero, lp);
        PostMessage(target, WM_LBUTTONDOWN, (IntPtr)1 /*MK_LBUTTON*/, lp);
        PostMessage(target, WM_LBUTTONUP, IntPtr.Zero, lp);

        Log(string.Format("sign: background click at ({0},{1}) [offset right-{2} bottom-{3}] target=0x{4:X}",
            x, y, dx, dy, target.ToInt64()));
    }

    // stari metod (fallback): fokusira RGC i klikce pravim misem
    static void SignClickMouse(IntPtr h, int x, int y, int dx, int dy) {
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

        Log(string.Format("sign: mouse click at ({0},{1}) [offset right-{2} bottom-{3}]", x, y, dx, dy));
    }

    static void Calibrate() {
        var rgc = FindWithWindow("rgc");
        if (rgc == null) { Log("calibrate: RGC window not found"); return; }
        RECT wr; GetWindowRect(rgc.MainWindowHandle, out wr);
        POINT cur; GetCursorPos(out cur);
        if (cur.X < wr.L || cur.X > wr.R || cur.Y < wr.T || cur.Y > wr.B) {
            Log(string.Format("calibrate: cursor ({0},{1}) is outside the RGC window", cur.X, cur.Y));
            Sfx(sfxErr);
            return;
        }
        int dx = wr.R - cur.X, dy = wr.B - cur.Y;
        File.WriteAllText(offsetFile, dx + "," + dy);
        Log(string.Format("calibrate OK: SIGN at (right-{0}, bottom-{1})", dx, dy));
        // dugme prilikom kalibracije pise SIGN -> zapamti kao IN boju, OUT se uci sam
        Color c = GetButtonColor(rgc.MainWindowHandle, wr, cur.X, cur.Y);
        if (!c.IsEmpty) {
            refIn = c; hasIn = true; hasOut = false; SaveSignStates();
            Log("calibrate: IN color R" + c.R + " G" + c.G + " B" + c.B);
        }
        Sfx(sfxOk);
    }
}

// ---------- Settings prozor ----------
class SettingsForm : Form {
    public CheckBox cbB, cbS, cbN, cbH, cbM;
    public int hkVk; public bool hkAlt, hkCtrl, hkShift;
    Button btnHk;
    bool capturing = false;

    public SettingsForm() {
        Text = "Wekz App - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(340, 300);
        BackColor = Color.FromArgb(240, 240, 238);
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        cbB = Mk("Borderless Warcraft (auto-lobby)", 22);
        cbS = Mk("Sound when a game starts", 52);
        cbN = Mk("Windows notification", 82);
        cbH = Mk("SIGN hotkey enabled", 112);
        cbM = Mk("Old sign method (moves mouse, alt-tabs)", 142);

        var lbl = new Label();
        lbl.Text = "SIGN key:";
        lbl.ForeColor = Color.FromArgb(30, 30, 28);
        lbl.SetBounds(24, 180, 70, 24);
        lbl.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(lbl);

        btnHk = new Button();
        btnHk.FlatStyle = FlatStyle.Flat;
        btnHk.FlatAppearance.BorderColor = Color.FromArgb(198, 198, 193);
        btnHk.BackColor = Color.White;
        btnHk.ForeColor = Color.FromArgb(30, 30, 28);
        btnHk.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        btnHk.SetBounds(100, 178, 220, 28);
        btnHk.Click += delegate {
            capturing = true;
            btnHk.Text = "Press keys...  (Esc = cancel)";
            btnHk.BackColor = Color.FromArgb(255, 250, 205);
        };
        Controls.Add(btnHk);

        KeyDown += delegate(object s, KeyEventArgs e) {
            if (!capturing) return;
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Menu || e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey)
                return;  // sam modifikator - cekaj glavni taster
            if (e.KeyCode == Keys.Escape) { StopCapture(); return; }
            hkVk = (int)e.KeyCode;
            hkAlt = e.Alt; hkCtrl = e.Control; hkShift = e.Shift;
            StopCapture();
        };

        var save = new Button();
        save.Text = "SAVE";
        save.FlatStyle = FlatStyle.Flat;
        save.FlatAppearance.BorderSize = 0;
        save.BackColor = Color.FromArgb(63, 217, 104);
        save.ForeColor = Color.FromArgb(13, 43, 22);
        save.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        save.SetBounds(20, 236, 300, 40);
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

    public void SetHotkey(int vk, bool alt, bool ctrl, bool shift) {
        hkVk = vk; hkAlt = alt; hkCtrl = ctrl; hkShift = shift;
        btnHk.Text = ComboText();
    }

    void StopCapture() {
        capturing = false;
        btnHk.Text = ComboText();
        btnHk.BackColor = Color.White;
    }

    string ComboText() {
        string s = "";
        if (hkCtrl)  s += "Ctrl+";
        if (hkAlt)   s += "Alt+";
        if (hkShift) s += "Shift+";
        Keys k = (Keys)hkVk;
        return s + (k == Keys.Oemtilde ? "`" : k.ToString());
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
