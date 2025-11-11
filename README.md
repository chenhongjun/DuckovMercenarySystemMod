# DuckovMercenarySystemMod - 《逃离鸭科夫》雇佣兵系统Mod

这是一个完整的、可直接使用的Mod开发演示项目，展示了《逃离鸭科夫》mod开发的核心功能和最佳实践。

## 📦 项目结构

```
DuckovMercenarySystemMod/
├── ModBehaviour.cs                 # Mod核心逻辑文件
├── DuckovMercenarySystemMod.csproj # C# 项目配置文件
├── DuckovMercenarySystemMod.sln    # Visual Studio 解决方案文件
├── README.md                       # 本说明文档
└── ReleaseExample/                 # 发布示例
    └── DuckovMercenarySystemMod/   # 实际发布的Mod文件夹
        ├── info.ini                # Mod配置信息
        ├── preview.png             # 预览图（可选）
        └── DuckovMercenarySystemMod.dll  # (编译后生成)
```

## ✨ 功能特性

这个演示Mod包含以下功能：

### 1. **游戏启动欢迎消息**
- 进入游戏3秒后，在玩家头顶显示欢迎对话气泡
- 演示异步编程和延迟执行

### 2. **按键交互系统**
- **F9键** - 给玩家添加一个Glick物品（物品ID #254）
- **F10键** - 在玩家头顶显示随机对话气泡
- 演示键盘输入处理

### 3. **物品生成演示**
- 使用 `ItemAssetsCollection.InstantiateAsync()` 生成物品
- 使用 `ItemUtilities.SendToPlayer()` 将物品送给玩家
- 完整的错误处理机制

### 4. **对话气泡系统**
- 使用 `DialogueBubblesManager.Show()` 显示对话
- 自定义位置、持续时间等参数
- 多条随机消息演示

### 5. **调试日志输出**
- 定期输出运行状态（每30秒）
- 完整的生命周期日志
- 方便开发调试

## 🚀 快速开始

### 步骤1：配置开发环境

1. 安装 Visual Studio 2022（或其他C# IDE）
2. 在 `DuckovMercenarySystemMod.csproj` 文件中修改游戏路径：

```xml
<!-- Windows 用户修改这里 -->
<DuckovPath>你的游戏安装路径\Escape from Duckov</DuckovPath>

<!-- Mac 用户修改这里 -->
<DuckovPath Condition="'$(IsMac)'">你的游戏安装路径/Escape from Duckov</DuckovPath>
```

### 步骤2：编译项目

在命令行中运行：
```bash
dotnet build DuckovMercenarySystemMod.sln -c Release
```

或在 Visual Studio 中：
- 打开 `DuckovMercenarySystemMod.sln`
- 选择 `Release` 配置
- 点击 `生成 > 生成解决方案`

### 步骤3：准备发布文件

1. 编译完成后，生成的 `DuckovMercenarySystemMod.dll` 会自动复制到 `ReleaseExample/DuckovMercenarySystemMod/` 目录
2. 如需手动访问，可在 `bin/Release/netstandard2.1/` 目录找到 `DuckovMercenarySystemMod.dll`
3. (可选) 准备一个 256x256 的 `preview.png` 预览图

### 步骤4：安装Mod

将 `ReleaseExample/DuckovMercenarySystemMod/` 整个文件夹复制到游戏目录：

**Windows:**
```
<游戏安装目录>\Duckov_Data\Mods\DuckovMercenarySystemMod\
```

**Mac:**
```
<游戏安装目录>/Duckov.app/Contents/Mods/DuckovMercenarySystemMod/
```

### 步骤5：在游戏中启用Mod

1. 启动《逃离鸭科夫》
2. 在主菜单点击 `Mods`
3. 找到 "雇佣兵系统Mod"，启用它
4. 重启游戏或开始新游戏

## 🎮 使用说明

启动游戏后：

