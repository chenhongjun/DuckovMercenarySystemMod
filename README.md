# DuckovMercenarySystemMod - 《逃离鸭科夫》雇佣兵系统Mod

一个功能完整的雇佣兵系统Mod，允许玩家使用金钱招募敌人成为友军，为你战斗。

## 📦 项目结构

```
DuckovMercenarySystemMod/
├── ModBehaviour.cs                 # Mod核心逻辑文件
├── DebugFeatures.cs                # 调试功能类（可选，条件编译）
├── GameObjectInspector.cs          # 游戏对象检查工具
├── BribeRecord.cs                   # 贿赂记录数据结构
├── DuckovMercenarySystemMod.csproj # C# 项目配置文件
├── DuckovMercenarySystemMod.sln    # Visual Studio 解决方案文件
├── README.md                       # 本说明文档
├── 更新日志.md                     # 版本更新历史
├── 物品ID列表.md                   # 常用物品ID参考
└── ReleaseExample/                 # 发布示例
    └── DuckovMercenarySystemMod/   # 实际发布的Mod文件夹
        ├── info.ini                # Mod配置信息
        ├── preview.png             # 预览图（可选）
        └── DuckovMercenarySystemMod.dll  # (编译后生成)
```

## ✨ 功能特性

### 🎮 核心功能

#### 1. **贿赂招募系统**
- **E键** - 靠近敌人（4米内）后按E进行贿赂
- 每次贿赂消耗 100 金币，转移给目标敌人
- 每个敌人有随机要价（50-800金币）
- 累计金额达到要价后，有概率成功招募
- 失败次数越多，成功概率越低（增加挑战性）

#### 2. **智能友军系统**
- ✅ **保留完整AI智能** - 友军会主动攻击敌人、使用掩体、躲避危险
- ✅ **自然跟随** - 友军围绕玩家周围移动，保持合理距离
- ✅ **动态调整** - 根据玩家移动速度自动调整跟随范围
- ✅ **友军上限** - 最多同时拥有 2 名友军（可配置）

#### 3. **队伍管理**
- **Q键** - 解散所有友军，恢复为敌对状态
- 队伍满员时，友军会显示拒绝消息

### 🔧 调试功能（可选）

调试功能已分离到独立的 `DebugFeatures` 类，通过条件编译控制：

- **F9键** - 给自己添加100测试金币
- **F7键** - 切换玩家锁血状态（防止生命值减少）
- **F6键** - 递归打印玩家和所有队友的属性（用于调试）

**启用调试功能：**
- 在项目文件中添加编译符号 `ENABLE_DEBUG_FEATURES`
- 或在代码文件顶部添加 `#define ENABLE_DEBUG_FEATURES`

**禁用调试功能（上线时）：**
- 不定义 `ENABLE_DEBUG_FEATURES` 宏，调试代码会被完全排除

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

### 基本操作

1. **招募友军**
   - 靠近敌人（4米内）
   - 按 **E键** 进行贿赂（每次100金币）
   - 持续贿赂直到累计金额达到敌人的要价
   - 有概率成功招募，失败后继续贿赂会增加难度

2. **管理队伍**
   - 按 **Q键** 解散所有友军
   - 队伍满员时无法招募新友军

3. **友军行为**
   - 友军会自动跟随玩家
   - 会主动攻击附近的敌人
   - 会使用掩体和战术动作
   - 保持合理的跟随距离

### 调试功能（如已启用）

- **F9键** - 添加100测试金币
- **F7键** - 切换锁血状态
- **F6键** - 打印玩家和友军属性信息

## 🔧 代码架构说明

### 核心类说明

#### ModBehaviour.cs
- Mod主类，包含所有核心游戏逻辑
- 贿赂系统、友军管理、AI控制等

#### DebugFeatures.cs
- 调试功能类，通过条件编译控制
- 包含锁血、添加金币、属性打印等功能
- 测试时启用，上线时禁用

#### GameObjectInspector.cs
- 游戏对象检查工具
- 用于递归打印游戏对象的属性和字段

### 核心实现原理

#### 1. 友军跟随机制
```csharp
// 通过修改AI的巡逻中心点实现跟随
FieldInfo patrolPosField = aiType.GetField("patrolPosition");
patrolPosField.SetValue(aiController, playerPosition); // 持续更新为玩家位置
```

