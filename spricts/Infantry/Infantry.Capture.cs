using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Capture（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

    [ExportGroup("远程搭载")]
    [Export]
    public CaptureAbility captureAbility = CaptureAbility.CannotCapture;  // 默认不能占领
    
    [Export]
    public int capturePower = 10;  // 占领力，默认10（两回合）

    [Export]
    public bool allowCaptureAfterMove = true;  // 移动后是否允许占领（AW标准=true）


    // 当前占领进度（运行时）
    public int currentCaptureProgress = 0;
    public Grids capturingGrid = null;  // 正在占领的格子
    private CaptureEffect currentCaptureEffect;
    public virtual int GetCapturePower()
{
    if (health <= maxHealth)
    {
        float healthPerBar = maxHealth / 10f;
        // ✅ CO 模块系统·模块18（效率/速率：占领加成）
        return Mathf.Clamp(Mathf.RoundToInt(health / healthPerBar) + CoModuleSystem.GetCaptureBonus(this), 1, 10);
    }
    else
    {
        float healthPerBar = maxHealth / 10f;
        return Mathf.RoundToInt(health / healthPerBar) + CoModuleSystem.GetCaptureBonus(this);
    }
}

       public virtual bool CanCaptureCurrentGrid()
{
    if (captureAbility == CaptureAbility.CannotCapture)
        return false;
    
    if (grid == null)
        return false;
    
    if (grid.city == null)
        return false;
    
    // ✅ 关键：如果城市已被己方占领，不能再次占领
    if (grid.city.facilityTeam == team)
        return false;
    
    // ✅ 如果正在占领中，但占领的是当前格子，允许继续
    if (currentCaptureProgress > 0 && capturingGrid == grid)
        return true;
    
    // ✅ 如果其他己方单位正在占领，检查是否可以协助
    if (grid.city.capturingUnits.Count > 0)
    {
        var allyCapturing = grid.city.capturingUnits
            .FirstOrDefault(u => u.team == this.team && u != this);
        if (allyCapturing != null)
        {
            // 同队伍可以协助占领
            return true;
        }
    }
    
    return true;
}

    private bool isCapturingInProgress = false;
    public virtual void PerformCapture()
{
    if (isStealthed && !canCaptureWhileStealthed) return;   // ✅ 隐身占点锁

    if (isCapturingInProgress) return;
    
    if (!CanCaptureCurrentGrid())
        return;

    // 拆旗冻结：敌方撕旗者在场时，本楼占领进度冻结（只冻结不清零；遭遇清零由 PerformTearFlag 负责）
    if (grid != null && grid.city != null && grid.infantries != null &&
        grid.infantries.Any(u => u != this && u != null && IsInstanceValid(u) && u.team != this.team
            && u.tearingGrid == grid && u.currentTearProgress > 0))
    {
        ShowCaptureInterruptedEffect();
        return;
    }

    isCapturingInProgress = true;  // 设置标志
    
    try  // ✅ 添加 try-finally
    {
        int currentCapturePower = GetCapturePower();
        
        // 第一次占领这个格子
        if (capturingGrid != grid)
        {
            if (capturingGrid != null)
            {
                capturingGrid.city?.OnCaptureInterrupted(this);
            }
            
            currentCaptureProgress = 0;
            capturingGrid = grid;
            grid.city?.OnCaptureStarted(this);
        }
        
        var contestUnits = grid.infantries
            .Where(u => u != this 
                        && u != null
                        && IsInstanceValid(u)
                        && u.currentCaptureProgress > 0 
                        && u.capturingGrid == grid
                        && u.team != this.team)
            .ToList();
            
        if (contestUnits.Count > 0)
        {
            grid.city?.ShowCaptureContestVisual();
            currentCaptureProgress = 0;
            foreach (var enemy in contestUnits)
            {
                enemy.currentCaptureProgress = 0;
            }
            
            MarkAsCapturing();
            return;  // 这里return，但finally会执行！
        }
        
        currentCaptureProgress += currentCapturePower;
        
        ShowCaptureEffect();
        
        if (currentCaptureProgress >= (grid.city?.GetCurrentCaptureState() ?? 20))
        {
            CompleteCapture();
        }
        else
        {
            MarkAsCapturing();
        }
    }
    finally  // ✅ 确保标志总是重置
    {
        isCapturingInProgress = false;
    }
}


    protected virtual void CompleteCapture()
{
    // 远程占领时 capturingGrid=目标设施格；本格占领时为脚下格
    var targetGrid = (capturingGrid != null && IsInstanceValid(capturingGrid)) ? capturingGrid : grid;
    if (targetGrid?.city == null) return;

    string oldOwner = targetGrid.city.facilityTeam ?? "";

    // ✅ 实际改变城市归属
    targetGrid.city.facilityTeam = team;
    // 占领后状态（开创人2026-11-11 Warside 2HP）：-1=回满(现状)；>=0=新归属初始防御值，之后每回合按 captureStateRegenPerTurn 回满
    targetGrid.city.currentCaptureState = targetGrid.city.captureStateAfterCapture >= 0
        ? targetGrid.city.captureStateAfterCapture
        : targetGrid.city.capturePointsRequired;

    // 显示占领完成特效
    ShowCaptureCompleteEffect();

    // 重置占领状态
    currentCaptureProgress = 0;
    capturingGrid = null;

    // 标记为已完全行动
    isMoved = true;
    isAttacked = true;
    state = UnitState.Acted;
    originalGrid = null;

    // 视觉变暗
    SetWaitVisual(true);

    // 通知刷新城市视觉
    targetGrid.city?.UpdateCityVisual();

    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    gm?.CheckVictoryCondition();
}

    protected virtual void ShowCaptureInterruptedEffect()
    {
        var effect = new CaptureInterruptedEffect
        {
            Position = GlobalPosition
        };
        GetTree().CurrentScene.AddChild(effect);
    }

    protected virtual void MarkAsCapturing()
    {
        // 关键：占领中也算已行动，不能移动/攻击
        isMoved = true;
        isAttacked = true;
        state = UnitState.Acted;
        originalGrid = null;
        
        // 视觉变暗（但可能用不同颜色表示占领中？）
        FreezeToIdleDark();
    }

    protected virtual void ShowCaptureEffect()
    {
        // 清理旧特效
        if (currentCaptureEffect != null && IsInstanceValid(currentCaptureEffect))
        {
            currentCaptureEffect.QueueFree();
            currentCaptureEffect = null;
        }
        
        // ✅ 计算进度百分比（0-100）
        float progressPercent = Mathf.Min(100f, (currentCaptureProgress / (float)(grid.city?.GetCurrentCaptureState() ?? 20)) * 100f);
        
        currentCaptureEffect = new CaptureEffect
        {
            CapturePower = GetCapturePower(), 
            CaptureProgress = progressPercent,  // 传递正确的进度
            TeamColor = GetTeamColor(),
            IsCapturing = true,
            Position = new Vector2(0, -20)
        };
        AddChild(currentCaptureEffect);
    }

    protected virtual void ShowCaptureCompleteEffect()
    {
        if (currentCaptureEffect != null && IsInstanceValid(currentCaptureEffect))
        {
            currentCaptureEffect.QueueFree();
            currentCaptureEffect = null;
        }
        
        var effect = new CaptureEffect
        {
            CaptureProgress = 100f,
            TeamColor = GetTeamColor(),
            IsCapturing = false,  // 完成状态
            Position = new Vector2(0, -20)
        };
        AddChild(effect);
        // 完成特效不记录引用，让它自己消失
    }

    // ==================== 舱内占领（内部单位操作 · 统一进度池）====================
    // 开创人规格 20/5：占领力 × loadedOperationFactor；进度池挂运输车（车毁人亡池亡，下车无缝续占）

    [ExportGroup("内部单位操作 · 占领进度池")]
    [Export] public int loadedCaptureProgress = 0;                 // 舱内占领统一进度池
    public Grids loadedCapturingGrid = null;

    /// <summary>舱内第一个可占领单位（步兵/机甲等）</summary>
    public virtual Infantry GetFirstCapturableLoadedUnit()
        => transportedUnits?.FirstOrDefault(u => u != null && IsInstanceValid(u)
             && u.captureAbility != CaptureAbility.CannotCapture);

    /// <summary>选中运输车时占领按钮亮起条件（条款1/2/3/4 + 撕旗冻结）</summary>
    public virtual bool CanPerformLoadedCapture()
    {
        if (!canOperateLoadedUnits) return false;
        if (IsActionBlacklisted("Capture")) return false;
        if (GetRemainingLoadedOperations() <= 0) return false;
        var loaded = GetFirstCapturableLoadedUnit();
        if (loaded == null || loaded.isAttacked) return false;
        if (grid == null || grid.city == null) return false;
        if (grid.city.facilityTeam == team) return false;
        if (grid.infantries != null && grid.infantries.Any(u => u != null && IsInstanceValid(u)
            && u.team != team && u.tearingGrid == grid && u.currentTearProgress > 0)) return false;
        return true;
    }

    /// <summary>执行舱内占领：进度写入统一池（多个步兵合并到同一进度），完成归属运输车势力</summary>
    public virtual void PerformLoadedCapture()
    {
        var loaded = GetFirstCapturableLoadedUnit();
        if (!CanPerformLoadedCapture()) return;
        if (grid == null || grid.city == null) return;

        int power = ApplyLoadedFactor(loaded.GetCapturePower());   // 条款4：10 → 5（20点建筑4回合）

        if (loadedCapturingGrid != grid)                            // 统一池换建筑清零重记
        {
            loadedCaptureProgress = 0;
            loadedCapturingGrid = grid;
            grid.city?.OnCaptureStarted(loaded);
        }

        var contest = grid.infantries.Where(u => u != null && IsInstanceValid(u)
            && u.currentCaptureProgress > 0 && u.capturingGrid == grid && u.team != team).ToList();
        if (contest.Count > 0)
        {
            loadedCaptureProgress = 0;
            foreach (var enemy in contest) enemy.currentCaptureProgress = 0;
            grid.city?.ShowCaptureContestVisual();
        }

        loadedCaptureProgress += power;
        bool completed = loadedCaptureProgress >= (grid.city?.GetCurrentCaptureState() ?? 20);

        // 借道原生完成/特效逻辑：池子临时灌进运输车字段（归属=运输车势力）
        int keepP = currentCaptureProgress; var keepG = capturingGrid;
        currentCaptureProgress = loadedCaptureProgress; capturingGrid = grid;
        if (completed) CompleteCapture(); else ShowCaptureEffect();
        currentCaptureProgress = keepP; capturingGrid = keepG;

        if (completed) { loadedCaptureProgress = 0; loadedCapturingGrid = null; }

        loaded.MarkAsCapturing();
        ConsumeLoadedOperation();
    }

    // ==================== 远程占领（开创人2026-11-11）====================

    /// 扫描全图：远程可占领设施（只看设施判定：开关＋n-m 环带＋敌对＋未冻结）。
    public List<Facility> GetRemoteCapturableFacilities()
    {
        var result = new List<Facility>();
        if (!canCaptureFacilities || isCapturingInProgress || grid == null) return result;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var grids = gm?.gridManager?.grids;
        if (grids == null) return result;
        foreach (var g in grids)
        {
            var city = g.city;
            if (city == null || !IsInstanceValid(city)) continue;
            if (!city.remoteCaptureEnabled || city.capturePointsRequired <= 0) continue;
            if (city.facilityTeam == team) continue;
            int dist = Math.Abs(g.GridIndex.X - grid.GridIndex.X) + Math.Abs(g.GridIndex.Y - grid.GridIndex.Y);
            if (dist == 0 || dist < city.captureRangeMin || dist > city.captureRangeMax) continue;
            if (g.infantries != null && g.infantries.Any(u => u != null && IsInstanceValid(u)
                    && u.team != team && u.currentTearProgress > 0 && u.tearingGrid == g)) continue;   // 拆旗冻结
            result.Add(city);
        }
        return result;
    }

    public bool CanRemoteCapture() => GetRemoteCapturableFacilities().Count > 0;

    /// 远程占领一次结算：人不用站上设施，占领力打进设施格。遭遇/冻结规则与 PerformCapture 逐条一致。
    public void PerformRemoteCapture(Facility city)
    {
        if (city == null || !IsInstanceValid(city)) return;
        var target = city.ParentGrid;
        if (target == null || !IsInstanceValid(target)) return;
        if (isCapturingInProgress) return;

        isCapturingInProgress = true;
        try
        {
            if (target.infantries != null && target.infantries.Any(u => u != null && IsInstanceValid(u)
                    && u != this && u.team != team && u.currentTearProgress > 0 && u.tearingGrid == target))
            { ShowCaptureInterruptedEffect(); return; }   // 拆旗冻结

            var contest = target.infantries.Where(u => u != null && IsInstanceValid(u) && u != this
                && u.currentCaptureProgress > 0 && u.capturingGrid == target && u.team != team).ToList();
            if (contest.Count > 0)
            {
                city.ShowCaptureContestVisual();
                currentCaptureProgress = 0;
                foreach (var e in contest) e.currentCaptureProgress = 0;
                capturingGrid = target;
                if (!city.capturingUnits.Contains(this)) city.capturingUnits.Add(this);
                MarkAsCapturing();
                return;
            }

            if (capturingGrid != target)
            {
                capturingGrid?.city?.OnCaptureInterrupted(this);
                currentCaptureProgress = 0;
                capturingGrid = target;
                if (!city.capturingUnits.Contains(this)) city.capturingUnits.Add(this);
                city.OnCaptureStarted(this);
            }

            int captureValue = (int)Math.Floor(capturePower * capturePowerFactor);
            captureValue = Math.Max(1, captureValue);
            currentCaptureProgress += captureValue;
            ShowCaptureEffect();

            if (currentCaptureProgress >= city.GetCurrentCaptureState()) CompleteCapture();
            else MarkAsCapturing();
        }
        finally { isCapturingInProgress = false; }
    }
}
