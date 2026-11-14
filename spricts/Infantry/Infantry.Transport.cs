using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Transport（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

[ExportGroup("搭载系统")]
[Export] public bool canTransportUnits = false;
[Export] public bool canSupplyUnits = false;  // 能否补给其他单位（独立于搭载系统）

[Export] public int maxTransportCapacity = 0;
[Export] public Godot.Collections.Array<string> canTransportUnitTypes = new();
[Export] public bool canLaunchCargo = false;   // ✅ 舰载起飞（DoR 航母/生产舰）：回合开始时已在舱内的单位，载具本回合未移动时可直接“起飞”到相邻格并正常行动；起飞后载具本回合不能再移动
[Export] public bool unloadCanCrossScreens = false; // 卸载相邻格是否允许跨屏
[Export] public int maxLoadCapacity { get => maxTransportCapacity; set => maxTransportCapacity = value; }
[Export] public int minSupplyRange = 1;
[Export] public int maxSupplyRange = 1;
[Export] public bool canSupplyAfterMove = true;

// ✅ 新增：按单位类别过滤（在Inspector中勾选可搭载的类别）
[Export] public Godot.Collections.Array<UnitCategory> canTransportUnitCategories = new();

// ✅ 新增：按属性过滤（勾选即可生效，零代码适配新单位）
[Export] public bool transportExcludeAirUnits = false;      // 排除空军单位
[Export] public bool transportExcludeArmored = false;      // 排除装甲单位
[Export] public bool transportRequireCanCapture = false;   // 只能运输可占领单位
[Export] public int transportMaxMovePoints = 0;            // 最大移动力限制（0=无限制）

[Export] public AnimatedSprite2D loadedIcon;
[Export] public AnimatedSprite2D noFuelIcon;

// 状态
public bool isUnloading = false;

// 私有
private Tween loadedIconTween;


// ========== 新增：核心伤害公式 ==========




public List<Infantry> transportedUnits = new List<Infantry>();
	[Export]
	public bool isTransported = false;

    [Export]
    public AttackOverlapPolicy attackOverlap = AttackOverlapPolicy.AllowAttackEnemyOnSameTile;
    [Export]
    public UnitOverlapType overlapType = UnitOverlapType.NonOverlapping;   // ✅ 覆盖方式：NonOverlapping=不可重叠 / Overlapping=仅重叠 / Oozium=覆盖+吞噬(自动，无需canDevour)
    [Export]
    public bool canPassThroughEnemyUnits = false;   // ✅ 是否可穿过敌方单位：开=敌方不阻挡范围扩展/不把敌格踢出范围/不触发截断遭遇（可穿过不可停留，与友军规则一致，落点仍受CanStayOnGrid管）；关=原作式敌方阻挡
    [Export]
    public MoveType moveType = MoveType.Infantry;  
    // ✅ 单位级地形移动开销覆盖（优先于移动方式表与自定义移动方式）："地形=开销"，如 "BRIDGE=1"、"BEACH=1"；999=不可通行。留空=不覆盖
    [Export] public Godot.Collections.Array<string> terrainMoveCostOverrides = new();
    [Export]
    public UnitState state = UnitState.Idle;
    [Export]
    public GridManager gridManager;

    [Export]
    public AttackType attackType = AttackType.CanAttack;

    [Export]
    public int attackRange = 1;
    [Export] public int defaultMovePoints = 3;  // 默认移动力，Inspector可改
    [Export] public int movePoints = 3;  // 当前剩余移动力
    [Export]
    public int maxHealth = 100;
    [Export]
    public int health = 100;
    [Export]
    public int baseAttack = 100;
    [Export]
    public int baseDefense = 0;
    [Export] public int minAttackRange = 1;        // 最小攻击范围（盲区）
    [Export] public int maxAttackRange = 1;        // 最大攻击范围
    [Export] public bool useMinMaxAttackRange = false;  // 是否使用环形攻击范围
    [Export] public bool canAttackAfterMoving = true;   // 移动后能否攻击

    [Export] public AnimatedSprite2D noAmmoIcon;
    // ✅ 新增：通用弹药系统（所有单位可用）




    [Export] public int maxFuel = 99;
    [Export] public int fuel = 99;
    [Export] public bool consumeFuel = false;
    [Export] public int lowFuelThreshold = 15;
    [Export] public int lowAmmoThreshold = -1;               // ✅ 缺弹警告阈值：-1=自动(弹药上限/3,最少1)；0=永不警告；>0=固定值               // 低燃料阈值，低于此值闪烁 NoFuelIcon
    [Export] public int dailyFuelConsumption = 0;          // 每回合开始自动消耗的燃料
    [Export] public bool destroyOnOutOfFuel = false;       // 燃料耗尽后是否自毁

