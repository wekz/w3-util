// =====================================================================
// Wekzy Setup - sve-u-jednom instalator (za USB)
// Jedan dupli klik (UAC) instalira:
//  - "Wekzy Dark" skin u RGC
//  - custom zvukove u RGC
//  - Wekz App (tray) u Documents\RGC-tools + Scheduled Task (autostart)
//  - JetBrains Mono font (per-user)
// Payload (skin/zvukovi/app/fontovi) je zapakovan unutar ovog exe-a.
// Build: build-setup.ps1
// =====================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WekzySetup {
static class Setup {
    [STAThread]
    static void Main() {
        try {
            // 1) nadji RGC
            string rgc = @"C:\Program Files (x86)\Warcraft III\Ranked Gaming Client";
            if (!File.Exists(Path.Combine(rgc, "rgc.exe"))) {
                MessageBox.Show("Ne mogu da nadjem RGC na standardnoj putanji.\nIzaberi rgc.exe rucno.",
                    "Wekzy Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                var ofd = new OpenFileDialog();
                ofd.Filter = "RGC klijent (rgc.exe)|rgc.exe";
                ofd.Title = "Pronadji rgc.exe";
                if (ofd.ShowDialog() != DialogResult.OK) return;
                rgc = Path.GetDirectoryName(ofd.FileName);
            }

            if (MessageBox.Show(
                "Instaliram u:\n" + rgc + "\n\n- skin 'Wekzy Dark'\n- custom zvukove\n- Wekz App (autostart)\n- JetBrains Mono font\n\nNastavi?",
                "Wekzy Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            // 2) raspakuj payload iz sopstvenog exe-a
            string temp = Path.Combine(Path.GetTempPath(), "wekzy-setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(temp);
            string zipPath = Path.Combine(temp, "payload.zip");
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var f = File.Create(zipPath)) {
                if (s == null) throw new Exception("payload.zip nije nadjen u exe-u");
                s.CopyTo(f);
            }
            ZipFile.ExtractToDirectory(zipPath, temp);

            // 3) skin
            CopyDir(Path.Combine(temp, "skin"), Path.Combine(rgc, @"skins\Wekzy Dark"));

            // 4) zvukovi
            CopyDir(Path.Combine(temp, "sound"), Path.Combine(rgc, "sound"));

            // 5) Wekz App
            string tools = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RGC-tools");
            foreach (var p in Process.GetProcessesByName("Wekzy")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            CopyDir(Path.Combine(temp, "tools"), tools);

            // 6) fontovi (per-user, bez admina bi takodje radilo)
            string fontDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");
            Directory.CreateDirectory(fontDir);
            string fsrc = Path.Combine(temp, "fonts");
            if (Directory.Exists(fsrc)) {
                using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts")) {
                    foreach (var f2 in Directory.GetFiles(fsrc, "*.ttf")) {
                        string dst = Path.Combine(fontDir, Path.GetFileName(f2));
                        File.Copy(f2, dst, true);
                        reg.SetValue(Path.GetFileNameWithoutExtension(f2).Replace("JetBrainsMono-", "JetBrains Mono ") + " (TrueType)", dst);
                    }
                }
            }

            // 7) scheduled task (autostart kao admin) + pokreni odmah
            string exe = Path.Combine(tools, "Wekzy.exe");
            Run("schtasks", "/Create /F /TN \"RGC Game Watcher\" /TR \"\\\"" + exe + "\\\"\" /SC ONLOGON /RL HIGHEST");
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = tools, UseShellExecute = true });

            try { Directory.Delete(temp, true); } catch { }

            MessageBox.Show(
                "Instalirano!\n\n1. Pokreni RGC i u Preferences izaberi skin 'Wekzy Dark'\n2. U WC3 launch opcije upisi: -window\n3. Wekz App je u system tray-u (Alt+F2 = kalibracija SIGN dugmeta)\n\nGL HF!",
                "Wekzy Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) {
            MessageBox.Show("Greska: " + ex.Message, "Wekzy Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void CopyDir(string src, string dst) {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dst + d.Substring(src.Length));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, dst + f.Substring(src.Length), true);
    }

    static void Run(string exe, string args) {
        var psi = new ProcessStartInfo(exe, args);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        using (var p = Process.Start(psi)) p.WaitForExit();
    }
}
}
