@echo off
chcp 65001 >nul
echo ================================================
echo   清理并重新编译 DuckovMercenarySystemMod
echo ================================================
echo.

echo [1/4] 清理旧的编译文件...
if exist "bin\" (
    rmdir /s /q "bin"
    echo ✅ 已删除 bin 目录
)
if exist "obj\" (
    rmdir /s /q "obj"
    echo ✅ 已删除 obj 目录
)
echo.

echo [2/4] 恢复依赖包...
dotnet restore DuckovMercenarySystemMod.csproj
if %errorlevel% neq 0 (
    echo ❌ 恢复依赖包失败
    pause
    exit /b 1
)
echo ✅ 依赖包恢复成功
echo.

echo [3/4] 编译项目 (Release)...
dotnet build DuckovMercenarySystemMod.csproj -c Release
if %errorlevel% neq 0 (
    echo.
    echo ❌ 编译失败！
    echo.
    echo 常见问题排查：
    echo 1. 确认游戏路径是否正确 (.csproj 第9行)
    echo 2. 确认所有 DLL 文件都存在
    echo 3. 尝试在 Visual Studio 中打开项目并查看错误列表
    echo.
    pause
    exit /b 1
)
echo ✅ 编译成功！
echo.

echo [4/4] 复制 DLL 到发布目录...
if not exist "bin\Release\netstandard2.1\DuckovMercenarySystemMod.dll" (
    echo ❌ 找不到编译后的 DLL 文件
    pause
    exit /b 1
)

copy /Y "bin\Release\netstandard2.1\DuckovMercenarySystemMod.dll" "ReleaseExample\DuckovMercenarySystemMod\"
echo ✅ DLL 已复制到发布目录
echo.

echo ================================================
echo ✅ 编译完成！
echo.
echo 发布文件位置：
echo   %CD%\ReleaseExample\DuckovMercenarySystemMod\
echo.
echo 下一步：
echo 1. 将 ReleaseExample\DuckovMercenarySystemMod\ 文件夹
echo 2. 复制到游戏目录：
echo    C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\
echo 3. 启动游戏并在 Mods 菜单中启用
echo ================================================
pause


