@echo off
chcp 65001 >nul
echo ================================================
echo   《逃离鸭科夫》Mod 快速构建脚本
echo   DuckovMercenarySystemMod 编译工具
echo ================================================
echo.

echo [1/3] 正在清理旧文件...
if exist "bin\" rmdir /s /q "bin"
if exist "obj\" rmdir /s /q "obj"
echo 清理完成！
echo.

echo [2/3] 正在编译项目...
dotnet build DuckovMercenarySystemMod.csproj -c Release
if %errorlevel% neq 0 (
    echo.
    echo ❌ 编译失败！请检查错误信息。
    pause
    exit /b 1
)
echo 编译成功！
echo.

echo [3/3] 正在复制DLL文件到发布目录...
if not exist "bin\Release\netstandard2.1\DuckovMercenarySystemMod.dll" (
    echo ❌ 找不到编译后的DLL文件！
    pause
    exit /b 1
)

copy /Y "bin\Release\netstandard2.1\DuckovMercenarySystemMod.dll" "ReleaseExample\DuckovMercenarySystemMod\"
echo 复制完成！
echo.

echo ================================================
echo ✅ 构建完成！
echo.
echo 发布文件位置：
echo   %CD%\ReleaseExample\DuckovMercenarySystemMod\
echo.
echo 文件清单：
dir /b "ReleaseExample\DuckovMercenarySystemMod\"
echo.
echo 下一步：
echo 1. 准备一个 256x256 的 preview.png 预览图
echo 2. 将整个 DuckovMercenarySystemMod 文件夹复制到游戏的 Duckov_Data\Mods\ 目录
echo 3. 启动游戏，在Mods菜单中启用此Mod
echo ================================================
pause

