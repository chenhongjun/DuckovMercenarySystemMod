# Unity单进程内NPC角色复制重建技术文档

## 概述

本文档说明如何在单个Unity游戏进程内，从一个场景（Scene A）复制NPC/敌人角色到另一个场景（Scene B）并完整重建，包括角色的外观、装备、状态以及**AI行为系统**。

## 目录

1. [核心策略](#核心策略)
2. [实现流程](#实现流程)
3. [关键API与参数](#关键api与参数)
4. [AI行为系统处理](#ai行为系统处理)
5. [完整代码示例](#完整代码示例)
6. [注意事项与最佳实践](#注意事项与最佳实践)

---

## 核心策略

### 1. 数据序列化策略

NPC角色需要保存以下数据：

- **基础信息**：
  - 位置（Vector3）
  - 旋转（Quaternion）
  - AI唯一ID（int）
  - 生成根ID（int，用于确定生成源）

- **外观数据**：
  - 角色模型名称（string）
  - 自定义面部配置（JSON）
  - 图标类型（CharacterIconTypes）
  - 显示名称（string）

- **装备数据**：
  - 护甲、头盔、面罩、背包、耳机（5个槽位）
  - 每个槽位的物品TypeID

- **武器数据**：
  - 主武器（枪械/近战）
  - 副武器
  - 手持槽位类型

- **状态数据**：
  - 当前血量（float）
  - 最大血量（float）
  - 物品树快照（ItemSnapshot，包含附件和容器内容）

- **AI行为数据**：
  - AI控制器状态（可选，通常由系统自动恢复）
  - FSM状态机当前状态（可选）
  - Blackboard黑板数据（可选）

### 2. 重建策略

采用**模板克隆 + 状态还原 + AI系统恢复**的方式：

1. **克隆模板对象**：使用角色预设或现有NPC对象作为模板
2. **应用序列化数据**：还原外观、装备、武器、血量等
3. **恢复AI行为**：启用AI组件，确保AI系统正常工作
4. **注册到管理系统**：将重建的角色注册到AI管理字典

---

## 实现流程

### 阶段一：场景A - 数据采集

#### 1.1 采集NPC角色快照

```csharp
public struct NPCCharacterSnapshot
{
    // 基础信息
    public int aiId;                    // AI唯一标识
    public int rootId;                  // 生成根ID
    public Vector3 position;             // 位置
    public Quaternion rotation;         // 旋转
    
    // 外观
    public string modelName;            // 模型名称
    public string customFaceJson;       // 外观JSON
    public int iconType;                // 图标类型
    public bool showName;               // 是否显示名字
    public string displayName;          // 显示名称
    
    // 装备（5槽）
    public List<(int slotHash, int itemTypeId)> equipmentList;
    
    // 武器
    public List<(int slotHash, int itemTypeId)> weaponList;
    
    // 状态
    public float currentHealth;
    public float maxHealth;
    public ItemSnapshot itemSnapshot;   // 完整物品树
    
    // AI相关（可选）
    public bool aiEnabled;              // AI是否启用
}
```

#### 1.2 采集实现

```csharp
public static NPCCharacterSnapshot CaptureNPCSnapshot(CharacterMainControl cmc)
{
    if (cmc == null || !AITool.IsRealAI(cmc))
        throw new ArgumentException("不是有效的NPC角色");
    
    var snapshot = new NPCCharacterSnapshot();
    
    // 1. 获取AI ID
    var tag = cmc.GetComponent<NetAiTag>();
    snapshot.aiId = tag != null ? tag.aiId : 0;
    
    // 2. 获取生成根ID
    var root = cmc.GetComponentInParent<CharacterSpawnerRoot>();
    snapshot.rootId = root != null ? AITool.StableRootId(root) : 0;
    
    // 3. 位置和旋转
    snapshot.position = cmc.transform.position;
    snapshot.rotation = cmc.modelRoot != null 
        ? cmc.modelRoot.transform.rotation 
        : cmc.transform.rotation;
    
    // 4. 模型名称
    snapshot.modelName = AIName.NormalizePrefabName(
        cmc.characterModel ? cmc.characterModel.name : null
    );
    
    // 5. 外观数据
    try
    {
        var preset = cmc.characterPreset;
        if (preset != null)
        {
            // 获取图标类型
            snapshot.iconType = (int)AIName.FR_IconType(preset);
            snapshot.showName = preset.showName;
            snapshot.displayName = preset.Name;
            
            // 获取外观JSON（如果使用玩家预设）
            if (FR_UsePlayerPreset(preset))
            {
                var faceData = LevelManager.Instance.CustomFaceManager.LoadMainCharacterSetting();
                snapshot.customFaceJson = JsonUtility.ToJson(faceData);
            }
            else
            {
                var facePreset = FR_FacePreset(preset);
                if (facePreset != null)
                    snapshot.customFaceJson = JsonUtility.ToJson(facePreset.settings);
            }
        }
    }
    catch (Exception ex)
    {
        Debug.LogWarning($"采集外观数据失败: {ex.Message}");
    }
    
    // 6. 装备数据
    snapshot.equipmentList = AITool.GetLocalAIEquipment(cmc)
        .Select(eq => {
            int itemId = 0;
            int.TryParse(eq.ItemId, out itemId);
            return (eq.SlotHash, itemId);
        })
        .ToList();
    
    // 7. 武器数据
    snapshot.weaponList = new List<(int, int)>();
    var gun = cmc.GetGun();
    if (gun != null && gun.Item != null)
    {
        snapshot.weaponList.Add(((int)gun.handheldSocket, gun.Item.TypeID));
    }
    var melee = cmc.GetMeleeWeapon();
    if (melee != null && melee.Item != null)
    {
        snapshot.weaponList.Add(((int)melee.handheldSocket, melee.Item.TypeID));
    }
    
    // 8. 血量数据
    var health = cmc.Health;
    if (health != null)
    {
        snapshot.currentHealth = health.CurrentHealth;
        snapshot.maxHealth = health.MaxHealth;
    }
    
    // 9. 物品树快照
    var characterItem = cmc.CharacterItem;
    if (characterItem != null)
    {
        snapshot.itemSnapshot = ItemTool.MakeSnapshot(characterItem);
    }
    
    // 10. AI状态
    var aiController = cmc.GetComponent<AICharacterController>();
    snapshot.aiEnabled = aiController != null && aiController.enabled;
    
    return snapshot;
}
```

### 阶段二：场景切换

#### 2.1 保存快照到临时存储

```csharp
// 场景切换前，采集所有NPC快照
public static List<NPCCharacterSnapshot> CaptureAllNPCsInScene()
{
    var snapshots = new List<NPCCharacterSnapshot>();
    
    // 遍历所有已注册的AI
    foreach (var kv in AITool.aiById)
    {
        var cmc = kv.Value;
        if (cmc == null || !AITool.IsRealAI(cmc)) continue;
        
        try
        {
            var snapshot = CaptureNPCSnapshot(cmc);
            snapshots.Add(snapshot);
        }
        catch (Exception ex)
        {
            Debug.LogError($"采集NPC快照失败 (aiId={kv.Key}): {ex.Message}");
        }
    }
    
    return snapshots;
}

// 保存到临时存储（可以是静态变量、ScriptableObject或文件）
public static class NPCTransferManager
{
    private static List<NPCCharacterSnapshot> _pendingSnapshots = new();
    
    public static void SaveSnapshotsForTransfer(List<NPCCharacterSnapshot> snapshots)
    {
        _pendingSnapshots = snapshots;
        Debug.Log($"[NPC-Transfer] 保存了 {snapshots.Count} 个NPC快照");
    }
    
    public static List<NPCCharacterSnapshot> GetPendingSnapshots()
    {
        return _pendingSnapshots;
    }
    
    public static void ClearPendingSnapshots()
    {
        _pendingSnapshots.Clear();
    }
}
```

#### 2.2 场景加载完成回调

```csharp
private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
{
    // 场景加载完成后，恢复NPC
    if (scene.name == "TargetScene")
    {
        // 延迟一帧，确保场景完全初始化
        StartCoroutine(RestoreNPCsAfterSceneLoad());
    }
}

private IEnumerator RestoreNPCsAfterSceneLoad()
{
    yield return null; // 等待一帧
    
    var snapshots = NPCTransferManager.GetPendingSnapshots();
    if (snapshots == null || snapshots.Count == 0)
        yield break;
    
    Debug.Log($"[NPC-Transfer] 开始恢复 {snapshots.Count} 个NPC");
    
    foreach (var snapshot in snapshots)
    {
        try
        {
            RestoreNPCFromSnapshot(snapshot).Forget();
        }
        catch (Exception ex)
        {
            Debug.LogError($"恢复NPC失败 (aiId={snapshot.aiId}): {ex.Message}");
        }
        
        // 每恢复一个NPC后等待一小段时间，避免一次性创建过多对象
        yield return new WaitForSeconds(0.1f);
    }
    
    NPCTransferManager.ClearPendingSnapshots();
    Debug.Log("[NPC-Transfer] NPC恢复完成");
}
```

### 阶段三：场景B - 角色重建

#### 3.1 重建NPC角色

```csharp
public static async UniTask RestoreNPCFromSnapshot(NPCCharacterSnapshot snapshot)
{
    // 1. 查找或创建生成根
    CharacterSpawnerRoot spawnerRoot = null;
    if (snapshot.rootId != 0)
    {
        spawnerRoot = FindSpawnerRootById(snapshot.rootId);
    }
    
    // 2. 查找角色预设（根据模型名称）
    CharacterModel modelPrefab = null;
    if (!string.IsNullOrEmpty(snapshot.modelName))
    {
        modelPrefab = AIName.FindCharacterModelByName_Any(snapshot.modelName);
    }
    
    // 3. 如果没有找到预设，使用默认NPC预设
    if (modelPrefab == null)
    {
        // 从CharacterSpawnerRoot获取预设，或使用系统默认预设
        if (spawnerRoot != null)
        {
            var spawnerComp = Traverse.Create(spawnerRoot)
                .Field<CharacterSpawnerComponentBase>("spawnerComponent")
                .Value;
            // 尝试从spawnerComponent获取预设
        }
        
        // 如果还是找不到，使用默认预设
        if (modelPrefab == null)
        {
            Debug.LogWarning($"[NPC-Transfer] 未找到模型预设: {snapshot.modelName}，使用默认预设");
            // 使用系统默认NPC预设
        }
    }
    
    // 4. 创建角色对象
    GameObject npcInstance = null;
    if (modelPrefab != null)
    {
        // 从模型预设创建
        npcInstance = GameObject.Instantiate(modelPrefab.gameObject, snapshot.position, snapshot.rotation);
    }
    else
    {
        // 使用CharacterSpawnerRoot的生成逻辑
        if (spawnerRoot != null)
        {
            // 触发生成逻辑（但需要确保只生成一个）
            // 注意：这里需要修改生成逻辑，避免重复生成
        }
        else
        {
            // 最后兜底：使用系统默认NPC对象
            Debug.LogError("[NPC-Transfer] 无法创建NPC：缺少模型预设和生成根");
            return;
        }
    }
    
    if (npcInstance == null) return;
    
    var cmc = npcInstance.GetComponent<CharacterMainControl>();
    if (cmc == null)
    {
        Debug.LogError("[NPC-Transfer] 创建的对象缺少CharacterMainControl组件");
        Object.Destroy(npcInstance);
        return;
    }
    
    // 5. 设置位置和旋转
    npcInstance.transform.SetPositionAndRotation(snapshot.position, snapshot.rotation);
    if (cmc.modelRoot != null)
    {
        var euler = snapshot.rotation.eulerAngles;
        cmc.modelRoot.transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
    }
    
    // 6. 应用物品树
    if (snapshot.itemSnapshot.typeId > 0)
    {
        var item = ItemTool.BuildItemFromSnapshot(snapshot.itemSnapshot);
        if (item != null)
        {
            Traverse.Create(cmc).Field<Item>("characterItem").Value = item;
        }
    }
    
    // 7. 应用外观
    if (!string.IsNullOrEmpty(snapshot.customFaceJson))
    {
        try
        {
            var faceData = JsonUtility.FromJson<CustomFaceSettingData>(snapshot.customFaceJson);
            if (cmc.characterModel?.CustomFace != null)
            {
                cmc.characterModel.CustomFace.LoadFromData(faceData);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"应用外观数据失败: {ex.Message}");
        }
    }
    
    // 8. 应用装备
    foreach (var (slotHash, itemTypeId) in snapshot.equipmentList)
    {
        if (itemTypeId > 0)
        {
            var item = await COOPManager.GetItemAsync(itemTypeId);
            if (item != null)
            {
                ApplyEquipmentToSlot(cmc, slotHash, item);
            }
        }
    }
    
    // 9. 应用武器
    foreach (var (slotHash, itemTypeId) in snapshot.weaponList)
    {
        if (itemTypeId > 0)
        {
            var item = await COOPManager.GetItemAsync(itemTypeId);
            if (item != null)
            {
                var socketType = (HandheldSocketTypes)slotHash;
                COOPManager.ChangeWeaponModel(cmc.characterModel, item, socketType);
            }
        }
    }
    
    // 10. 应用血量
    var health = cmc.Health;
    if (health != null && snapshot.maxHealth > 0)
    {
        health.autoInit = false; // 阻止自动初始化
        HealthM.Instance.ForceSetHealth(health, snapshot.maxHealth, snapshot.currentHealth);
    }
    
    // 11. 设置AI ID和注册
    var aiTag = cmc.GetComponent<NetAiTag>();
    if (aiTag == null)
        aiTag = cmc.gameObject.AddComponent<NetAiTag>();
    aiTag.aiId = snapshot.aiId;
    
    // 12. 恢复AI行为系统
    RestoreAIBehavior(cmc, snapshot);
    
    // 13. 注册到AI管理系统
    COOPManager.AIHandle.RegisterAi(snapshot.aiId, cmc);
    
    // 14. 应用名称和图标
    if (!string.IsNullOrEmpty(snapshot.displayName))
    {
        aiTag.nameOverride = snapshot.displayName;
    }
    if (snapshot.iconType > 0)
    {
        aiTag.iconTypeOverride = snapshot.iconType;
    }
    aiTag.showNameOverride = snapshot.showName;
    
    Debug.Log($"[NPC-Transfer] NPC恢复成功: aiId={snapshot.aiId}, model={snapshot.modelName}");
}
```

#### 3.2 恢复AI行为系统

```csharp
private static void RestoreAIBehavior(CharacterMainControl cmc, NPCCharacterSnapshot snapshot)
{
    // 1. 确保AI控制器存在
    var aiController = cmc.GetComponent<AICharacterController>();
    if (aiController == null)
    {
        // 如果不存在，尝试添加（需要根据项目实际情况）
        Debug.LogWarning("[NPC-Transfer] NPC缺少AICharacterController组件");
        return;
    }
    
    // 2. 启用AI控制器
    if (snapshot.aiEnabled)
    {
        aiController.enabled = true;
    }
    
    // 3. 确保NavMeshAgent启用
    var navAgent = cmc.GetComponent<NavMeshAgent>();
    if (navAgent != null)
    {
        navAgent.enabled = true;
    }
    
    // 4. 确保FSM状态机启用
    var fsmOwner = cmc.GetComponent<FSMOwner>();
    if (fsmOwner != null)
    {
        fsmOwner.enabled = true;
    }
    
    // 5. 确保Blackboard启用
    var blackboard = cmc.GetComponent<Blackboard>();
    if (blackboard != null)
    {
        blackboard.enabled = true;
    }
    
    // 6. 确保AI路径控制启用
    var pathControl = cmc.GetComponent<AI_PathControl>();
    if (pathControl != null)
    {
        pathControl.enabled = true;
    }
    
    // 7. 确保CharacterController启用（如果使用）
    var charController = cmc.GetComponent<CharacterController>();
    if (charController != null)
    {
        charController.enabled = true;
    }
    
    // 8. 确保Rigidbody不是运动学（如果使用物理驱动）
    var rb = cmc.GetComponent<Rigidbody>();
    if (rb != null && rb.isKinematic)
    {
        rb.isKinematic = false;
    }
    
    // 9. 确保Animator启用根运动（如果需要）
    var animator = cmc.GetComponentInChildren<Animator>();
    if (animator != null)
    {
        animator.applyRootMotion = true; // AI需要根运动
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // 优化模式
    }
    
    Debug.Log($"[NPC-Transfer] AI行为系统已恢复: aiId={snapshot.aiId}");
}
```

#### 3.3 查找生成根

```csharp
private static CharacterSpawnerRoot FindSpawnerRootById(int rootId)
{
    // 方法1：通过SpawnerGuid查找
    var allRoots = Object.FindObjectsOfType<CharacterSpawnerRoot>(true);
    foreach (var root in allRoots)
    {
        if (root.SpawnerGuid != 0 && root.SpawnerGuid == rootId)
            return root;
    }
    
    // 方法2：通过稳定ID计算查找
    foreach (var root in allRoots)
    {
        var stableId = AITool.StableRootId(root);
        if (stableId == rootId)
            return root;
    }
    
    return null;
}
```

#### 3.4 应用装备到槽位

```csharp
private static void ApplyEquipmentToSlot(CharacterMainControl cmc, int slotHash, Item item)
{
    var model = cmc.characterModel;
    if (model == null) return;
    
    if (slotHash == CharacterEquipmentController.armorHash)
        COOPManager.ChangeArmorModel(model, item);
    else if (slotHash == CharacterEquipmentController.helmatHash)
        COOPManager.ChangeHelmatModel(model, item);
    else if (slotHash == CharacterEquipmentController.faceMaskHash)
        COOPManager.ChangeFaceMaskModel(model, item);
    else if (slotHash == CharacterEquipmentController.backpackHash)
        COOPManager.ChangeBackpackModel(model, item);
    else if (slotHash == CharacterEquipmentController.headsetHash)
        COOPManager.ChangeHeadsetModel(model, item);
}
```

---

## 关键API与参数

### 1. AI识别与管理API

#### `AITool.IsRealAI(CharacterMainControl cmc)`
**功能**：判断是否为真正的AI角色（排除玩家、宠物等）  
**参数**：
- `cmc` (CharacterMainControl): 角色控制器

**返回值**：bool

#### `AITool.StableRootId(CharacterSpawnerRoot root)`
**功能**：计算生成根的稳定ID  
**参数**：
- `root` (CharacterSpawnerRoot): 生成根对象

**返回值**：int（稳定ID）

**说明**：优先使用`SpawnerGuid`，否则使用场景索引+名称+位置的哈希

#### `AITool.DeriveSeed(int rootId, int serial)`
**功能**：根据根ID和序号派生AI ID  
**参数**：
- `rootId` (int): 生成根ID
- `serial` (int): 序号（从1开始）

**返回值**：int（AI唯一ID）

#### `COOPManager.AIHandle.RegisterAi(int aiId, CharacterMainControl cmc)`
**功能**：注册AI到管理系统  
**参数**：
- `aiId` (int): AI唯一标识
- `cmc` (CharacterMainControl): 角色控制器

**说明**：注册后可通过`AITool.aiById[aiId]`访问

### 2. 外观与模型API

#### `AIName.NormalizePrefabName(string name)`
**功能**：规范化模型名称（去除Clone后缀等）  
**参数**：
- `name` (string): 原始名称

**返回值**：string（规范化后的名称）

#### `AIName.FindCharacterModelByName_Any(string modelName)`
**功能**：根据名称查找角色模型预设  
**参数**：
- `modelName` (string): 模型名称

**返回值**：CharacterModel（模型预设）

#### `AIName.FR_IconType(CharacterRandomPreset preset)`
**功能**：获取角色图标类型（反射访问）  
**参数**：
- `preset` (CharacterRandomPreset): 角色预设

**返回值**：CharacterIconTypes

### 3. 装备与武器API

#### `AITool.GetLocalAIEquipment(CharacterMainControl cmc)`
**功能**：获取AI角色的装备列表  
**参数**：
- `cmc` (CharacterMainControl): 角色控制器

**返回值**：`List<EquipmentSyncData>`

**EquipmentSyncData结构**：
```csharp
public struct EquipmentSyncData
{
    public int SlotHash;    // 槽位哈希
    public string ItemId;   // 物品ID（字符串）
}
```

#### `COOPManager.ChangeArmorModel(CharacterModel model, Item item)`
**功能**：更换护甲模型  
**参数**：
- `model` (CharacterModel): 角色模型
- `item` (Item): 护甲物品（null表示移除）

#### `COOPManager.ChangeWeaponModel(CharacterModel model, Item item, HandheldSocketTypes socket)`
**功能**：更换武器模型  
**参数**：
- `model` (CharacterModel): 角色模型
- `item` (Item): 武器物品
- `socket` (HandheldSocketTypes): 手持槽位类型

### 4. 物品序列化API

#### `ItemTool.MakeSnapshot(Item item)`
**功能**：创建物品快照（递归包含所有子物品）  
**参数**：
- `item` (Item): 要序列化的物品

**返回值**：`ItemSnapshot`

#### `ItemTool.BuildItemFromSnapshot(ItemSnapshot snapshot)`
**功能**：从快照重建物品对象  
**参数**：
- `snapshot` (ItemSnapshot): 物品快照

**返回值**：Item（异步加载）

**注意**：此方法内部调用`COOPManager.GetItemAsync()`，是异步的

#### `COOPManager.GetItemAsync(int itemTypeId)`
**功能**：异步加载物品资源  
**参数**：
- `itemTypeId` (int): 物品类型ID

**返回值**：`Task<Item>`

### 5. 血量系统API

#### `HealthM.Instance.ForceSetHealth(Health health, float max, float current)`
**功能**：强制设置血量（绕过自动初始化）  
**参数**：
- `health` (Health): 血量组件
- `max` (float): 最大血量
- `current` (float): 当前血量

### 6. AI行为系统API

#### `AITool.TryFreezeAI(CharacterMainControl cmc)`
**功能**：冻结AI（禁用所有AI相关组件）  
**参数**：
- `cmc` (CharacterMainControl): 角色控制器

**说明**：禁用AICharacterController、FSMOwner、Blackboard、NavMeshAgent等

#### AI组件启用/禁用
```csharp
// AICharacterController
var aiController = cmc.GetComponent<AICharacterController>();
aiController.enabled = true; // 启用AI

// NavMeshAgent
var navAgent = cmc.GetComponent<NavMeshAgent>();
navAgent.enabled = true;

// FSMOwner（状态机）
var fsmOwner = cmc.GetComponent<FSMOwner>();
fsmOwner.enabled = true;

// Blackboard（黑板）
var blackboard = cmc.GetComponent<Blackboard>();
blackboard.enabled = true;
```

---

## AI行为系统处理

### 1. AI组件结构

典型的NPC角色包含以下AI相关组件：

```
CharacterMainControl
├── AICharacterController      // AI控制器（核心）
├── NavMeshAgent               // 导航网格代理
├── FSMOwner                   // 状态机拥有者
├── Blackboard                 // 黑板（存储AI变量）
├── AI_PathControl             // AI路径控制
├── CharacterController        // 角色控制器（物理）
└── Rigidbody                  // 刚体（可选）
```

### 2. AI ID生成规则

AI ID的生成遵循确定性规则，确保同一NPC在不同场景中具有相同的ID：

```csharp
// 规则：aiId = DeriveSeed(rootId, serial)
// rootId: 生成根的稳定ID
// serial: 在该根下的序号（从1开始）

// 示例：
// rootId = 12345 (来自CharacterSpawnerRoot)
// serial = 1 (第一个生成的NPC)
// aiId = DeriveSeed(12345, 1) = 确定的哈希值
```

### 3. AI行为恢复要点

#### 3.1 组件启用顺序

```csharp
// 正确的启用顺序：
// 1. 基础组件（CharacterController, Rigidbody）
// 2. 导航组件（NavMeshAgent）
// 3. AI逻辑组件（AICharacterController）
// 4. 状态机（FSMOwner）
// 5. 黑板（Blackboard）
```

#### 3.2 避免AI冲突

```csharp
// 在恢复AI前，确保：
// 1. 位置已设置（NavMeshAgent需要有效位置）
// 2. 血量已设置（避免AI因死亡状态异常）
// 3. 装备已应用（AI可能需要装备信息）
```

#### 3.3 处理AI初始化延迟

```csharp
// AI组件可能需要一帧才能完全初始化
// 使用协程或UniTask延迟启用
private IEnumerator EnableAIAfterFrame(CharacterMainControl cmc)
{
    yield return null; // 等待一帧
    
    var aiController = cmc.GetComponent<AICharacterController>();
    if (aiController != null)
    {
        aiController.enabled = true;
    }
}
```

---

## 完整代码示例

### 场景切换管理器

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

public class NPCSceneTransferManager : MonoBehaviour
{
    private static NPCSceneTransferManager _instance;
    public static NPCSceneTransferManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("NPCTransferManager");
                _instance = go.AddComponent<NPCSceneTransferManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    // 场景切换前调用：保存当前场景的所有NPC
    public void SaveNPCsBeforeSceneSwitch(string targetSceneName)
    {
        var snapshots = CaptureAllNPCsInScene();
        NPCTransferManager.SaveSnapshotsForTransfer(snapshots);
        
        Debug.Log($"[NPC-Transfer] 已保存 {snapshots.Count} 个NPC，准备切换到: {targetSceneName}");
    }
    
    // 场景加载完成回调
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var snapshots = NPCTransferManager.GetPendingSnapshots();
        if (snapshots == null || snapshots.Count == 0)
            return;
        
        // 延迟恢复，确保场景完全初始化
        StartCoroutine(RestoreNPCsAfterSceneLoad());
    }
    
    private IEnumerator RestoreNPCsAfterSceneLoad()
    {
        yield return new WaitForSeconds(0.5f); // 等待场景初始化
        
        var snapshots = NPCTransferManager.GetPendingSnapshots();
        Debug.Log($"[NPC-Transfer] 开始恢复 {snapshots.Count} 个NPC");
        
        foreach (var snapshot in snapshots)
        {
            try
            {
                RestoreNPCFromSnapshot(snapshot).Forget();
            }
            catch (Exception ex)
            {
                Debug.LogError($"恢复NPC失败 (aiId={snapshot.aiId}): {ex.Message}");
            }
            
            yield return new WaitForSeconds(0.1f); // 避免一次性创建过多
        }
        
        NPCTransferManager.ClearPendingSnapshots();
        Debug.Log("[NPC-Transfer] NPC恢复完成");
    }
    
    // 使用示例
    public void SwitchSceneWithNPCs(string targetSceneName)
    {
        // 1. 保存NPC
        SaveNPCsBeforeSceneSwitch(targetSceneName);
        
        // 2. 切换场景
        SceneManager.LoadScene(targetSceneName);
        
        // 3. 恢复NPC（在OnSceneLoaded中自动执行）
    }
}
```

---

## 注意事项与最佳实践

### 1. AI ID唯一性

- **确保ID唯一**：使用稳定的生成规则，避免ID冲突
- **场景切换时保持ID**：同一NPC在不同场景应保持相同ID
- **ID冲突处理**：如果检测到ID冲突，可以选择覆盖或跳过

### 2. 资源加载

- **异步加载**：物品资源加载是异步的，使用`await`或`UniTask`
- **加载失败处理**：提供默认物品或跳过该装备
- **批量加载优化**：考虑使用资源池或预加载

### 3. AI行为恢复

- **组件依赖**：确保组件按正确顺序启用
- **初始化延迟**：AI组件可能需要一帧才能完全初始化
- **状态同步**：如果AI有复杂状态（FSM状态、Blackboard变量），需要额外保存和恢复

### 4. 性能优化

- **分批恢复**：不要一次性恢复所有NPC，分批处理
- **距离剔除**：只恢复玩家附近的NPC
- **对象池**：考虑复用NPC对象，避免频繁创建销毁

### 5. 错误处理

```csharp
try
{
    var item = await COOPManager.GetItemAsync(itemTypeId);
    if (item == null)
    {
        Debug.LogWarning($"物品加载失败: {itemTypeId}，使用默认物品");
        // 使用默认物品或跳过
        continue;
    }
    // 应用物品
}
catch (Exception ex)
{
    Debug.LogError($"恢复NPC装备失败: {ex.Message}");
    // 继续处理其他装备
}
```

### 6. 内存管理

- **及时清理**：场景切换时清理旧场景的NPC引用
- **快照清理**：恢复完成后清理快照数据
- **资源释放**：临时创建的对象及时销毁

```csharp
// 场景切换时清理
private void OnSceneUnloaded(Scene scene)
{
    // 清理已注册的AI（可选，取决于需求）
    // AITool.aiById.Clear(); // 谨慎使用，可能影响其他系统
}
```

### 7. AI行为状态保存（高级）

如果需要保存AI的详细行为状态：

```csharp
// 保存FSM状态
public struct FSMStateSnapshot
{
    public string currentStateName;
    public Dictionary<string, object> blackboardValues;
}

// 保存FSM状态
private FSMStateSnapshot CaptureFSMState(CharacterMainControl cmc)
{
    var snapshot = new FSMStateSnapshot();
    
    var fsmOwner = cmc.GetComponent<FSMOwner>();
    if (fsmOwner != null && fsmOwner.graph != null)
    {
        // 获取当前状态（需要反射或公开API）
        // snapshot.currentStateName = ...
    }
    
    var blackboard = cmc.GetComponent<Blackboard>();
    if (blackboard != null)
    {
        // 保存黑板变量（需要反射访问）
        // snapshot.blackboardValues = ...
    }
    
    return snapshot;
}

// 恢复FSM状态
private void RestoreFSMState(CharacterMainControl cmc, FSMStateSnapshot snapshot)
{
    var fsmOwner = cmc.GetComponent<FSMOwner>();
    if (fsmOwner != null)
    {
        // 设置当前状态
        // ...
    }
    
    var blackboard = cmc.GetComponent<Blackboard>();
    if (blackboard != null)
    {
        // 恢复黑板变量
        // ...
    }
}
```

---

## 总结

单进程内NPC角色复制重建的核心流程：

1. **数据采集** → 序列化NPC状态（位置、外观、装备、血量、AI ID）
2. **场景切换** → 保存快照到临时存储
3. **场景加载** → 场景加载完成后触发恢复
4. **对象重建** → 克隆模板 + 应用状态数据
5. **AI恢复** → 启用AI组件，确保AI系统正常工作
6. **注册管理** → 注册到AI管理系统

关键点：
- ✅ 使用稳定的AI ID生成规则
- ✅ 完整的状态序列化（包含物品树）
- ✅ AI组件按正确顺序启用
- ✅ 异步资源加载避免阻塞
- ✅ 错误处理和性能优化

---

**文档版本**：v1.0  
**最后更新**：2025-01-27  
**参考项目**：Escape-From-Duckov-Coop-Mod-Preview

