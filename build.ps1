param([string]$Dotnet = 'dotnet')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    & $Dotnet run --project tests\Verdant.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & $Dotnet publish src\Verdant.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -o release
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $package=Join-Path $PSScriptRoot 'package'
    New-Item -ItemType Directory -Force $package | Out-Null
    Copy-Item release\Verdant.exe,Install.ps1,Install.cmd,Uninstall.ps1,README.md,THIRD-PARTY-NOTICES.txt -Destination $package -Force
    New-Item -ItemType Directory -Force (Join-Path $package 'docs') | Out-Null
    Copy-Item docs\ARCHITECTURE.md,docs\VALIDATION.md -Destination (Join-Path $package 'docs') -Force
    Compress-Archive -Path "$package\*" -DestinationPath Verdant-0.1.0-win-x64.zip -Force
    Get-FileHash Verdant-0.1.0-win-x64.zip -Algorithm SHA256
} finally { Pop-Location }

