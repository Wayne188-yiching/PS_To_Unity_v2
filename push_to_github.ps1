# PS_To_Unity_v2 - Push to GitHub
# Usage: .\push_to_github.ps1

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$remoteUrl   = "https://github.com/Wayne188-yiching/PS_To_Unity_v2.git"
$branch      = "main"
$commitMsg   = "feat: v2.2.0 - PS/Unity auto-update, remove TMP StyleMap and batch generate, rename to Importer_v2, refresh docs"
$gitTemplate = Join-Path $env:TEMP "empty-git-template"

Set-Location $projectRoot
Write-Host "[INFO] Working dir: $projectRoot" -ForegroundColor Cyan

# Init git if needed. A partial .git folder can exist after an interrupted init,
# so verify with git instead of only checking whether the folder exists.
$insideWorkTree = $false
git rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -eq 0) {
    $insideWorkTree = $true
}

if (-not $insideWorkTree) {
    $configLock = Join-Path $projectRoot ".git\config.lock"
    if (Test-Path $configLock) {
        try {
            Remove-Item -LiteralPath $configLock -Force -ErrorAction Stop
        } catch {
            throw "Cannot remove stale Git lock file: $configLock. Close Git tools or delete the lock file manually, then rerun this script."
        }
    }
    if (-not (Test-Path $gitTemplate)) {
        New-Item -ItemType Directory -Force -Path $gitTemplate | Out-Null
    }
    git init --template="$gitTemplate"
    if ($LASTEXITCODE -ne 0) {
        throw "git init failed"
    }
    Write-Host "[OK] git init done" -ForegroundColor Green
} else {
    Write-Host "[INFO] Valid git repository found" -ForegroundColor Yellow
}

# Ensure branch
$currentBranch = git branch --show-current
if ($currentBranch -ne $branch) {
    git checkout -B $branch
    if ($LASTEXITCODE -ne 0) {
        throw "git checkout failed"
    }
    Write-Host "[OK] branch set to $branch" -ForegroundColor Green
}

# Set remote
$existingRemote = git remote 2>$null
if ($existingRemote -contains "origin") {
    git remote set-url origin $remoteUrl
    Write-Host "[OK] remote origin updated" -ForegroundColor Green
} else {
    git remote add origin $remoteUrl
    Write-Host "[OK] remote origin added" -ForegroundColor Green
}

# Create .gitignore if missing
if (-not (Test-Path ".gitignore")) {
    $ignore = @(
        "# Unity generated",
        "[Ll]ibrary/", "[Tt]emp/", "[Oo]bj/",
        "[Bb]uild/", "[Bb]uilds/", "[Ll]ogs/", "[Uu]ser[Ss]ettings/",
        "*.csproj", "*.unityproj", "*.sln", "*.suo", "*.tmp",
        "*.user", "*.userprefs", "*.pidb", "*.booproj",
        "*.svd", "*.pdb", "*.mdb", "*.opendb", "*.VC.db",
        ".DS_Store", "*.swp"
    )
    $ignore | Out-File -Encoding UTF8 ".gitignore"
    Write-Host "[OK] .gitignore created" -ForegroundColor Green
}

# Stage and commit
git add -A
$staged = git diff --cached --name-only
if ($staged.Count -eq 0) {
    Write-Host "[WARN] Nothing to commit" -ForegroundColor Yellow
} else {
    git commit -m $commitMsg
    Write-Host "[OK] Committed" -ForegroundColor Green
}

# Push
Write-Host "[INFO] Pushing to GitHub..." -ForegroundColor Cyan
git push --force --set-upstream origin $branch

Write-Host ""
Write-Host "Done. Check: https://github.com/Wayne188-yiching/PS_To_Unity_v2" -ForegroundColor Green
