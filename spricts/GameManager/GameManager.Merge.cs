using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Merge —— 单位合流全 Export 版
// 开创人 2026-11-19 定案：
//   ① 运输门：废类型硬编码 → 仅"载有单位"才禁（对齐原作 empty-transports 可合流），Export 可调
//   ② 燃料弹药：mergeResourceMode 0=合计封顶(原版) / 1=取最大值(旧行为)
//   ③ 占领/撕旗进度保留：双 Export（原版：占领进度经合流保留，压楼合流续压）
//   ④ 隐身门：废 stealthOverlapMode==1 硬编码 → stealthBlocksMerge Export
//   ⑤ 交互：真人先弹「合流/取消」确认框（GridManager 调用 PromptMergeConfirmation），AI 直合流
public partial class Infantry
{
    [ExportGroup("合流模块（Join）")]
    [Export] public bool mergePreserveCaptureProgress = true;    // 合流保留占领进度（原版行为）
    [Export] public bool mergePreserveTearFlagProgress = true;   // 合流保留撕旗进度
    [Export(PropertyHint.Enum, "合计封顶(原版),取最大值")]
    public int mergeResourceMode = 0;                            // 燃料/弹药合流方式
    [Export] public bool stealthBlocksMerge = true;              // 隐身单位禁止合流（原硬编码 stealthOverlapMode==1）
    [Export] public bool mergeLoadedTransportBlocked = true;       // 载有单位的运输单位禁止合流（原版：空载可合流）
}

public partial class GameManager
{
    // ========== 资源结算：0=合计封顶(原版) / 1=取最大值；无上限单位取大值 ==========
    private static int MergeResourceValue(int mode, int a, int b, int max)
    {
        if (mode == 1 || max <= 0) return Mathf.Max(a, b);
        return Mathf.Min(Mathf.Max(1, max), a + b);
    }

    // ========== ✅ 合流执行：两个同类型友方单位合并 ==========
    public void MergeUnits(Infantry source, Infantry target)
    {
        if (source == null || target == null) return;
        if (!CanMerge(source, target)) return;   // ✅ 统一走 CanMerge（含全部 Export 门）

        // ① 血量合计封顶 + 溢出按满血比例变现（等价原版每点 10% 造价）
        int combinedHealth = source.health + target.health;
        int maxHealthCap = Mathf.Max(1, target.maxHealth);
        int excessHealth = Mathf.Max(0, combinedHealth - maxHealthCap);
        target.health = Mathf.Min(maxHealthCap, combinedHealth);

        if (excessHealth > 0 && target.cost > 0)
        {
            int fundsGained = Mathf.RoundToInt((float)excessHealth / maxHealthCap * target.cost);
            int currentFunds = GetTeamFunds(target.team);
            SetTeamFunds(target.team, currentFunds + fundsGained);
        }

        // ② 燃料 + 八弹种（mergeResourceMode 见 Infantry 导出）
        int rm = source.mergeResourceMode;
        target.fuel = MergeResourceValue(rm, source.fuel, target.fuel, target.maxFuel);
        target.currentPrimaryAmmo = MergeResourceValue(rm, source.currentPrimaryAmmo, target.currentPrimaryAmmo, target.maxPrimaryAmmo);
        target.currentExplodeAmmo = MergeResourceValue(rm, source.currentExplodeAmmo, target.currentExplodeAmmo, target.maxExplodeAmmo);
        target.currentFlareAmmo = MergeResourceValue(rm, source.currentFlareAmmo, target.currentFlareAmmo, target.maxFlareAmmo);
        target.currentStealthBullets = MergeResourceValue(rm, source.currentStealthBullets, target.currentStealthBullets, target.maxStealthBullets);
        target.currentRevealBullets = MergeResourceValue(rm, source.currentRevealBullets, target.currentRevealBullets, target.maxRevealBullets);
        target.currentCamoBullets = MergeResourceValue(rm, source.currentCamoBullets, target.currentCamoBullets, target.maxCamoBullets);
        target.currentProductionAmmo = MergeResourceValue(rm, source.currentProductionAmmo, target.currentProductionAmmo, target.maxProductionAmmo);
        target.currentConstructionAmmo = MergeResourceValue(rm, source.currentConstructionAmmo, target.currentConstructionAmmo, target.maxConstructionAmmo);

        // ③ 占领/撕旗进度：source 有进度且 target 空闲时接管（压楼合流续压）
        if (source.mergePreserveCaptureProgress && source.currentCaptureProgress > 0 && target.currentCaptureProgress <= 0)
        {
            target.currentCaptureProgress = source.currentCaptureProgress;
            target.capturingGrid = source.capturingGrid;
        }
        if (source.mergePreserveTearFlagProgress && source.currentTearProgress > 0 && target.currentTearProgress <= 0)
        {
            target.currentTearProgress = source.currentTearProgress;
            target.tearingGrid = source.tearingGrid;
        }

        // ④ source 清理占领/撕旗占用登记后离场（防幽灵 capturingUsers/tearingUsers）
        if (source.capturingGrid?.city != null) source.capturingGrid.city.OnCaptureInterrupted(source);
        if (source.tearingGrid?.city != null) source.tearingGrid.city.OnCaptureInterrupted(source);
        source.currentCaptureProgress = 0;
        source.currentTearProgress = 0;
        source.capturingGrid = null;
        source.tearingGrid = null;

        target.UpdateHpLabel();

        RemoveUnit(source);
        UpdateUnitLists();

        // ⑤ 行动状态：canActAfterMerge=true 白赚全新行动；否则 AW 基准（本回合不可行动）
        if (target.canActAfterMerge)
        {
            target.OnTurnEnd();
        }
        else
        {
            target.isMoved = true;
            target.isAttacked = true;
            target.hasActed = true;
            target.state = UnitState.Acted;
            target.SetWaitVisual(true);
        }
    }