[ExportGroup("远程搭载")]
[Export] public bool CanFarMounting = false;
[Export] public int minFarMountingDistance = 0;    // 远程搭载最小距离（0=无限制，可贴身上船）
[Export] public int maxFarMountingDistance = 1;    // 远程搭载最大距离（原 FarMountingDistance） 

    // ========== ✅ 究极自由：搭载系统方法 ==========

    /// <summary>
    /// 检查是否可以搭载指定单位
    /// </summary>
    /// <summary>
    /// 检查是否可以搭载指定单位（模块化：通过Inspector配置，零代码适配新单位）
    /// </summary>
    public virtual bool CanTransportUnit(Infantry unit)
    {
        if (!canTransportUnits || maxTransportCapacity <= 0) return false;
        if (unit == null || unit == this || !IsInstanceValid(unit) || unit.isTransported) return false;
        if (transportedUnits.Count >= maxTransportCapacity) return false;
        // 同队或盟军可搭；P-1 公共队由 TeamHelper 统一视为盟军关系
        if (!TeamHelper.IsFriendlyTeam(this.team, unit.team) && !TeamHelper.IsAlliedTeam(this.team, unit.team)) return false;
        // 载着人的运输单位不能再被装（套娃会丢失内部单位）
        if (unit.canTransportUnits && unit.transportedUnits.Count > 0) return false;

        // ✅ issue#1 二期（开创人2026-11-15 拍板）：废除多格双向禁运——多格可运可载，装卸时足印随迁；容量按单位数计（多格=1席）

        // 1. 显式白名单（兵种ID 或 参照兵种ID 命中均可）——白名单是权威：命中即放行，不再被属性过滤误伤
        //    （此前航母白名单全是空军却勾了“排除空军”，导致什么都装不了）
        bool hasWhitelist = canTransportUnitTypes != null && canTransportUnitTypes.Count > 0;
        if (hasWhitelist)
            return canTransportUnitTypes.Contains(unit.UnitTypeId) || canTransportUnitTypes.Contains(unit.TemplateId);

        // 2. 未配白名单 → 按单位类别过滤
        if (canTransportUnitCategories != null && canTransportUnitCategories.Count > 0)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm != null && gm.unitCategories.TryGetValue(unit, out var category))
            {
                if (!canTransportUnitCategories.Contains(category)) return false;
            }
        }

        // 3. 按属性标志过滤（零代码扩展新单位；只在无白名单时生效）
        if (transportExcludeAirUnits && unit.IsAirUnit) return false;
        if (transportExcludeArmored && unit.isArmoredUnit) return false;
        if (transportRequireCanCapture && unit.captureAbility != CaptureAbility.CanCapture) return false;
        if (transportMaxMovePoints > 0 && unit.defaultMovePoints > transportMaxMovePoints) return false;

        return true;
    }

    /// <summary>统一搭载查找：同队或盟军运输单位，只返回确实能装下 rider 的对象。</summary>
    public static Infantry FindTransportFor(System.Collections.Generic.IEnumerable<Infantry> units, Infantry rider)
    {
        if (units == null || rider == null) return null;
        foreach (var u in units)
        {
            if (u == null || u == rider || !IsInstanceValid(u) || !u.CanTransportUnit(rider)) continue;
            if (TeamHelper.IsFriendlyTeam(rider.team, u.team) || TeamHelper.IsAlliedTeam(rider.team, u.team)) return u;
        }
        return null;
    }

    /// <summary>
    /// 搭载单位
    /// </summary>
    public virtual bool TransportUnit(Infantry unit) => LoadUnit(unit);   // ✅ 统一走 LoadUnit（原实现漏设 isTransported）

    // ========== ✅ 舰载起飞：记录“本回合才进舱”的单位（它们本回合不能起飞） ==========
    private readonly System.Collections.Generic.HashSet<Infantry> cargoBoardedThisTurn = new();
    public void MarkCargoBoardedThisTurn(Infantry u) { if (u != null) cargoBoardedThisTurn.Add(u); }
    public void ResetCargoBoardTracking()
    {
        cargoBoardedThisTurn.Clear();
        ResetLoadedOperationTracking();   // ✅ 内部单位操作：操作数每回合重置
    }   // 回合开始时调用
    /// <summary>此舱内单位当前能否起飞（载具开启舰载起飞、本回合未移动、单位不是本回合才进舱的）</summary>
    public bool CanLaunchCargoUnit(Infantry u)
        => canLaunchCargo && u != null && transportedUnits.Contains(u) && !isMoved && !cargoBoardedThisTurn.Contains(u);

    /// <summary>✅ 起飞：卸到相邻格但保留完整行动（不受“卸下后本回合不能动”限制）</summary>
    public bool LaunchCargoUnit(Infantry u, Grids targetGrid)
    {
        if (!CanLaunchCargoUnit(u)) return false;
        if (!UnloadUnit(u, targetGrid)) return false;
        u.isMoved = false;
        u.isAttacked = false;
        u.hasActed = false;
        u.state = UnitState.Idle;
        u.SetWaitVisual(false);
        unitsCannotMove.Remove(u);
        isMoved = true;   // 载具起飞后本回合不能再移动（可继续让其他舰载机起飞/攻击）
        return true;
    }

