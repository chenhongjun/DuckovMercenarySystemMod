@echo off
chcp 65001 >nul
echo ================================================
echo   打开《逃离鸭科夫》游戏日志
echo ================================================
echo.

set "LOG_PATH=%AppData%\..\LocalLow\TeamSoda\Duckov\Player.log"

if exist "%LOG_PATH%" (
    echo ✅ 找到日志文件
    echo 路径: %LOG_PATH%
    echo.
    echo 正在用记事本打开...
    start notepad "%LOG_PATH%"
) else (
    echo ❌ 日志文件不存在！
    echo.
    echo 可能的原因：
    echo 1. 游戏还未运行过
    echo 2. 日志路径不同
    echo.
    echo 请尝试手动打开：
    echo %AppData%\..\LocalLow\TeamSoda\Duckov\
)

echo.
pause

