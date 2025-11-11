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
        private int maxRequiredAmount = 1000;      // 每个敌人最高报价
        private float bribeRange = 3f;             // 贿赂范围（米）- 俯视图游戏用靠近方式
        
        // 物品ID常量
        private const int ITEM_ID_COIN = 451;      // 金币ID
        
        // 缓存的玩家对象
        private CharacterMainControl cachedPlayer = null;
        
        // 贿赂记录类
        private class BribeRecord
        {
            public int Times = 0;         // 贿赂次数
            public int TotalAmount = 0;   // 累计金额
            public int FailedAttempts = 0; // 达到门槛后的失败次数
            public int RequiredAmount = 0; // 目标开价
        }
        
        // 存储每个敌人的贿赂记录
        private Dictionary<CharacterMainControl, BribeRecord> bribeRecords = new Dictionary<CharacterMainControl, BribeRecord>();
        
        // 存储被贿赂的友军（跟随玩家移动）
        private List<CharacterMainControl> allies = new List<CharacterMainControl>();
        private int maxAllyCount = 2;               // 友军上限
        private static readonly string[] MaxPartyAllyMessages = new[]
        {
            "人太多会把我的战术动作堵死！",
            "再来一个就得轮流蹲坑了！",
            "别挤别挤，护甲都快被磨花了！",
            "我这身肌肉可是需要呼吸空间的！",
            "队伍爆满啦，留条命给我们喘气！",
            "兄弟，多一个人就要分我战利品了！",
            "饿的时候我的口粮可不够分！",
            "再来人我就要睡走廊了！",
            "别再拉人啦，我们已经够壮观了！",
            "排队贿赂好吗？一个个来！"
        };
        
        // 友军跟随更新参数
        private float followUpdateInterval = 0.05f; // 跟随更新间隔（秒）- 每秒20次
        private float followTimer = 0f;

        void Awake()
        {
            Debug.Log("=== 雇佣兵系统Mod v1.8 已加载 ===");
            Debug.Log("功能说明：");
            Debug.Log($"  E键 - 靠近敌人后按E贿赂（每次 {perBribeAmount} 金币，范围{bribeRange}米）");
            Debug.Log($"  转换条件：敌人随机要价 {minRequiredAmount}-{maxRequiredAmount} 金币，凑够后有概率招募（失败越多越倔强）");
            Debug.Log($"  ✅ 友军保留完整AI智能（会攻击、会躲避、自然移动）");
            Debug.Log("调试功能：");
            Debug.Log($"  F9键 - 给自己添加测试金币");
            Debug.Log($"  F8键 - 打印友军的所有组件（含子对象）");
            Debug.Log($"  F7键 - 深度探索CharacterMainControl");
            Debug.Log($"  F6键 - 探索AI控制器（查看巡逻点等AI参数）");
            Debug.Log($"  F5键 - 探索CharacterItemControl（查看背包字段）");
            Debug.Log($"  F4键 - 探索Item类（查看数量字段名）");
            Debug.Log("========================");
        }

        void Update()
        {
            // E键 - 贿赂敌人
            if (Input.GetKeyDown(KeyCode.E))
            {
                TryBribeEnemy();
            }

            // F9键 - 测试：给自己添加金币（方便测试）
            if (Input.GetKeyDown(KeyCode.F9))
            {
                AddTestMoney();
            }
            
            // F8键 - 打印友军的所有组件列表
            if (Input.GetKeyDown(KeyCode.F8))
            {
                PrintAllyComponents();
            }
            
            // F7键 - 深度探索CharacterMainControl组件
            if (Input.GetKeyDown(KeyCode.F7))
            {
                PrintCharacterMainControlDetails();
            }
            
            // F6键 - 探索AIControllerTemplate子对象
            if (Input.GetKeyDown(KeyCode.F6))
            {
                ExploreAIController();
            }
            
            // F5键 - 探索CharacterItemControl组件
            if (Input.GetKeyDown(KeyCode.F5))
            {
                ExploreCharacterItemControl();
            }
            
            // F4键 - 探索Item类
            if (Input.GetKeyDown(KeyCode.F4))
            {
                ExploreItemClass();
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

                // 2. 查找附近的所有碰撞体
                Collider[] nearbyColliders = Physics.OverlapSphere(playerPos, bribeRange);

                // 3. 找到所有附近的敌人
                List<CharacterMainControl> nearbyEnemies = new List<CharacterMainControl>();
                
                foreach (Collider col in nearbyColliders)
                {
                    CharacterMainControl character = col.GetComponent<CharacterMainControl>();
                    if (character == null)
                    {
                        character = col.GetComponentInParent<CharacterMainControl>();
                    }

                    if (character != null && character.gameObject != playerObj && !IsAlly(character))
                    {
                        nearbyEnemies.Add(character);
                    }
                }

                // 4. 如果没有敌人
                if (nearbyEnemies.Count == 0)
                {
                    Debug.Log($"❌ 附近{bribeRange}米内没有敌人");
                    ShowPlayerBubble("附近没有敌人...", 2f);
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

                // 7. 检查玩家金钱
                if (!HasEnoughMoney(perBribeAmount))
                {
                    Debug.LogWarning($"❌ 金钱不足！需要 {perBribeAmount} 金币");
                    ShowPlayerBubble($"金钱不足！需要 {perBribeAmount} 金币", 2f);
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
                    ShowCharacterBubble(targetCharacter, $"想让我帮忙？至少拿出 {newRecord.RequiredAmount} 金币。", 3f);
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
                            0 => "好吧好吧…反正也没人看见，我跟你走！",
                            1 => "哎呀别推了，我只是……暂时借个肩膀！",
                            2 => "唉……都是你害的，我居然对铜臭妥协了……",
                            3 => "好吧！真香！但你得保密，我可还是那个冷酷刺客！",
                            4 => "行行行，别塞了，我怕了！真香定律又成功了……",
                            _ => "别说话，钱包让我倒向了你，我承认我失败了……"
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
                            1 => "滚开！贫穷不能打败信仰！",
                            2 => "别想用铜臭玷污我！",
                            3 => "你的钱臭气熏天，我宁死不屈！",
                            4 => "我不在乎你给多少！……其实给太多也…不对，我不要！",
                            5 => "手别抖了，再试也没用！",
                            6 => "我绝对不会向你低头……你以为我会说真香吗？",
                            _ => "我…绝对不会向金钱低头！再加点试试？"
                        };
                        ShowCharacterBubble(targetCharacter, failMessage, 2.5f);
                    }
                }
                else
                {
                    int needMoney = Mathf.Max(0, record.RequiredAmount - record.TotalAmount);
                    string message = $"贿赂中... 还差 {needMoney} 金币（总要价 {record.RequiredAmount}）";
                    Debug.Log($"   还需累计 {needMoney} 金币 / 要价 {record.RequiredAmount}");
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
            // 如果缓存存在且有效，直接返回
            if (cachedPlayer != null)
            {
                return cachedPlayer;
            }

            GameObject playerObj = GetPlayerObject();
            if (playerObj != null)
            {
                cachedPlayer = playerObj.GetComponent<CharacterMainControl>();
            }

            return cachedPlayer;
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
            return character.Team == player.Team;
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

                Teams playerTeam = player.Team;

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
                    if (currentRange < 15f)
                    {
                        patrolRangeField.SetValue(aiController, 15f);
                        if (!silent)
                        {
                            Debug.Log($"      ✅ patrolRange: {currentRange} → 15米");
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
        /// 给角色添加金币（放在脚下，让角色自己捡）
        /// </summary>
        private void GiveCoinsToCharacter(CharacterMainControl character, int amount)
        {
            try
            {
                // 创建金币物品
                Item coinItem = ItemAssetsCollection.InstantiateSync(ITEM_ID_COIN);
                if (coinItem != null)
                {
                    SetItemAmount(coinItem, amount);
                    
                    // 将金币放在角色脚下（让角色自己捡取）
                    coinItem.transform.position = character.transform.position + Vector3.up * 0.5f; // 稍微抬高避免卡地下
                    
                    Debug.Log($"   💰 已将 {amount} 金币放置在 {character.gameObject.name} 脚下");
                    Debug.Log($"   💡 角色会自动捡起金币");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"给角色添加金币时出错: {ex.Message}");
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
        /// F8 - 打印友军的所有组件列表
        /// </summary>
        private void PrintAllyComponents()
        {
            try
            {
                if (allies.Count == 0)
                {
                    Debug.Log("⚠️ 当前没有友军");
                    Debug.Log("💡 先用E键贿赂敌人，然后再按F8查看组件");
                    return;
                }
                
                Debug.Log("=== 📦 友军组件列表 ===");
                Debug.Log("");
                
                foreach (var ally in allies)
                {
                    if (ally == null) continue;
                    
                    Debug.Log($"角色: {ally.gameObject.name}");
                    Debug.Log($"位置: {ally.transform.position}");
                    Debug.Log($"队伍: {ally.Team}");
                    Debug.Log("");
                    
                    Component[] components = ally.GetComponents<Component>();
                    Debug.Log($"共 {components.Length} 个组件:");
                    
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        
                        string typeName = comp.GetType().Name;
                        bool isMonoBehaviour = comp is MonoBehaviour;
                        bool isEnabled = isMonoBehaviour ? ((MonoBehaviour)comp).enabled : true;
                        string status = isMonoBehaviour ? (isEnabled ? "🟢" : "🔴") : "⚪";
                        
                        Debug.Log($"  {status} {typeName}");
                    }
                    
                    Debug.Log("");
                    
                    // 打印子对象状态及其组件
                    int childCount = ally.transform.childCount;
                    Debug.Log($"子对象 ({childCount}个):");
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform child = ally.transform.GetChild(i);
                        string activeStatus = child.gameObject.activeSelf ? "🟢" : "🔴";
                        Debug.Log($"  {activeStatus} {child.name}");
                        
                        // 如果是AI控制器，列出它的组件
                        if (child.name.Contains("AI") || child.name.Contains("Controller"))
                        {
                            Component[] childComponents = child.GetComponents<Component>();
                            Debug.Log($"      └─ 共 {childComponents.Length} 个组件:");
                            foreach (var comp in childComponents)
                            {
                                if (comp == null) continue;
                                string typeName = comp.GetType().Name;
                                bool isMonoBehaviour = comp is MonoBehaviour;
                                bool isEnabled = isMonoBehaviour ? ((MonoBehaviour)comp).enabled : true;
                                string status = isMonoBehaviour ? (isEnabled ? "🟢" : "🔴") : "⚪";
                                Debug.Log($"         {status} {typeName}");
                            }
                        }
                    }
                    
                    Debug.Log("");
                }
                
                Debug.Log("=== 列表完成 ===");
            }
            catch (Exception ex)
            {
                Debug.LogError($"打印组件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// F7 - 打印CharacterMainControl详细信息（字段、属性、方法、子对象）
        /// </summary>
        private void PrintCharacterMainControlDetails()
        {
            try
            {
                if (allies.Count == 0)
                {
                    Debug.Log("⚠️ 当前没有友军");
                    Debug.Log("💡 先用E键贿赂敌人，然后再按F7深度探索");
                    return;
                }
                
                Debug.Log("=== 🔬 CharacterMainControl 深度探索 ===");
                Debug.Log("");
                
                foreach (var ally in allies)
                {
                    if (ally == null) continue;
                    
                    Debug.Log($"角色: {ally.gameObject.name}");
                    Debug.Log($"位置: {ally.transform.position}");
                    Debug.Log("");
                    
                    Type type = ally.GetType();
                    
                    // 1. 字段 (Fields)
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    Debug.Log($"📋 字段 ({fields.Length}个):");
                    foreach (var field in fields)
                    {
                        try
                        {
                            object value = field.GetValue(ally);
                            string valueStr = value != null ? value.ToString() : "null";
                            if (valueStr.Length > 60) valueStr = valueStr.Substring(0, 60) + "...";
                            Debug.Log($"  • {field.Name} ({field.FieldType.Name}): {valueStr}");
                        }
                        catch
                        {
                            Debug.Log($"  • {field.Name} ({field.FieldType.Name}): [无法读取]");
                        }
                    }
                    Debug.Log("");
                    
                    // 2. 属性 (Properties)
                    var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    Debug.Log($"🔧 属性 ({properties.Length}个):");
                    foreach (var prop in properties)
                    {
                        try
                        {
                            if (prop.CanRead)
                            {
                                object value = prop.GetValue(ally);
                                string valueStr = value != null ? value.ToString() : "null";
                                if (valueStr.Length > 60) valueStr = valueStr.Substring(0, 60) + "...";
                                Debug.Log($"  • {prop.Name} ({prop.PropertyType.Name}): {valueStr}");
                            }
                            else
                            {
                                Debug.Log($"  • {prop.Name} ({prop.PropertyType.Name}): [不可读]");
                            }
                        }
                        catch
                        {
                            Debug.Log($"  • {prop.Name} ({prop.PropertyType.Name}): [无法读取]");
                        }
                    }
                    Debug.Log("");
                    
                    // 3. 方法 (Methods) - 只显示公共方法
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    Debug.Log($"⚙️ 公共方法 ({methods.Length}个):");
                    foreach (var method in methods)
                    {
                        var parameters = method.GetParameters();
                        string paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        Debug.Log($"  • {method.Name}({paramStr}) → {method.ReturnType.Name}");
                    }
                    Debug.Log("");
                    
                    // 4. 子对象 (Children)
                    Transform trans = ally.transform;
                    int childCount = trans.childCount;
                    Debug.Log($"👶 子对象 ({childCount}个):");
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform child = trans.GetChild(i);
                        Debug.Log($"  • {child.name} (位置: {child.localPosition})");
                    }
                    Debug.Log("");
                }
                
                Debug.Log("=== 探索完成 ===");
            }
            catch (Exception ex)
            {
                Debug.LogError($"探索CharacterMainControl时出错: {ex.Message}");
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
        /// F4 - 探索Item类（查看数量字段名）
        /// </summary>
        private void ExploreItemClass()
        {
            try
            {
                Debug.Log("=== 💰 Item类探索 ===");
                Debug.Log("");
                
                // 找到场景中所有的Item
                Item[] allItems = FindObjectsOfType<Item>();
                
                // 找到玩家身上的第一个金币
                Item coinItem = null;
                foreach (Item item in allItems)
                {
                    if (item != null && item.IsInPlayerCharacter() && item.TypeID == ITEM_ID_COIN)
                    {
                        coinItem = item;
                        break;
                    }
                }
                
                if (coinItem == null)
                {
                    Debug.Log("⚠️ 未找到玩家身上的金币");
                    Debug.Log("💡 请先按F9添加测试金币，然后再按F4");
                    return;
                }
                
                Debug.Log($"📦 找到金币物品: TypeID = {coinItem.TypeID}");
                Debug.Log("");
                
                Type itemType = coinItem.GetType();
                
                // 列出所有字段（公共+私有）
                var fields = itemType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Debug.Log($"📋 所有字段 ({fields.Length}个):");
                
                foreach (var field in fields)
                {
                    try
                    {
                        object value = field.GetValue(coinItem);
                        string valueStr = value != null ? value.ToString() : "null";
                        
                        string accessLevel = field.IsPublic ? "public" : "private";
                        string fieldTypeName = field.FieldType.Name;
                        
                        // 高亮显示数字类型的字段
                        string highlight = "";
                        if (fieldTypeName == "Int32" || fieldTypeName == "Single" || fieldTypeName == "Float" || 
                            field.Name.ToLower().Contains("amount") || 
                            field.Name.ToLower().Contains("count") ||
                            field.Name.ToLower().Contains("quantity"))
                        {
                            highlight = " 🎯";
                        }
                        
                        Debug.Log($"  {accessLevel} {field.Name} ({fieldTypeName}): {valueStr}{highlight}");
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"  {field.Name} ({field.FieldType.Name}): [无法读取: {ex.Message}]");
                    }
                }
                
                Debug.Log("");
                
                // 列出所有属性
                var properties = itemType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Debug.Log($"🔧 所有属性 ({properties.Length}个):");
                
                foreach (var prop in properties)
                {
                    try
                    {
                        if (prop.CanRead)
                        {
                            object value = prop.GetValue(coinItem);
                            string valueStr = value != null ? value.ToString() : "null";
                            
                            string propTypeName = prop.PropertyType.Name;
                            
                            string highlight = "";
                            if (propTypeName == "Int32" || propTypeName == "Single" || propTypeName == "Float" ||
                                prop.Name.ToLower().Contains("amount") || 
                                prop.Name.ToLower().Contains("count") ||
                                prop.Name.ToLower().Contains("quantity"))
                            {
                                highlight = " 🎯";
                            }
                            
                            Debug.Log($"  {prop.Name} ({propTypeName}): {valueStr}{highlight}");
                        }
                        else
                        {
                            Debug.Log($"  {prop.Name} ({prop.PropertyType.Name}): [不可读]");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"  {prop.Name} ({prop.PropertyType.Name}): [无法读取: {ex.Message}]");
                    }
                }
                
                Debug.Log("");
                Debug.Log("=== 探索完成 ===");
                Debug.Log("💡 查找标记为 🎯 的字段/属性，这些可能是数量相关的");
                Debug.Log("💡 特别注意 Int32 类型且值接近你的金币数量的字段");
            }
            catch (Exception ex)
            {
                Debug.LogError($"探索Item类时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// F5 - 探索CharacterItemControl组件（查看背包相关字段）
        /// </summary>
        private void ExploreCharacterItemControl()
        {
            try
            {
                CharacterMainControl player = GetOrFindPlayer();
                if (player == null)
                {
                    Debug.Log("⚠️ 未找到玩家");
                    return;
                }
                
                Debug.Log("=== 🎒 CharacterItemControl 探索 ===");
                Debug.Log("");
                Debug.Log($"玩家: {player.gameObject.name}");
                Debug.Log($"位置: {player.transform.position}");
                Debug.Log("");
                
                // 获取CharacterItemControl组件
                Component itemControl = player.GetComponent("CharacterItemControl");
                if (itemControl == null)
                {
                    Debug.Log("⚠️ 未找到CharacterItemControl组件");
                    return;
                }
                
                Debug.Log($"📦 找到CharacterItemControl组件");
                Debug.Log("");
                
                Type itemControlType = itemControl.GetType();
                
                // 列出所有字段（公共+私有）
                var fields = itemControlType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Debug.Log($"📋 所有字段 ({fields.Length}个):");
                
                foreach (var field in fields)
                {
                    try
                    {
                        object value = field.GetValue(itemControl);
                        string valueStr = value != null ? value.ToString() : "null";
                        
                        // 限制显示长度
                        if (valueStr.Length > 80)
                        {
                            valueStr = valueStr.Substring(0, 80) + "...";
                        }
                        
                        string accessLevel = field.IsPublic ? "public" : "private";
                        string fieldTypeName = field.FieldType.Name;
                        
                        // 高亮显示可能是背包的字段
                        string highlight = "";
                        if (field.Name.ToLower().Contains("inventory") || 
                            field.Name.ToLower().Contains("item") ||
                            field.Name.ToLower().Contains("container") ||
                            fieldTypeName.Contains("Inventory"))
                        {
                            highlight = " 🎯";
                        }
                        
                        Debug.Log($"  {accessLevel} {field.Name} ({fieldTypeName}): {valueStr}{highlight}");
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"  {field.Name} ({field.FieldType.Name}): [无法读取: {ex.Message}]");
                    }
                }
                
                Debug.Log("");
                
                // 列出所有属性
                var properties = itemControlType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Debug.Log($"🔧 所有属性 ({properties.Length}个):");
                
                foreach (var prop in properties)
                {
                    try
                    {
                        if (prop.CanRead)
                        {
                            object value = prop.GetValue(itemControl);
                            string valueStr = value != null ? value.ToString() : "null";
                            
                            if (valueStr.Length > 80)
                            {
                                valueStr = valueStr.Substring(0, 80) + "...";
                            }
                            
                            string highlight = "";
                            if (prop.Name.ToLower().Contains("inventory") || 
                                prop.Name.ToLower().Contains("item") ||
                                prop.PropertyType.Name.Contains("Inventory"))
                            {
                                highlight = " 🎯";
                            }
                            
                            Debug.Log($"  {prop.Name} ({prop.PropertyType.Name}): {valueStr}{highlight}");
                        }
                        else
                        {
                            Debug.Log($"  {prop.Name} ({prop.PropertyType.Name}): [不可读]");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"  {prop.Name} ({prop.PropertyType.Name}): [无法读取: {ex.Message}]");
                    }
                }
                
                Debug.Log("");
                Debug.Log("=== 探索完成 ===");
                Debug.Log("💡 查找标记为 🎯 的字段/属性，这些可能是背包相关的");
            }
            catch (Exception ex)
            {
                Debug.LogError($"探索CharacterItemControl时出错: {ex.Message}");
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
                
                // 直接使用StackCount属性（从F4调试中发现的）
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
                
                // 直接使用StackCount属性（从F4调试中发现的）
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