    // ========== ✅ 合流合法性（全 Export 门集中在此） ==========
    public bool CanMerge(Infantry source, Infantry target)
    {
        if (source == null || target == null) return false;
        if (!source.canMerge || !target.canMerge) return false;
        if (source.GetType() != target.GetType()) return false;
        if (source.team != target.team) return false;
        if (source == target) return false;
        if (target.health >= target.maxHealth) return false;   // AW 规则：目标必须残血

        // 隐身门（Export 化；原硬编码 stealthOverlapMode==1）
        if (source.stealthBlocksMerge && source.isStealthed) return false;
        if (target.stealthBlocksMerge && target.isStealthed) return false;

        // 运输门：仅"载有单位"才禁（对齐原作：空载运输单位可合流；原 RepairBoat 写死例外由本规则取代）
        if (source.mergeLoadedTransportBlocked && (source.transportedUnits?.Count ?? 0) > 0) return false;
        if (target.mergeLoadedTransportBlocked && (target.transportedUnits?.Count ?? 0) > 0) return false;

        return true;
    }

    // ========== ✅ 真人「合流/取消」确认框（开创人2026-11-19：出按钮，不要直接） ==========
    // GridManager 在移动前调用：确认→onConfirm（通常为 MoveTo，落点自动合流）；取消→onCancel（原地不动）。
    public void PromptMergeConfirmation(Infantry source, Infantry target, Node dialogParent, Action onConfirm, Action onCancel = null)
    {
        var dlg = new ConfirmationDialog
        {
            DialogText = $"是否与 {target.unitDisplayName}（{target.health}/{target.maxHealth}）合流？\n（确认=移动并合流；取消=原地不动）",
            OkButtonText = "合流",
            CancelButtonText = "取消"
        };
        dialogParent.AddChild(dlg);
        dlg.Confirmed += () =>
        {
            onConfirm?.Invoke();
            dlg.QueueFree();
        };
        dlg.Canceled += () =>
        {
            onCancel?.Invoke();
            dlg.QueueFree();
        };
        dlg.PopupCentered();
    }
}
