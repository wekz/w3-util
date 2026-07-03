# =====================================================================
# RGC Game Watcher v5
#  - war3 start -> borderless fullscreen + fokus u lobi + zvuk + notifikacija
#    (zahteva "-window" u RGC WC3 launch opcijama)
#  - Alt+` (taster iznad Tab) -> postavi mode na SD i klikni SIGN
#    (jedan pritisak = jedan sign; klikovi na koordinate iz layouta skina)
# Gasenje: desni klik na tray ikonicu -> Exit
# =====================================================================

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]  public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]  public static extern uint  MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")]  public static extern bool  PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")]  public static extern void  keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")]  public static extern bool  GetClientRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [DllImport("user32.dll")]  public static extern bool  ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")]  public static extern bool  GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")]  public static extern bool  GetCursorPos(out POINT p);
    [DllImport("user32.dll")]  public static extern bool  SetCursorPos(int x, int y);
    [DllImport("user32.dll")]  public static extern void  mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    public const int  GWL_STYLE      = -16;
    public const int  WS_CAPTION     = 0x00C00000;
    public const int  WS_THICKFRAME  = 0x00040000;
    public const int  WS_MINIMIZEBOX = 0x00020000;
    public const int  WS_MAXIMIZEBOX = 0x00010000;
    public const int  WS_SYSMENU     = 0x00080000;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW   = 0x0040;
    public const uint SWP_NOZORDER     = 0x0004;
}
"@

$alertWav = Join-Path $PSScriptRoot 'game-alert.wav'
$logFile  = Join-Path $PSScriptRoot 'watcher.log'
Set-Content $logFile ''   # svez log na svakom startu
function Log([string]$m) { Add-Content $logFile "$(Get-Date -Format 'HH:mm:ss') $m" }
$elev = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Log "watcher start | elevated=$elev"

$icon = New-Object System.Windows.Forms.NotifyIcon
$icon.Icon = [System.Drawing.SystemIcons]::Information
$icon.Text = 'RGC Game Watcher - aktivan'
$icon.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$exitItem = $menu.Items.Add('Exit')
$exitItem.add_Click({ $icon.Visible = $false; [System.Windows.Forms.Application]::Exit() })
$icon.ContextMenuStrip = $menu

