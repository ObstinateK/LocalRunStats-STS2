# Pull origin/main and install the built DLL into the local STS2 mods folder.
#
#   powershell -File .\pull-and-install.ps1
#
# Close Slay the Spire 2 first — the game locks local-run-stats.dll while running.
# If Steam is not in a default path, pass the install folder:
#   powershell -File .\pull-and-install.ps1 -Sts2Path "D:\Games\Slay the Spire 2"
param(
    [string] $Sts2Path = ""
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$gameLock = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
if ($gameLock) {
    throw "Slay the Spire 2 is running (PID $($gameLock.Id)). Close it, then run this script again so the DLL can be replaced."
}

git pull --ff-only
if ($LASTEXITCODE -ne 0) {
    throw "git pull failed"
}

$buildArgs = @(
    "build",
    ".\LocalRunStats\LocalRunStats.csproj",
    "-c", "Release"
)
if ($Sts2Path) {
    $buildArgs += "/p:Sts2Path=$Sts2Path"
}

dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed"
}

Write-Host "Installed the pulled Local Run Stats into mods\LocalRunStats"
