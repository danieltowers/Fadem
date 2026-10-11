# Builds FademSetup-<version>.exe into artifacts\installer\.
# Publishes the standalone app self-contained (no .NET runtime needed on the user's machine),
# builds the MusicBee plugin, then compiles fadem.iss with Inno Setup 6
# (winget install JRSoftware.InnoSetup).
# The version is the latest semver tag reachable from HEAD (v1.2.3 or v1.2.3-beta.1).

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Invoke-Git {
    # Windows PowerShell turns a native command's stderr into an error under 'Stop'.
    $ErrorActionPreference = 'Continue'
    git -C $root @args 2>$null
}

$tag = Invoke-Git describe --tags --abbrev=0 --match 'v[0-9]*'
if ($LASTEXITCODE -ne 0 -or -not $tag) {
    Write-Warning 'No v* tag yet, so this installer is versioned 0.0.0.'
    $tag = 'v0.0.0'
}
if ($tag -notmatch '^v(?<numeric>\d+\.\d+\.\d+)(-[0-9A-Za-z.-]+)?$') {
    throw "The latest tag, $tag, is not a semver tag such as v1.2.3."
}
$Version = $tag.Substring(1)
# Windows' file version takes numbers only, so a prerelease drops its suffix there.
$numericVersion = $Matches.numeric
$since = [int](Invoke-Git rev-list --count "$tag..HEAD")
if ($since -gt 0) { Write-Warning "HEAD is $since commit(s) past $tag; the installer is still versioned $Version." }
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
$wizard = Join-Path $root 'artifacts\wizard'
& (Join-Path $PSScriptRoot 'make-wizard-images.ps1') -OutDir $wizard

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup' }

Invoke-Checked { & $iscc "/DAppVersion=$Version" "/DNumericVersion=$numericVersion" "/DAppSource=$appOut" "/DPluginSource=$pluginOut" "/DWizardImages=$wizard" `
    "/DOutputDir=$(Join-Path $root 'artifacts\installer')" (Join-Path $PSScriptRoot 'fadem.iss') }
