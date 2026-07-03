# Instalira/azurira RGC Watcher kao exe (pokrenuti KAO ADMINISTRATOR):
#  - gasi staru verziju (ps1 ili exe)
#  - Scheduled Task "RGC Game Watcher" -> RGCWatcher.exe (pri logovanju, kao admin)
#  - odmah pokrece novu verziju
# Upgrade postupak: izmeni src\RGCWatcher.cs -> .\build.ps1 -> .\install.ps1 (kao admin)
$ErrorActionPreference = 'Stop'
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $tools 'setup.log'
Add-Content $log "$(Get-Date) install (exe) start"
try {
    if (-not (Test-Path "$tools\Wekzy.exe")) { throw "Wekzy.exe ne postoji - prvo pokreni build.ps1" }

    # ugasi stare instance (ps1 wrapper i exe, stara i nova imena)
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
        Where-Object { $_.CommandLine -like '*rgc-game-watcher*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Get-Process RGCWatcher, Wekzy -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 1

    $action    = New-ScheduledTaskAction -Execute "$tools\Wekzy.exe" -WorkingDirectory $tools
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User "$env:COMPUTERNAME\Admin"
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -Hidden
    $principal = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\Admin" -LogonType Interactive -RunLevel Highest
    Register-ScheduledTask -TaskName 'RGC Game Watcher' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null

    Start-ScheduledTask -TaskName 'RGC Game Watcher'
    Add-Content $log "$(Get-Date) OK - task sada pokrece Wekzy.exe"
}
catch {
    Add-Content $log "$(Get-Date) GRESKA: $_"
}
