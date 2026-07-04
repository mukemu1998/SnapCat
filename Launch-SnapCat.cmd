@echo off
setlocal
chcp 65001 >nul

set "ROOT_DIR=%~dp0"
set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
set "PACKAGE_SCRIPT=%ROOT_DIR%tools\package-dev-build.ps1"
set "APP_EXE=%ROOT_DIR%artifacts\dev-test\current\SnapCat.exe"

echo [SnapCat] 正在生成当前测试包...
"%PS_EXE%" -ExecutionPolicy Bypass -File "%PACKAGE_SCRIPT%" -Configuration Release -Runtime win-x64
if errorlevel 1 (
    echo.
    echo [SnapCat] 生成测试包失败。
    echo [SnapCat] 如果 SnapCat 仍在运行，请先关闭后再重试。
    pause
    exit /b 1
)

if not exist "%APP_EXE%" (
    echo.
    echo [SnapCat] 未找到可执行文件：
    echo %APP_EXE%
    pause
    exit /b 1
)

echo [SnapCat] 正在启动...
start "" "%APP_EXE%"
exit /b 0
