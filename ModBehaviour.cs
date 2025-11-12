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
        
        // 缓存的玩家对象
        private CharacterMainControl cachedPlayer = null;
        private Teams cachedPlayerTeam;
        private bool hasCachedPlayerTeam = false;
        
        // 存储每个敌人的贿赂记录
        private Dictionary<CharacterMainControl, BribeRecord> bribeRecords = new Dictionary<CharacterMainControl, BribeRecord>();
        
        // 存储被贿赂的友军（跟随玩家移动）
        private List<CharacterMainControl> allies = new List<CharacterMainControl>();
        private int maxAllyCount = 2;               // 友军上限
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
        
        // 锁血功能
        private bool isHealthLocked = false;        // 是否锁血
        private float lockedHealth = 100f;          // 锁定的生命值
        
        // F6调试打印功能（独立子类）
        private GameObjectInspector inspector = new GameObjectInspector();

        void Awake()
        {
            Debug.Log("=== 雇佣兵系统Mod v1.8 已加载 ===");
            Debug.Log("功能说明：");
            Debug.Log($"  E键 - 靠近敌人后按E贿赂（每次 {perBribeAmount} 金币，范围{bribeRange}米）");
            Debug.Log($"  转换条件：敌人随机要价 {minRequiredAmount}-{maxRequiredAmount} 金币，凑够后有概率招募（失败越多越倔强）");
            Debug.Log($"  ✅ 友军保留完整AI智能（会攻击、会躲避、自然移动）");
            Debug.Log("调试功能：");
            Debug.Log($"  Q键 - 解散所有友军");
            Debug.Log($"  F9键 - 给自己添加测试金币");
            Debug.Log($"  F7键 - 切换玩家锁血（防止生命值减少）");
            Debug.Log($"  F6键 - 递归打印玩家和所有队友的属性");
            Debug.Log("========================");

            // 启动时预缓存玩家与队伍信息
            GetOrFindPlayer();
        }

        void Update()
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
                inspector.PrintPlayerAndAlliesProperties(GetOrFindPlayer(), allies);
            }
            
            // 锁血检查（如果开启锁血，持续恢复生命值）
            if (isHealthLocked)
            {
                MaintainPlayerHealth();
            }
            
            // 更新友军跟随
            UpdateAlliesFollow();
        }
        
        /// <summary>
        /// 更新所有友军的跟随行为
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
            
            // 清理已死亡或无效的友军
            allies.RemoveAll(ally => ally == null || ally.gameObject == null);
            
            // 更新每个友军的移动
            foreach (var ally in allies)
            {
                try
                {
                    UpdateAllyFollow(ally, playerPos);
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
                            Debug.Log($"✅ 已解散友军: {ally.gameObject.name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"解散友军时出错: {ex.Message}");
                    }
                }
                
                allies.Clear();
                bribeRecords.Clear(); // 清空贿赂记录
                
                Debug.Log($"✅ 已解散所有友军 (共 {count} 名)");
                ShowPlayerBubble($"已解散所有友军 ({count}名)", 3f);
            }
            catch (Exception ex)
            {
                Debug.LogError($"解散友军时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// F7键 - 切换玩家锁血状态
        /// </summary>
        private void ToggleHealthLock()
        {
            Debug.Log("🔍 [ToggleHealthLock] 函数开始执行");
            
            try
            {
                CharacterMainControl player = GetOrFindPlayer();
                if (player == null)
                {
                    Debug.LogWarning("❌ [ToggleHealthLock] 未找到玩家，无法切换锁血");
                    return;
                }
                
                isHealthLocked = !isHealthLocked;
                
                if (isHealthLocked)
                {
                    lockedHealth = GetPlayerHealth(player);
                    Debug.Log($"🔒 锁血已开启，锁定生命值: {lockedHealth}");
                    ShowPlayerBubble($"🔒 锁血已开启 ({lockedHealth:F0} HP)", 2.5f);
                }
                else
                {
                    Debug.Log("🔓 锁血已关闭");
                    ShowPlayerBubble("🔓 锁血已关闭", 2.5f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"切换锁血状态时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 维持玩家生命值（锁血功能）
        /// </summary>
        private void MaintainPlayerHealth()
        {
            try
            {
                CharacterMainControl player = GetOrFindPlayer();
                if (player == null) return;
                
                float currentHealth = GetPlayerHealth(player);
                if (currentHealth < lockedHealth)
                {
                    SetPlayerHealth(player, lockedHealth);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"维持玩家生命值时出错: {ex.Message}");
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
                Debug.Log($"✅ [GetPlayerHealth] 成功获取生命值: {health}");
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
                    return;
                }
                
                // 如果SetHealth方法不存在，尝试直接设置CurrentHealth属性
                PropertyInfo currentHealthProp = healthType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
                if (currentHealthProp != null && currentHealthProp.CanWrite)
                {
                    currentHealthProp.SetValue(healthComponent, health);
                    Debug.Log($"✅ [SetPlayerHealth] 使用CurrentHealth属性设置生命值: {health}");
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
        /// 控制友军跟随玩家（更新AI巡逻中心）
        /// </summary>
        private void UpdateAllyFollow(CharacterMainControl ally, Vector3 playerPos)
        {
            try
            {
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
                    return;  // 没有AI控制器，跳过
                }
                
                // 查找AICharacterController组件
                Component aiCharacterController = aiController.GetComponent("AICharacterController");
                if (aiCharacterController == null)
                {
                    return;  // 没有组件，跳过
                }
                
                // 更新巡逻位置为玩家当前位置
                UpdateAIPatrolPosition(aiCharacterController, playerPos);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"控制友军跟随时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 在玩家头顶显示气泡消息
        /// </summary>
        private void ShowPlayerBubble(string message, float duration = 2f)
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

        /// <summary>
        /// 获取或查找玩家角色（带缓存）
        /// </summary>
        private CharacterMainControl GetOrFindPlayer()
        {
            if (cachedPlayer == null)
            {
                hasCachedPlayerTeam = false;
            }

            // 如果缓存存在且有效，直接返回
            if (cachedPlayer != null)
            {
                if (!hasCachedPlayerTeam)
                {
                    CachePlayerTeam(cachedPlayer);
                }
                return cachedPlayer;
            }

            GameObject playerObj = GetPlayerObject();
            if (playerObj != null)
            {
                cachedPlayer = playerObj.GetComponent<CharacterMainControl>();
                if (cachedPlayer != null)
                {
                    CachePlayerTeam(cachedPlayer);
                }
            }

            return cachedPlayer;
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
                return playerByTag;
            }

            // 方法2：尝试通过名称查找
            GameObject playerByName = GameObject.Find("Character(Clone)");
            if (playerByName != null)
            {
                CharacterMainControl charControl = playerByName.GetComponent<CharacterMainControl>();
                if (charControl != null && charControl.Team.ToString().ToLower() == "player")
                {
                    return playerByName;
                }
            }

            // 方法3：遍历所有CharacterMainControl，找team为player的
            CharacterMainControl[] allCharacters = FindObjectsOfType<CharacterMainControl>();
            foreach (var character in allCharacters)
            {
                string teamName = character.Team.ToString().ToLower();
                if (teamName == "player" || teamName.Contains("player"))
                {
                    return character.gameObject;
                }
            }

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

                // 转换阵营
                enemy.SetTeam(playerTeam);

                // 添加到友军列表
                if (!allies.Contains(enemy))
                {
                    allies.Add(enemy);
                    Debug.Log($"   ✅ 已添加到友军列表 (当前友军数: {allies.Count})");
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
                Debug.Log($"   💡 策略：保留AI智能，修改巡逻中心点为玩家位置");
                
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
                
                // 修改巡逻位置为玩家位置（显示日志）
                UpdateAIPatrolPosition(aiCharacterController, player.transform.position, silent: false);
                
                Debug.Log($"   ✅ AI保留完整智能（会攻击、会躲避），巡逻中心已设为玩家位置");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"设置友军AI时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新AI的巡逻位置（静默模式，用于Update循环）
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
                    
                    // 只有位置变化超过1米才更新（避免频繁修改）
                    if (Vector3.Distance(oldPos, newPosition) > 1f)
                    {
                        patrolPosField.SetValue(aiController, newPosition);
                        
                        if (!silent)
                        {
                            Debug.Log($"      ✅ patrolPosition: {oldPos} → {newPosition}");
                        }
                    }
                }
                else if (!silent)
                {
                    Debug.Log($"      ⚠️ 未找到patrolPosition字段");
                }
                
                // 可选：增加巡逻范围，让友军不要离太远（只设置一次）
                FieldInfo patrolRangeField = aiType.GetField("patrolRange", BindingFlags.Public | BindingFlags.Instance);
                if (patrolRangeField != null && patrolRangeField.FieldType == typeof(float))
                {
                    float currentRange = (float)patrolRangeField.GetValue(aiController);
                    if (currentRange < 2f || currentRange > 4f)
                    {
                        patrolRangeField.SetValue(aiController, 2f);
                        if (!silent)
                        {
                            Debug.Log($"      ✅ patrolRange: {currentRange} → 2米");
                        }
                    }
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
        /// 统计玩家背包中的金币数量（使用ItemUtilities API）
        /// </summary>
        private int CountPlayerCoins(CharacterMainControl player)
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
        /// 给角色添加金币（尝试直接添加到背包）
        /// </summary>
        private void GiveCoinsToCharacter(CharacterMainControl character, int amount)
        {
            try
            {
                Debug.Log($"🔍 [GiveCoinsToCharacter] 开始执行 - 角色: {character?.gameObject?.name ?? "null"}, 金额: {amount}");
                
                if (character == null)
                {
                    Debug.LogError("❌ [GiveCoinsToCharacter] 角色对象为null");
                    return;
                }
                
                if (character.gameObject == null)
                {
                    Debug.LogError("❌ [GiveCoinsToCharacter] 角色的gameObject为null");
                    return;
                }
                
                Debug.Log($"📍 [GiveCoinsToCharacter] 角色位置: {character.transform.position}");
                
                // 创建金币物品
                Debug.Log($"🔄 [GiveCoinsToCharacter] 正在创建金币物品 (ID: {ITEM_ID_COIN})...");
                Item coinItem = ItemAssetsCollection.InstantiateSync(ITEM_ID_COIN);
                
                if (coinItem == null)
                {
                    Debug.LogError($"❌ [GiveCoinsToCharacter] 金币物品创建失败！物品ID: {ITEM_ID_COIN}");
                    return;
                }
                
                Debug.Log($"✅ [GiveCoinsToCharacter] 金币物品创建成功: {coinItem.gameObject.name}");
                
                // 设置物品数量
                Debug.Log($"🔄 [GiveCoinsToCharacter] 正在设置物品数量为 {amount}...");
                int oldAmount = GetItemAmount(coinItem);
                Debug.Log($"📊 [GiveCoinsToCharacter] 设置前物品数量: {oldAmount}");
                
                SetItemAmount(coinItem, amount);
                
                int newAmount = GetItemAmount(coinItem);
                Debug.Log($"📊 [GiveCoinsToCharacter] 设置后物品数量: {newAmount} (期望: {amount})");
                
                if (newAmount != amount)
                {
                    Debug.LogWarning($"⚠️ [GiveCoinsToCharacter] 物品数量设置可能失败！期望: {amount}, 实际: {newAmount}");
                }
                
                // 方法1：尝试使用Item.Attach直接附加到角色
                Debug.Log($"🔄 [GiveCoinsToCharacter] 尝试方法1: 使用Item.Attach附加到角色...");
                try
                {
                    Type itemType = coinItem.GetType();
                    MethodInfo attachMethod = itemType.GetMethod("Attach", new[] { typeof(CharacterMainControl) });
                    if (attachMethod != null)
                    {
                        attachMethod.Invoke(coinItem, new object[] { character });
                        Debug.Log($"✅ [GiveCoinsToCharacter] 方法1成功: 使用Attach附加到角色");
                        CheckCharacterCoinsAfterDelay(character, amount, 1f).Forget();
                        return;
                    }
                    else
                    {
                        Debug.Log($"⚠️ [GiveCoinsToCharacter] 方法1失败: 未找到Attach(CharacterMainControl)方法");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"⚠️ [GiveCoinsToCharacter] 方法1失败: {ex.Message}");
                }
                
                // 方法2：尝试通过CharacterItemControl添加
                Debug.Log($"🔄 [GiveCoinsToCharacter] 尝试方法2: 通过CharacterItemControl添加...");
                try
                {
                    Component itemControl = character.GetComponent("CharacterItemControl");
                    if (itemControl != null)
                    {
                        Type itemControlType = itemControl.GetType();
                        // 尝试查找AddItem或类似方法
                        MethodInfo[] methods = itemControlType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var method in methods)
                        {
                            if (method.Name.ToLower().Contains("add") && method.Name.ToLower().Contains("item"))
                            {
                                Debug.Log($"🔍 [GiveCoinsToCharacter] 找到可能的方法: {method.Name}");
                                try
                                {
                                    method.Invoke(itemControl, new object[] { coinItem });
                                    Debug.Log($"✅ [GiveCoinsToCharacter] 方法2成功: 使用{method.Name}添加物品");
                                    CheckCharacterCoinsAfterDelay(character, amount, 1f).Forget();
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning($"⚠️ [GiveCoinsToCharacter] 方法2调用失败: {ex.Message}");
                                }
                            }
                        }
                        Debug.Log($"⚠️ [GiveCoinsToCharacter] 方法2失败: 未找到合适的添加物品方法");
                    }
                    else
                    {
                        Debug.Log($"⚠️ [GiveCoinsToCharacter] 方法2失败: 未找到CharacterItemControl组件");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"⚠️ [GiveCoinsToCharacter] 方法2失败: {ex.Message}");
                }
                
                // 方法3：放在角色脚下（备用方案）
                Debug.Log($"🔄 [GiveCoinsToCharacter] 尝试方法3: 放在角色脚下（备用方案）...");
                Vector3 targetPosition = character.transform.position + Vector3.up * 0.5f;
                coinItem.transform.position = targetPosition;
                
                Debug.Log($"📍 [GiveCoinsToCharacter] 金币已放置在位置: {targetPosition}");
                Debug.Log($"   💰 已将 {amount} 金币放置在 {character.gameObject.name} 脚下（备用方案）");
                Debug.Log($"   ⚠️ 注意：角色可能不会自动捡起，需要手动验证");
                
                // 延迟检查角色是否捡到金币（1秒后）
                CheckCharacterCoinsAfterDelay(character, amount, 1f).Forget();
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
        /// 添加测试金币（F9键）- 真实添加到背包
        /// </summary>
        private void AddTestMoney()
        {
            try
            {
                CharacterMainControl player = GetOrFindPlayer();
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
                    SetItemAmount(coinItem, testAmount);
                    
                    // 发送到玩家背包
                    bool success = ItemUtilities.SendToPlayerCharacterInventory(coinItem);
                    
                    if (success)
                    {
                        Debug.Log($"✅ 已添加 {testAmount} 金币到玩家背包");
                        
                        // 显示当前金币总数
                        int totalCoins = CountPlayerCoins(player);
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
                                            CharacterMainControl player = GetOrFindPlayer();
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
        
        /// <summary>
        /// 设置物品数量（使用StackCount属性）
        /// </summary>
        private void SetItemAmount(Item item, int amount)
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

        void OnDestroy()
        {
            Debug.Log("=== 雇佣兵系统Mod 已卸载 ===");
        }
    }
}
