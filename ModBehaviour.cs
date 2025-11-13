using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovMercenarySystemMod
{
    /// <summary>
    /// 雇佣兵系统Mod - Mercenary System Mod
    /// 功能：使用金钱招募敌人成为雇佣兵，为你战斗
    /// </summary>
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 配置参数
        private int perBribeAmount = 100;          // 每次贿赂向敌人转移的金额
        private int minRequiredAmount = 50;        // 每个敌人最少报价
        private int maxRequiredAmount = 800;      // 每个敌人最高报价
        private float bribeRange = 4f;             // 贿赂范围（米）- 俯视图游戏用靠近方式
        
        // 物品ID常量
        private const int ITEM_ID_COIN = 451;      // 金币ID
        
        // 缓存的玩家队伍信息
        private Teams cachedPlayerTeam;
        private bool hasCachedPlayerTeam = false;
        
        // 存储每个敌人的贿赂记录
        private Dictionary<CharacterMainControl, BribeRecord> bribeRecords = new Dictionary<CharacterMainControl, BribeRecord>();
        
        // 存储被贿赂的友军（跟随玩家移动）
        private List<CharacterMainControl> allies = new List<CharacterMainControl>();
        private int maxAllyCount = 2;               // 友军上限
        
        // AI状态重置冷却时间（避免频繁重置）
        private Dictionary<CharacterMainControl, float> lastResetTime = new Dictionary<CharacterMainControl, float>();
        private float resetCooldown = 2f;          // 重置冷却时间（秒）
        private static readonly string[] MaxPartyAllyMessages = new[]
        {
            "够了够了，别挤！",
            "队伍满编，暂停。",
            "让点位置透口气。",
            "人手够用，别招了。",
            "我护甲要被挤坏了！",
            "再来我就没床睡了。",
            "兄弟，战利品不够分。",
            "口粮告急，先别加人。",
            "爆满啦，保持阵型。",
            "排队来，别抢道。"
        };
        
        // 友军跟随更新参数
        private float followUpdateInterval = 0.05f; // 跟随更新间隔（秒）- 每秒20次
        private float followTimer = 0f;
        private Vector3 lastPlayerPosition = Vector3.zero; // 上次玩家位置（用于计算移动速度）
        private float playerMoveSpeed = 0f; // 玩家移动速度（米/秒）
        
        // AI控制器缓存（避免每次Update都查找Transform和GetComponent）
        // 注意：缓存的是组件对象引用，不是数据。每次通过反射读取字段值时都是实时读取，数据始终是最新的
        private Dictionary<CharacterMainControl, Component> aiControllerCache = new Dictionary<CharacterMainControl, Component>();
        
#if ENABLE_DEBUG_FEATURES
        // 调试功能类（仅在定义了 ENABLE_DEBUG_FEATURES 时启用）
        private DebugFeatures? debugFeatures;
#endif

        void Awake()
        {
            Debug.Log("=== 雇佣兵系统Mod v1.8 已加载 ===");
            Debug.Log("功能说明：");
            Debug.Log($"  E键 - 靠近敌人后按E贿赂（每次 {perBribeAmount} 金币，范围{bribeRange}米）");
            Debug.Log($"  转换条件：敌人随机要价 {minRequiredAmount}-{maxRequiredAmount} 金币，凑够后有概率招募（失败越多越倔强）");
            Debug.Log($"  ✅ 友军保留完整AI智能（会攻击、会躲避、自然移动）");
            Debug.Log($"  Q键 - 解散所有友军");
#if ENABLE_DEBUG_FEATURES
            debugFeatures = new DebugFeatures(this);
            string debugDesc = debugFeatures.GetDebugFeaturesDescription();
            if (!string.IsNullOrEmpty(debugDesc))
            {
                Debug.Log(debugDesc);
            }
#endif
            Debug.Log("========================");

            // 启动时预缓存玩家与队伍信息
            GetOrFindPlayer();
        }

        void Update()
        {
            // E键 - 贿赂敌人
            if (Input.GetKeyDown(KeyCode.E))
            {
                TryBribeEnemy();
            }
            
            // Q键 - 解散所有友军
            if (Input.GetKeyDown(KeyCode.Q))
            {
                DismissAllAllies();
            }
            
#if ENABLE_DEBUG_FEATURES
            // 调试功能更新
            if (debugFeatures != null)
            {
                debugFeatures.Update();
            }
#endif
            
            // 更新友军跟随
            UpdateAlliesFollow();
        }
        
        /// <summary>
        /// 更新所有友军的跟随行为（优化版：计算玩家移动速度）
        /// </summary>
        private void UpdateAlliesFollow()
        {
            if (allies.Count == 0) return;

            // 使用计时器减少更新频率
            followTimer += Time.deltaTime;
            if (followTimer < followUpdateInterval)
            {
                return;
            }
            followTimer = 0f;
            
            CharacterMainControl player = GetOrFindPlayer();
            if (player == null) return;
            
            Vector3 playerPos = player.transform.position;
            
            // 计算玩家移动速度（用于动态调整巡逻范围）
            if (lastPlayerPosition != Vector3.zero)
            {
                float distanceMoved = Vector3.Distance(lastPlayerPosition, playerPos);
                playerMoveSpeed = distanceMoved / followUpdateInterval; // 米/秒
            }
            else
            {
                playerMoveSpeed = 0f;
            }
            lastPlayerPosition = playerPos;
            
                // 清理已死亡或无效的友军
                var invalidAllies = allies.Where(ally => ally == null || ally.gameObject == null).ToList();
                foreach (var invalidAlly in invalidAllies)
                {
                    lastResetTime.Remove(invalidAlly); // 清理重置时间记录
                    aiControllerCache.Remove(invalidAlly); // 清理AI控制器缓存
                }
                allies.RemoveAll(ally => ally == null || ally.gameObject == null);
            
            // 更新每个友军的移动
            foreach (var ally in allies)
            {
                try
                {
                    // 🔑 关键检查：确保ally不是玩家自己
                    if (ally == null || ally == player)
                    {
                        if (ally == player)
                        {
                            Debug.LogWarning($"[UpdateAlliesFollow] ⚠️ 发现玩家自己被添加到友军列表，跳过并移除");
                            allies.Remove(ally);
                        }
                        continue;
                    }
                    
                    // 验证对象ID（确保不是同一个对象）
                    if (ally.GetInstanceID() == player.GetInstanceID())
                    {
                        Debug.LogWarning($"[UpdateAlliesFollow] ⚠️ 发现玩家和友军是同一个对象实例 (ID: {ally.GetInstanceID()})，跳过并移除");
                        allies.Remove(ally);
                        continue;
                    }
                    
                    UpdateAllyFollow(ally, player);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"更新友军跟随时出错: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Q键 - 解散所有友军
        /// </summary>
        private void DismissAllAllies()
        {
            try
            {
                if (allies.Count == 0)
                {
                    Debug.Log("⚠️ 当前没有友军需要解散");
                    ShowPlayerBubble("没有友军需要解散", 2f);
                    return;
                }
                
                int count = allies.Count;
                CharacterMainControl player = GetOrFindPlayer();
                Teams originalTeam = Teams.scav; // 默认恢复到scav阵营
                
                // 清理无效的友军
                var invalidAllies = allies.Where(ally => ally == null || ally.gameObject == null).ToList();
                foreach (var invalidAlly in invalidAllies)
                {
                    lastResetTime.Remove(invalidAlly); // 清理重置时间记录
                }
                allies.RemoveAll(ally => ally == null || ally.gameObject == null);
                
                // 解散所有友军
                foreach (var ally in allies.ToList())
                {
                    try
                    {
                        if (ally != null && ally.gameObject != null)
                        {
                            // 恢复为敌对阵营（或删除）
                            ally.SetTeam(originalTeam);
                            lastResetTime.Remove(ally); // 清理重置时间记录
                            Debug.Log($"✅ 已解散友军: {ally.gameObject.name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"解散友军时出错: {ex.Message}");
                    }
                }
                
                allies.Clear();
                lastResetTime.Clear(); // 清理所有重置时间记录
                aiControllerCache.Clear(); // 清理AI控制器缓存
                bribeRecords.Clear(); // 清空贿赂记录
                
                Debug.Log($"✅ 已解散所有友军 (共 {count} 名)");
                ShowPlayerBubble($"已解散所有友军 ({count}名)", 3f);
            }
            catch (Exception ex)
            {
                Debug.LogError($"解散友军时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        // ============================================
        // 公共方法 - 供 DebugFeatures 访问
        // ============================================
        
        /// <summary>
        /// 获取或查找玩家角色（公共方法，供DebugFeatures使用）
        /// </summary>
        public CharacterMainControl GetOrFindPlayer()
        {
            GameObject playerObj = GetPlayerObject();
            if (playerObj != null)
            {
                CharacterMainControl player = playerObj.GetComponent<CharacterMainControl>();
                if (player != null)
                {
                    // 更新玩家队伍缓存
                    if (!hasCachedPlayerTeam)
                    {
                        CachePlayerTeam(player);
                    }
                    return player;
                }
            }

            return null;
        }
        
        /// <summary>
        /// 在玩家头顶显示气泡消息（公共方法，供DebugFeatures使用）
        /// </summary>
        public void ShowPlayerBubble(string message, float duration = 2f)
        {
            try
            {
                CharacterMainControl player = GetOrFindPlayer();
                if (player != null)
                {
                    DialogueBubblesManager.Show(message, player.transform, duration);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"显示气泡消息时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取友军列表（公共方法，供DebugFeatures使用）
        /// </summary>
        public List<CharacterMainControl> GetAllies()
        {
            return allies;
        }
        
        /// <summary>
        /// 统计玩家背包中的金币数量（公共方法，供DebugFeatures使用）
        /// </summary>
        public int CountPlayerCoins(CharacterMainControl player)
        {
            try
            {
                int totalCoins = 0;
                
                // 找到场景中所有的Item
                Item[] allItems = FindObjectsOfType<Item>();
                
                // 筛选出在玩家身上的金币
                foreach (Item item in allItems)
                {
                    if (item == null) continue;
                    
                    // 检查是否在玩家身上 且 是金币
                    if (item.IsInPlayerCharacter() && item.TypeID == ITEM_ID_COIN)
                    {
                        int itemAmount = GetItemAmount(item);
                        totalCoins += itemAmount;
                    }
                }
                
                return totalCoins;
            }
            catch (Exception ex)
            {
                Debug.LogError($"统计金币时出错: {ex.Message}\n{ex.StackTrace}");
                return 0;
            }
        }
        
        /// <summary>
        /// 设置物品数量（公共方法，供DebugFeatures使用）
        /// </summary>
        public void SetItemAmount(Item item, int amount)
        {
            try
            {
                Type itemType = item.GetType();
                
                // 直接使用StackCount属性（从F6调试中发现的）
                PropertyInfo stackCountProp = itemType.GetProperty("StackCount", BindingFlags.Public | BindingFlags.Instance);
                
                if (stackCountProp != null && stackCountProp.CanWrite)
                {
                    stackCountProp.SetValue(item, amount);
                    return;
                }
                
                Debug.LogWarning($"无法找到Item.StackCount属性或该属性不可写");
            }
            catch (Exception ex)
            {
                Debug.LogError($"设置物品数量时出错: {ex.Message}");
            }
        }
        
        // ============================================
        // 私有方法
        // ============================================
        
        /// <summary>
        /// 获取玩家前方指定距离和角度的位置（用于友军跟随）
        /// </summary>
        /// <param name="player">玩家对象</param>
        /// <param name="distance">距离（米）</param>
        /// <param name="angleOffset">角度偏移（度，0为正前方，顺时针为正）</param>
        /// <returns>目标位置</returns>
        private Vector3 GetPlayerForwardPosition(CharacterMainControl player, float distance = 5f, float angleOffset = 0f)
        {
            if (player == null)
            {
                return Vector3.zero;
            }
            
            // 获取玩家朝向（忽略Y轴旋转，只在水平面计算）
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            
            // 如果玩家没有朝向（比如刚生成），使用默认方向
            if (forward.magnitude < 0.1f)
            {
                forward = Vector3.forward;
            }
            
            // 如果有角度偏移，旋转方向向量
            if (Mathf.Abs(angleOffset) > 0.01f)
            {
                Quaternion rotation = Quaternion.Euler(0f, angleOffset, 0f);
                forward = rotation * forward;
            }
            
            // 计算目标位置
            Vector3 targetPos = player.transform.position + forward * distance;
            return targetPos;
        }
        
        /// <summary>
        /// 控制友军跟随玩家（简化版：基于距离和速度的跟随策略）
        /// </summary>
        private void UpdateAllyFollow(CharacterMainControl ally, CharacterMainControl player)
        {
            try
            {
                if (ally == null || ally.gameObject == null || player == null)
                {
                    return;
                }
                
                Vector3 playerPos = player.transform.position;
                Vector3 allyPos = ally.transform.position;
                float distanceToPlayer = Vector3.Distance(allyPos, playerPos);
                
                // 🔑 保底策略：超过40米强制传送到玩家位置
                if (distanceToPlayer > 40f)
                {
                    Vector3 teleportPos = playerPos + Vector3.up * 0.5f; // 稍微抬高避免卡地下
                    ally.transform.position = teleportPos;
                    Debug.Log($"[UpdateAllyFollow] ⚠️ 距离过远({distanceToPlayer:F2}米)，强制传送到玩家位置: {teleportPos}");
                    return;
                }
                
                // 获取或缓存AI控制器组件
                Component aiCharacterController = null;
                bool fromCache = aiControllerCache.TryGetValue(ally, out aiCharacterController);
                
                if (!fromCache)
                {
                    // 查找AI控制器子对象
                    Transform aiController = ally.transform.Find("AIControllerTemplate(Clone)");
                    if (aiController == null)
                    {
                        // 尝试查找包含"AI"的子对象
                        foreach (Transform child in ally.transform)
                        {
                            string childName = child.name.ToLower();
                            if (childName.Contains("ai") && childName.Contains("controller"))
                            {
                                aiController = child;
                                break;
                            }
                        }
                    }
                    
                    if (aiController == null)
                    {
                        return;  // 没有AI控制器，跳过
                    }
                    
                    // 查找AICharacterController组件
                    aiCharacterController = aiController.GetComponent("AICharacterController");
                    if (aiCharacterController == null)
                    {
                        return;  // 没有组件，跳过
                    }
                    
                    // 缓存AI控制器
                    aiControllerCache[ally] = aiCharacterController;
                }
                
                Type aiType = aiCharacterController.GetType();
                
                // 检查AI状态：战斗状态、巡逻状态等
                FieldInfo searchedEnemyField = aiType.GetField("searchedEnemy", BindingFlags.Public | BindingFlags.Instance);
                object currentEnemy = null;
                if (searchedEnemyField != null)
                {
                    currentEnemy = searchedEnemyField.GetValue(aiCharacterController);
                }
                
                FieldInfo noticedField = aiType.GetField("noticed", BindingFlags.Public | BindingFlags.Instance);
                bool isNoticed = false;
                if (noticedField != null && noticedField.FieldType == typeof(bool))
                {
                    isNoticed = (bool)noticedField.GetValue(aiCharacterController);
                }
                
                bool isInCombat = currentEnemy != null || isNoticed;
                
                // 🔑 简化策略：判断是否需要重置AI并移动
                // 条件：玩家速度 > 4米/秒 且 距离 > 8米 且 (战斗状态 或 巡逻状态)
                // 注意：如果不在战斗状态，那就是在巡逻状态，所以只要满足速度和距离条件就重置
                bool shouldResetAndMove = (playerMoveSpeed > 4f && distanceToPlayer > 8f);
                
                if (shouldResetAndMove)
                {
                    Debug.Log($"[UpdateAllyFollow] 🔄 重置AI并移动 - 玩家速度: {playerMoveSpeed:F2}米/秒, 距离: {distanceToPlayer:F2}米, 战斗状态: {isInCombat}");
                    
                    // 重置AI状态：清除敌人、警戒状态
                    if (searchedEnemyField != null && currentEnemy != null)
                    {
                        searchedEnemyField.SetValue(aiCharacterController, null);
                    }
                    
                    FieldInfo cachedSearchedEnemyField = aiType.GetField("cachedSearchedEnemy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (cachedSearchedEnemyField != null)
                    {
                        cachedSearchedEnemyField.SetValue(aiCharacterController, null);
                    }
                    
                    if (noticedField != null && isNoticed)
                    {
                        noticedField.SetValue(aiCharacterController, false);
                    }
                    
                    FieldInfo alertField = aiType.GetField("alert", BindingFlags.Public | BindingFlags.Instance);
                    if (alertField != null && alertField.FieldType == typeof(bool))
                    {
                        alertField.SetValue(aiCharacterController, false);
                    }
                    
                    // 让队友往玩家方向移动
                    MethodInfo moveToPosMethod = aiType.GetMethod("MoveToPos", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(Vector3) }, null);
                    if (moveToPosMethod != null)
                    {
                        try
                        {
                            // 计算朝向玩家的方向
                            Vector3 directionToPlayer = (playerPos - allyPos).normalized;
                            Vector3 targetPos = playerPos + directionToPlayer * 5f; // 玩家位置前方5米
                            moveToPosMethod.Invoke(aiCharacterController, new object[] { targetPos });
                            Debug.Log($"[UpdateAllyFollow] ✅ 已重置AI并移动到玩家方向: {targetPos}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[UpdateAllyFollow] ⚠️ MoveToPos调用失败: {ex.Message}");
                        }
                    }
                }
                
                // 🔑 设置玩家位置为巡逻中心点，巡逻范围改为5米
                FieldInfo patrolPosField = aiType.GetField("patrolPosition", BindingFlags.Public | BindingFlags.Instance);
                if (patrolPosField != null && patrolPosField.FieldType == typeof(Vector3))
                {
                    patrolPosField.SetValue(aiCharacterController, playerPos);
                }
                
                FieldInfo patrolRangeField = aiType.GetField("patrolRange", BindingFlags.Public | BindingFlags.Instance);
                if (patrolRangeField != null && patrolRangeField.FieldType == typeof(float))
                {
                    patrolRangeField.SetValue(aiCharacterController, 5f);
                }
                
                // 🔑 设置队友朝向：面向玩家方向（让走路时面向前方）
                Vector3 lookDirection = (playerPos - allyPos);
                lookDirection.y = 0f; // 只在水平面计算方向，忽略高度差
                if (lookDirection.magnitude > 0.1f) // 确保方向向量有效
                {
                    Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized);
                    // 平滑旋转，避免突然转向（旋转速度：每秒10倍）
                    ally.transform.rotation = Quaternion.Slerp(ally.transform.rotation, targetRotation, Time.deltaTime * 10f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UpdateAllyFollow] ❌ 异常分支：控制友军跟随时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }


        /// <summary>
        /// 在指定角色头顶显示气泡消息
        /// </summary>
        private void ShowCharacterBubble(CharacterMainControl character, string message, float duration = 2f)
        {
            try
            {
                if (character != null)
                {
                    DialogueBubblesManager.Show(message, character.transform, duration);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"显示角色气泡时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试贿赂敌人（俯视图游戏，使用距离检测）
        /// </summary>
        private void TryBribeEnemy()
        {
            try
            {
                // 首先检查友军数量是否达到上限
                var invalidAllies = allies.Where(ally => ally == null || ally.gameObject == null).ToList();
                foreach (var invalidAlly in invalidAllies)
                {
                    lastResetTime.Remove(invalidAlly); // 清理重置时间记录
                }
                allies.RemoveAll(ally => ally == null || ally.gameObject == null);
                if (allies.Count >= maxAllyCount)
                {
                    ShowPlayerBubble("队伍已满，保持阵型就好！", 2.5f);
                    foreach (var ally in allies)
                    {
                        if (ally != null)
                        {
                            string complain = MaxPartyAllyMessages[UnityEngine.Random.Range(0, MaxPartyAllyMessages.Length)];
                            ShowCharacterBubble(ally, complain, 2.5f);
                        }
                    }
                    return;
                }

                // 1. 找到玩家位置
                GameObject playerObj = GetPlayerObject();
                if (playerObj == null)
                {
                    Debug.LogWarning("❌ 未找到玩家对象");
                    return;
                }

                Vector3 playerPos = playerObj.transform.position;
                Debug.Log($"📍 [TryBribeEnemy] 玩家位置: {playerPos}");

                // 2. 直接查找所有角色，然后按距离筛选（更可靠，不依赖碰撞体）
                CharacterMainControl[] allCharacters = FindObjectsOfType<CharacterMainControl>();
                List<CharacterMainControl> nearbyEnemies = new List<CharacterMainControl>();
                
                foreach (CharacterMainControl character in allCharacters)
                {
                    if (character == null || character.gameObject == null) continue;
                    if (character.gameObject == playerObj) continue;
                    if (IsAlly(character)) continue;
                    
                    float distance = Vector3.Distance(playerPos, character.transform.position);
                    if (distance <= bribeRange)
                    {
                        nearbyEnemies.Add(character);
                        Debug.Log($"🎯 发现敌人: {character.gameObject.name} (距离: {distance:F2}米)");
                    }
                }

                // 4. 如果没有敌人
                if (nearbyEnemies.Count == 0)
                {
                    Debug.Log($"❌ 附近{bribeRange}米内没有敌人");
                    ShowPlayerBubble("附近没敌人...", 2f);
                    return;
                }

                // 5. 找到最近的敌人
                CharacterMainControl targetCharacter = null;
                float minDistance = float.MaxValue;
                
                foreach (var enemy in nearbyEnemies)
                {
                    float distance = Vector3.Distance(playerPos, enemy.transform.position);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        targetCharacter = enemy;
                    }
                }

                if (targetCharacter == null)
                {
                    Debug.Log("❌ 未找到目标敌人");
                    return;
                }

                Debug.Log($"🎯 贿赂目标: {targetCharacter.gameObject.name} (距离: {minDistance:F2}米)");
                Debug.Log($"📍 [TryBribeEnemy] 目标角色位置: {targetCharacter.transform.position}");

                // 7. 检查玩家金钱
                if (!HasEnoughMoney(perBribeAmount))
                {
                    Debug.LogWarning($"❌ 金钱不足！需要 {perBribeAmount} 金币");
                    ShowPlayerBubble($"现金不足！", 2f);
                    return;
                }

                // 8. 扣除金钱并转移给敌人
                DeductMoney(perBribeAmount, targetCharacter);

                // 9. 更新贿赂记录
                if (!bribeRecords.ContainsKey(targetCharacter))
                {
                    var newRecord = new BribeRecord
                    {
                        RequiredAmount = UnityEngine.Random.Range(minRequiredAmount, maxRequiredAmount + 1)
                    };
                    bribeRecords[targetCharacter] = newRecord;

                    Debug.Log($"💬 {targetCharacter.gameObject.name} 的要价: {newRecord.RequiredAmount} 金币");
                    ShowCharacterBubble(targetCharacter, $"我要价 {newRecord.RequiredAmount} 元/次。", 3f);
                }
                
                BribeRecord record = bribeRecords[targetCharacter];
                record.Times++;
                record.TotalAmount += perBribeAmount;

                Debug.Log($"💰 贿赂成功！");
                Debug.Log($"   贿赂次数: {record.Times}");
                Debug.Log($"   累计金额: {record.TotalAmount}");
                Debug.Log($"   目标要价: {record.RequiredAmount}");

                if (record.TotalAmount >= record.RequiredAmount)
                {
                    float successChance = Mathf.Max(0.05f, 0.5f - 0.05f * record.FailedAttempts); // 失败越多越难
                    Debug.Log($"🎲 当前成功概率: {successChance * 100f:F1}% (贿赂次数: {record.Times})");

                    bool convert = UnityEngine.Random.value < successChance;
                    if (convert)
                    {
                        Debug.Log($"✅ 贿赂成功！敌人愿意加入你");
                        string successMessage = record.FailedAttempts switch
                        {
                            0 => "好吧，被你收买了。",
                            1 => "哎，好像也不错。",
                            2 => "真香…我承认。",
                            3 => "行，保密啊。",
                            4 => "别塞了，我跟你走。",
                            5 => "好吧，我投降。",
                            6 => "行行行，真香了。",
                            _ => "钱包赢了，我输了。"
                        };
                        ShowCharacterBubble(targetCharacter, successMessage, 3f);
                        bribeRecords.Remove(targetCharacter);
                        ConvertToAlly(targetCharacter);
                    }
                    else
                    {
                        Debug.Log($"⚠️ 敌人仍然犹豫，未加入");
                        record.FailedAttempts++;
                        string failMessage = record.FailedAttempts switch
                        {
                            1 => "滚开，没兴趣。",
                            2 => "铜臭味太重。",
                            3 => "这点钱？走开。",
                            4 => "我宁死不屈。",
                            5 => "再塞也没用。",
                            6 => "别妄想真香。",
                            7 => "梦里才会答应。",
                            8 => "钱包赢不了我。",
                            _ => "我不会向钱低头！"
                        };
                        ShowCharacterBubble(targetCharacter, failMessage, 2.5f);
                    }
                }
                else
                {
                    int needMoney = Mathf.Max(0, record.RequiredAmount - record.TotalAmount);
                    string message = $"还差 {needMoney}/{record.RequiredAmount} 金币";
                    Debug.Log($"   还需累计 {needMoney} 现金 / 要价 {record.RequiredAmount}");
                    ShowPlayerBubble(message, 2.5f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"贿赂时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }


        private void CachePlayerTeam(CharacterMainControl player)
        {
            if (player == null) return;
            cachedPlayerTeam = player.Team;
            hasCachedPlayerTeam = true;
        }

        private bool TryGetPlayerTeam(out Teams team)
        {
            team = default;

            CharacterMainControl player = GetOrFindPlayer();
            if (player == null)
            {
                hasCachedPlayerTeam = false;
                return false;
            }

            if (!hasCachedPlayerTeam)
            {
                CachePlayerTeam(player);
            }

            if (!hasCachedPlayerTeam)
            {
                return false;
            }

            team = cachedPlayerTeam;
            return true;
        }

        /// <summary>
        /// 获取玩家对象（多种方法尝试）
        /// </summary>
        private GameObject GetPlayerObject()
        {
            // 方法1：尝试通过Tag查找
            GameObject playerByTag = GameObject.FindGameObjectWithTag("Player");
            if (playerByTag != null)
            {
                CharacterMainControl charControl = playerByTag.GetComponent<CharacterMainControl>();
                if (charControl != null)
                {
                    // 检查IsMainCharacter属性
                    Type charType = charControl.GetType();
                    PropertyInfo isMainCharProp = charType.GetProperty("IsMainCharacter", BindingFlags.Public | BindingFlags.Instance);
                    if (isMainCharProp != null)
                    {
                        object isMainValue = isMainCharProp.GetValue(charControl);
                        if (isMainValue != null && Convert.ToBoolean(isMainValue))
                        {
                            Debug.Log($"[GetPlayerObject] ✅ 通过Tag和IsMainCharacter找到玩家: {playerByTag.name} (ID: {charControl.GetInstanceID()})");
                            return playerByTag;
                        }
                    }
                }
            }

            // 方法2：遍历所有CharacterMainControl，优先找IsMainCharacter为True的
            CharacterMainControl[] allCharacters = FindObjectsOfType<CharacterMainControl>();
            CharacterMainControl mainCharacter = null;
            CharacterMainControl playerTeamCharacter = null;
            
            foreach (var character in allCharacters)
            {
                if (character == null) continue;
                
                Type charType = character.GetType();
                PropertyInfo isMainCharProp = charType.GetProperty("IsMainCharacter", BindingFlags.Public | BindingFlags.Instance);
                
                // 优先查找IsMainCharacter为True的
                if (isMainCharProp != null)
                {
                    object isMainValue = isMainCharProp.GetValue(character);
                    if (isMainValue != null && Convert.ToBoolean(isMainValue))
                    {
                        mainCharacter = character;
                        Debug.Log($"[GetPlayerObject] ✅ 通过IsMainCharacter找到主玩家: {character.gameObject.name} (ID: {character.GetInstanceID()}, Team: {character.Team})");
                        return character.gameObject;
                    }
                }
                
                // 备选：查找team为player的
                string teamName = character.Team.ToString().ToLower();
                if ((teamName == "player" || teamName.Contains("player")) && playerTeamCharacter == null)
                {
                    playerTeamCharacter = character;
                }
            }
            
            // 如果找到team为player的，返回它
            if (playerTeamCharacter != null)
            {
                Debug.Log($"[GetPlayerObject] ✅ 通过Team找到玩家: {playerTeamCharacter.gameObject.name} (ID: {playerTeamCharacter.GetInstanceID()}, Team: {playerTeamCharacter.Team})");
                return playerTeamCharacter.gameObject;
            }
            
            // 方法3：尝试通过名称查找（最后备选）
            GameObject playerByName = GameObject.Find("Character(Clone)");
            if (playerByName != null)
            {
                CharacterMainControl charControl = playerByName.GetComponent<CharacterMainControl>();
                if (charControl != null && charControl.Team.ToString().ToLower() == "player")
                {
                    Debug.Log($"[GetPlayerObject] ⚠️ 通过名称找到玩家（可能不准确）: {playerByName.name} (ID: {charControl.GetInstanceID()})");
                    return playerByName;
                }
            }

            Debug.LogWarning($"[GetPlayerObject] ⚠️ 未找到玩家对象 (共检查了 {allCharacters.Length} 个角色)");
            return null;
        }

        /// <summary>
        /// 判断角色是否已经是友军
        /// </summary>
        private bool IsAlly(CharacterMainControl character)
        {
            CharacterMainControl player = GetOrFindPlayer();
            if (player == null) return false;

            // 检查是否和玩家同队
            if (!TryGetPlayerTeam(out Teams playerTeam)) return false;
            return character.Team == playerTeam;
        }

        /// <summary>
        /// 转换敌人为友军
        /// </summary>
        private void ConvertToAlly(CharacterMainControl enemy)
        {
            try
            {
                // 获取玩家队伍
                CharacterMainControl player = GetOrFindPlayer();
                if (player == null)
                {
                    Debug.LogError("❌ 无法转换阵营：找不到玩家队伍");
                    return;
                }

                if (!TryGetPlayerTeam(out Teams playerTeam))
                {
                    Debug.LogError("❌ 无法转换阵营：未能缓存玩家队伍");
                    return;
                }

                Debug.Log($"🎉 贿赂成功！{enemy.gameObject.name} 现在为你效力！");
                Debug.Log($"   转换阵营: {enemy.Team} → {playerTeam}");

                // 🔑 关键检查：确保不会把玩家自己添加到友军列表
                if (enemy == player || enemy.GetInstanceID() == player.GetInstanceID())
                {
                    Debug.LogError($"❌ 错误：尝试将玩家自己添加到友军列表！玩家ID: {player.GetInstanceID()}, 敌人ID: {enemy.GetInstanceID()}, 玩家名称: {player.gameObject.name}, 敌人名称: {enemy.gameObject.name}");
                    return;
                }

                // 转换阵营
                enemy.SetTeam(playerTeam);

                // 添加到友军列表
                if (!allies.Contains(enemy))
                {
                    allies.Add(enemy);
                    Debug.Log($"   ✅ 已添加到友军列表 (当前友军数: {allies.Count}, 敌人ID: {enemy.GetInstanceID()}, 玩家ID: {player.GetInstanceID()})");
                }
                else
                {
                    Debug.LogWarning($"   ⚠️ 友军已在列表中，跳过添加 (敌人ID: {enemy.GetInstanceID()})");
                }

                // 设置友军AI
                SetupAllyAI(enemy, player);
            }
            catch (Exception ex)
            {
                Debug.LogError($"转换阵营时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 设置友军的AI行为（修改巡逻中心点）
        /// </summary>
        private void SetupAllyAI(CharacterMainControl ally, CharacterMainControl player)
        {
            try
            {
                Debug.Log($"   💡 策略：保留AI智能，修改巡逻中心点为玩家正前方5米位置");
                
                // 查找AI控制器子对象
                Transform aiController = ally.transform.Find("AIControllerTemplate(Clone)");
                if (aiController == null)
                {
                    // 尝试查找包含"AI"的子对象
                    foreach (Transform child in ally.transform)
                    {
                        if (child.name.ToLower().Contains("ai") && child.name.ToLower().Contains("controller"))
                        {
                            aiController = child;
                            break;
                        }
                    }
                }
                
                if (aiController == null)
                {
                    Debug.Log($"   ⚠️ 未找到AI控制器子对象");
                    return;
                }
                
                Debug.Log($"   🎯 找到AI控制器: {aiController.name}");
                
                // 查找AICharacterController组件
                Component aiCharacterController = aiController.GetComponent("AICharacterController");
                if (aiCharacterController == null)
                {
                    Debug.Log($"   ⚠️ 未找到AICharacterController组件");
                    return;
                }
                
                Debug.Log($"   📦 找到AICharacterController组件");
                
                // 计算玩家正前方5米的位置（友军跟随目标位置）
                Vector3 targetPos = GetPlayerForwardPosition(player, 5f);
                
                // 修改巡逻位置为玩家正前方5米位置（显示日志）
                UpdateAIPatrolPosition(aiCharacterController, targetPos, silent: false);
                
                Debug.Log($"   ✅ AI保留完整智能（会攻击、会躲避），巡逻中心已设为玩家正前方5米位置");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"设置友军AI时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新AI的巡逻位置（优化版：降低阈值，动态调整范围）
        /// </summary>
        private void UpdateAIPatrolPosition(Component aiController, Vector3 newPosition, bool silent = true)
        {
            try
            {
                Type aiType = aiController.GetType();
                
                // 查找patrolPosition字段
                FieldInfo patrolPosField = aiType.GetField("patrolPosition", BindingFlags.Public | BindingFlags.Instance);
                if (patrolPosField != null && patrolPosField.FieldType == typeof(Vector3))
                {
                    Vector3 oldPos = (Vector3)patrolPosField.GetValue(aiController);
                    float distance = Vector3.Distance(oldPos, newPosition);
                    
                    // 🔑 关键修复：强制更新巡逻位置，无论距离如何
                    // 问题：如果阈值太高，AI可能不会响应位置变化
                    // 解决方案：每次调用都强制更新，确保AI始终跟随玩家
                    bool shouldUpdate = true; // 强制更新，不再使用阈值判断
                    
                    if (!silent || distance > 0.1f) // 降低日志频率，但距离变化时输出
                    {
                        Debug.Log($"[UpdateAIPatrolPosition] 巡逻位置检查 - 旧位置: {oldPos}, 新位置: {newPosition}, 距离: {distance:F2}米, shouldUpdate: {shouldUpdate}");
                    }
                    
                    if (shouldUpdate)
                    {
                        patrolPosField.SetValue(aiController, newPosition);
                        
                        // 验证设置是否成功
                        Vector3 verifyPos = (Vector3)patrolPosField.GetValue(aiController);
                        float verifyDistance = Vector3.Distance(verifyPos, newPosition);
                        
                        if (!silent || distance > 0.1f)
                        {
                            Debug.Log($"[UpdateAIPatrolPosition] ✅ 已更新巡逻位置: {oldPos} → {newPosition} (距离: {distance:F2}米, 验证距离: {verifyDistance:F2}米)");
                        }
                        
                        // 如果验证失败，输出警告
                        if (verifyDistance > 0.1f)
                        {
                            Debug.LogWarning($"[UpdateAIPatrolPosition] ⚠️ 巡逻位置设置后验证失败: 期望 {newPosition}, 实际 {verifyPos}, 距离: {verifyDistance:F2}米");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[UpdateAIPatrolPosition] ⚠️ 未找到patrolPosition字段 (字段存在: {patrolPosField != null}, 类型: {(patrolPosField != null ? patrolPosField.FieldType.Name : "null")})");
                }
                
                // 优化3：根据玩家移动速度动态调整巡逻范围
                FieldInfo patrolRangeField = aiType.GetField("patrolRange", BindingFlags.Public | BindingFlags.Instance);
                if (patrolRangeField != null && patrolRangeField.FieldType == typeof(float))
                {
                    float currentRange = (float)patrolRangeField.GetValue(aiController);
                    
                    // 根据玩家移动速度动态调整巡逻范围
                    // 移动速度慢（<2米/秒）：3米范围
                    // 移动速度中等（2-5米/秒）：4米范围
                    // 移动速度快（>5米/秒）：5米范围
                    float targetRange = 3f;
                    if (playerMoveSpeed > 5f)
                    {
                        targetRange = 5f; // 快速移动时增大范围
                    }
                    else if (playerMoveSpeed > 2f)
                    {
                        targetRange = 4f; // 中等速度
                    }
                    
                    // 如果当前范围与目标范围差距较大，更新它
                    if (Mathf.Abs(currentRange - targetRange) > 0.5f)
                    {
                        patrolRangeField.SetValue(aiController, targetRange);
                        Debug.Log($"[UpdateAIPatrolPosition] ✅ 更新巡逻范围: {currentRange} → {targetRange}米 (玩家速度: {playerMoveSpeed:F2}米/秒)");
                    }
                    
                    // 每帧都输出当前范围（用于诊断）
                    if (!silent || Time.frameCount % 20 == 0)
                    {
                        Debug.Log($"[UpdateAIPatrolPosition] 📊 巡逻范围信息 - 当前范围: {currentRange}米, 目标范围: {targetRange}米, 玩家速度: {playerMoveSpeed:F2}米/秒");
                    }
                }
                else
                {
                    Debug.LogWarning($"[UpdateAIPatrolPosition] ⚠️ 未找到patrolRange字段 (字段存在: {patrolRangeField != null}, 类型: {(patrolRangeField != null ? patrolRangeField.FieldType.Name : "null")})");
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    Debug.LogWarning($"更新AI巡逻位置时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 检查玩家金钱是否足够
        /// </summary>
        private bool HasEnoughMoney(int amount)
        {
            try
            {
                CharacterMainControl player = GetOrFindPlayer();
                if (player == null)
                {
                    Debug.LogWarning("❌ 未找到玩家，无法检查金钱");
                    return false;
                }

                // 获取玩家身上所有的金币数量
                int totalCoins = CountPlayerCoins(player);
                
                Debug.Log($"💰 玩家拥有金币: {totalCoins}，需要: {amount}");
                
                return totalCoins >= amount;
            }
            catch (Exception ex)
            {
                Debug.LogError($"检查金钱时出错: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// 扣除玩家金钱并转移给目标角色
        /// </summary>
        private void DeductMoney(int amount, CharacterMainControl targetEnemy)
        {
            try
            {
                CharacterMainControl player = GetOrFindPlayer();
                if (player == null)
                {
                    Debug.LogWarning("❌ 未找到玩家，无法扣除金钱");
                    return;
                }

                // 从玩家身上移除金币
                bool success = RemovePlayerCoins(player, amount);
                
                if (success)
                {
                    Debug.Log($"✅ 已从玩家扣除 {amount} 金币");
                    
                    // 将金币转移给被贿赂的敌人
                    GiveCoinsToCharacter(targetEnemy, amount);
                }
                else
                {
                    Debug.LogWarning($"❌ 扣除金币失败");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"扣除金钱时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 从玩家背包移除指定数量的金币（使用ItemUtilities API）
        /// </summary>
        private bool RemovePlayerCoins(CharacterMainControl player, int amount)
        {
            try
            {
                int remaining = amount;
                
                // 找到场景中所有的Item
                Item[] allItems = FindObjectsOfType<Item>();
                
                // 收集玩家身上的所有金币
                List<Item> coinItems = new List<Item>();
                foreach (Item item in allItems)
                {
                    if (item != null && item.IsInPlayerCharacter() && item.TypeID == ITEM_ID_COIN)
                    {
                        coinItems.Add(item);
                    }
                }
                
                // 从金币物品中扣除
                foreach (Item coinItem in coinItems)
                {
                    if (remaining <= 0) break;
                    
                    int itemAmount = GetItemAmount(coinItem);
                    
                    if (itemAmount >= remaining)
                    {
                        // 这个物品够扣除剩余数量
                        SetItemAmount(coinItem, itemAmount - remaining);
                        Debug.Log($"   从金币堆扣除 {remaining}，剩余 {GetItemAmount(coinItem)}");
                        
                        // 如果数量为0，移除这个物品
                        if (GetItemAmount(coinItem) <= 0)
                        {
                            coinItem.Detach();
                            Debug.Log($"   金币已用完，移除物品");
                        }
                        
                        remaining = 0;
                    }
                    else
                    {
                        // 这个物品不够，全部扣除
                        remaining -= itemAmount;
                        Debug.Log($"   扣除整堆金币 {itemAmount}，还需 {remaining}");
                        coinItem.Detach();
                    }
                }
                
                return remaining == 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"移除金币时出错: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 给角色添加金币（使用CharacterItemControl.PickupItem方法）
        /// </summary>
        private void GiveCoinsToCharacter(CharacterMainControl character, int amount)
        {
            try
            {
                if (character == null || character.gameObject == null)
                {
                    Debug.LogError("❌ [GiveCoinsToCharacter] 角色对象无效");
                    return;
                }
                
                // 创建金币物品
                Item coinItem = ItemAssetsCollection.InstantiateSync(ITEM_ID_COIN);
                if (coinItem == null)
                {
                    Debug.LogError($"❌ [GiveCoinsToCharacter] 金币物品创建失败！物品ID: {ITEM_ID_COIN}");
                    return;
                }
                
                // 设置物品数量
                SetItemAmount(coinItem, amount);
                
                // 通过CharacterItemControl.PickupItem添加物品
                Component itemControl = character.GetComponent("CharacterItemControl");
                if (itemControl == null)
                {
                    Debug.LogError($"❌ [GiveCoinsToCharacter] 未找到CharacterItemControl组件");
                    coinItem.Detach(); // 清理物品
                    return;
                }
                
                Type itemControlType = itemControl.GetType();
                MethodInfo pickupMethod = itemControlType.GetMethod("PickupItem", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(Item) }, null);
                
                if (pickupMethod == null)
                {
                    Debug.LogError($"❌ [GiveCoinsToCharacter] 未找到PickupItem方法");
                    coinItem.Detach(); // 清理物品
                    return;
                }
                
                // 调用PickupItem方法
                object result = pickupMethod.Invoke(itemControl, new object[] { coinItem });
                bool success = result != null && (bool)result;
                
                if (success)
                {
                    Debug.Log($"✅ [GiveCoinsToCharacter] 成功给 {character.gameObject.name} 添加 {amount} 金币");
                    CheckCharacterCoinsAfterDelay(character, amount, 1f).Forget();
                }
                else
                {
                    Debug.LogWarning($"⚠️ [GiveCoinsToCharacter] PickupItem返回false，可能背包已满或其他原因");
                    coinItem.Detach(); // 清理物品
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ [GiveCoinsToCharacter] 给角色添加金币时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 延迟检查角色是否捡到金币
        /// </summary>
        private async UniTaskVoid CheckCharacterCoinsAfterDelay(CharacterMainControl character, int expectedAmount, float delaySeconds)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(delaySeconds));
                
                if (character == null || character.gameObject == null)
                {
                    Debug.LogWarning($"⚠️ [CheckCharacterCoins] 角色已无效，无法检查金币");
                    return;
                }
                
                // 统计角色身上的金币
                int totalCoins = CountCharacterCoins(character);
                Debug.Log($"💰 [CheckCharacterCoins] {character.gameObject.name} 当前金币总数: {totalCoins} (期望增加: {expectedAmount})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"⚠️ [CheckCharacterCoins] 检查金币时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 统计角色身上的金币数量
        /// </summary>
        private int CountCharacterCoins(CharacterMainControl character)
        {
            try
            {
                int totalCoins = 0;
                
                // 找到场景中所有的Item
                Item[] allItems = FindObjectsOfType<Item>();
                
                // 筛选出在角色身上的金币
                foreach (Item item in allItems)
                {
                    if (item == null) continue;
                    
                    // 检查是否在角色身上 且 是金币
                    // 注意：这里需要根据实际API判断物品是否在角色身上
                    // 可能需要使用不同的方法，比如检查item的持有者
                    if (item.TypeID == ITEM_ID_COIN)
                    {
                        // 尝试通过距离判断（临时方案）
                        float distance = Vector3.Distance(item.transform.position, character.transform.position);
                        if (distance < 2f) // 如果物品在角色附近2米内，认为可能在角色身上
                        {
                            int itemAmount = GetItemAmount(item);
                            totalCoins += itemAmount;
                            Debug.Log($"   📦 发现金币物品: {item.gameObject.name}, 数量: {itemAmount}, 距离: {distance:F2}米");
                        }
                    }
                }
                
                return totalCoins;
            }
            catch (Exception ex)
            {
                Debug.LogError($"统计角色金币时出错: {ex.Message}");
                return 0;
            }
        }


        /// <summary>
        /// 获取物品数量（使用StackCount属性）
        /// </summary>
        private int GetItemAmount(Item item)
        {
            try
            {
                Type itemType = item.GetType();
                
                // 直接使用StackCount属性（从F6调试中发现的）
                PropertyInfo stackCountProp = itemType.GetProperty("StackCount", BindingFlags.Public | BindingFlags.Instance);
                
                if (stackCountProp != null && stackCountProp.CanRead)
                {
                    object value = stackCountProp.GetValue(item);
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }
                
                Debug.LogWarning($"无法找到Item.StackCount属性");
                return 1; // 默认返回1
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取物品数量时出错: {ex.Message}");
                return 1;
            }
        }
        

        void OnDestroy()
        {
            Debug.Log("=== 雇佣兵系统Mod 已卸载 ===");
        }
    }
}
