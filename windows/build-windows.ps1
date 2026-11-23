<#
.SYNOPSIS
  Builds, tests and packages Desktop Buddy for Windows (the Windows counterpart of ../build-app.sh).

.DESCRIPTION
  1. Runs every unit test (the port of the Mac self-test). Stops if any fail.
  2. Publishes a self-contained build (no .NET or Windows App SDK install needed).
  3. Runs the built app's --selftest (Credential Manager and other Windows-only checks).
  4. With -Share: zips it as dist\Desktop-Buddy-Windows-<arch>.zip plus a .sha256 file,
     the two files to attach to the GitHub release (the in-app update check looks for these names).
  5. With -Install: copies it to %LOCALAPPDATA%\Programs\Desktop Buddy and opens it.

  A team.json in this folder (windows\team.json, git-ignored) is built into the app, so people
  only click Sign In. On Windows the Google client must be a Desktop-type client.

.EXAMPLE
  .\build-windows.ps1              # test + build
  .\build-windows.ps1 -Share       # also make the zip and checksum for a release
  .\build-windows.ps1 -Install     # also install for this user and open it
  .\build-windows.ps1 -Arch arm64  # build for Windows on ARM
#>
param(
    [switch]$Share,
    [switch]$Install,
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$out = Join-Path $dist "Desktop Buddy ($Arch)"
$rid = "win-$Arch"

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

Step 'Running the tests'
dotnet test (Join-Path $root 'DesktopBuddy.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing was packaged.' }

Step "Building Desktop Buddy for $rid"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src\DesktopBuddy.App\DesktopBuddy.App.csproj') -c Release -r $rid --self-contained true -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if (Test-Path (Join-Path $root 'team.json')) {
    Write-Host 'Sign-in setup (team.json) is built in.'
} else {
    Write-Warning 'No windows\team.json: this copy will say "Sign-in isn''t set up in this copy yet".'
}

$exe = Join-Path $out 'DesktopBuddy.exe'
if ($Arch -eq 'x64' -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
    Step 'Running the Windows checks (--selftest)'
    $p = Start-Process -FilePath $exe -ArgumentList '--selftest' -Wait -PassThru
    Get-Content (Join-Path $out 'selftest.txt') | ForEach-Object { Write-Host "  $_" }
    Remove-Item (Join-Path $out 'selftest.txt') -ErrorAction SilentlyContinue
    if ($p.ExitCode -ne 0) { throw 'Windows checks failed. Nothing was packaged.' }
}

if ($Share) {
    Step 'Packaging'
    $zipName = "Desktop-Buddy-Windows-$Arch.zip"
    $zip = Join-Path $dist $zipName
    if (Test-Path $zip) { Remove-Item -Force $zip }
    # The zip holds one folder, "Desktop Buddy", with the app inside.
    $staging = Join-Path $dist 'staging'
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    New-Item -ItemType Directory -Force (Join-Path $staging 'Desktop Buddy') | Out-Null
    Copy-Item -Recurse -Force (Join-Path $out '*') (Join-Path $staging 'Desktop Buddy')
    Compress-Archive -Path (Join-Path $staging 'Desktop Buddy') -DestinationPath $zip
    Remove-Item -Recurse -Force $staging
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    # Same format as `shasum -a 256` on the Mac: "<hash>  <file name>".
    [System.IO.File]::WriteAllText("$zip.sha256", "$hash  $zipName`n")
    Write-Host "Share: $zip and $zipName.sha256 (attach both to the GitHub release)"
}

if ($Install) {
    Step 'Installing for this user'
    $target = Join-Path $env:LOCALAPPDATA 'Programs\Desktop Buddy'
    Get-Process DesktopBuddy -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" } | Stop-Process -Force
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item -Recurse -Force (Join-Path $out '*') $target
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Desktop Buddy.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = Join-Path $target 'DesktopBuddy.exe'
    $link.WorkingDirectory = $target
    $link.Description = 'Desktop Buddy'
    $link.Save()
    Start-Process (Join-Path $target 'DesktopBuddy.exe')
    Write-Host "Installed to $target and added to the Start menu."
}

Write-Host "`nDone." -ForegroundColor Green