1. **等待欢迎消息** - 进入游戏约3秒后，会在角色头顶显示欢迎消息
2. **按F9键** - 获得一个Glick物品
3. **按F10键** - 显示随机对话气泡
4. **查看控制台** - 可以看到详细的调试日志

## 🔧 代码说明

### 核心API使用示例

#### 1. 物品生成
```csharp
// 异步生成物品
Item item = await ItemAssetsCollection.InstantiateAsync(物品ID);
// 送给玩家
ItemUtilities.SendToPlayer(item);
```

#### 2. 对话气泡
```csharp
await DialogueBubblesManager.Show(
    "消息内容",
    玩家Transform,
    yOffset: 2f,      // Y轴偏移
    duration: 3f      // 持续时间（秒）
);
```

#### 3. 获取玩家角色
```csharp
var player = TeamSoda.Players.PlayerManager.LocalPlayerCharacter;
```

### Unity生命周期

```csharp
void Awake()     // Mod加载时立即执行
void Start()     // 第一帧Update之前
void Update()    // 每帧执行
void OnEnable()  // Mod启用时
void OnDisable() // Mod禁用时
void OnDestroy() // Mod卸载时
```

## 📚 进阶学习

### 推荐阅读

- [值得注意的API文档](../Documents/NotableAPIs_CN.md)
- [主README](../README.md)
- [DisplayItemValue示例](../DisplayItemValue/)

### 常用API命名空间

```csharp
using Duckov.Modding;              // Mod基础类
using Duckov.UI.DialogueBubbles;   // 对话气泡
using ItemStatsSystem;             // 物品系统
using TeamSoda.Players;            // 玩家管理
using Cysharp.Threading.Tasks;     // 异步任务（UniTask）
using UnityEngine;                 // Unity引擎
```

## 🎯 自定义开发建议

基于这个模板，你可以：

1. **添加更多物品** - 修改物品ID，生成不同的物品
2. **自定义按键** - 添加更多 `KeyCode` 检测
3. **创建UI界面** - 使用Unity的UI系统
4. **添加配置文件** - 保存Mod设置
5. **注册游戏事件** - 监听游戏内的各种事件
6. **自定义角色行为** - 修改角色属性和行为

## ⚠️ 注意事项

1. **命名空间** - 确保 `namespace` 名称与 `info.ini` 中的 `name` 一致
2. **类名** - ModBehaviour类必须命名为 `ModBehaviour`
3. **继承关系** - 必须继承自 `Duckov.Modding.ModBehaviour`
4. **目标框架** - 使用 `.NET Standard 2.1`
5. **异步编程** - 使用 `UniTask` 而不是 `Task`
6. **错误处理** - 添加 try-catch 保证稳定性

## 🐛 调试技巧

1. **查看日志**
   - 游戏日志位置（Windows）：`%AppData%\..\LocalLow\TeamSoda\Duckov\Player.log`
   - 使用 `Debug.Log()` 输出调试信息

2. **热重载**
   - 修改代码后重新编译
   - 在游戏Mod菜单中禁用后重新启用
   - 或重启游戏

3. **错误排查**
   - 检查命名空间是否正确
   - 确认DLL文件在正确位置
   - 查看 `info.ini` 配置是否正确

## 📝 版本信息

- **Mod版本**: 1.0.0
- **目标游戏**: 《逃离鸭科夫》(Escape from Duckov)
- **框架版本**: .NET Standard 2.1

## 🎨 社区准则

在发布Mod时，请遵守[鸭科夫社区准则](../README.md#鸭科夫社区准则)。

## 📧 反馈与支持

如果你在使用过程中遇到问题，可以：
- 查看[主项目README](../README.md)
- 参考[API文档](../Documents/NotableAPIs_CN.md)
- 加入鸭科夫Mod开发社区

---

**祝你Mod开发愉快！🦆**

上传steam:
steamcmd.exe +login user passwd +workshop_build_item D:\code\github\DuckovMercenarySystemMod\workshop.vdf +quit