using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · TearFlag（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{
    [ExportGroup("拆旗模块")]
    [Export] public bool canTearFlag = false;    // 拆旗开关：与占领并行的第二条进度
    [Export] public int tearFlagPower = 5;       // 拆旗力：建筑拆旗值=占领值(20) → 5=四回合；步兵10=两回合
    public int currentTearProgress = 0;          // 拆旗进度（镜像 currentCaptureProgress）
    public Grids tearingGrid = null;             // 正在拆的建筑格（镜像 capturingGrid）
    private bool isTearingInProgress = false;    // 重入锁（镜像 isCapturingInProgress）

    // ========== 拆旗模块（占领的镜像：第二条进度）==========
    /// <summary>拆旗合法性：开关 + 敌属建筑 + 非中立（中立旗已被撕过，无对象）。</summary>
    public virtual bool CanTearCurrentGrid()
    {
        if (!canTearFlag) return false;
        if (grid == null || grid.city == null) return false;
        if (isStealthed && !canCaptureWhileStealthed) return false;   // 沿用占楼隐身锁
        if (string.IsNullOrEmpty(grid.city.facilityTeam)) return false;
        if (grid.city.facilityTeam == team) return false;             // 自己的旗不拆
        if (grid.city.facilityTeam == TeamHelper.Player0) return false; // 中立旗：撕毁已完成
        return true;
    }

    /// <summary>
    /// 拆旗执行：进度 +tearFlagPower/回合；与占领互斥——
    /// ①敌方撕旗者在场时本楼占领进度冻结（PerformCapture 钩子）
    /// ②拆旗遭遇敌方占领时双方进度清零（"我得不到，你的旗也被撕毁"）
    /// 进度满（拆旗值=占领值）→ 建筑转中立。
    /// ⚠️ 美术急需：撕旗专属动画（当前复用占领表现）
    /// </summary>
    public virtual void PerformTearFlag()
    {
        if (isStealthed && !canCaptureWhileStealthed) return;   // 隐身拆旗锁（同占楼）
        if (isTearingInProgress) return;
        if (!CanTearCurrentGrid()) return;
        isTearingInProgress = true;
        try
        {
            int power = tearFlagPower;
            if (tearingGrid != grid)
            {
                if (tearingGrid != null) tearingGrid.city?.OnCaptureInterrupted(this);
                currentTearProgress = 0;
                tearingGrid = grid;
                grid.city?.OnCaptureStarted(this);   // 撕旗动画挂这里（复用开始钩子）
            }

            // 占拆遭遇：双方进度同归于尽
            var contestUnits = grid.infantries
                .Where(u => u != this && u != null && IsInstanceValid(u) && u.team != this.team
                    && u.currentCaptureProgress > 0 && u.capturingGrid == grid).ToList();
            if (contestUnits.Count > 0)
            {
                grid.city?.ShowCaptureContestVisual();
                currentTearProgress = 0;
                foreach (var enemy in contestUnits) enemy.currentCaptureProgress = 0;
                MarkAsCapturing();
                return;
            }

            currentTearProgress += power;
            ShowCaptureEffect();   // 复用表现；⚠️ 撕旗专属动画待美术
            if (currentTearProgress >= (grid.city?.capturePointsRequired ?? 20))
                CompleteTearFlag();
            else
                MarkAsCapturing();
        }
        finally { isTearingInProgress = false; }
    }

    /// <summary>拆满 → 建筑转中立（P0）：谁都别想有。</summary>
    protected virtual void CompleteTearFlag()
    {
        if (grid == null) return;
        if (grid.city != null)
            grid.city.facilityTeam = TeamHelper.Player0;   // 拆旗的终点不是易主，是剥夺
        ShowTearFlagEffect();   // 纯代码美术：程序化撕旗
        currentTearProgress = 0;
        tearingGrid = null;
        originalGrid = null;
        SetWaitVisual(true);
        grid.city?.UpdateCityVisual();
        gameManager?.CheckVictoryCondition();
        GD.Print($"[拆旗] {unitDisplayName} 撕毁旗帜：{grid.GridIndex} 现为中立");
    }

    /// <summary>
    /// 纯代码美术·撕旗：程序化旗面动画（零手绘资产）。
    /// 倒三角旗面 = 被撕裂的旗：蜷曲坠落 → 压扁 → 消散。打底沿用占领光效。
    /// </summary>
    protected virtual void ShowTearFlagEffect()
    {
        ShowCaptureCompleteEffect();   // 打底光效沿用占领同款
        if (grid?.city == null) return;
        var flag = new Polygon2D();
        flag.Polygon = new Vector2[] { new Vector2(-14, -26), new Vector2(14, -26), new Vector2(0, 4) };   // 倒三角=撕裂旗面
        flag.Color = new Color(0.9f, 0.15f, 0.1f, 0.95f);
        flag.Position = new Vector2(0, -30);
        grid.city.AddChild(flag);
        var tw = flag.CreateTween().SetParallel();
        tw.TweenProperty(flag, "rotation", Mathf.DegToRad(160), 1.1f).SetEase(Tween.EaseType.In);
        tw.TweenProperty(flag, "scale", new Vector2(0.15f, 0.05f), 1.1f).SetEase(Tween.EaseType.In);
        tw.TweenProperty(flag, "modulate:a", 0.0f, 1.2f);
        var tw2 = flag.CreateTween();
        tw2.TweenInterval(1.25f);
        tw2.TweenCallback(Callable.From(() => flag.QueueFree()));
    }

    // ==================== 舱内撕旗（遥控搭载模块 · 镜像原生）====================
    public virtual Infantry GetFirstTearCapableLoadedUnit()
        => transportedUnits?.FirstOrDefault(u => u != null && IsInstanceValid(u) && u.canTearFlag);

    public virtual bool CanPerformLoadedTearFlag()
    {
        if (!canOperateLoadedUnits) return false;
        if (IsActionBlacklisted("TearFlag")) return false;
        if (GetRemainingLoadedOperations() <= 0) return false;
        var loaded = GetFirstTearCapableLoadedUnit();
        if (loaded == null || loaded.isAttacked) return false;
        if (grid == null || grid.city == null || grid.city.facilityTeam == team) return false;
        return true;
    }

    public virtual void PerformLoadedTearFlag()
    {
        var loaded = GetFirstTearCapableLoadedUnit();
        if (!CanPerformLoadedTearFlag()) return;
        if (grid == null || grid.city == null) return;
        if (loaded.isStealthed && !loaded.canCaptureWhileStealthed) return;
        if (isTearingInProgress) return;

        int power = ApplyLoadedFactor(tearFlagPower);
        if (tearingGrid != grid)
        {
            if (tearingGrid != null) tearingGrid.city?.OnCaptureInterrupted(loaded);
            currentTearProgress = 0;
            tearingGrid = grid;
            grid.city?.OnCaptureStarted(loaded);
        }
        var contestUnits = grid.infantries
            .Where(u => u != null && IsInstanceValid(u) && u.team != team
                && u.currentCaptureProgress > 0 && u.capturingGrid == grid).ToList();
        if (contestUnits.Count > 0)
        {
            grid.city?.ShowCaptureContestVisual();
            currentTearProgress = 0;
            foreach (var enemy in contestUnits) enemy.currentCaptureProgress = 0;
            loaded.MarkAsCapturing();
            ConsumeLoadedOperation();
            return;
        }
        currentTearProgress += power;
        ShowCaptureEffect();
        if (currentTearProgress >= (grid.city?.capturePointsRequired ?? 20))
            CompleteTearFlag();
        else
            loaded.MarkAsCapturing();
        ConsumeLoadedOperation();
    }
}
