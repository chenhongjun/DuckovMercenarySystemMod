# 🔍 查找物品ID指南

## 📋 方法总览

### 方法1：使用内置物品浏览器（推荐）✅
使用Mod提供的快捷键查看和测试物品ID

### 方法2：暴力枚举法 🔢
遍历可能的ID范围，测试每个ID

### 方法3：查看游戏文件 📁
分析游戏数据文件（如果可访问）

---

## 🎯 方法1：使用内置物品浏览器

### **快捷键功能**

| 按键 | 功能 | 说明 |
|------|------|------|
| **F8** | 列出所有物品 | 显示物品数据库结构 |
| **F7** | 搜索物品 | 按关键词搜索（需修改代码）|
| **F9** | 测试生成物品 | 生成指定ID的物品 |

### **使用步骤**

#### 1️⃣ **探索物品数据库结构**
```
游戏中操作：
1. 进入游戏
2. 按 F8 键
3. 查看控制台/日志输出
4. 记录物品数据库的结构信息
```

**预期输出：**
```
=== 开始列出所有物品 ===
物品数据库类型: [数据库类名]
找到 X 个字段和 Y 个属性
字段: items, 类型: List
属性: Count, 类型: Int32
...
```

#### 2️⃣ **搜索特定物品**
修改代码中的搜索关键词：

```csharp
// 在 Update() 方法中找到这一行：
if (Input.GetKeyDown(KeyCode.F7))
{
    SearchItems("coin");  // ← 修改这里！
}

// 搜索示例：
SearchItems("coin");    // 搜索金币
SearchItems("money");   // 搜索钱
SearchItems("gold");    // 搜索黄金
SearchItems("weapon");  // 搜索武器
SearchItems("food");    // 搜索食物
```

#### 3️⃣ **测试物品ID**
在代码中添加测试：

```csharp
// 在 Update() 方法中添加：
if (Input.GetKeyDown(KeyCode.F6))
{
    TestItemByID(100);  // 测试ID 100
}

if (Input.GetKeyDown(KeyCode.F5))
{
    TestItemByID(254);  // 测试ID 254（Glick）
}
```

---

## 🔢 方法2：暴力枚举法

### **手动测试范围**

创建一个ID范围测试函数：

```csharp
// 添加到 ModBehaviour 类中
private void TestIDRange(int start, int end)
{
    TestIDRangeAsync(start, end).Forget();
}

private async UniTaskVoid TestIDRangeAsync(int start, int end)
{
    Debug.Log($"=== 测试ID范围: {start} ~ {end} ===");
    
    for (int id = start; id <= end; id++)
    {
        try
        {
            Item item = await ItemAssetsCollection.InstantiateAsync(id);
            if (item != null)
            {
                Debug.Log($"✅ ID {id}: {item.name} ({item.GetType().Name})");
                
                // 立即销毁，不发送给玩家
                UnityEngine.Object.Destroy(item.gameObject);
            }
        }
        catch
        {
            // ID不存在，跳过
        }
        
        // 每10个ID暂停一下，避免卡顿
        if (id % 10 == 0)
        {
            await UniTask.Delay(100); // 暂停100ms
        }
    }
    
    Debug.Log("=== 范围测试完成 ===");
}
```

### **使用方法**
```csharp
// 在 Update() 中添加：
if (Input.GetKeyDown(KeyCode.F4))
{
    TestIDRange(1, 100);    // 测试 1-100
}

if (Input.GetKeyDown(KeyCode.F3))
{
    TestIDRange(100, 300);  // 测试 100-300
}
```

### **推荐的测试范围**
```
第1轮：1-100     （基础物品）
第2轮：100-300   （常见物品）
第3轮：200-500   （扩展测试）
```

---

## 📁 方法3：查看游戏数据

### **可能的数据位置**

```
游戏目录\
├── Duckov_Data\
│   ├── Resources\
│   │   └── Items.json (可能)
│   ├── StreamingAssets\
│   │   └── ItemDatabase.json (可能)
│   └── Managed\
│       └── ItemStatsSystem.dll (包含物品定义)
```

### **工具推荐**

1. **dnSpy** - 反编译 .NET DLL
   - 可以查看 ItemStatsSystem.dll
   - 查找物品枚举或常量定义

2. **Unity Asset Bundle Extractor**
   - 提取Unity资源文件
   - 查看物品预制体

---

## 🎯 实用技巧

### **快速找到金币ID**

#### 技巧1：关键词搜索
```csharp
// 常见的金钱相关关键词
SearchItems("coin");
SearchItems("money");
SearchItems("gold");
SearchItems("cash");
SearchItems("currency");
SearchItems("dollar");
```

#### 技巧2：观察已知物品
```
已知：ID 254 = Glick
推测：金币可能在附近的ID范围
测试：250-260
```

#### 技巧3：按类别测试
```
货币类：通常在较小的ID（1-50）
武器类：可能在 50-150
食物类：可能在 150-250
```

---

## 📊 记录模板

创建一个物品ID列表：

```markdown
# 物品ID列表

## 货币类
- [ ] ID ???: 金币 (Coin)
- [ ] ID ???: 钱 (Money)
- [x] ID 254: Glick

## 武器类
- [ ] ID ???: 剑
- [ ] ID ???: 枪

## 食物类
- [ ] ID ???: 面包
- [ ] ID ???: 水

## 其他
...
```

---

## 🔧 调试工具代码

将以下代码添加到 `ModBehaviour.cs`：

```csharp
// 物品ID快速查询工具
private Dictionary<string, int> knownItems = new Dictionary<string, int>()
{
    {"glick", 254},
    // 在这里添加你发现的物品ID
};

private void QuickSpawn(string itemName)
{
    if (knownItems.ContainsKey(itemName.ToLower()))
    {
        int id = knownItems[itemName.ToLower()];
        TestItemByID(id);
    }
    else
    {
        Debug.LogWarning($"未知物品: {itemName}");
    }
}
```

---

## 📝 查找流程总结

```
1. 进入游戏
   ↓
2. 按 F8 - 查看数据库结构
   ↓
3. 按 F7 - 搜索关键词（修改代码中的关键词）
   ↓
4. 按 F4/F3 - 测试ID范围
   ↓
5. 记录找到的物品ID
   ↓
6. 更新代码中的 bribePrice 和物品ID
```

---

## 🎁 最后建议

1. **有耐心** - 可能需要测试很多ID
2. **记录一切** - 建立自己的物品ID数据库
3. **查看日志** - Player.log 包含所有输出
4. **分享发现** - 和其他Mod开发者分享你的发现

---

**祝你找到金币ID！🪙**

