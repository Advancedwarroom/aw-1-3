using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Repair（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{


    // ========== ✅ 通用修复模块（RepairBoat / APC 等维修单位可用）==========
    [ExportGroup("修复模块")]
    [Export] public bool canRepair = false;                          // 是否可以修复
    public virtual bool CanPerformRepair => canRepair;               // ✅ 虚属性：子类可覆盖（解决字段隐藏问题）
    [Export] public int minRepairRange = 1;                          // 最小修复范围
    [Export] public int maxRepairRange = 1;                          // 最大修复范围
    [Export] public bool repairCostsFunds = false;                   // 修复是否消耗资金
    [Export] public int repairCostPerHp = 0;                         // 每点血量消耗资金
    [Export] public bool canExceedMaxHealth = false;                 // 是否能超越最大血量
    [Export] public int exceededMaxHealth = 100;                     // 超越后的新最大值
    [Export] public bool canRepairAfterMove = true;                  // 是否可以移动后修复
    [Export] public int repairAmountPerAction = 1;                   // 每次修复多少血量（可为负=扣血，可为0）
    [Export] public bool isAreaRepair = false;                       // 能否群体修复
    [Export] public int repairTargetMode = 1;                        // 0=所有 1=仅友方 2=仅敌方
    [Export] public bool autoRepairOnTurnStart = false;              // 每回合自动修复（不占用操作）
    [Export] public bool autoRepairAfterMove = false;                // 移动后自动修复（不占用操作）
    [Export] public bool autoSupplyOnTurnStart = false;              // 每回合自动补给（不占用操作）
    [Export] public bool supplyCostsFunds = false;                   // 补给是否消耗资金
    // ========== ✅ 通用修理模块（任何 canRepair 单位可用；原 RepairBoat 专属逻辑上移） ==========
    /// <summary>修理范围：[min,max] 环；repairTargetMode=3（仅自己）时只含自身格</summary>
    public virtual List<Grids> CalculateRepairRange()
    {
        var result = new List<Grids>();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || grid == null) return result;
        if (repairTargetMode == 3) { result.Add(grid); return result; }
        var allRange = gm.gridManager.FindRange(grid, maxRepairRange, false);
        if (minRepairRange > 1)
        {
            var blindZone = gm.gridManager.FindRange(grid, minRepairRange - 1, false);
            allRange = allRange.Where(g => !blindZone.Contains(g)).ToList();
        }
        return allRange.Where(g => g != grid).ToList();
    }

    /// <summary>目标是否符合修理模式：0=所有 1=仅友方 2=仅敌方 3=仅自己</summary>
    public bool IsRepairTargetAllowed(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit) || unit.isTransported) return false;
        bool isAlly = unit.team == team;
        switch (repairTargetMode)
        {
            case 1: if (!isAlly || unit == this) return false; break;
            case 2: if (isAlly) return false; break;
            case 3: if (unit != this) return false; break;
            default: if (unit == this) return false; break;
        }
        // 加血模式：满血（且不能超上限）无意义
        if (repairAmountPerAction > 0 && !canExceedMaxHealth && unit.health >= unit.maxHealth) return false;
        if (repairAmountPerAction == 0) return false;
        return true;
    }

    public virtual List<Infantry> GetRepairableTargets()
    {
        var result = new List<Infantry>();
        foreach (var g in CalculateRepairRange())
        {
            if (g == null) continue;
            foreach (var unit in g.infantries)
                if (IsRepairTargetAllowed(unit)) result.Add(unit);
        }
        return result;
    }

    /// <summary>执行修理：正值加血（可耗资），负值扣血（受伤现形按广义规则处理）</summary>
    public virtual bool PerformRepair(Infantry target)
    {
        if (target == null || !IsInstanceValid(target) || !CanPerformRepair) return false;
        if (!CalculateRepairRange().Contains(target.grid)) return false;
        if (!IsRepairTargetAllowed(target)) return false;

        int actualRepair = repairAmountPerAction;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (actualRepair > 0)
        {
            int oldHealth = target.health;
            int healCap = canExceedMaxHealth ? exceededMaxHealth : target.maxHealth;
            int newHealth = target.healthLocked ? target.health : Mathf.Min(healCap, target.health + actualRepair); // 力场锁血
            int actualHealed = newHealth - oldHealth;
            if (actualHealed <= 0) return false;
            if (repairCostsFunds && gm != null)
            {
                int cost = actualHealed * repairCostPerHp;
                int currentFunds = gm.GetTeamFunds(team);
                if (currentFunds < cost) return false; // 资金不足
                gm.SetTeamFunds(team, currentFunds - cost);
            }
            target.health = newHealth;
            target.UpdateHpLabel();
            ShowRepairEffect(target, actualHealed);
        }
        else
        {
            int damage = Mathf.Abs(actualRepair);
            target.health -= damage;
            target.UpdateHpLabel();
            ShowRepairEffect(target, -damage);
            if (target.health <= 0 && !target.TryFlee())
            {
                target.OnDestroyed();
                if (target.grid != null)   // 与 Attack 的死亡处理一致：从格子移除，避免残留引用
                {
                    target.grid.infantries.Remove(target);
                    if (target.grid.infantry == target)
                        target.grid.infantry = target.grid.infantries.Count > 0 ? target.grid.infantries[0] : null;
                    target.grid = null;
                }
                gm?.RemovePiece(target);
                target.CallDeferred(Node.MethodName.QueueFree);
            }
        }
        return true;
    }

    public virtual void PerformAreaRepair()
    {
        if (!isAreaRepair) return;
        foreach (var target in GetRepairableTargets()) PerformRepair(target);
    }

    /// <summary>自动修理（回合开始 / 移动后），不占用操作</summary>
    public virtual void PerformAutoRepair()
    {
        if (!CanPerformRepair) return;
        if (isAreaRepair) PerformAreaRepair();
        else
        {
            var targets = GetRepairableTargets();
            if (targets.Count > 0) PerformRepair(targets.OrderBy(t => t.health).First());
        }
    }

    public virtual void OnTurnStartAutoRepair()
    {
        if (!autoRepairOnTurnStart) return;
        PerformAutoRepair();
    }

    // 移动后自动修理：移动标记 + 本回合行动结束（state=Acted）时触发一次，避免撤销移动刷血
    public bool movedThisTurnForAutoRepair = false;
    private bool autoRepairAfterMoveDone = false;

    protected void ShowRepairEffect(Infantry target, int amount)
    {
        var label = new Label();
        label.Text = amount > 0 ? $"+{amount}" : $"{amount}";
        label.Modulate = amount > 0 ? new Color(1, 0.4f, 0.7f) : new Color(0.8f, 0.2f, 0.2f);
        label.Position = target.GlobalPosition + new Godot.Vector2(0, -20);
        label.ZIndex = 300;
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeConstantOverride("outline_size", 3);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        GetTree()?.CurrentScene?.AddChild(label);
        var tween = CreateTween();
        tween.TweenProperty(label, "position", label.Position + new Godot.Vector2(0, -40), 1.2f);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.5f);
        tween.TweenCallback(Callable.From(() => label.QueueFree()));
    }
}
