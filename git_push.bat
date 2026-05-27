@echo off
chcp 65001 > nul
cd /d "%~dp0"
echo === Git Init ===
git init
git checkout -b main 2>nul || git branch -m master main 2>nul
echo === Set Remote ===
git remote remove origin 2>nul
git remote add origin https://github.com/Wayne188-yiching/PS_To_Unity_v2.git
echo === Stage All Files ===
git add -A
echo === Commit ===
git commit -m "chore: UX optimizations, consolidated docs, new folder structure"
echo === Force Push ===
git push --force --set-upstream origin main
echo.
echo === Done! ===
echo https://github.com/Wayne188-yiching/PS_To_Unity_v2
pause
