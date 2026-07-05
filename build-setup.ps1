# Pravi W3UtilSetup.exe - sve-u-jednom instalator za USB
# Pakuje: trenutni skin iz RGC-a + zvukove + W3Util.exe + JetBrains Mono fontove
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$rgc = "C:\Program Files (x86)\Warcraft III\Ranked Gaming Client"

if (-not (Test-Path "$tools\W3Util.exe")) { Write-Host "Prvo build.ps1 (nema W3Util.exe)"; exit 1 }

$stage = Join-Path $env:TEMP 'wekzy-stage'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item "$rgc\skins\Wekzy Dark" "$stage\skin" -Recurse
New-Item -ItemType Directory -Force "$stage\sound" | Out-Null
Copy-Item "$rgc\sound\*.wav" "$stage\sound\"
New-Item -ItemType Directory -Force "$stage\tools" | Out-Null
Copy-Item "$tools\W3Util.exe","$tools\game-alert.wav" "$stage\tools\"
Copy-Item "$tools\backgrounds" "$stage\tools\backgrounds" -Recurse
Copy-Item "$tools\uiskins" "$stage\tools\uiskins" -Recurse
New-Item -ItemType Directory -Force "$stage\fonts" | Out-Null
Copy-Item "$env:LOCALAPPDATA\Microsoft\Windows\Fonts\JetBrainsMono-*.ttf" "$stage\fonts\" -ErrorAction SilentlyContinue

$zip = Join-Path $env:TEMP 'payload.zip'
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage\*" -DestinationPath $zip

$csc = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:SystemRoot\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

& $csc /nologo /target:winexe /platform:anycpu `
    /out:"$tools\W3UtilSetup.exe" `
    /win32icon:"$tools\src\wekzy.ico" `
    /win32manifest:"$tools\src\setup.manifest" `
    "/resource:$zip,payload.zip" `
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "$tools\src\W3UtilSetup.cs"

if ($LASTEXITCODE -eq 0) {
    $mb = ((Get-Item "$tools\W3UtilSetup.exe").Length / 1MB).ToString('0.0')
    Write-Host "OK: W3UtilSetup.exe ($mb MB) - kopiraj na USB i gotovo"
} else { Write-Host "GRESKA pri kompajliranju setup-a (kod $LASTEXITCODE)" }

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue
