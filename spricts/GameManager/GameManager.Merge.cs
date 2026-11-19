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
    [ExportGroup("合流·舱内乘客策略（开创人2026-11-19）")]
    [Export(PropertyHint.Enum, "先卸后融,拒融,保留乘客")]
    public int mergeLoadedPolicy = 1;                          // 0=先卸后融 1=拒融(默认=旧行为) 2=保留乘客
    [Export(PropertyHint.Enum, "只要搭载即触发,仅冲突时触发")]
    public int mergeLoadedTrigger = 1;                          // 0=任一方有乘客即触发 1=合体装不下(超载/白名单)才触发
    public bool transportMergeOverflowLock = false;             // 运行时：保留策略后锁再装载（"我的仁慈"只给一次）
    [Export] public bool mergeOverflowAllowed = false;             // 合流结果可超上限（true=血量直接相加传递，无封顶无变现）
    [Export] public int mergeRefundPercent = 10;                   // 溢出变现率：每满血点返还造价百分比（10=原版；可负=反扣资金，可>10=超100%返还）
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
        // 动态封顶：上限 = 被融合单位【自身的 maxHealth】（非固定100）；overflowAllowed=true 则直接相加传递（无封顶无变现）
        int combinedHealth = source.health + target.health;
        if (source.mergeOverflowAllowed)
        {
            target.health = combinedHealth;
        }
        else
        {
            int maxHealthCap = Mathf.Max(1, target.maxHealth);
            int excessHealth = Mathf.Max(0, combinedHealth - maxHealthCap);
            target.health = Mathf.Min(maxHealthCap, combinedHealth);

            // 溢出变现：可负=反扣资金，可>10=超100%返还
            if (excessHealth > 0 && target.cost > 0)
            {
                int fundsGained = Mathf.RoundToInt((float)excessHealth / maxHealthCap * target.cost * source.mergeRefundPercent / 10f);
                int currentFunds = GetTeamFunds(target.team);
                SetTeamFunds(target.team, currentFunds + fundsGained);
            }
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

        // ✦舱内乘客·保留策略：target 吸收 source 全部乘客（可超载/违白名单——"我的仁慈"），锁再装载
        if ((source.transportedUnits?.Count ?? 0) > 0)
        {
            target.transportedUnits ??= new System.Collections.Generic.List<Infantry>();
            foreach (var p in source.transportedUnits)
            {
                if (p == null || !IsInstanceValid(p)) continue;
                target.transportedUnits.Add(p);
                p.isTransported = true;
            }
            source.transportedUnits.Clear();
            target.transportMergeOverflowLock = true;
            target.UpdateLoadedIcon();
        }

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

        // 隐身门（Export 化；原硬编码 stealthOverlapMode==1）
        if (source.stealthBlocksMerge && source.isStealthed) return false;
        if (target.stealthBlocksMerge && target.isStealthed) return false;

        // 运输门（✦2026-11-19 舱内乘客三策略，取代 mergeLoadedTransportBlocked 硬拒）：
        //   0=先卸后融（移动层接管卸载流程） 1=拒融 2=保留（MergeUnits 吸收乘客）
        if (AnyLoadedTransport(source, target) && GetMergeLoadedPolicy(source, target) == 1) return false;

        return true;
    }

    // ========== ✦舱内乘客策略（开创人2026-11-19）==========
    public bool AnyLoadedTransport(Infantry a, Infantry b)
        => ((a?.transportedUnits?.Count ?? 0) > 0) || ((b?.transportedUnits?.Count ?? 0) > 0);

    // 冲突 = 合体后 target 装不下全部乘客：数量超载 或 白名单不符
    public bool MergeLoadedConflict(Infantry source, Infantry target)
    {
        var all = (target?.transportedUnits ?? new System.Collections.Generic.List<Infantry>())
            .Concat(source?.transportedUnits ?? Enumerable.Empty<Infantry>())
            .Where(u => u != null && IsInstanceValid(u)).ToList();
        if (all.Count > Mathf.Max(0, target?.maxTransportCapacity ?? 0)) return true;
        var wl = target?.canTransportUnitTypes;
        if (wl != null && wl.Count > 0)
            foreach (var p in all)
                if (!wl.Contains(p.UnitTypeId) && !wl.Contains(p.TemplateId)) return true;
        return false;
    }

    // 裁决：-1=正常合流 0=先卸后融 1=拒融 2=保留
    public int GetMergeLoadedPolicy(Infantry source, Infantry target)
    {
        if (!AnyLoadedTransport(source, target)) return -1;
        bool triggered = source.mergeLoadedTrigger == 0 ? true : MergeLoadedConflict(source, target);
        if (!triggered) return -1;
        return source.mergeLoadedPolicy;
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
