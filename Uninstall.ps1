$ErrorActionPreference = 'Stop'
$installRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Verdant'))
if ([IO.Path]::GetFullPath($PSScriptRoot) -ne $installRoot) { throw 'Run the installed uninstaller from Windows Installed apps.' }
$targetExe = Join-Path $installRoot 'Verdant.exe'
Get-Process Verdant -ErrorAction SilentlyContinue | ForEach-Object { if ($_.Path -eq $targetExe) { throw 'Exit Verdant from its tray menu, then run uninstall again.' } }
$startup = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $startup -Name Verdant -ErrorAction SilentlyContinue
$link = Join-Path ([Environment]::GetFolderPath('Programs')) 'Verdant.lnk'
if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link }
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Verdant' -ErrorAction SilentlyContinue
foreach ($name in @('Verdant.exe', 'Uninstall.ps1')) {
    $file = [IO.Path]::GetFullPath((Join-Path $installRoot $name))
    if (-not $file.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe uninstall path.' }
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
}
if ((Get-ChildItem -LiteralPath $installRoot -Force | Measure-Object).Count -eq 0) { Remove-Item -LiteralPath $installRoot }
Write-Host 'Verdant removed. Your profiles and macros are preserved in %LOCALAPPDATA%\Verdant.'
