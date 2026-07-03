# Kompajlira src\RGCWatcher.cs -> RGCWatcher.exe
# (koristi C# kompajler koji dolazi uz Windows/.NET Framework - nista se ne instalira)
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:SystemRoot\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

& $csc /nologo /target:winexe /platform:anycpu `
    /out:"$tools\Wekzy.exe" `
    /win32icon:"$tools\src\wekzy.ico" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "$tools\src\RGCWatcher.cs"

if ($LASTEXITCODE -eq 0) { Write-Host "OK: Wekzy.exe izgradjen ($tools\Wekzy.exe)" }
else { Write-Host "GRESKA pri kompajliranju (kod $LASTEXITCODE)" }