// ========== ✅ 究极自由：通用装载方法（从 APC 迁移） ==========
public virtual bool LoadUnit(Infantry unit)
{
        if (isStealthed && !canLoadUnloadWhileStealthed) return false; // ✅ 隐身行为锁
    if (!CanTransportUnit(unit)) return false;
    
    // ✅ 彻底停止所有视觉组件的动画残留（sprite + animSprite + 嵌套子节点）
    StopAllVisuals(unit);
    
    transportedUnits.Add(unit);
    unit.isTransported = true;
    ApplyInternalExportService(unit);
    MarkCargoBoardedThisTurn(unit);
    
    unit.Hide();
    unit.ProcessMode = ProcessModeEnum.Disabled;
    
    if (unit.grid != null)
    {
        unit.grid.infantries.Remove(unit);
        if (unit.grid.infantry == unit)
            unit.grid.infantry = unit.grid.infantries.Count > 0 ? unit.grid.infantries[0] : null;
        unit.grid = null; // ✅ 彻底断开与格子的关联
    }
    
    unit.ClearFootprint();   // ✅ issue#1 二期：多格乘员装载时清足印（禁运已废除，侧格释放）
    
    UpdateLoadedIcon();
    
    // ✅ 装载后刷新迷雾，确保被装载单位正确隐藏
    var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
    gm?.fogOfWarManager?.OnUnitMoved();
    
    return true;
}

    // ✅ 彻底停止并重置单位的所有视觉组件（防止装载后残留动画）
    private void StopAllVisuals(Infantry unit)
    {
        unit.breathTween?.Kill();
        unit.breathTween = null;
        
        void ResetNode(Node node)
        {
            if (node is Sprite2D sp && IsInstanceValid(sp))
            {
                sp.Scale = Vector2.One;
                sp.Modulate = Colors.White;
                sp.Hide();
            }
            else if (node is AnimatedSprite2D anim && IsInstanceValid(anim))
            {
                anim.Scale = Vector2.One;
                anim.Modulate = Colors.White;
                anim.Stop();
                anim.Hide();
            }
            else if (node is Label lbl && IsInstanceValid(lbl))
            {
                lbl.Hide();
            }
            foreach (var child in node.GetChildren())
                ResetNode(child);
        }
        
        ResetNode(unit);
        unit.Hide();
    }
    
    // ✅ 恢复单位的所有视觉组件（卸载后调用）
    private void RestoreAllVisuals(Infantry unit)
    {
        void RestoreNode(Node node)
        {
            if (node is Sprite2D sp && IsInstanceValid(sp))
            {
                sp.Modulate = Colors.White;
            }
            else if (node is AnimatedSprite2D anim && IsInstanceValid(anim))
            {
                anim.Modulate = Colors.White;
            }
            foreach (var child in node.GetChildren())
                RestoreNode(child);
        }
        
        RestoreNode(unit);
        unit.SetWaitVisual(false);
        unit.StartBreath();
    }

