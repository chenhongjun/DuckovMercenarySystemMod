using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovMercenarySystemMod
{
    /// <summary>
    /// 调试功能类 - 仅在开发/测试时启用
    /// 通过条件编译控制：定义 ENABLE_DEBUG_FEATURES 宏来启用
    /// </summary>
#if ENABLE_DEBUG_FEATURES
    public class DebugFeatures
    {
        private readonly ModBehaviour modBehaviour;
        
        // 锁血功能
        private bool isHealthLocked = false;
        private float lockedHealth = 100f;
        
        // F6调试打印功能（独立子类）
        private GameObjectInspector inspector = new GameObjectInspector();
        
        // 物品ID常量
        private const int ITEM_ID_COIN = 451;
        
        public DebugFeatures(ModBehaviour modBehaviour)
        {
            this.modBehaviour = modBehaviour;
        }
        
        /// <summary>
        /// 更新调试功能（在ModBehaviour.Update中调用）
        /// </summary>
        public void Update()
        {
            // 通用按键检测测试（调试用）
            if (Input.anyKeyDown)
            {
                // 检测F6-F12键
                for (int i = 6; i <= 12; i++)
                {
                    KeyCode fKey = (KeyCode)((int)KeyCode.F1 + i - 1);
                    if (Input.GetKeyDown(fKey))
                    {
                        Debug.Log($"🔍 [Update] 检测到按键按下: {fKey}");
                    }
                }
            }
            
            // F9键 - 测试：给自己添加金币（方便测试）
            if (Input.GetKeyDown(KeyCode.F9))
            {
                AddTestMoney();
            }
            
            // F7键 - 切换玩家锁血（方便测试）
            if (Input.GetKeyDown(KeyCode.F7))
            {
                Debug.Log("🔍 [Update] F7键被按下，准备切换锁血状态");
                ToggleHealthLock();
            }
            
            // F6键 - 递归打印玩家和所有队友的属性（方便测试）
            if (Input.GetKeyDown(KeyCode.F6))
            {
                Debug.Log("🔍 [Update] F6键被按下，准备打印玩家和队友属性");
                var player = modBehaviour.GetOrFindPlayer();
                var allies = modBehaviour.GetAllies();
                inspector.PrintPlayerAndAlliesProperties(player, allies);
            }
            
            // 锁血检查（如果开启锁血，持续恢复生命值）
            if (isHealthLocked)
            {
                MaintainPlayerHealth();
            }
        }
        
        /// <summary>
        /// 获取调试功能的说明文本（用于Awake中显示）
        /// </summary>
        public string GetDebugFeaturesDescription()
        {
            return "调试功能：\n" +
                   "  F9键 - 给自己添加测试金币\n" +
                   "  F7键 - 切换玩家锁血（防止生命值减少）\n" +
                   "  F6键 - 递归打印玩家和所有队友的属性";
        }
        
        /// <summary>
        /// F7键 - 切换玩家锁血状态
        /// </summary>
        private void ToggleHealthLock()
        {
            Debug.Log("🔍 [ToggleHealthLock] 函数开始执行");
            
            try
            {
                CharacterMainControl player = modBehaviour.GetOrFindPlayer();
                if (player == null)
                {
                    Debug.LogWarning("❌ [ToggleHealthLock] 未找到玩家，无法切换锁血");
                    return;
                }
                
                isHealthLocked = !isHealthLocked;
                
                if (isHealthLocked)
                {
                    lockedHealth = GetPlayerHealth(player);
                    
                    Debug.Log($"[ToggleHealthLock] 锁血已开启，锁定生命值: {lockedHealth}");
                    modBehaviour.ShowPlayerBubble($"锁血已开启 ({lockedHealth:F0} HP)", 2.5f);
                }
                else
                {
                    Debug.Log("[ToggleHealthLock] 锁血已关闭");
                    modBehaviour.ShowPlayerBubble("锁血已关闭", 2.5f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"切换锁血状态时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 维持玩家生命值（锁血功能 - 每帧强制设置为锁定值）
        /// </summary>
        private void MaintainPlayerHealth()
        {
            try
            {
                CharacterMainControl player = modBehaviour.GetOrFindPlayer();
                if (player == null)
                {
                    Debug.LogWarning("⚠️ [MaintainPlayerHealth] 未找到玩家");
                    return;
                }
                
                float currentHealth = GetPlayerHealth(player);
                
                // 如果当前生命值不等于锁定值，强制设置为锁定值
                // 使用小的误差范围（0.1）避免浮点数精度问题
                if (Mathf.Abs(currentHealth - lockedHealth) > 0.1f)
                {
                    Debug.Log($"[MaintainPlayerHealth] 检测到生命值变化: {currentHealth} → {lockedHealth} (锁定值: {lockedHealth})");
                    SetPlayerHealth(player, lockedHealth);
                    
                    // 验证设置是否成功
                    float verifyHealth = GetPlayerHealth(player);
                    if (Mathf.Abs(verifyHealth - lockedHealth) > 0.1f)
                    {
                        Debug.LogWarning($"[MaintainPlayerHealth] 锁血设置后验证失败: 期望 {lockedHealth}, 实际 {verifyHealth}");
                    }
                    else
                    {
                        Debug.Log($"[MaintainPlayerHealth] 锁血设置成功: {currentHealth} → {verifyHealth}");
                    }
                }
                else
                {
                    // 即使生命值匹配，也输出调试信息（降低频率）
                    if (Time.frameCount % 60 == 0) // 每60帧输出一次
                    {
                        Debug.Log($"[MaintainPlayerHealth] 生命值已锁定: {currentHealth} (锁定值: {lockedHealth})");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ [MaintainPlayerHealth] 维持玩家生命值时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 获取玩家生命值（通过Health组件的CurrentHealth属性）
        /// </summary>
        private float GetPlayerHealth(CharacterMainControl player)
        {
            try
            {
                if (player == null)
                {
                    Debug.LogWarning("⚠️ [GetPlayerHealth] 玩家对象为空");
                    return 0f;
                }
                
                // 通过CharacterMainControl的Health属性获取Health组件
                Type playerType = player.GetType();
                PropertyInfo healthProp = playerType.GetProperty("Health", BindingFlags.Public | BindingFlags.Instance);
                
                if (healthProp == null)
                {
                    Debug.LogWarning("⚠️ [GetPlayerHealth] 未找到CharacterMainControl.Health属性");
                    return 0f;
                }
                
                object healthComponent = healthProp.GetValue(player);
                if (healthComponent == null)
                {
                    Debug.LogWarning("⚠️ [GetPlayerHealth] Health组件为空");
                    return 0f;
                }
                
                // 通过Health组件的CurrentHealth属性获取当前生命值
                Type healthType = healthComponent.GetType();
                PropertyInfo currentHealthProp = healthType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
                
                if (currentHealthProp == null)
                {
                    Debug.LogWarning("⚠️ [GetPlayerHealth] 未找到Health.CurrentHealth属性");
                    return 0f;
                }
                
                object healthValue = currentHealthProp.GetValue(healthComponent);
                if (healthValue == null)
                {
                    Debug.LogWarning("⚠️ [GetPlayerHealth] CurrentHealth值为null");
                    return 0f;
                }
                
                float health = Convert.ToSingle(healthValue);
                
                // 尝试获取MaxHealth用于对比
                PropertyInfo maxHealthProp = healthType.GetProperty("MaxHealth", BindingFlags.Public | BindingFlags.Instance);
                if (maxHealthProp != null)
                {
                    object maxHealthValue = maxHealthProp.GetValue(healthComponent);
                    if (maxHealthValue != null)
                    {
                        float maxHealth = Convert.ToSingle(maxHealthValue);
                        Debug.Log($"✅ [GetPlayerHealth] CurrentHealth: {health}, MaxHealth: {maxHealth}");
                    }
                    else
                    {
                        Debug.Log($"✅ [GetPlayerHealth] 成功获取生命值: {health}");
                    }
                }
                else
                {
                    Debug.Log($"✅ [GetPlayerHealth] 成功获取生命值: {health}");
                }
                
                return health;
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ [GetPlayerHealth] 获取生命值时出错: {ex.Message}\n{ex.StackTrace}");
                return 0f;
            }
        }
        
        /// <summary>
        /// 设置玩家生命值（通过Health组件的CurrentHealth属性或SetHealth方法）
        /// </summary>
        private void SetPlayerHealth(CharacterMainControl player, float health)
        {
            try
            {
                if (player == null)
                {
                    Debug.LogWarning("⚠️ [SetPlayerHealth] 玩家对象为空");
                    return;
                }
                
                // 通过CharacterMainControl的Health属性获取Health组件
                Type playerType = player.GetType();
                PropertyInfo healthProp = playerType.GetProperty("Health", BindingFlags.Public | BindingFlags.Instance);
                
                if (healthProp == null)
                {
                    Debug.LogWarning("⚠️ [SetPlayerHealth] 未找到CharacterMainControl.Health属性");
                    return;
                }
                
                object healthComponent = healthProp.GetValue(player);
                if (healthComponent == null)
                {
                    Debug.LogWarning("⚠️ [SetPlayerHealth] Health组件为空");
                    return;
                }
                
                Type healthType = healthComponent.GetType();
                
                // 优先尝试使用SetHealth方法（更安全）
                MethodInfo setHealthMethod = healthType.GetMethod("SetHealth", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(float) }, null);
                if (setHealthMethod != null)
                {
                    setHealthMethod.Invoke(healthComponent, new object[] { health });
                    Debug.Log($"✅ [SetPlayerHealth] 使用SetHealth方法设置生命值: {health}");
                    
                    // 验证设置是否成功
                    PropertyInfo verifyProp = healthType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
                    if (verifyProp != null)
                    {
                        float verifyValue = Convert.ToSingle(verifyProp.GetValue(healthComponent));
                        if (Mathf.Abs(verifyValue - health) > 0.1f)
                        {
                            Debug.LogWarning($"⚠️ [SetPlayerHealth] SetHealth方法设置后验证失败: 期望 {health}, 实际 {verifyValue}");
                            // 如果SetHealth失败，尝试直接设置CurrentHealth属性
                            PropertyInfo fallbackHealthProp = healthType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
                            if (fallbackHealthProp != null && fallbackHealthProp.CanWrite)
                            {
                                fallbackHealthProp.SetValue(healthComponent, health);
                                Debug.Log($"✅ [SetPlayerHealth] 回退方案：直接设置CurrentHealth属性: {health}");
                            }
                        }
                    }
                    return;
                }
                
                // 如果SetHealth方法不存在，尝试直接设置CurrentHealth属性
                PropertyInfo currentHealthProp = healthType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
                if (currentHealthProp != null && currentHealthProp.CanWrite)
                {
                    float oldValue = Convert.ToSingle(currentHealthProp.GetValue(healthComponent));
                    currentHealthProp.SetValue(healthComponent, health);
                    
                    // 验证设置是否成功
                    float verifyValue = Convert.ToSingle(currentHealthProp.GetValue(healthComponent));
                    if (Mathf.Abs(verifyValue - health) > 0.1f)
                    {
                        Debug.LogWarning($"⚠️ [SetPlayerHealth] CurrentHealth属性设置后验证失败: 期望 {health}, 实际 {verifyValue}");
                    }
                    else
                    {
                        Debug.Log($"✅ [SetPlayerHealth] 使用CurrentHealth属性设置生命值: {oldValue} → {health}");
                    }
                    return;
                }
                
                Debug.LogWarning("⚠️ [SetPlayerHealth] 未找到SetHealth方法或CurrentHealth属性不可写");
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ [SetPlayerHealth] 设置生命值时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 添加测试金币（F9键）- 真实添加到背包
        /// </summary>
        private void AddTestMoney()
        {
            try
            {
                CharacterMainControl player = modBehaviour.GetOrFindPlayer();
                if (player == null)
                {
                    Debug.Log("❌ 未找到玩家");
                    return;
                }

                // 创建金币物品
                int testAmount = 100; // 每次添加100金币
                Item coinItem = ItemAssetsCollection.InstantiateSync(ITEM_ID_COIN);
                
                if (coinItem != null)
                {
                    modBehaviour.SetItemAmount(coinItem, testAmount);
                    
                    // 发送到玩家背包
                    bool success = ItemUtilities.SendToPlayerCharacterInventory(coinItem);
                    
                    if (success)
                    {
                        Debug.Log($"✅ 已添加 {testAmount} 金币到玩家背包");
                        
                        // 显示当前金币总数
                        int totalCoins = modBehaviour.CountPlayerCoins(player);
                        Debug.Log($"💰 当前金币总数: {totalCoins}");
                    }
                    else
                    {
                        Debug.LogWarning($"❌ 添加金币失败（背包可能已满）");
                        // 尝试直接放在玩家脚下
                        coinItem.transform.position = player.transform.position;
                        Debug.Log($"💰 {testAmount} 金币已掉落在玩家脚下");
                    }
                }
                else
                {
                    Debug.LogError($"❌ 无法创建金币物品 (ID: {ITEM_ID_COIN})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"添加测试金币时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// F6 - 探索AIControllerTemplate子对象（这是AI的核心）
        /// </summary>
        private void ExploreAIController()
        {
            try
            {
                var allies = modBehaviour.GetAllies();
                if (allies.Count == 0)
                {
                    Debug.Log("⚠️ 当前没有友军");
                    Debug.Log("💡 先用E键贿赂敌人，然后再按F6探索AI控制器");
                    return;
                }
                
                Debug.Log("=== 🤖 AIControllerTemplate 探索 ===");
                Debug.Log("");
                
                foreach (var ally in allies)
                {
                    if (ally == null) continue;
                    
                    Debug.Log($"角色: {ally.gameObject.name}");
                    Debug.Log($"位置: {ally.transform.position}");
                    Debug.Log("");
                    
                    // 查找AIControllerTemplate子对象
                    Transform aiControllerTransform = ally.transform.Find("AIControllerTemplate(Clone)");
                    if (aiControllerTransform == null)
                    {
                        // 尝试查找包含"AI"的子对象
                        Debug.Log("📍 查找所有子对象中包含'AI'的：");
                        foreach (Transform child in ally.transform)
                        {
                            if (child.name.ToLower().Contains("ai"))
                            {
                                aiControllerTransform = child;
                                Debug.Log($"   找到: {child.name}");
                                break;
                            }
                        }
                        
                        if (aiControllerTransform == null)
                        {
                            Debug.Log("   ⚠️ 未找到AI控制器");
                            continue;
                        }
                    }
                    
                    Debug.Log($"🎯 找到AI控制器: {aiControllerTransform.name}");
                    Debug.Log("");
                    
                    // 列出AI控制器的所有组件
                    Component[] aiComponents = aiControllerTransform.GetComponents<Component>();
                    Debug.Log($"📦 AI控制器组件 ({aiComponents.Length}个):");
                    foreach (var comp in aiComponents)
                    {
                        if (comp == null) continue;
                        
                        string typeName = comp.GetType().Name;
                        bool isMonoBehaviour = comp is MonoBehaviour;
                        bool isEnabled = isMonoBehaviour ? ((MonoBehaviour)comp).enabled : true;
                        string status = isMonoBehaviour ? (isEnabled ? "🟢" : "🔴") : "⚪";
                        
                        Debug.Log($"  {status} {typeName}");
                        
                        // 深度探索组件的字段和属性
                        if (isMonoBehaviour)
                        {
                            Type compType = comp.GetType();
                            
                            // 1. 所有字段（公共+私有）
                            var allFields = compType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            
                            // 筛选出位置相关的字段（Vector3, Transform）
                            var positionFields = allFields.Where(f => 
                                f.FieldType == typeof(Vector3) || 
                                f.FieldType == typeof(Transform) ||
                                f.Name.ToLower().Contains("position") ||
                                f.Name.ToLower().Contains("target") ||
                                f.Name.ToLower().Contains("home") ||
                                f.Name.ToLower().Contains("patrol") ||
                                f.Name.ToLower().Contains("spawn")
                            ).ToList();
                            
                            if (positionFields.Count > 0)
                            {
                                Debug.Log($"     🎯 位置相关字段 ({positionFields.Count}个):");
                                foreach (var field in positionFields)
                                {
                                    try
                                    {
                                        object value = field.GetValue(comp);
                                        string valueStr = value != null ? value.ToString() : "null";
                                        string accessLevel = field.IsPublic ? "public" : "private";
                                        
                                        // 计算距离玩家的距离（如果是Vector3）
                                        string distanceInfo = "";
                                        if (field.FieldType == typeof(Vector3) && value != null)
                                        {
                                            Vector3 pos = (Vector3)value;
                                            CharacterMainControl player = modBehaviour.GetOrFindPlayer();
                                            if (player != null)
                                            {
                                                float distance = Vector3.Distance(pos, player.transform.position);
                                                distanceInfo = $" [距离玩家: {distance:F1}米]";
                                            }
                                        }
                                        
                                        Debug.Log($"       🔹 {accessLevel} {field.Name} ({field.FieldType.Name}): {valueStr}{distanceInfo}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.Log($"       🔹 {field.Name} ({field.FieldType.Name}): [无法读取: {ex.Message}]");
                                    }
                                }
                            }
                            
                            // 2. 显示所有其他字段
                            var otherFields = allFields.Except(positionFields).ToList();
                            if (otherFields.Count > 0 && otherFields.Count < 20)  // 只显示不超过20个的
                            {
                                Debug.Log($"     📋 其他字段 ({otherFields.Count}个):");
                                foreach (var field in otherFields)
                                {
                                    try
                                    {
                                        object value = field.GetValue(comp);
                                        string valueStr = value != null ? value.ToString() : "null";
                                        if (valueStr.Length > 40) valueStr = valueStr.Substring(0, 40) + "...";
                                        string accessLevel = field.IsPublic ? "public" : "private";
                                        Debug.Log($"       • {accessLevel} {field.Name} ({field.FieldType.Name}): {valueStr}");
                                    }
                                    catch
                                    {
                                        Debug.Log($"       • {field.Name} ({field.FieldType.Name}): [无法读取]");
                                    }
                                }
                            }
                            else if (otherFields.Count > 0)
                            {
                                Debug.Log($"     📋 其他字段: {otherFields.Count}个（太多，已省略）");
                            }
                            
                            // 3. 属性（Properties）
                            var properties = compType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                            var positionProps = properties.Where(p => 
                                p.PropertyType == typeof(Vector3) || 
                                p.PropertyType == typeof(Transform) ||
                                p.Name.ToLower().Contains("position") ||
                                p.Name.ToLower().Contains("target")
                            ).ToList();
                            
                            if (positionProps.Count > 0)
                            {
                                Debug.Log($"     🔧 位置相关属性 ({positionProps.Count}个):");
                                foreach (var prop in positionProps)
                                {
                                    try
                                    {
                                        if (prop.CanRead)
                                        {
                                            object value = prop.GetValue(comp);
                                            string valueStr = value != null ? value.ToString() : "null";
                                            Debug.Log($"       🔸 {prop.Name} ({prop.PropertyType.Name}): {valueStr}");
                                        }
                                    }
                                    catch
                                    {
                                        Debug.Log($"       🔸 {prop.Name} ({prop.PropertyType.Name}): [无法读取]");
                                    }
                                }
                            }
                        }
                    }
                    
                    Debug.Log("");
                }
                
                Debug.Log("=== 探索完成 ===");
                Debug.Log("");
                Debug.Log("💡 使用建议：");
                Debug.Log("   1. 查找标记为 🔹 的位置相关字段（Vector3/Transform）");
                Debug.Log("   2. 特别关注包含 'target', 'home', 'patrol', 'spawn' 的字段");
                Debug.Log("   3. 使用反射修改这些字段为玩家位置");
                Debug.Log("   4. 或者直接禁用这些AI组件（SetActive(false)）");
            }
            catch (Exception ex)
            {
                Debug.LogError($"探索AI控制器时出错: {ex.Message}");
            }
        }
    }
#else
    // 调试功能类 - 发布版本为空实现
    public class DebugFeatures
    {
        public DebugFeatures(ModBehaviour modBehaviour) { }
        public void Update() { }
        public string GetDebugFeaturesDescription() { return ""; }
    }
#endif
}