function Set-Borderless([System.Diagnostics.Process]$proc) {
    $proc.Refresh()
    $h = $proc.MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { return $false }

    $style = [Win32]::GetWindowLong($h, [Win32]::GWL_STYLE)
    $newStyle = $style -band (-bnot ([Win32]::WS_CAPTION -bor [Win32]::WS_THICKFRAME -bor `
                [Win32]::WS_MINIMIZEBOX -bor [Win32]::WS_MAXIMIZEBOX -bor [Win32]::WS_SYSMENU))
    if ($newStyle -ne $style) {
        [Win32]::SetWindowLong($h, [Win32]::GWL_STYLE, $newStyle) | Out-Null
    }
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    [Win32]::SetWindowPos($h, [IntPtr]::Zero, $b.X, $b.Y, $b.Width, $b.Height,
        ([Win32]::SWP_FRAMECHANGED -bor [Win32]::SWP_SHOWWINDOW -bor [Win32]::SWP_NOZORDER)) | Out-Null
    [Win32]::ShowWindow($h, 9) | Out-Null      # SW_RESTORE
    [Win32]::SetForegroundWindow($h) | Out-Null
    return $true
}

# jednokratno na startu: VRATI naslovnu traku RGC prozoru ako je skinuta
function Restore-Titlebar {
    $rgc = Get-Process -Name rgc -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $rgc) { return }
    $h = $rgc.MainWindowHandle
    $style = [Win32]::GetWindowLong($h, [Win32]::GWL_STYLE)
    if (-not ($style -band [Win32]::WS_CAPTION)) {
        [Win32]::SetWindowLong($h, [Win32]::GWL_STYLE, ($style -bor [Win32]::WS_CAPTION)) | Out-Null
        [Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 0, 0,
            ([Win32]::SWP_FRAMECHANGED -bor 0x0001 -bor 0x0002 -bor [Win32]::SWP_NOZORDER)) | Out-Null
        Log "RGC traka VRACENA"
    }
}
Restore-Titlebar

# ===== SIGN hotkey =====
# Alt+F2 = kalibracija: namesti kursor na SIGN dugme i pritisni -> pamti se
#          pozicija relativno od DONJEG DESNOG ugla RGC prozora
# Alt+`  = klik na zapamcenu poziciju (radi i kad se prozor pomeri/promeni velicinu)
$offsetFile = Join-Path $PSScriptRoot 'sign-offset.txt'

function Get-RgcWindow {
    Get-Process -Name rgc -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}

function Save-SignOffset {
    $rgc = Get-RgcWindow
    if (-not $rgc) { Log 'kalibracija: RGC prozor nije nadjen'; return }
    $wr = New-Object Win32+RECT
    [Win32]::GetWindowRect($rgc.MainWindowHandle, [ref]$wr) | Out-Null
    $cur = New-Object Win32+POINT
    [Win32]::GetCursorPos([ref]$cur) | Out-Null
    if ($cur.X -lt $wr.L -or $cur.X -gt $wr.R -or $cur.Y -lt $wr.T -or $cur.Y -gt $wr.B) {
        Log "kalibracija: kursor ($($cur.X),$($cur.Y)) nije unutar RGC prozora ($($wr.L),$($wr.T))-($($wr.R),$($wr.B))"
        [console]::beep(400, 300)
        return
    }
    $dx = $wr.R - $cur.X
    $dy = $wr.B - $cur.Y
    Set-Content $offsetFile "$dx,$dy"
    Log "kalibracija OK: SIGN je na (desno-$dx, dno-$dy)"
    [console]::beep(1046, 80); Start-Sleep -Milliseconds 60; [console]::beep(1568, 110)
}

function Invoke-SignClick {
    $rgc = Get-RgcWindow
    if (-not $rgc) { Log 'sign: RGC prozor nije nadjen'; return }
    if (-not (Test-Path $offsetFile)) {
        Log 'sign: nije kalibrisano - namesti kursor na SIGN i pritisni Alt+F2'
        [console]::beep(400, 300)
        return
    }
    try {
        $parts = (Get-Content $offsetFile -Raw).Trim() -split ','
        $dx = [int]$parts[0]; $dy = [int]$parts[1]

        $h = $rgc.MainWindowHandle
        $wr = New-Object Win32+RECT
        [Win32]::GetWindowRect($h, [ref]$wr) | Out-Null
        $x = $wr.R - $dx; $y = $wr.B - $dy

        [Win32]::ShowWindow($h, 9) | Out-Null
        [Win32]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 120

        $old = New-Object Win32+POINT
        [Win32]::GetCursorPos([ref]$old) | Out-Null
        [Win32]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 40
        [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
        Start-Sleep -Milliseconds 50
        [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
        Start-Sleep -Milliseconds 60
        [Win32]::SetCursorPos($old.X, $old.Y) | Out-Null

        Log "sign: klik na ($x,$y) [offset desno-$dx dno-$dy]"
        [console]::beep(1318, 70)
    } catch { Log "sign greska: $_" }
}

# hotkey: Alt + taster iznad Tab (backtick) - VK se resava iz scancode-a 0x29
# da radi nezavisno od rasporeda tastature; debounce + cooldown 800ms
$script:vkGrave = [Win32]::MapVirtualKey(0x29, 3)   # MAPVK_VSC_TO_VK_EX
if (-not $script:vkGrave) { $script:vkGrave = 0xC0 }
$hkTimer = New-Object System.Windows.Forms.Timer
$hkTimer.Interval = 75
$script:hkHeld  = $false
$script:hkLast  = [DateTime]::MinValue
$script:hk2Held = $false
$hkTimer.add_Tick({
    $alt   = [Win32]::GetAsyncKeyState(0x12) -band 0x8000            # VK_MENU
    $grave = [Win32]::GetAsyncKeyState([int]$script:vkGrave) -band 0x8000
    $f2    = [Win32]::GetAsyncKeyState(0x71) -band 0x8000            # VK_F2

    if ($alt -and $grave) {
        if (-not $script:hkHeld -and ([DateTime]::Now - $script:hkLast).TotalMilliseconds -gt 800) {
            $script:hkHeld = $true
            $script:hkLast = [DateTime]::Now
            Invoke-SignClick
        }
    } else { $script:hkHeld = $false }

    if ($alt -and $f2) {
        if (-not $script:hk2Held) { $script:hk2Held = $true; Save-SignOffset }
    } else { $script:hk2Held = $false }
})
$hkTimer.Start()
Log "hotkeys aktivni: Alt+`` = sign klik, Alt+F2 = kalibracija | vkGrave=0x$($script:vkGrave.ToString('X'))"

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1500
$script:handled  = $false
$script:notified = $false

$timer.add_Tick({

    $proc = Get-Process -Name war3 -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc) {
        if (-not $script:notified) {
            $script:notified = $true
            if (Test-Path $alertWav) {
                try { (New-Object System.Media.SoundPlayer $alertWav).Play() } catch {}
            }
            $icon.ShowBalloonTip(8000, 'DOTA - igra krece!', 'Warcraft III se podize - ulazis u lobi. GLHF!', [System.Windows.Forms.ToolTipIcon]::Info)
        }
        if (-not $script:handled) {
            try { $script:handled = Set-Borderless $proc; if ($script:handled) { Log "war3 borderless primenjen" } } catch { Log "war3 borderless greska: $_" }
        }
    }
    else {
        $script:handled  = $false
        $script:notified = $false
    }
})
$timer.Start()

try {
    [System.Windows.Forms.Application]::Run()
}
finally {
    $timer.Stop()
    $hkTimer.Stop()
    $icon.Visible = $false
    $icon.Dispose()
}