// ========== ✅ 究极自由：通用卸载方法（从 APC 迁移） ==========
public virtual bool UnloadUnit(Infantry unit, Grids targetGrid)
{
        if (isStealthed && !canLoadUnloadWhileStealthed) return false; // ✅ 隐身行为锁
    if (!transportedUnits.Contains(unit)) return false;
    if (targetGrid == null || grid == null) return false;
    if (targetGrid.gridType == GridType.METEORITE) return false;
    int unloadDx = Mathf.Abs(targetGrid.GridIndex.X - grid.GridIndex.X), unloadDy = Mathf.Abs(targetGrid.GridIndex.Y - grid.GridIndex.Y);
    var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
    if (Math.Max(unloadDx, unloadDy) != 1 || gm?.gridManager == null || !gm.gridManager.IsRangeAllowed(grid, targetGrid, unloadCanCrossScreens)) return false;

    // ✅ 开创人2026-11-11 拍板：废除 P-1 公共运输卸载限制——共用运输可装即可卸，无势力门禁

    
    var blockingUnit = targetGrid.infantries.FirstOrDefault(u =>
        u != null && IsInstanceValid(u) &&
        u.overlapType == UnitOverlapType.NonOverlapping);
    if (blockingUnit != null)
    { ScreenErrorLogger.Diag($"[卸载] 拒绝：目标格被非重叠单位 {blockingUnit.Name}({blockingUnit.team}) 阻挡"); return false; }

    // ✅ issue#1 二期（开创人2026-11-15 拍板）：多格乘员可卸载——朝向回退(当前落不下自动换朝向) + 整足印预检 + 侧格占用裁定
    if (unit.isMultiTileUnit)
    {
        int fitFacing = unit.FindFittingFacing(targetGrid.GridIndex, gm);
        if (fitFacing < 0) return false;
        unit.unitFacing = fitFacing;
        unit.ApplyFacingVisual();
        if (!unit.CheckFootprintFit(targetGrid.GridIndex, gm, unit.unitFacing)) return false;
        foreach (var sideIdx in unit.GetFootprintIndices(targetGrid.GridIndex, unit.unitFacing))
        {
            if (sideIdx == targetGrid.GridIndex) continue;
            var sideGrid = gm.gridManager.map[sideIdx.X, sideIdx.Y];
            if (sideGrid == null || !unit.ResolveFootprintSideCell(sideGrid, gm)) return false;
        }
    }

    RemoveInternalExportService(unit);
    transportedUnits.Remove(unit);
    unit.isTransported = false;
    ScreenErrorLogger.Diag($"[卸载] ✓ {unit.Name}({unit.team}) 在 ({targetGrid.GridIndex.X},{targetGrid.GridIndex.Y}) 下船");
    
    unit.Show();
    unit.ProcessMode = ProcessModeEnum.Inherit;
    
    // ✅ 恢复被装载单位的所有视觉组件
    RestoreAllVisuals(unit);
    
    // ✅ 修复：若单位不在单位容器名下（如内部生产曾被挂在舰船名下），
    //    先移回单位容器，否则 unit.Position 是相对舰船的局部坐标，
    //    卸载后单位会飞到错误位置甚至图外（表现为没有贴图/凭空消失）
    if (gm?.unitManager?.units != null && unit.GetParent() != gm.unitManager.units)
    {
        Vector2 worldPos = unit.GlobalPosition;
        unit.GetParent()?.RemoveChild(unit);
        gm.unitManager.units.AddChild(unit);
        unit.GlobalPosition = worldPos;
    }
    
    unit.Position = targetGrid.Position;
    unit.grid = targetGrid;

        // ✅ 内部单位操作：车上统一占领进度池转移给下车单位（无缝续占）
        if (loadedCaptureProgress > 0 && loadedCapturingGrid != null
            && unit.captureAbility != CaptureAbility.CannotCapture)
        {
            unit.currentCaptureProgress = loadedCaptureProgress;
            unit.capturingGrid = loadedCapturingGrid;
            loadedCaptureProgress = 0;
            loadedCapturingGrid = null;
        }
    
    if (!targetGrid.infantries.Contains(unit))
        targetGrid.infantries.Add(unit);
    if (targetGrid.infantry == null)
        targetGrid.infantry = unit;
    
    // 卸下后本回合不能移动
    unit.isMoved = true;
    unit.isAttacked = true;
    unit.state = UnitState.Acted;
    unit.SetWaitVisual(true);
    
    unitsCannotMove[unit] = 1;
    
    gm?.unitManager?.BindUnitToGrid(unit, true);
    gm?.fogOfWarManager?.OnUnitMoved(); // ✅ 卸载后刷新迷雾
    
    UpdateLoadedIcon();
    
    return true;
}

