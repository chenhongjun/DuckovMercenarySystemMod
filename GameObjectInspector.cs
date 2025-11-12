using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DuckovMercenarySystemMod
{
    /// <summary>
    /// GameObject调试检查器 - 用于F6键的调试打印功能
    /// 独立于主功能，专门用于启发式开发和调试
    /// </summary>
    public class GameObjectInspector
    {
        /// <summary>
        /// 组件统计信息
        /// </summary>
        private class ComponentStats
        {
            public int gameObjectCount = 0;
            public int componentCount = 0;
            public int fieldCount = 0;
            public int propertyCount = 0;
            public int methodCount = 0;
        }
        
        /// <summary>
        /// 关键词列表（用于高亮显示重要字段/属性/方法）
        /// 这些关键词覆盖了游戏中常见的功能需求
        /// </summary>
        private static readonly string[] ImportantKeywords = new[]
        {
            // 生命值相关
            "health", "hp", "life", "lives", "damage", "hurt", "blood", "armor", "shield",
            // 金钱/物品相关
            "money", "coin", "cash", "gold", "currency", "item", "inventory", "storage", "bag", "backpack",
            // 队伍/阵营相关
            "team", "ally", "enemy", "faction", "side", "relation", "friend", "foe",
            // 位置/移动相关
            "position", "pos", "location", "transform", "move", "movement", "speed", "velocity", "walk", "run", "jump",
            // 战斗相关
            "attack", "weapon", "gun", "shoot", "fire", "ammo", "bullet", "reload", "aim", "target",
            // 状态相关
            "state", "status", "condition", "active", "enable", "disable", "alive", "dead", "kill",
            // AI相关
            "ai", "controller", "behavior", "behaviour", "patrol", "follow", "chase", "flee",
            // 属性/统计相关
            "stat", "statistic", "level", "exp", "experience", "skill", "ability", "power",
            // 其他重要字段
            "name", "id", "type", "tag", "layer", "owner", "master", "parent", "child"
        };
        
        /// <summary>
        /// 打印玩家和所有队友的完整属性
        /// </summary>
        public void PrintPlayerAndAlliesProperties(CharacterMainControl player, List<CharacterMainControl> allies)
        {
            Debug.Log("🔍 [GameObjectInspector] 函数开始执行");
            try
            {
                Debug.Log("╔════════════════════════════════════════════════════════════════════════════╗");
                Debug.Log("║              📋 玩家和队友完整属性分析报告                                ║");
                Debug.Log("╚════════════════════════════════════════════════════════════════════════════╝");
                Debug.Log("");
                Debug.Log($"📅 生成时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Debug.Log("");
                Debug.Log("────────────────────────────────────────────────────────────────────────────");
                Debug.Log("💡 使用说明:");
                Debug.Log("   • 本报告用于调试和开发，包含玩家和所有队友的完整属性信息");
                Debug.Log("   • ⭐ 标记的字段/属性/方法为重要项（包含关键词）");
                Debug.Log("   • 重要项会优先显示，便于快速定位关键信息");
                Debug.Log("   • 所有信息都会完整打印，不会跳过任何组件");
                Debug.Log("   • 在日志中搜索 ⭐ 可快速找到重要字段/属性/方法");
                Debug.Log("   • 搜索关键词: health, hp, money, coin, team, speed, attack 等");
                Debug.Log("────────────────────────────────────────────────────────────────────────────");
                Debug.Log("");
                
                // 1. 打印玩家属性
                if (player != null && player.gameObject != null)
                {
                    Debug.Log($"╔════════════════════════════════════════════════════════════════════════════╗");
                    Debug.Log($"║ 🎮 玩家角色");
                    Debug.Log($"╠════════════════════════════════════════════════════════════════════════════╣");
                    Debug.Log($"║ 名称: {player.gameObject.name}");
                    Debug.Log($"║ 位置: X={player.transform.position.x:F2}, Y={player.transform.position.y:F2}, Z={player.transform.position.z:F2}");
                    Debug.Log($"║ 队伍: {player.Team}");
                    Debug.Log($"║ 状态: {(player.gameObject.activeSelf ? "🟢 激活" : "🔴 未激活")}");
                    Debug.Log($"╚════════════════════════════════════════════════════════════════════════════╝");
                    Debug.Log("");
                    
                    var playerStats = new ComponentStats();
                    PrintGameObjectTree(player.gameObject, 0, ref playerStats);
                    
                    Debug.Log("");
                    Debug.Log("────────────────────────────────────────────────────────────────────────────");
                    Debug.Log($"📊 玩家统计汇总:");
                    Debug.Log($"   • GameObject总数: {playerStats.gameObjectCount}");
                    Debug.Log($"   • 组件总数: {playerStats.componentCount}");
                    Debug.Log($"   • 字段总数: {playerStats.fieldCount}");
                    Debug.Log($"   • 属性总数: {playerStats.propertyCount}");
                    Debug.Log($"   • 方法总数: {playerStats.methodCount}");
                    Debug.Log("────────────────────────────────────────────────────────────────────────────");
                    Debug.Log("");
                    Debug.Log("");
                }
                else
                {
                    Debug.LogWarning("⚠️ [GameObjectInspector] 未找到玩家角色");
                }
                
                // 2. 打印所有队友属性
                allies?.RemoveAll(ally => ally == null || ally.gameObject == null);
                if (allies != null && allies.Count > 0)
                {
                    Debug.Log($"╔════════════════════════════════════════════════════════════════════════════╗");
                    Debug.Log($"║ 👥 队友列表 (共 {allies.Count} 名)");
                    Debug.Log($"╚════════════════════════════════════════════════════════════════════════════╝");
                    Debug.Log("");
                    
                    int allyIndex = 0;
                    foreach (var ally in allies)
                    {
                        if (ally == null || ally.gameObject == null) continue;
                        
                        allyIndex++;
                        Debug.Log($"╔════════════════════════════════════════════════════════════════════════════╗");
                        Debug.Log($"║ 队友 #{allyIndex} / {allies.Count}");
                        Debug.Log($"╠════════════════════════════════════════════════════════════════════════════╣");
                        Debug.Log($"║ 名称: {ally.gameObject.name}");
                        Debug.Log($"║ 位置: X={ally.transform.position.x:F2}, Y={ally.transform.position.y:F2}, Z={ally.transform.position.z:F2}");
                        Debug.Log($"║ 队伍: {ally.Team}");
                        Debug.Log($"║ 状态: {(ally.gameObject.activeSelf ? "🟢 激活" : "🔴 未激活")}");
                        Debug.Log($"╚════════════════════════════════════════════════════════════════════════════╝");
                        Debug.Log("");
                        
                        var allyStats = new ComponentStats();
                        PrintGameObjectTree(ally.gameObject, 0, ref allyStats);
                        
                        Debug.Log("");
                        Debug.Log("────────────────────────────────────────────────────────────────────────────");
                        Debug.Log($"📊 队友 #{allyIndex} 统计汇总:");
                        Debug.Log($"   • GameObject总数: {allyStats.gameObjectCount}");
                        Debug.Log($"   • 组件总数: {allyStats.componentCount}");
                        Debug.Log($"   • 字段总数: {allyStats.fieldCount}");
                        Debug.Log($"   • 属性总数: {allyStats.propertyCount}");
                        Debug.Log($"   • 方法总数: {allyStats.methodCount}");
                        Debug.Log("────────────────────────────────────────────────────────────────────────────");
                        Debug.Log("");
                        Debug.Log("");
                    }
                }
                else
                {
                    Debug.Log("⚠️ [GameObjectInspector] 当前没有队友");
                }
                
                Debug.Log("╔════════════════════════════════════════════════════════════════════════════╗");
                Debug.Log("║                            ✅ 分析完成                                    ║");
                Debug.Log("╚════════════════════════════════════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                Debug.LogError($"打印玩家和队友属性时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 递归打印GameObject树及其所有组件详情
        /// </summary>
        private void PrintGameObjectTree(GameObject obj, int depth, ref ComponentStats stats)
        {
            if (obj == null) return;
            
            try
            {
                stats.gameObjectCount++;
                
                string indent = new string(' ', depth * 2);
                string treeSymbol = depth == 0 ? "┌" : (depth > 0 ? "├" : "");
                string activeStatus = obj.activeSelf ? "🟢" : "🔴";
                string depthIndicator = depth > 0 ? $" [L{depth}]" : " [ROOT]";
                
                Debug.Log($"{indent}{treeSymbol}─ {activeStatus} GameObject: {obj.name}{depthIndicator}");
                Debug.Log($"{indent}│   路径: {GetGameObjectPath(obj)}");
                
                // 打印所有组件
                Component[] components = obj.GetComponents<Component>();
                if (components.Length > 0)
                {
                    Debug.Log($"{indent}│   ┌─ 📦 组件列表 ({components.Length}个)");
                    int compIndex = 0;
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        compIndex++;
                        bool isLast = compIndex == components.Length;
                        PrintComponentDetails(comp, depth, compIndex, components.Length, isLast, ref stats);
                    }
                    Debug.Log($"{indent}│   └─ 组件列表结束");
                }
                else
                {
                    Debug.Log($"{indent}│   └─ (无组件)");
                }
                
                // 递归处理子对象
                int childCount = obj.transform.childCount;
                if (childCount > 0)
                {
                    Debug.Log($"{indent}│   └─ 子对象 ({childCount}个):");
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform child = obj.transform.GetChild(i);
                        if (child != null && child.gameObject != null)
                        {
                            PrintGameObjectTree(child.gameObject, depth + 1, ref stats);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"遍历GameObject时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取GameObject的完整路径
        /// </summary>
        private string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "";
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
        
        /// <summary>
        /// 打印组件的详细信息（字段、属性、方法）
        /// </summary>
        private void PrintComponentDetails(Component comp, int depth, int compIndex, int totalComps, bool isLast, ref ComponentStats stats)
        {
            if (comp == null) return;
            
            try
            {
                stats.componentCount++;
                string indent = new string(' ', depth * 2);
                string connector = isLast ? "└" : "├";
                string subConnector = isLast ? " " : "│";
                
                Type compType = comp.GetType();
                string typeName = compType.Name;
                string fullTypeName = compType.FullName;
                
                bool isMonoBehaviour = comp is MonoBehaviour;
                bool isEnabled = isMonoBehaviour ? ((MonoBehaviour)comp).enabled : true;
                string status = isMonoBehaviour ? (isEnabled ? "🟢" : "🔴") : "⚪";
                
                // 判断是否为Unity标准组件（仅用于标记，不跳过打印）
                bool isUnityStandard = fullTypeName?.StartsWith("UnityEngine.") == true;
                string unityTag = isUnityStandard ? " [Unity标准]" : "";
                
                Debug.Log($"{indent}{subConnector}   {connector}─ [{compIndex}/{totalComps}] {status} {typeName}{unityTag}");
                Debug.Log($"{indent}{subConnector}      │ 命名空间: {compType.Namespace ?? "(无)"}");
                Debug.Log($"{indent}{subConnector}      │ 完整类型: {fullTypeName}");
                
                // 1. 字段 (Fields) - 先显示重要字段，再显示其他字段
                var allFields = compType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                stats.fieldCount += allFields.Length;
                if (allFields.Length > 0)
                {
                    // 分离重要字段和普通字段
                    var importantFields = allFields.Where(f => ImportantKeywords.Any(kw => f.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    var normalFields = allFields.Where(f => !ImportantKeywords.Any(kw => f.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    
                    Debug.Log($"{indent}{subConnector}      │ ┌─ 📋 字段 ({allFields.Length}个)");
                    
                    int fieldIndex = 0;
                    
                    // 先打印重要字段
                    if (importantFields.Count > 0)
                    {
                        Debug.Log($"{indent}{subConnector}      │ │ ⭐ 重要字段 ({importantFields.Count}个):");
                        foreach (var field in importantFields)
                        {
                            fieldIndex++;
                            bool isFieldLast = fieldIndex == allFields.Length;
                            string fieldConnector = isFieldLast ? "└" : "├";
                            
                            try
                            {
                                object value = null;
                                bool canRead = true;
                                try
                                {
                                    value = field.GetValue(comp);
                                }
                                catch
                                {
                                    canRead = false;
                                }
                                
                                string accessLevel = field.IsPublic ? "public" : (field.IsPrivate ? "private" : "protected");
                                string staticMod = field.IsStatic ? "static " : "";
                                string valueStr = canRead ? (value != null ? value.ToString() : "null") : "[无法读取]";
                                
                                if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                
                                Debug.Log($"{indent}{subConnector}      │ │ {fieldConnector}─ [{fieldIndex:00}] ⭐ {accessLevel} {staticMod}{field.Name}");
                                Debug.Log($"{indent}{subConnector}      │ │   类型: {field.FieldType.Name} | 值: {valueStr}");
                            }
                            catch (Exception ex)
                            {
                                Debug.Log($"{indent}{subConnector}      │ │ {fieldConnector}─ [{fieldIndex:00}] ⭐ {field.Name} ({field.FieldType.Name}): [读取错误: {ex.Message}]");
                            }
                        }
                    }
                    
                    // 再打印普通字段
                    if (normalFields.Count > 0)
                    {
                        if (importantFields.Count > 0)
                        {
                            Debug.Log($"{indent}{subConnector}      │ │ ────────────────────────────────────────────────────────────────");
                        }
                        foreach (var field in normalFields)
                        {
                            fieldIndex++;
                            bool isFieldLast = fieldIndex == allFields.Length;
                            string fieldConnector = isFieldLast ? "└" : "├";
                            
                            try
                            {
                                object value = null;
                                bool canRead = true;
                                try
                                {
                                    value = field.GetValue(comp);
                                }
                                catch
                                {
                                    canRead = false;
                                }
                                
                                string accessLevel = field.IsPublic ? "public" : (field.IsPrivate ? "private" : "protected");
                                string staticMod = field.IsStatic ? "static " : "";
                                string valueStr = canRead ? (value != null ? value.ToString() : "null") : "[无法读取]";
                                
                                if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                
                                Debug.Log($"{indent}{subConnector}      │ │ {fieldConnector}─ [{fieldIndex:00}] {accessLevel} {staticMod}{field.Name}");
                                Debug.Log($"{indent}{subConnector}      │ │   类型: {field.FieldType.Name} | 值: {valueStr}");
                            }
                            catch (Exception ex)
                            {
                                Debug.Log($"{indent}{subConnector}      │ │ {fieldConnector}─ [{fieldIndex:00}] {field.Name} ({field.FieldType.Name}): [读取错误: {ex.Message}]");
                            }
                        }
                    }
                    
                    Debug.Log($"{indent}{subConnector}      │ └─ 字段列表结束");
                }
                
                // 2. 属性 (Properties) - 先显示重要属性，再显示其他属性
                var allProperties = compType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                stats.propertyCount += allProperties.Length;
                if (allProperties.Length > 0)
                {
                    // 分离重要属性和普通属性
                    var importantProps = allProperties.Where(p => ImportantKeywords.Any(kw => p.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    var normalProps = allProperties.Where(p => !ImportantKeywords.Any(kw => p.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    
                    Debug.Log($"{indent}{subConnector}      │ ┌─ 🔧 属性 ({allProperties.Length}个)");
                    
                    int propIndex = 0;
                    
                    // 先打印重要属性
                    if (importantProps.Count > 0)
                    {
                        Debug.Log($"{indent}{subConnector}      │ │ ⭐ 重要属性 ({importantProps.Count}个):");
                        foreach (var prop in importantProps)
                        {
                            propIndex++;
                            bool isPropLast = propIndex == allProperties.Length;
                            string propConnector = isPropLast ? "└" : "├";
                            
                            try
                            {
                                object value = null;
                                bool canRead = prop.CanRead;
                                if (canRead)
                                {
                                    try
                                    {
                                        value = prop.GetValue(comp);
                                    }
                                    catch
                                    {
                                        canRead = false;
                                    }
                                }
                                
                                string valueStr = canRead ? (value != null ? value.ToString() : "null") : "[不可读]";
                                if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                
                                string readWrite = prop.CanRead && prop.CanWrite ? "get;set;" : (prop.CanRead ? "get;" : "set;");
                                
                                Debug.Log($"{indent}{subConnector}      │ │ {propConnector}─ [{propIndex:00}] ⭐ {prop.Name}");
                                Debug.Log($"{indent}{subConnector}      │ │   类型: {prop.PropertyType.Name} | 访问器: [{readWrite}] | 值: {valueStr}");
                            }
                            catch (Exception ex)
                            {
                                Debug.Log($"{indent}{subConnector}      │ │ {propConnector}─ [{propIndex:00}] ⭐ {prop.Name} ({prop.PropertyType.Name}): [读取错误: {ex.Message}]");
                            }
                        }
                    }
                    
                    // 再打印普通属性
                    if (normalProps.Count > 0)
                    {
                        if (importantProps.Count > 0)
                        {
                            Debug.Log($"{indent}{subConnector}      │ │ ────────────────────────────────────────────────────────────────");
                        }
                        foreach (var prop in normalProps)
                        {
                            propIndex++;
                            bool isPropLast = propIndex == allProperties.Length;
                            string propConnector = isPropLast ? "└" : "├";
                            
                            try
                            {
                                object value = null;
                                bool canRead = prop.CanRead;
                                if (canRead)
                                {
                                    try
                                    {
                                        value = prop.GetValue(comp);
                                    }
                                    catch
                                    {
                                        canRead = false;
                                    }
                                }
                                
                                string valueStr = canRead ? (value != null ? value.ToString() : "null") : "[不可读]";
                                if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                
                                string readWrite = prop.CanRead && prop.CanWrite ? "get;set;" : (prop.CanRead ? "get;" : "set;");
                                
                                Debug.Log($"{indent}{subConnector}      │ │ {propConnector}─ [{propIndex:00}] {prop.Name}");
                                Debug.Log($"{indent}{subConnector}      │ │   类型: {prop.PropertyType.Name} | 访问器: [{readWrite}] | 值: {valueStr}");
                            }
                            catch (Exception ex)
                            {
                                Debug.Log($"{indent}{subConnector}      │ │ {propConnector}─ [{propIndex:00}] {prop.Name} ({prop.PropertyType.Name}): [读取错误: {ex.Message}]");
                            }
                        }
                    }
                    
                    Debug.Log($"{indent}{subConnector}      │ └─ 属性列表结束");
                }
                
                // 3. 方法 (Methods) - 先显示重要方法，再显示其他方法
                var allMethods = compType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName).ToArray();
                stats.methodCount += allMethods.Length;
                if (allMethods.Length > 0)
                {
                    // 分离重要方法和普通方法
                    var importantMethods = allMethods.Where(m => ImportantKeywords.Any(kw => m.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    var normalMethods = allMethods.Where(m => !ImportantKeywords.Any(kw => m.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    
                    Debug.Log($"{indent}{subConnector}      │ ┌─ ⚙️ 方法 ({allMethods.Length}个)");
                    
                    int methodIndex = 0;
                    
                    // 先打印重要方法
                    if (importantMethods.Count > 0)
                    {
                        Debug.Log($"{indent}{subConnector}      │ │ ⭐ 重要方法 ({importantMethods.Count}个):");
                        foreach (var method in importantMethods)
                        {
                            methodIndex++;
                            bool isMethodLast = methodIndex == allMethods.Length;
                            string methodConnector = isMethodLast ? "└" : "├";
                            
                            try
                            {
                                var parameters = method.GetParameters();
                                string paramStr = parameters.Length > 0 
                                    ? string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))
                                    : "(无参数)";
                                
                                string accessLevel = method.IsPublic ? "public" : (method.IsPrivate ? "private" : "protected");
                                string staticMod = method.IsStatic ? "static " : "";
                                string returnType = method.ReturnType.Name;
                                
                                Debug.Log($"{indent}{subConnector}      │ │ {methodConnector}─ [{methodIndex:00}] ⭐ {accessLevel} {staticMod}{returnType} {method.Name}({paramStr})");
                            }
                            catch (Exception ex)
                            {
                                Debug.Log($"{indent}{subConnector}      │ │ {methodConnector}─ [{methodIndex:00}] ⭐ {method.Name}: [方法信息读取错误: {ex.Message}]");
                            }
                        }
                    }
                    
                    // 再打印普通方法
                    if (normalMethods.Count > 0)
                    {
                        if (importantMethods.Count > 0)
                        {
                            Debug.Log($"{indent}{subConnector}      │ │ ────────────────────────────────────────────────────────────────");
                        }
                        foreach (var method in normalMethods)
                        {
                            methodIndex++;
                            bool isMethodLast = methodIndex == allMethods.Length;
                            string methodConnector = isMethodLast ? "└" : "├";
                            
                            try
                            {
                                var parameters = method.GetParameters();
                                string paramStr = parameters.Length > 0 
                                    ? string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))
                                    : "(无参数)";
                                
                                string accessLevel = method.IsPublic ? "public" : (method.IsPrivate ? "private" : "protected");
                                string staticMod = method.IsStatic ? "static " : "";
                                string returnType = method.ReturnType.Name;
                                
                                Debug.Log($"{indent}{subConnector}      │ │ {methodConnector}─ [{methodIndex:00}] {accessLevel} {staticMod}{returnType} {method.Name}({paramStr})");
                            }
                            catch (Exception ex)
                            {
                                Debug.Log($"{indent}{subConnector}      │ │ {methodConnector}─ [{methodIndex:00}] {method.Name}: [方法信息读取错误: {ex.Message}]");
                            }
                        }
                    }
                    
                    Debug.Log($"{indent}{subConnector}      │ └─ 方法列表结束");
                }
                
                Debug.Log($"{indent}{subConnector}      └─ 组件详情结束");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{new string(' ', depth * 2)}组件详情读取错误: {ex.Message}");
            }
        }
    }
}

