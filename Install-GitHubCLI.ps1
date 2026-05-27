$ErrorActionPreference = "Stop"

$Version = "2.92.0"
$AssetName = "gh_$Version`_windows_amd64.zip"
$ChecksumName = "gh_$Version`_checksums.txt"
$ReleaseBaseUrl = "https://github.com/cli/cli/releases/download/v$Version"
$InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI"
$DownloadRoot = Join-Path $env:TEMP "github-cli-install"
$ZipPath = Join-Path $DownloadRoot $AssetName
$ChecksumPath = Join-Path $DownloadRoot $ChecksumName

Write-Host "Installing GitHub CLI $Version..."

New-Item -ItemType Directory -Force -Path $DownloadRoot | Out-Null
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null

Invoke-WebRequest -Uri "$ReleaseBaseUrl/$AssetName" -OutFile $ZipPath
Invoke-WebRequest -Uri "$ReleaseBaseUrl/$ChecksumName" -OutFile $ChecksumPath

$ActualHash = (Get-FileHash -Algorithm SHA256 $ZipPath).Hash.ToLowerInvariant()
$ExpectedLine = Select-String -Path $ChecksumPath -Pattern ([regex]::Escape($AssetName)) | Select-Object -First 1
if (-not $ExpectedLine) {
    throw "Could not find checksum entry for $AssetName."
}

$ExpectedHash = ($ExpectedLine.Line -split "\s+")[0].ToLowerInvariant()
if ($ActualHash -ne $ExpectedHash) {
    throw "Checksum mismatch. Expected $ExpectedHash but got $ActualHash."
}

$ExtractRoot = Join-Path $DownloadRoot "extract"
Remove-Item -LiteralPath $ExtractRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ExtractRoot | Out-Null
Expand-Archive -LiteralPath $ZipPath -DestinationPath $ExtractRoot -Force

$GhExe = Get-ChildItem -Path $ExtractRoot -Recurse -Filter "gh.exe" | Select-Object -First 1
if (-not $GhExe) {
    throw "gh.exe was not found after extraction."
}

Copy-Item -LiteralPath $GhExe.FullName -Destination (Join-Path $InstallRoot "gh.exe") -Force

$UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (-not ($UserPath -split ";" | Where-Object { $_ -eq $InstallRoot })) {
    [Environment]::SetEnvironmentVariable("Path", (($UserPath.TrimEnd(";") + ";" + $InstallRoot).TrimStart(";")), "User")
}

$env:Path = "$env:Path;$InstallRoot"
& (Join-Path $InstallRoot "gh.exe") --version

Write-Host ""
Write-Host "GitHub CLI installed to:"
Write-Host $InstallRoot
Write-Host ""
Write-Host "Open a new PowerShell window, then run:"
Write-Host "gh auth login"