#### 2. 贿赂系统
```csharp
// 扣除玩家金币并转移给敌人
RemovePlayerCoins(player, amount);
GiveCoinsToCharacter(enemy, amount);

// 检查是否达到要价并尝试招募
if (totalAmount >= requiredAmount) {
    bool success = Random.value < successChance;
    if (success) ConvertToAlly(enemy);
}
```

#### 3. 条件编译
```csharp
#if ENABLE_DEBUG_FEATURES
    // 调试功能代码
    debugFeatures.Update();
#endif
```

### 常用API

#### 物品系统
```csharp
// 创建物品
Item coinItem = ItemAssetsCollection.InstantiateSync(ITEM_ID_COIN);
// 设置数量
item.StackCount = amount;
// 发送到玩家背包
ItemUtilities.SendToPlayerCharacterInventory(item);
```

#### 对话气泡
```csharp
DialogueBubblesManager.Show(message, character.transform, duration);
```

#### 角色控制
```csharp
// 转换阵营
character.SetTeam(targetTeam);
// 获取玩家对象
GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
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

### 配置参数调整

在 `ModBehaviour.cs` 中可以修改以下参数：

```csharp
private int perBribeAmount = 100;      // 每次贿赂金额
private int minRequiredAmount = 50;    // 最低要价
private int maxRequiredAmount = 800;  // 最高要价
private float bribeRange = 4f;         // 贿赂范围（米）
private int maxAllyCount = 2;         // 友军上限
```

### 扩展功能建议

1. **添加配置文件** - 使用JSON或INI文件保存Mod设置
2. **自定义UI界面** - 显示友军状态、贿赂进度等
3. **友军命令系统** - 添加更多控制友军的命令
4. **友军装备管理** - 自动给友军装备武器和护甲
5. **友军等级系统** - 友军可以升级并获得更好的属性
6. **多语言支持** - 支持不同语言的对话消息

## ⚠️ 注意事项

1. **命名空间** - 确保 `namespace` 名称与 `info.ini` 中的 `name` 一致
2. **类名** - ModBehaviour类必须命名为 `ModBehaviour`
3. **继承关系** - 必须继承自 `Duckov.Modding.ModBehaviour`
4. **目标框架** - 使用 `.NET Standard 2.1`
5. **异步编程** - 使用 `UniTask` 而不是 `Task`
6. **错误处理** - 添加 try-catch 保证稳定性

## 🐛 调试技巧

### 启用调试功能

1. **方法一：修改项目文件**
   在 `DuckovMercenarySystemMod.csproj` 中添加：
   ```xml
   <PropertyGroup>
     <DefineConstants>ENABLE_DEBUG_FEATURES</DefineConstants>
   </PropertyGroup>
   ```

2. **方法二：在代码中定义**
   在 `ModBehaviour.cs` 或 `DebugFeatures.cs` 文件顶部添加：
   ```csharp
   #define ENABLE_DEBUG_FEATURES
   ```

### 调试工具

- **F6键** - 打印玩家和友军的详细属性信息
- **F7键** - 切换锁血状态（防止测试时死亡）
- **F9键** - 快速添加测试金币

### 日志查看

- **游戏日志位置（Windows）**：`%AppData%\..\LocalLow\TeamSoda\Duckov\Player.log`
- 使用 `Debug.Log()` 输出调试信息
- 使用 `打开日志.bat` 快速打开日志文件

### 常见问题排查

1. **Mod未加载**
   - 检查命名空间是否与 `info.ini` 中的 `name` 一致
   - 确认DLL文件在正确位置
   - 查看游戏日志中的错误信息

2. **功能不工作**
   - 确认Mod已在游戏中启用
   - 检查按键是否被其他Mod占用
   - 查看控制台日志中的错误信息

3. **友军不跟随**
   - 检查是否有AI控制器组件
   - 查看日志中的AI更新信息
   - 确认友军阵营是否正确转换

## 📝 版本信息

- **Mod版本**: 1.8
- **目标游戏**: 《逃离鸭科夫》(Escape from Duckov)
- **框架版本**: .NET Standard 2.1
- **最后更新**: 2025-01-XX

### 主要特性

- ✅ 完整的贿赂招募系统
- ✅ 智能友军AI跟随
- ✅ 友军保留完整战斗AI
- ✅ 调试功能分离（条件编译）
- ✅ 完善的错误处理机制

详细更新历史请查看 [更新日志.md](更新日志.md)

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
steamcmd.exe +login user passwd +workshop_build_item workshop.vdf +quit