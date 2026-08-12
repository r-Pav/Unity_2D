using UnityEditor;
using UnityEngine;

/// <summary>
/// 敌人实时属性 Inspector — 覆盖全部 enemy 类型（Melee/Ranged/Boss 子类）。
/// Play 模式下在默认字段下方显示运行时终值：Lv 档 + 装备加成 + 管线后的实际数值，每帧刷新。
/// 用途：验证 enemy 捡装备后的属性变化（装备加成注入 StatModifierManager 的实时效果）。
/// </summary>
[CustomEditor(typeof(EnemyControllerBase), true)]
public class EnemyInspector : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();   // 默认序列化字段（Lv/config/0 哨兵字段）

        var enemy = (EnemyControllerBase)target;

        if (!Application.isPlaying)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("运行游戏后此处实时显示 enemy 属性终值（Lv 档 + 装备加成 + 管线）。", MessageType.Info);
            return;
        }

        // ── 运行时实时属性 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("实时属性（运行时）", EditorStyles.boldLabel);

        EnemyConfigSO so = enemy.Config;
        EditorGUILayout.LabelField("等级", enemy.Level.ToString());
        EditorGUILayout.LabelField("配置 SO", so != null ? $"{so.name} (Lv{enemy.Level}档)" : "无（走 Inspector/内置默认）");
        EditorGUILayout.LabelField("MaxHP（管线终值）", enemy.MaxHealth.ToString("F1"));
        EditorGUILayout.LabelField("当前 HP", enemy.CurrentHealth.ToString("F1"));
        EditorGUILayout.LabelField("攻击冷却", enemy.AttackCooldownDuration.ToString("F2"));
        EditorGUILayout.LabelField("攻击范围", $"宽 {enemy.AttackWidth:F1} × 高 {enemy.AttackHeight:F1}");
        EditorGUILayout.LabelField("检测范围", $"宽 {enemy.DetectionWidth:F1} × 高 {enemy.DetectionHeight:F1}");

        // 攻击力 / 移速（管线终值，装备加成后实时变化）
        var atk = enemy.GetComponent<EnemyMeleeAttack>();
        EditorGUILayout.LabelField("攻击力", atk != null ? atk.FinalDamage.ToString("F1") : "-");
        EditorGUILayout.LabelField("移速", enemy.CurrentMoveSpeed.ToString("F1"));

        EditorGUILayout.LabelField("状态", enemy.IsDead ? "死亡" : (enemy.IsInCombatState ? "战斗中" : "待机"));
        EditorGUILayout.LabelField("当前状态类", enemy.Fsm?.CurrentState?.GetType().Name ?? "-");

        // ── 装备 ──
        var equip = enemy.GetComponent<EnemyEquipment>();
        if (equip != null)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("装备", EditorStyles.boldLabel);
            var item = equip.GetEquippedItem();
            EditorGUILayout.LabelField("有装备", equip.HasEquipment.ToString());
            EditorGUILayout.LabelField("装备等级", equip.EquippedLevel.ToString());
            EditorGUILayout.LabelField("装备名", item != null ? item.DisplayName : "无");
        }

        // Play 模式每帧刷新（实时看变化）
        Repaint();
    }
}
