# Builds FademSetup-<version>.exe into artifacts\installer\.
# Publishes the standalone app self-contained (no .NET runtime needed on the user's machine),
# builds the MusicBee plugin, then compiles fadem.iss with Inno Setup 6
# (winget install JRSoftware.InnoSetup).
param(
    [string]$Version = (Get-Date -Format 'yyyy.M.d')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'
$appOut = Join-Path $publish 'app'
$pluginOut = Join-Path $publish 'plugin'

function Invoke-Checked([scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Failed with exit code $LASTEXITCODE" }
}

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

Invoke-Checked { dotnet publish (Join-Path $root 'src\Fadem.App') -c Release -r win-x64 --self-contained true -o $appOut "-p:Version=$Version" }
Invoke-Checked { dotnet build (Join-Path $root 'src\Fadem.MusicBee') -c Release "-p:Version=$Version" }
Copy-Item -Recurse (Join-Path $root 'src\Fadem.MusicBee\bin\Release\net48\plugin') $pluginOut

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup' }

Invoke-Checked { & $iscc "/DAppVersion=$Version" "/DAppSource=$appOut" "/DPluginSource=$pluginOut" `
    "/DOutputDir=$(Join-Path $root 'artifacts\installer')" (Join-Path $PSScriptRoot 'fadem.iss') }
