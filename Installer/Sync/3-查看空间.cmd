@echo off
chcp 65001 >nul
setlocal














set "NOVARA_SYNC_DATA=%LOCALAPPDATA%\Novara\Server\data"
if not exist "%NOVARA_SYNC_DATA%" mkdir "%NOVARA_SYNC_DATA%"

echo.
echo  Novara 互联同步 - 查看空间
echo  数据目录: %NOVARA_SYNC_DATA%
echo.

"%~dp0NovaraSync.exe" space list
if errorlevel 1 goto failed

echo.
echo  ------------------------------------------------------------
echo  要看某个空间的详情？直接回车跳过，或粘贴上面某一行的 space id。
echo  ------------------------------------------------------------
set "SPACE_ID="
set /p "SPACE_ID= space id: "

if not defined SPACE_ID goto done
echo.
"%~dp0NovaraSync.exe" space show %SPACE_ID%
if errorlevel 1 goto failed

:done
echo.
echo  ^> 完成。
echo  ^> 若要删除某个空间（不可逆），请在本目录打开命令行执行：
echo  ^>   NovaraSync.exe space delete ^<space id^> --yes
echo  ^> 按任意键关闭本窗口。
pause >nul
exit /b 0

:failed
echo.
echo  ^> 命令未能完成（原因见上方提示）。按任意键关闭本窗口。
pause >nul
exit /b 1
