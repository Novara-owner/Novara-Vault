@echo off
chcp 65001 >nul
setlocal









set "NOVARA_SYNC_DATA=%LOCALAPPDATA%\Novara\Server\data"
if not exist "%NOVARA_SYNC_DATA%" mkdir "%NOVARA_SYNC_DATA%"

echo.
echo  Novara 互联同步 - 建同步空间（只需执行一次）
echo  数据目录: %NOVARA_SYNC_DATA%
echo.
echo  下面会打印 space id 与 enrollment secret，两者都只显示这一次。
echo  请立刻抄下来或截图保存（配对电脑时要用）。
echo.

"%~dp0NovaraSync.exe" space create --name my-space

echo.
echo  ^> 完成。下一步：双击同目录下的「2-启动服务端.cmd」。
echo  ^> 按任意键关闭本窗口。
pause >nul
