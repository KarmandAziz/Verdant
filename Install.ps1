param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Verdant'
$sourceExe = Join-Path $PSScriptRoot 'Verdant.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { $sourceExe = Join-Path $PSScriptRoot 'release\Verdant.exe' }
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Verdant.exe is missing. Extract the complete package first.' }
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
$targetExe = Join-Path $installRoot 'Verdant.exe'
Get-Process Verdant -ErrorAction SilentlyContinue | ForEach-Object { if ($_.Path -eq $targetExe) { throw 'Exit Verdant from its tray menu before installing an update.' } }
Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination (Join-Path $installRoot 'Uninstall.ps1') -Force
$shell = New-Object -ComObject WScript.Shell
$linkPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Verdant.lnk'
$shortcut = $shell.CreateShortcut($linkPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $installRoot
$shortcut.Description = 'Local mouse bindings and macros'
$shortcut.IconLocation = $targetExe + ',0'
$shortcut.Save()
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Verdant'
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty $uninstallKey DisplayName -Value 'Verdant Mouse & Macro Manager' -Force | Out-Null
New-ItemProperty $uninstallKey DisplayVersion -Value '0.1.0' -Force | Out-Null
New-ItemProperty $uninstallKey Publisher -Value 'Verdant' -Force | Out-Null
New-ItemProperty $uninstallKey InstallLocation -Value $installRoot -Force | Out-Null
New-ItemProperty $uninstallKey DisplayIcon -Value $targetExe -Force | Out-Null
$uninstaller = Join-Path $installRoot 'Uninstall.ps1'
New-ItemProperty $uninstallKey UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $uninstaller + '"') -Force | Out-Null
Write-Host "Installed Verdant in $installRoot. Find it in Start. Launching Verdant requests administrator approval."
if (-not $NoLaunch) { Start-Process -FilePath $targetExe }