// ========== ✅ 究极自由：检查是否可以卸载到格子（从 APC 迁移） ==========
public virtual bool CanUnloadToGrid(Grids targetGrid)
{
    if (transportedUnits.Count == 0) return false;
    if (targetGrid == null || grid == null) return false;
    if (targetGrid.gridType == GridType.METEORITE) return false;
    int unloadDx = Mathf.Abs(targetGrid.GridIndex.X - grid.GridIndex.X), unloadDy = Mathf.Abs(targetGrid.GridIndex.Y - grid.GridIndex.Y);
    var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
    if (Math.Max(unloadDx, unloadDy) != 1 || gm?.gridManager == null || !gm.gridManager.IsRangeAllowed(grid, targetGrid, unloadCanCrossScreens)) return false;
    
    var blockingUnit = targetGrid.infantries.FirstOrDefault(u => 
        u != null && IsInstanceValid(u) && 
        u.overlapType == UnitOverlapType.NonOverlapping);
    
    return blockingUnit == null;
}

/// <summary>✅ 按具体乘员判定能否卸到该格：除占用检查外，乘员必须能站上这个地形（不会把坦克卸进海里）</summary>
public virtual bool CanUnloadUnitToGrid(Infantry unit, Grids targetGrid)
{
    if (!CanUnloadToGrid(targetGrid)) return false;
    if (unit == null) return true;
    int cost = unit.GetMoveCostOnGrid(targetGrid);
    if (cost >= 999 || cost == int.MaxValue) return false;
    // ✅ issue#1 二期：多格乘员候选锚点校验（含朝向回退，卸载高亮同步生效）
    if (unit.isMultiTileUnit)
    {
        var gmU = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        int fitFacing = unit.FindFittingFacing(targetGrid.GridIndex, gmU);
        if (fitFacing < 0) return false;
        foreach (var sideIdx in unit.GetFootprintIndices(targetGrid.GridIndex, fitFacing))
        {
            if (sideIdx == targetGrid.GridIndex) continue;
            var sideGrid = gmU?.gridManager?.map?[sideIdx.X, sideIdx.Y];
            if (sideGrid == null) return false;
            if (sideGrid.infantries.Any(x => x != null && IsInstanceValid(x) && x.overlapType == UnitOverlapType.NonOverlapping)) return false;
        }
    }
    return true;
}

// ========== ✅ 究极自由：获取第一个已装载单位（从 APC 迁移） ==========
public virtual Infantry GetFirstLoadedUnit()
{
    return transportedUnits.FirstOrDefault(u => u != null && IsInstanceValid(u));
}

