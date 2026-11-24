@echo off
chcp 65001 >nul
echo FocusGuard を最新版にビルドして再起動します...
taskkill /IM FocusGuard.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul
powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
    echo ビルドに失敗しました。
    pause
    exit /b 1
)
start "" "%~dp0FocusGuard.exe"
echo 更新・再起動が完了しました！
