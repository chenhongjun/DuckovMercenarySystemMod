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
        
        // 敌人查找方法选择
        private enum EnemyFindMethod
        {
            FindObjectsOfType,  // 方法1：遍历所有角色（性能开销大，但更可靠）
            PhysicsOverlap      // 方法2：使用物理查询（性能好，但需要碰撞体）
        }
        private EnemyFindMethod enemyFindMethod = EnemyFindMethod.PhysicsOverlap; // 默认使用方法1
        
        // 按键配置（支持改键）
        private KeyCode bribeKey = KeyCode.E;      // 贿赂按键（默认E）
        private KeyCode dismissKey = KeyCode.Q;    // 解散按键（默认Q）
        private const string PREFS_BRIBE_KEY = "DuckovMercenary_BribeKey";      // PlayerPrefs键名
        private const string PREFS_DISMISS_KEY = "DuckovMercenary_DismissKey";  // PlayerPrefs键名
        
        // 设置界面相关
        private bool showSettingsGUI = false;       // 是否显示设置界面
        private bool isWaitingForKey = false;      // 是否正在等待按键输入
        private string waitingForKeyType = "";      // 等待设置的按键类型（"bribe"或"dismiss"）
        
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
        
        // 缓存玩家对象（避免重复获取）
        private CharacterMainControl? cachedPlayer = null;
        
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
            // 加载按键配置（从PlayerPrefs读取，如果没有则使用默认值）
            LoadKeyBindings();
            
            Debug.Log("=== 雇佣兵系统Mod v1.8 已加载 ===");
            Debug.Log("功能说明：");
            Debug.Log($"  {bribeKey}键 - 靠近敌人后按{bribeKey}贿赂（每次 {perBribeAmount} 金币，范围{bribeRange}米）");
            Debug.Log($"  转换条件：敌人随机要价 {minRequiredAmount}-{maxRequiredAmount} 金币，凑够后有概率招募（失败越多越倔强）");
            Debug.Log($"  ✅ 友军保留完整AI智能（会攻击、会躲避、自然移动）");
            Debug.Log($"  {dismissKey}键 - 解散所有友军");
            Debug.Log($"  改键方法：调用 SetBribeKey(KeyCode) 和 SetDismissKey(KeyCode) 方法");
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
            // F10键 - 打开/关闭设置界面
            if (Input.GetKeyDown(KeyCode.F10))
            {
                showSettingsGUI = !showSettingsGUI;
                if (showSettingsGUI)
                {
                    Debug.Log("[设置] 设置界面已打开，按F10关闭");
                }
                else
                {
                    isWaitingForKey = false;
                    Debug.Log("[设置] 设置界面已关闭");
                }
            }
            
            // 如果正在等待按键输入，检测按键
            if (isWaitingForKey)
            {
                // 检测所有按键（排除鼠标按键）
                foreach (KeyCode keyCode in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (Input.GetKeyDown(keyCode))
                    {
                        // 排除特殊按键
                        if (keyCode == KeyCode.Mouse0 || keyCode == KeyCode.Mouse1 || 
                            keyCode == KeyCode.Mouse2 || keyCode == KeyCode.Mouse3 ||
                            keyCode == KeyCode.Mouse4 || keyCode == KeyCode.Mouse5 ||
                            keyCode == KeyCode.Mouse6)
                        {
                            continue;
                        }
                        
                        // 设置按键
                        if (waitingForKeyType == "bribe")
                        {
                            SetBribeKey(keyCode);
                        }
                        else if (waitingForKeyType == "dismiss")
                        {
                            SetDismissKey(keyCode);
                        }
                        
                        isWaitingForKey = false;
                        waitingForKeyType = "";
                        break;
                    }
                }
            }
            else
            {
                // 贿赂按键 - 贿赂敌人
                if (Input.GetKeyDown(bribeKey))
                {
                    TryBribeEnemy();
                }
                
                // 解散按键 - 解散所有友军
                if (Input.GetKeyDown(dismissKey))
                {
                    DismissAllAllies();
                }
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
                CharacterMainControl? player = GetOrFindPlayerCached();
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
            CharacterMainControl? player = GetOrFindPlayerCached();
            return player ?? null;
        }
        
        /// <summary>
        /// 加载按键配置（从PlayerPrefs读取）
        /// </summary>
        private void LoadKeyBindings()
        {
            // 从PlayerPrefs读取按键配置，如果没有则使用默认值
            if (PlayerPrefs.HasKey(PREFS_BRIBE_KEY))
            {
                int bribeKeyInt = PlayerPrefs.GetInt(PREFS_BRIBE_KEY, (int)KeyCode.E);
                bribeKey = (KeyCode)bribeKeyInt;
            }
            
            if (PlayerPrefs.HasKey(PREFS_DISMISS_KEY))
            {
                int dismissKeyInt = PlayerPrefs.GetInt(PREFS_DISMISS_KEY, (int)KeyCode.Q);
                dismissKey = (KeyCode)dismissKeyInt;
            }
        }
        
        /// <summary>
        /// 设置贿赂按键（支持改键）
        /// </summary>
        /// <param name="keyCode">新的按键</param>
        public void SetBribeKey(KeyCode keyCode)
        {
            bribeKey = keyCode;
            PlayerPrefs.SetInt(PREFS_BRIBE_KEY, (int)keyCode);
            PlayerPrefs.Save();
            Debug.Log($"[改键] 贿赂按键已设置为: {keyCode}");
        }
        
        /// <summary>
        /// 设置解散按键（支持改键）
        /// </summary>
        /// <param name="keyCode">新的按键</param>
        public void SetDismissKey(KeyCode keyCode)
        {
            dismissKey = keyCode;
            PlayerPrefs.SetInt(PREFS_DISMISS_KEY, (int)keyCode);
            PlayerPrefs.Save();
            Debug.Log($"[改键] 解散按键已设置为: {keyCode}");
        }
        
        /// <summary>
        /// 获取当前贿赂按键
        /// </summary>
        public KeyCode GetBribeKey()
        {
            return bribeKey;
        }
        
        /// <summary>
        /// 获取当前解散按键
        /// </summary>
        public KeyCode GetDismissKey()
        {
            return dismissKey;
        }
        
        /// <summary>
        /// 重置按键为默认值（E和Q）
        /// </summary>
        public void ResetKeyBindings()
        {
            SetBribeKey(KeyCode.E);
            SetDismissKey(KeyCode.Q);
            Debug.Log("[改键] 按键已重置为默认值（E和Q）");
        }
        
        /// <summary>
        /// 开始等待按键输入（用于设置界面）
        /// </summary>
        private void StartWaitingForKey(string keyType)
        {
            isWaitingForKey = true;
            waitingForKeyType = keyType;
            Debug.Log($"[设置] 请按下要设置为{keyType}的按键...");
        }
        
        /// <summary>
        /// 绘制设置界面GUI
        /// </summary>
        void OnGUI()
        {
            if (!showSettingsGUI) return;
            
            // 设置窗口大小和位置
            float windowWidth = 400f;
            float windowHeight = 300f;
            float windowX = (Screen.width - windowWidth) / 2f;
            float windowY = (Screen.height - windowHeight) / 2f;
            
            // 创建窗口
            Rect windowRect = new Rect(windowX, windowY, windowWidth, windowHeight);
            GUI.Window(0, windowRect, DrawSettingsWindow, "雇佣兵系统Mod - 按键设置");
        }
        
        /// <summary>
        /// 绘制设置窗口内容
        /// </summary>
        void DrawSettingsWindow(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            
            // 标题
            GUILayout.Label("按键设置", new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold });
            GUILayout.Space(10);
            
            // 分隔线
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(10);
            
            // 当前按键显示
            GUILayout.Label($"当前贿赂按键: {bribeKey}", GUI.skin.label);
            GUILayout.Label($"当前解散按键: {dismissKey}", GUI.skin.label);
            GUILayout.Space(10);
            
            // 等待按键提示
            if (isWaitingForKey)
            {
                string keyTypeName = waitingForKeyType == "bribe" ? "贿赂" : "解散";
                GUILayout.Label($"正在等待按键输入... ({keyTypeName})", new GUIStyle(GUI.skin.label) { normal = { textColor = Color.yellow } });
                GUILayout.Space(5);
            }
            
            // 按钮区域
            GUILayout.BeginHorizontal();
            
            // 设置贿赂按键按钮
            if (GUILayout.Button($"设置贿赂按键 ({bribeKey})", GUILayout.Height(30)))
            {
                StartWaitingForKey("bribe");
            }
            
            // 设置解散按键按钮
            if (GUILayout.Button($"设置解散按键 ({dismissKey})", GUILayout.Height(30)))
            {
                StartWaitingForKey("dismiss");
            }
            
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            
            // 重置按钮
            if (GUILayout.Button("重置为默认值 (E/Q)", GUILayout.Height(30)))
            {
                ResetKeyBindings();
            }
            
            GUILayout.Space(10);
            
            // 关闭按钮
            if (GUILayout.Button("关闭 (F10)", GUILayout.Height(30)))
            {
                showSettingsGUI = false;
                isWaitingForKey = false;
            }
            
            GUILayout.Space(10);
            
            // 提示信息
            GUILayout.Label("提示：按F10可随时打开/关闭此设置界面", new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Color.gray } });
            
            GUILayout.EndVertical();
            
            // 允许拖动窗口
            GUI.DragWindow();
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
        /// 从玩家对象获取Inventory中的所有金币物品（优化版：避免遍历场景中所有Item）
        /// </summary>
        private List<Item> GetPlayerCoinItems(CharacterMainControl player)
        {
            try
            {
                if (player == null || player.gameObject == null)
                {
                    return new List<Item>();
                }
                
                // 1. 直接通过GetComponent获取CharacterItemControl组件（更可靠）
                Component itemControlComponent = player.gameObject.GetComponent("CharacterItemControl");
                if (itemControlComponent == null)
                {
                    Debug.LogWarning("❌ 未找到CharacterItemControl组件");
                    return new List<Item>();
                }
                
                // 2. 获取Inventory（先尝试属性，再尝试字段）
                Type itemControlType = itemControlComponent.GetType();
                object inventoryObj = null;
                
                // 获取Inventory属性
                PropertyInfo inventoryProp = itemControlType.GetProperty("inventory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (inventoryProp == null)
                {
                    Debug.LogWarning($"❌ 未找到CharacterItemControl的inventory属性 (类型: {itemControlType.Name})");
                    return new List<Item>();
                }
                
                inventoryObj = inventoryProp.GetValue(itemControlComponent);
                
                // 3. 获取Content属性（List<Item>）
                Type inventoryType = inventoryObj.GetType();
                PropertyInfo contentProp = inventoryType.GetProperty("Content", BindingFlags.Public | BindingFlags.Instance);
                if (contentProp == null)
                {
                    Debug.LogWarning($"❌ 未找到Inventory的Content属性 (类型: {inventoryType.Name})");
                    return new List<Item>();
                }
                
                object contentObj = contentProp.GetValue(inventoryObj);
                if (contentObj == null)
                {
                    return new List<Item>(); // 背包为空
                }
                
                // 4. 转换为List<Item>并筛选金币
                if (contentObj is System.Collections.IEnumerable contentList)
                {
                    List<Item> coinItems = new List<Item>();
                    foreach (object itemObj in contentList)
                    {
                        if (itemObj is Item item && item != null && item.TypeID == ITEM_ID_COIN)
                        {
                            coinItems.Add(item);
                        }
                    }
                    return coinItems;
                }
                
                return new List<Item>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"从玩家Inventory获取金币时出错: {ex.Message}\n{ex.StackTrace}");
                return new List<Item>();
            }
        }
        
        /// <summary>
        /// 统计玩家背包中的金币数量（优化版：直接从玩家Inventory获取，避免遍历所有Item）
        /// </summary>
        public int CountPlayerCoins(CharacterMainControl player)
        {
            try
            {
                int totalCoins = 0;
                
                // 优化：直接从玩家Inventory获取金币物品
                List<Item> coinItems = GetPlayerCoinItems(player);
                
                foreach (Item item in coinItems)
                {
                    if (item != null)
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
        /// 查找附近的敌人（提供两种方法可选）
        /// </summary>
        /// <param name="playerPos">玩家位置</param>
        /// <param name="playerObj">玩家对象</param>
        /// <param name="range">查找范围（米）</param>
        /// <param name="method">查找方法</param>
        /// <returns>附近的敌人列表</returns>
        private List<CharacterMainControl> FindNearbyEnemies(Vector3 playerPos, GameObject playerObj, float range, EnemyFindMethod method)
        {
            List<CharacterMainControl> nearbyEnemies = new List<CharacterMainControl>();
            
            try
            {
                if (method == EnemyFindMethod.FindObjectsOfType)
                {
                    // 方法1：遍历所有角色，然后按距离筛选（性能开销大，但更可靠，不依赖碰撞体）
                    CharacterMainControl[] allCharacters = FindObjectsOfType<CharacterMainControl>();
                    
                    foreach (CharacterMainControl character in allCharacters)
                    {
                        if (character == null || character.gameObject == null) continue;
                        if (character.gameObject == playerObj) continue;
                        if (IsAlly(character)) continue;
                        
                        float distance = Vector3.Distance(playerPos, character.transform.position);
                        if (distance <= range)
                        {
                            nearbyEnemies.Add(character);
                            Debug.Log($"🎯 [方法1] 发现敌人: {character.gameObject.name} (距离: {distance:F2}米)");
                        }
                    }
                }
                else if (method == EnemyFindMethod.PhysicsOverlap)
                {
                    // 方法2：使用Physics.OverlapSphere查找范围内的碰撞体（性能好，但需要碰撞体）
                    Collider[] colliders = Physics.OverlapSphere(playerPos, range);
                    
                    foreach (Collider collider in colliders)
                    {
                        if (collider == null || collider.gameObject == null) continue;
                        if (collider.gameObject == playerObj) continue;
                        
                        // 获取CharacterMainControl组件
                        CharacterMainControl character = collider.GetComponent<CharacterMainControl>();
                        if (character == null) continue;
                        if (IsAlly(character)) continue;
                        
                        float distance = Vector3.Distance(playerPos, character.transform.position);
                        nearbyEnemies.Add(character);
                        Debug.Log($"🎯 [方法2] 发现敌人: {character.gameObject.name} (距离: {distance:F2}米)");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"查找附近敌人时出错: {ex.Message}\n{ex.StackTrace}");
            }
            
            return nearbyEnemies;
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

                // 2. 查找附近的敌人（使用封装的方法）
                List<CharacterMainControl> nearbyEnemies = FindNearbyEnemies(playerPos, playerObj, bribeRange, enemyFindMethod);

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
        /// 获取玩家对象（使用路径查找，避免遍历所有对象）
        /// 从日志分析：玩家路径为 MultiSceneCore/Base/Character(Clone)
        /// </summary>
        private GameObject GetPlayerObject()
        {
            // 方法1：通过路径查找（最快，无需遍历）
            // 路径: MultiSceneCore/Base/Character(Clone)
            // 先找到根对象 MultiSceneCore，然后通过路径查找
            GameObject rootObj = GameObject.Find("MultiSceneCore");
            if (rootObj != null)
            {
                Transform baseTransform = rootObj.transform.Find("Base");
                if (baseTransform != null)
                {
                    Transform playerTransform = baseTransform.Find("Character(Clone)");
                    if (playerTransform != null)
                    {
                        CharacterMainControl charControl = playerTransform.GetComponent<CharacterMainControl>();
                        if (charControl != null)
                        {
                            // 备选：检查Team属性
                            if (charControl.Team.ToString().ToLower() == "player")
                            {
                                Debug.Log($"[GetPlayerObject] ✅ 通过路径和Team找到玩家: {playerTransform.name} (ID: {charControl.GetInstanceID()})");
                                return playerTransform.gameObject;
                            }
                        }
                    }
                }
            }
            
            // 方法2：遍历所有CharacterMainControl（备选，开销较大）
            // 仅在路径查找失败时使用
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
                        Debug.Log($"[GetPlayerObject] ✅ 通过FindObjectsOfType和IsMainCharacter找到主玩家: {character.gameObject.name} (ID: {character.GetInstanceID()}, Team: {character.Team})");
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
                Debug.Log($"[GetPlayerObject] ✅ 通过FindObjectsOfType和Team找到玩家: {playerTeamCharacter.gameObject.name} (ID: {playerTeamCharacter.GetInstanceID()}, Team: {playerTeamCharacter.Team})");
                return playerTeamCharacter.gameObject;
            }
            
            Debug.LogWarning($"[GetPlayerObject] ⚠️ 未找到玩家对象 (共检查了 {allCharacters.Length} 个角色)");
            return null;
        }

        /// <summary>
        /// 判断角色是否已经是友军（优化版：使用缓存的玩家队伍）
        /// </summary>
        private bool IsAlly(CharacterMainControl character)
        {
            // 优化：使用缓存的玩家队伍，避免重复获取玩家
            if (!hasCachedPlayerTeam)
            {
                CharacterMainControl player = GetOrFindPlayerCached();
                if (player == null) return false;
                cachedPlayerTeam = player.Team;
                hasCachedPlayerTeam = true;
            }
            
            return character.Team == cachedPlayerTeam;
        }
        
        /// <summary>
        /// 获取或查找玩家角色（使用缓存，避免重复查找）
        /// </summary>
        private CharacterMainControl? GetOrFindPlayerCached()
        {
            // 如果缓存有效，直接返回
            if (cachedPlayer != null && cachedPlayer.gameObject != null && cachedPlayer.gameObject)
            {
                return cachedPlayer;
            }
            
            // 缓存无效，重新查找
            GameObject? playerObj = GetPlayerObject();
            if (playerObj != null)
            {
                CharacterMainControl? player = playerObj.GetComponent<CharacterMainControl>();
                if (player != null)
                {
                    cachedPlayer = player;
                    // 同时更新队伍缓存
                    cachedPlayerTeam = player.Team;
                    hasCachedPlayerTeam = true;
                    return player;
                }
            }
            
            cachedPlayer = null;
            return null;
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
        /// 检查玩家金钱是否足够（优化版：使用缓存的玩家对象）
        /// </summary>
        private bool HasEnoughMoney(int amount)
        {
            try
            {
                CharacterMainControl? player = GetOrFindPlayerCached();
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
        /// 扣除玩家金钱并转移给目标角色（优化版：使用缓存的玩家对象）
        /// </summary>
        private void DeductMoney(int amount, CharacterMainControl targetEnemy)
        {
            try
            {
                CharacterMainControl? player = GetOrFindPlayerCached();
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
        /// 从玩家背包移除指定数量的金币（优化版：直接从玩家Inventory获取物品，避免遍历所有Item）
        /// </summary>
        private bool RemovePlayerCoins(CharacterMainControl player, int amount)
        {
            try
            {
                int remaining = amount;
                
                // 优化：直接从玩家对象获取Inventory，避免遍历场景中所有Item
                List<Item> coinItems = GetPlayerCoinItems(player);
                if (coinItems == null || coinItems.Count == 0)
                {
                    Debug.LogWarning("❌ 玩家背包中没有金币");
                    return false;
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
                    // CheckCharacterCoinsAfterDelay(character, amount, 1f).Forget();
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
        /// 统计角色身上的金币数量（优化版：直接从角色Inventory获取）
        /// </summary>
        private int CountCharacterCoins(CharacterMainControl character)
        {
            try
            {
                if (character == null || character.gameObject == null)
                {
                    return 0;
                }
                
                // 使用与玩家相同的方法：直接从Inventory获取
                List<Item> coinItems = GetPlayerCoinItems(character);
                
                int totalCoins = 0;
                foreach (Item item in coinItems)
                {
                    if (item != null)
                    {
                        int itemAmount = GetItemAmount(item);
                        totalCoins += itemAmount;
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
