@echo off
chcp 65001 >nul
echo ================================================
echo   《逃离鸭科夫》游戏路径检测工具
echo ================================================
echo.

echo 正在检测常见的Steam安装路径...
echo.

set FOUND=0

REM 检测常见的Steam路径
set "PATHS[0]=C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov"
set "PATHS[1]=D:\Steam\steamapps\common\Escape from Duckov"
set "PATHS[2]=E:\Steam\steamapps\common\Escape from Duckov"
set "PATHS[3]=F:\Steam\steamapps\common\Escape from Duckov"
set "PATHS[4]=C:\Program Files\Steam\steamapps\common\Escape from Duckov"
set "PATHS[5]=D:\SteamLibrary\steamapps\common\Escape from Duckov"
set "PATHS[6]=E:\SteamLibrary\steamapps\common\Escape from Duckov"

for /L %%i in (0,1,6) do (
    call set "PATH_TO_CHECK=%%PATHS[%%i]%%"
    call :CHECK_PATH "!PATH_TO_CHECK!"
)

if %FOUND%==0 (
    echo ❌ 未找到游戏安装路径！
    echo.
    echo 请手动查找游戏安装位置：
    echo 1. 打开Steam
    echo 2. 右键点击《逃离鸭科夫》^(Escape from Duckov^)
    echo 3. 选择"管理" ^> "浏览本地文件"
    echo 4. 复制地址栏的路径
    echo 5. 在 DuckovMercenarySystemMod.csproj 文件第9行修改 ^<DuckovPath^> 的值
    echo.
) else (
    echo.
    echo ✅ 找到游戏路径！
    echo.
    echo 下一步：
    echo 请打开 DuckovMercenarySystemMod.csproj 文件，
    echo 将第9行的 ^<DuckovPath^> 修改为上面显示的路径。
    echo.
)

echo ================================================
pause
exit /b

:CHECK_PATH
setlocal
set "CHECK_PATH=%~1"
if exist "%CHECK_PATH%\Duckov.exe" (
    echo ✅ 找到: %CHECK_PATH%
    if exist "%CHECK_PATH%\Duckov_Data\Managed\ItemStatsSystem.dll" (
        echo    └─ 核心DLL文件存在
        set FOUND=1
    ) else (
        echo    └─ ⚠️ 缺少核心DLL文件
    )
) else (
    echo ❌ 不存在: %CHECK_PATH%
)
endlocal & set FOUND=%FOUND%
exit /b

