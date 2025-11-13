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
                isHealthLocked = !isHealthLocked;
                
                if (isHealthLocked)
                {
                    lockedHealth = GetPlayerHealth();
                    
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
                float currentHealth = GetPlayerHealth();
                
                // 获取MaxHealth，确保锁定的生命值不超过MaxHealth
                float maxHealth = GetMaxHealth();
                float targetHealth = Mathf.Min(lockedHealth, maxHealth); // 确保不超过MaxHealth
                
                // 如果当前生命值不等于目标值，强制设置为目标值
                // 使用小的误差范围（0.1）避免浮点数精度问题
                if (Mathf.Abs(currentHealth - targetHealth) > 0.1f)
                {
                    SetPlayerHealth(targetHealth);
                }
            }
            catch (Exception ex)
            {
                // 静默处理错误
            }
        }
        
        /// <summary>
        /// 获取玩家最大生命值
        /// </summary>
        private float GetMaxHealth()
        {
            try
            {
                // 获取主玩家的Health组件
                object healthComponent = GetMainPlayerHealthComponent();
                if (healthComponent == null)
                {
                    return 0f;
                }
                
                Type healthType = healthComponent.GetType();
                PropertyInfo maxHealthProp = healthType.GetProperty("MaxHealth", BindingFlags.Public | BindingFlags.Instance);
                
                if (maxHealthProp != null)
                {
                    object maxHealthValue = maxHealthProp.GetValue(healthComponent);
                    if (maxHealthValue != null)
                    {
                        return Convert.ToSingle(maxHealthValue);
                    }
                }
                
                return 0f;
            }
            catch (Exception ex)
            {
                return 0f;
            }
        }
        
        /// <summary>
        /// 获取主玩家的Health组件（通过IsMainCharacter或IsMainCharacterHealth属性）
        /// </summary>
        private object GetMainPlayerHealthComponent()
        {
            try
            {
                // 方法1：遍历所有CharacterMainControl，找到IsMainCharacter为True的
                CharacterMainControl[] allCharacters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                foreach (var character in allCharacters)
                {
                    Type charType = character.GetType();
                    PropertyInfo isMainCharProp = charType.GetProperty("IsMainCharacter", BindingFlags.Public | BindingFlags.Instance);
                    if (isMainCharProp != null)
                    {
                        object isMainValue = isMainCharProp.GetValue(character);
                        if (isMainValue != null && Convert.ToBoolean(isMainValue))
                        {
                            // 找到主玩家，获取其Health组件
                            PropertyInfo healthProp = charType.GetProperty("Health", BindingFlags.Public | BindingFlags.Instance);
                            if (healthProp != null)
                            {
                                object healthComponent = healthProp.GetValue(character);
                                if (healthComponent != null)
                                {
                                    return healthComponent;
                                }
                            }
                        }
                    }
                }
                
                // 方法2：遍历所有Health组件，找到IsMainCharacterHealth为True的
                Component[] allComponents = UnityEngine.Object.FindObjectsOfType<Component>();
                foreach (var component in allComponents)
                {
                    if (component == null) continue;
                    
                    Type compType = component.GetType();
                    if (compType.Name == "Health")
                    {
                        PropertyInfo isMainHealthProp = compType.GetProperty("IsMainCharacterHealth", BindingFlags.Public | BindingFlags.Instance);
                        if (isMainHealthProp != null)
                        {
                            object isMainValue = isMainHealthProp.GetValue(component);
                            if (isMainValue != null && Convert.ToBoolean(isMainValue))
                            {
                                return component;
                            }
                        }
                    }
                }
                
                // 方法3：回退到使用GetOrFindPlayer获取的Health组件
                CharacterMainControl player = modBehaviour.GetOrFindPlayer();
                if (player != null)
                {
                    Type playerType = player.GetType();
                    PropertyInfo healthProp = playerType.GetProperty("Health", BindingFlags.Public | BindingFlags.Instance);
                    if (healthProp != null)
                    {
                        object healthComponent = healthProp.GetValue(player);
                        if (healthComponent != null)
                        {
                            return healthComponent;
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        
        /// <summary>
        /// 获取玩家生命值（通过主玩家的Health组件）
        /// </summary>
        private float GetPlayerHealth()
        {
            try
            {
                // 获取主玩家的Health组件
                object healthComponent = GetMainPlayerHealthComponent();
                if (healthComponent == null)
                {
                    return 0f;
                }
                
                Type healthType = healthComponent.GetType();
                
                // 通过Health组件的CurrentHealth属性获取当前生命值
                PropertyInfo currentHealthProp = healthType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
                
                if (currentHealthProp == null)
                {
                    return 0f;
                }
                
                object healthValue = currentHealthProp.GetValue(healthComponent);
                if (healthValue == null)
                {
                    return 0f;
                }
                
                float health = Convert.ToSingle(healthValue);
                return health;
            }
            catch (Exception ex)
            {
                return 0f;
            }
        }
        
        /// <summary>
        /// 设置玩家生命值（使用AddHealth方法或直接设置CurrentHealth属性）
        /// </summary>
        private void SetPlayerHealth(float targetHealth)
        {
            try
            {
                // 获取主玩家的Health组件
                object healthComponent = GetMainPlayerHealthComponent();
                if (healthComponent == null)
                {
                    return;
                }
                
                Type healthType = healthComponent.GetType();
                
                // 获取当前生命值
                PropertyInfo currentHealthProp = healthType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
                if (currentHealthProp == null)
                {
                    return;
                }
                
                float currentHealth = Convert.ToSingle(currentHealthProp.GetValue(healthComponent));
                float healthDifference = targetHealth - currentHealth;
                
                // 如果目标生命值大于当前生命值，使用AddHealth方法增加
                if (healthDifference > 0.1f)
                {
                    MethodInfo addHealthMethod = healthType.GetMethod("AddHealth", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(float) }, null);
                    if (addHealthMethod != null)
                    {
                        addHealthMethod.Invoke(healthComponent, new object[] { healthDifference });
                    }
                    else
                    {
                        // 如果AddHealth不存在，直接设置CurrentHealth
                        currentHealthProp.SetValue(healthComponent, targetHealth);
                    }
                }
                // 如果目标生命值小于当前生命值，直接设置CurrentHealth（减少生命值）
                else if (healthDifference < -0.1f)
                {
                    currentHealthProp.SetValue(healthComponent, targetHealth);
                }
                // 如果已经接近目标值，不需要修改
                else
                {
                    // 生命值已经正确，不需要修改
                    return;
                }
            }
            catch (Exception ex)
            {
                // 静默处理错误
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

