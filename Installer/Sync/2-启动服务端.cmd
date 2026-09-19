@echo off
chcp 65001 >nul
setlocal







set "NOVARA_SYNC_DATA=%LOCALAPPDATA%\Novara\Server\data"
if not exist "%NOVARA_SYNC_DATA%" mkdir "%NOVARA_SYNC_DATA%"

echo.
echo  Novara 互联同步 - 服务端
echo  数据目录: %NOVARA_SYNC_DATA%
echo  监听地址: http://127.0.0.1:5180  （仅本机；对外请配 HTTPS 反代）
echo.
echo  提示：若本机还没有同步空间，请先执行「1-建空间.cmd」。
echo  关闭本窗口即停止服务端。
echo.

"%~dp0NovaraSync.exe" --urls http://127.0.0.1:5180

echo.
echo  ^> 服务端已退出。按任意键关闭本窗口。
pause >nul