// ========== ✅ 究极自由：loadedIcon 视觉系统（从 APC 迁移） ==========
private void SetupLoadedIcon()
{
    if (loadedIcon == null)
    {
        return;
    }
    
    var spriteFrames = GD.Load<SpriteFrames>("res://asscets/AnimatedSprite/loaded.tres");
    if (spriteFrames == null)
    {
        return;
    }
    
    loadedIcon.SpriteFrames = spriteFrames;
    
    string[] animNames = loadedIcon.SpriteFrames.GetAnimationNames();
    int count = animNames.Length;
    
    
    if (count > 0)
    {
        string targetAnim = "";
        
        for (int i = 0; i < count; i++)
        {
            string animName = animNames[i];
            if (animName == "loaded")
            {
                targetAnim = animName;
                break;
            }
        }
        
        if (string.IsNullOrEmpty(targetAnim))
        {
            targetAnim = animNames[0];
        }
        
        loadedIcon.Animation = targetAnim;
        loadedIcon.Play();
        
    }
    
    loadedIcon.Hide();
    loadedIcon.ZIndex = 200;
    loadedIcon.Modulate = Colors.White;
}

private void StartLoadedIconBlink()
{
    if (loadedIcon == null) return;
    
    StopLoadedIconBlink();
    
    loadedIcon.Show();
    loadedIcon.Visible = true;
    
    if (!loadedIcon.IsPlaying())
    {
        loadedIcon.Play();
    }
    
    var timer = new Timer();
    timer.WaitTime = 1.0f;
    timer.OneShot = false;
    timer.Autostart = true;
    
    timer.Timeout += OnBlinkTimerTimeout;
    
    AddChild(timer);
    
    SetMeta("blink_timer", timer);
}

private void OnBlinkTimerTimeout()
{
    if (loadedIcon != null && IsInstanceValid(loadedIcon))
    {
        loadedIcon.Visible = !loadedIcon.Visible;
    }
}

private void StopLoadedIconBlink()
{
    if (loadedIconTween != null && loadedIconTween.IsValid())
    {
        loadedIconTween.Kill();
        loadedIconTween = null;
    }
    
    if (HasMeta("blink_timer"))
    {
        var timer = GetMeta("blink_timer").As<Timer>();
        if (timer != null && IsInstanceValid(timer))
        {
            timer.Stop();
            timer.QueueFree();
        }
        RemoveMeta("blink_timer");
    }
    
    if (loadedIcon != null)
    {
        loadedIcon.Hide();
        loadedIcon.Modulate = new Color(1, 1, 1, 1);
        loadedIcon.Visible = false;
    }
}

public void UpdateLoadedIcon()
{
    
    if (transportedUnits.Count > 0)
    {
        StartLoadedIconBlink();
    }
    else
    {
        StopLoadedIconBlink();
    }
}

    /// <summary>
    /// 获取可卸下的相邻格子
    /// </summary>
    public virtual List<Grids> GetAdjacentTransportGrids()
    {
        var result = new List<Grids>();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || grid == null) return result;

        Vector2I[] offsets = {
            new Vector2I(0, 1), new Vector2I(-1, 0),
            new Vector2I(0, -1), new Vector2I(1, 0)
        };

        foreach (var offset in offsets)
        {
            var pos = grid.GridIndex + offset;
            if (gm.unitManager.IsValidGrid(pos))
            {
                var targetGrid = gm.gridManager.map[pos.X, pos.Y];
                if (targetGrid != null && targetGrid.gridType != GridType.METEORITE && gm.gridManager.IsRangeAllowed(grid, targetGrid, unloadCanCrossScreens))
                {
                    var blockingUnit = targetGrid.infantries.FirstOrDefault(u =>
                        u != null && IsInstanceValid(u) &&
                        u.overlapType == UnitOverlapType.NonOverlapping);
                    if (blockingUnit == null)
                        result.Add(targetGrid);
                }
            }
        }
        return result;
    }
}
