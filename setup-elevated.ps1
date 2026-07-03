# Jednokratni setup (pokrece se kao admin):
#  - uklanja stari (ne-elevated) autostart shortcut
#  - gasi postojeci watcher
#  - registruje Scheduled Task koji pokrece watcher KAO ADMIN pri logovanju
#  - odmah pokrece watcher preko taska
$ErrorActionPreference = 'Stop'
$tools = 'C:\Users\Admin\Documents\RGC-tools'
$log = Join-Path $tools 'setup.log'
Set-Content $log "$(Get-Date) setup start"
try {
    Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\RGC Game Watcher.lnk" -Force -ErrorAction SilentlyContinue

    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
        Where-Object { $_.CommandLine -like '*rgc-game-watcher*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    $action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$tools\rgc-game-watcher.ps1`""
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User "$env:COMPUTERNAME\Admin"
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -Hidden
    $principal = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\Admin" -LogonType Interactive -RunLevel Highest
    Register-ScheduledTask -TaskName 'RGC Game Watcher' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null

    Start-ScheduledTask -TaskName 'RGC Game Watcher'
    Add-Content $log "$(Get-Date) OK - task registrovan i pokrenut (elevated)"
}
catch {
    Add-Content $log "$(Get-Date) GRESKA: $_"
}
