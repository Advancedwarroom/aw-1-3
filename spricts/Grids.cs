using Godot;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

public enum GridType
{
	GROUND, FOREST, ROAD, SEA, RIVER, HILL, METEORITE, PIPE, LAVA, BEACH, TP,
	REEF, WHIRLPOOL, LAVASIDE, SEAFOG, LANDFOG, WATERFALL, CLIFF, SLOPE, CAVE, HOLE,
	PIPESEAM, TRACK, STATION, BRIDGE, LAVABRIDGE, PASSABLEPIPE, SHIPGATE, OVERPASS,
	BROKENPIPE, RUINS, BROKENTRACK, LAVAFOG,
	PLASMA,  // 等离子（DoR：完全不可通行；陨石被毁→相连等离子消失）
	CREEPER, // 蔓生藤蔓（DoR Creeper：不可通行装饰地形）
	GATE,    // 门（Gate模块配套地形；通行由Gate兵器白名单控制）
	WALL, WEAK_WALL, WASTELAND, RUINS2, RUINS3, // ✅ CW墙系零成本项：墙/脆弱墙(不可通行)/荒地/废墟变体
	DESERT_GROUND, SNOW_GROUND, WASTE_GROUND,   // ✅ CW三气候皮肤组（消耗/防御与基础地形一致）
	DESERT_ROAD, SNOW_ROAD, WASTE_ROAD,
	DESERT_FOREST, SNOW_FOREST, WASTE_FOREST,
	DESERT_HILL, SNOW_HILL, WASTE_HILL,
	DESERT_RUINS, SNOW_RUINS, WASTE_RUINS,
	DESERT_WASTELAND, SNOW_WASTELAND, WASTE_WASTELAND,
	DESERT_PIPE, SNOW_PIPE, WASTE_PIPE,
	DESERT_PIPESEAM, SNOW_PIPESEAM, WASTE_PIPESEAM,
	DESERT_BROKENPIPE, SNOW_BROKENPIPE, WASTE_BROKENPIPE,
    // ✅ CoW补齐六地形(2026-11, 源码对比报告)
    FIRE, LAKE, ROUGH_SEA, STREET, PATH, DESERT_TRY_RIVER, MINE,
}

public enum GridVisionMode { None, TowerCone, TowerRay }
public enum TowerDirection { Up, Right, Down, Left }

public partial class Grids : Node2D
{
    public Sprite2D attackRangeIcon;
    public Vector2I GridIndex;
    public int screenId = 0; // ✅ N屏归属屏号（0起）
    public Sprite2D pathIcon;
    [Export] public GridType gridType;
    public List<Infantry> infantries = new();
    public Infantry infantry;
    public Infantry multiTileOccupant;   // ✅ issue#1 多格机动单位：足印占据者（运行时通道，不入存档；消费者须校验 IsInstanceValid）
    public Action<Grids> OnClickGrid;
    public Action OnClickEmpty;

    public Facility city;
    public bool IsInAttackRangeMode = false;
    public List<Weapon> weapons = new();
    public Weapon weapon;
    public bool IsInWeaponRange = false;

    [ExportGroup("格子自定义伤害")]
    [Export] public bool canDestroyUnit = true;
    [Export] public int fixedDamagePerTurn = 0;
    [Export] public int fixedAttackPerTurn = 0;
    [Export] public bool canOverMaxHealth = false;
    [Export] public int fixedMaxHealthChangePerTurn = 0; // ✦优化：最大血量每回合增减（正=增 负=减）

    [ExportGroup("格子弹药/燃料系统")]
    [Export] public int fixedAmmoChangePerTurn = 0; // 主武器弹药增减（旧字段兼容保留，与下方种类增减可叠加）
    // ✦优化：全部8种弹药实际值每回合增减（0=不生效；正=补充 负=消耗）
    [Export] public int ammoChangePrimary = 0;       // 主弹药
    [Export] public int ammoChangeExplode = 0;      // 自爆弹
    [Export] public int ammoChangeFlare = 0;        // 照明弹
    [Export] public int ammoChangeStealth = 0;      // 隐身弹
    [Export] public int ammoChangeReveal = 0;       // 显形弹
    [Export] public int ammoChangeCamo = 0;        // 迷彩弹
    [Export] public int ammoChangeProduction = 0;  // 生产弹
    [Export] public int ammoChangeConstruction = 0; // 建造弹
    // ✦优化：全部8种弹药上限每回合增减（正=扩容 负=缩容）
    [Export] public int ammoMaxChangePrimary = 0;
    [Export] public int ammoMaxChangeExplode = 0;
    [Export] public int ammoMaxChangeFlare = 0;
    [Export] public int ammoMaxChangeStealth = 0;
    [Export] public int ammoMaxChangeReveal = 0;
    [Export] public int ammoMaxChangeCamo = 0;
    [Export] public int ammoMaxChangeProduction = 0;
    [Export] public int ammoMaxChangeConstruction = 0;
    [Export] public bool ammoCanOverMax = false;
    [Export] public bool ammoCanReachZero = true;
    [Export] public int fixedFuelChangePerTurn = 0;
    [Export] public int fixedMaxFuelChangePerTurn = 0; // ✦优化：燃油上限每回合增减
    [Export] public bool fuelCanOverMax = false;
    [Export] public bool fuelCanReachZero = true;

    [ExportGroup("战争迷雾特殊规则")]
    [Export] public bool requiresAdjacentVision = false;
    [Export] public int visionBonus = 0;
    [Export] public bool isWatchtower = false;

    [ExportGroup("格子视野系统")]
    [Export] public GridVisionMode gridVisionMode = GridVisionMode.None;

    [ExportGroup("地形绑定图标")]
    [Export] public Godot.Collections.Dictionary<GridType, Texture2D> terrainIconTextures = new();
    [Export] public int terrainIconZIndex = 1;
    private Godot.Collections.Dictionary<GridType, Sprite2D> terrainIconSprites = new();
    private GridType _lastGridType;

    /// <summary>
    /// ✅ 多方向地形方向位掩码：N=1, E=2, S=4, W=8（0=孤立）。
    /// 适用于 PIPE / ROAD / BRIDGE / RIVER 等多方向地形，渲染时自动选择方向变体贴图。
    /// </summary>
    [ExportGroup("地形方向")]
    [Export] public int terrainDirection = 0;
    [Export] public bool autoOptimizeDirection = true;
    [Export] public string manualCowTexturePath = "";
    private int _lastTerrainDirection = -1;
    private Sprite2D _directionVariantSprite = null;

    /// <summary>
    /// ✅ CoW 风格烘焙：放置时冻结本格的气候/风格（-1=未烘焙）。
    /// 编辑器风格条只是"画笔"，切换后不得改变已放好的地块——首渲染即冻结。
    /// </summary>
    [ExportGroup("CoW风格烘焙")]
    [Export] public int cowBakedBiome = -1;
    [Export] public int cowBakedStyle = -1;

    [Export] public int gridVisionRange = 1;
    [Export] public TowerDirection towerDirection = TowerDirection.Up;
    [Export] public bool towerCanRotate = false;
    [Export] public float towerRayAngle = 0f;
    [Export] public bool gridVisionIgnoreTerrain = true;

    public Action OnMouseEnteredAttackGrid;
    public Action OnMouseExitedAttackGrid;

    public bool HasEnemyWeapon(string myTeam, bool includeFriendly = false)
    {
        // ✅ 盟友机制：同盟兵器不算敌方目标
        return weapons.Any(w => IsInstanceValid(w) && !w.IsQueuedForDeletion() && TeamHelper.IsEnemyForAttacker(myTeam, w.team, includeFriendly));
    }

    public override void _Ready()
    {
        pathIcon = GetNode<Sprite2D>("mrhl");
        attackRangeIcon = GetNode<Sprite2D>("AttackRange");
        pathIcon?.Hide();
        attackRangeIcon?.Hide();

        city = GetNodeOrNull<Facility>("City");
        if (city == null)
        {
            // 兼容不同名称的设施子节点
            foreach (Node child in GetChildren())
            {
                if (child is Facility f)
                {
                    city = f;
                    break;
                }
            }
        }

        var area2D = GetNodeOrNull<Area2D>("Area2D");
        if (area2D != null)
        {
            area2D.MouseEntered += OnArea2DMouseEntered;
            area2D.MouseExited += OnArea2DMouseExited;
            area2D.InputPickable = true;
        }

        // ✅ 初始化瞭望塔箭头朝向
        var arrow = GetNodeOrNull<Sprite2D>("TowerDirectionArrow");
        if (arrow != null)
        {
            arrow.Visible = gridVisionMode != GridVisionMode.None && towerCanRotate;
            arrow.RotationDegrees = towerDirection switch
            {
                TowerDirection.Up => 0,
                TowerDirection.Right => 90,
                TowerDirection.Down => 180,
                TowerDirection.Left => 270,
                _ => 0
            };
        }

        // ✅ 初始化地形绑定图标
        InitializeTerrainIcons();
        _lastGridType = gridType;
        UpdateTerrainIcon();
    }

    // ✅ 通用悬停回调（移动轨迹预览用，不受攻击模式门控）
    public Action<Grids> OnHoverGrid;
    public Action<Grids> OnUnhoverGrid;

    public void OnArea2DMouseEntered()
    {
        if (IsInAttackRangeMode) OnMouseEnteredAttackGrid?.Invoke();
        OnHoverGrid?.Invoke(this);
    }
    public void OnArea2DMouseExited()
    {
        if (IsInAttackRangeMode) OnMouseExitedAttackGrid?.Invoke();
        OnUnhoverGrid?.Invoke(this);
    }

    // 开关格子的物理输入拾取（移动端轨迹模式时禁用，防止触摸按下即触发点击）
    public void SetAreaPickable(bool pickable)
    {
        var area = GetNodeOrNull<Area2D>("GridArea");
        if (area != null) area.InputPickable = pickable;
    }

    public HashSet<Grids> CalculateGridVision(GameManager gm)
    {
        var visible = new HashSet<Grids>();
        if (gm == null || gm.gridManager?.map == null) return visible;

        if (city != null)
            visible.UnionWith(city.CalculateFacilityVision(gm));

        switch (gridVisionMode)
        {
            case GridVisionMode.TowerCone: visible.UnionWith(CalculateTowerConeVision(gm)); break;
            case GridVisionMode.TowerRay: visible.UnionWith(CalculateTowerRayVision(gm)); break;
        }

        return visible;
    }

    // ✅ 瞭望塔方向性视野（锥形/射线）——FogOfWarManager 接线专用；不含设施基础视野，避免重复合并
    public HashSet<Grids> CalculateTowerDirectionalVision(GameManager gm)
    {
        var visible = new HashSet<Grids>();
        switch (gridVisionMode)
        {
            case GridVisionMode.TowerCone:
                visible.UnionWith(CalculateTowerConeVision(gm));
                break;
            case GridVisionMode.TowerRay:
                visible.UnionWith(CalculateTowerRayVision(gm));
                break;
        }
        return visible;
    }

    private HashSet<Grids> CalculateTowerConeVision(GameManager gm)
    {
        var visible = new HashSet<Grids>();
        var dir = towerDirection switch
        {
            TowerDirection.Up => new Vector2I(0, -1), TowerDirection.Right => new Vector2I(1, 0),
            TowerDirection.Down => new Vector2I(0, 1), TowerDirection.Left => new Vector2I(-1, 0),
            _ => new Vector2I(0, -1)
        };
        var perp = new Vector2I(dir.Y, -dir.X);

        for (int depth = 1; depth <= gridVisionRange; depth++)
        {
            Vector2I center = GridIndex + dir * depth;
            int halfWidth = depth;
            for (int w = -halfWidth; w <= halfWidth; w++)
            {
                var pos = center + perp * w;
                if (IsValidGrid(pos, gm))
                {
                    var g = gm.gridManager.map[pos.X, pos.Y];
                    if (g != null)
                    {
                        visible.Add(g);
                        if (!gridVisionIgnoreTerrain && IsVisionBlockingTerrain(g.gridType)) break;
                    }
                }
            }
        }
        return visible;
    }

    private HashSet<Grids> CalculateTowerRayVision(GameManager gm)
    {
        var visible = new HashSet<Grids>();
        if (gm?.gridManager == null) return visible;

        float angleRad = Mathf.DegToRad(towerRayAngle);
        Godot.Vector2 dir = new(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        float cellSize = gm.gridManager.gridSize.X;
        Godot.Vector2 startPos = Position + new Godot.Vector2(cellSize * 0.5f, cellSize * 0.5f);

        Godot.Vector2 currentPos = startPos;
        int currentX = GridIndex.X, currentY = GridIndex.Y;
        int stepX = dir.X > 0 ? 1 : (dir.X < 0 ? -1 : 0);
        int stepY = dir.Y > 0 ? 1 : (dir.Y < 0 ? -1 : 0);
        int steps = 0;
        int maxSteps = gridVisionRange * 2;
        HashSet<Grids> visited = new();

        for (int step = 0; step < maxSteps && steps < gridVisionRange; step++)
        {
            float x0 = gm.gridManager.startPos.X + currentX * cellSize;
            float x1 = x0 + cellSize;
            float y0 = gm.gridManager.startPos.Y + currentY * cellSize;
            float y1 = y0 + cellSize;

            float tX = float.MaxValue, tY = float.MaxValue;
            if (stepX > 0) tX = (x1 - currentPos.X) / dir.X;
            else if (stepX < 0) tX = (x0 - currentPos.X) / dir.X;
            if (stepY > 0) tY = (y1 - currentPos.Y) / dir.Y;
            else if (stepY < 0) tY = (y0 - currentPos.Y) / dir.Y;

            float tStep = Mathf.Min(tX, tY);
            if (tStep < 0.0001f) tStep = 0.0001f;

            var targetGrid = gm.gridManager.map[currentX, currentY];
            if (targetGrid != null && !visited.Contains(targetGrid) && targetGrid != this)
            {
                visited.Add(targetGrid);
                visible.Add(targetGrid);
                steps++;
                if (!gridVisionIgnoreTerrain && IsVisionBlockingTerrain(targetGrid.gridType)) break;
            }

            currentPos += dir * tStep;
            bool xExit = Mathf.Abs(tX - tStep) < 0.0001f;
            bool yExit = Mathf.Abs(tY - tStep) < 0.0001f;
            if (xExit && yExit) { currentX += stepX; currentY += stepY; }
            else if (xExit) currentX += stepX;
            else if (yExit) currentY += stepY;

            if (!IsValidGrid(new Vector2I(currentX, currentY), gm)) break;
        }
        return visible;
    }

    private HashSet<Grids> CalculateManhattanVision(GameManager gm, Vector2I center, int range)
    {
        var visible = new HashSet<Grids>();
        for (int dx = -range; dx <= range; dx++)
            for (int dy = -range; dy <= range; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > range) continue;
                var pos = new Vector2I(center.X + dx, center.Y + dy);
                if (IsValidGrid(pos, gm))
                {
                    var g = gm.gridManager.map[pos.X, pos.Y];
                    if (g != null) visible.Add(g);
                }
            }
        return visible;
    }

    private bool IsValidGrid(Vector2I pos, GameManager gm)
    {
        return pos.X >= 0 && pos.X < gm.gridManager.searchRange.X &&
               pos.Y >= 0 && pos.Y < gm.gridManager.searchRange.Y;
    }

    private bool IsVisionBlockingTerrain(GridType type)
    {
        return type == GridType.METEORITE || type == GridType.PIPE || type == GridType.CLIFF;
    }

    public void ApplyTerrainDamage(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit) || unit.health <= 0) return;
        int damage = 0; bool isPercentage = false; float percentage = 0f; string damageSource = "";
        switch (gridType)
        {
            case GridType.WHIRLPOOL:
                if (unit.moveType == MoveType.Naval || unit.moveType == MoveType.Hover)
                { damage = 80; damageSource = "WHIRLPOOL"; }
                break;
            case GridType.LAVA:
                if (!unit.terrainDamageImmunity.Contains(GridType.LAVA)) { isPercentage = true; percentage = 0.10f; damageSource = "LAVA"; }   // 开创人2026-11-11：免疫权下放进单位 Export 白名单
                break;
            case GridType.LAVASIDE:
                if (!unit.terrainDamageImmunity.Contains(GridType.LAVASIDE)) { damage = 98; damageSource = "LAVASIDE"; }
                break;
            case GridType.LAVAFOG:
                if (!unit.terrainDamageImmunity.Contains(GridType.LAVAFOG)) { isPercentage = true; percentage = 0.20f; damageSource = "LAVAFOG"; }
                break;
        }
        if (isPercentage)
        {
            int actualDamage = Mathf.Max(1, Mathf.RoundToInt(unit.maxHealth * percentage));
            unit.health -= actualDamage; unit.UpdateHpLabel();
            ShowTerrainDamageEffect(unit, $"-{actualDamage} ({damageSource})", Colors.Red);
        }
        else if (damage > 0)
        {
            int finalDamage = Mathf.Max(1, CalculateDamageThroughDefense(unit, damage));
            unit.health -= finalDamage; unit.UpdateHpLabel();
            ShowTerrainDamageEffect(unit, $"-{finalDamage} ({damageSource})", Colors.Red);
        }
        CheckUnitDeath(unit, damageSource);
    }

    public void ApplyGridCustomDamage(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit) || fixedDamagePerTurn == 0) return;
        if (fixedDamagePerTurn > 0)
        {
            unit.health -= fixedDamagePerTurn; unit.UpdateHpLabel();
            ShowTerrainDamageEffect(unit, $"-{fixedDamagePerTurn} (地形)", new Color(1, 0.3f, 0.3f));
        }
        else
        {
            int healAmount = Mathf.Abs(fixedDamagePerTurn);
            if (canOverMaxHealth) unit.health += healAmount;
            else unit.health = Mathf.Min(unit.maxHealth, unit.health + healAmount);
            unit.UpdateHpLabel();
            ShowTerrainDamageEffect(unit, $"+{healAmount} (地形)", new Color(0.3f, 1, 0.3f));
        }
        CheckUnitDeath(unit, "GridCustomDamage");
    }

    public void ApplyGridCustomAttack(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit) || fixedAttackPerTurn == 0) return;
        if (fixedAttackPerTurn > 0)
        {
            int finalDamage = Mathf.Max(1, CalculateDamageThroughDefense(unit, fixedAttackPerTurn));
            unit.health -= finalDamage; unit.UpdateHpLabel();
            ShowTerrainDamageEffect(unit, $"-{finalDamage} (地形攻击)", new Color(1, 0.5f, 0.2f));
        }
        else
        {
            int healAmount = Mathf.Abs(fixedAttackPerTurn);
            if (canOverMaxHealth) unit.health += healAmount;
            else unit.health = Mathf.Min(unit.maxHealth, unit.health + healAmount);
            unit.UpdateHpLabel();
            ShowTerrainDamageEffect(unit, $"+{healAmount} (地形攻击)", new Color(0.3f, 1, 0.5f));
        }
        CheckUnitDeath(unit, "GridCustomAttack");
    }

    public void ApplyGridAmmoChange(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit)) return;

        // ✦优化：全种类弹药增减（统一走Infantry弹药系统；fixedAmmoChangePerTurn仍单独生效于主弹药，可叠加）
        foreach (Infantry.AmmoKind kind in Infantry.AllAmmoKinds)
        {
            int kindChange = GetAmmoKindChange(kind);
            if (kindChange == 0) continue;
            if (unit.GetBaseMaxAmmo(kind) <= 0) continue; // 该单位无此弹药种类
            int oldA = unit.GetAmmo(kind);
            if (kindChange > 0)
            {
                if (ammoCanOverMax) unit.SetAmmoRaw(kind, oldA + kindChange); // 无上限补充
                else unit.AddAmmo(kind, kindChange);                          // 夹到上限
            }
            else
            {
                int nv = oldA - Mathf.Abs(kindChange);
                nv = ammoCanReachZero ? Mathf.Max(0, nv) : Mathf.Max(1, nv);  // 不可归零则强制剩1
                unit.SetAmmoRaw(kind, nv);
            }
            int deltaA = unit.GetAmmo(kind) - oldA;
            if (deltaA != 0)
                ShowTerrainDamageEffect(unit, $"{GetAmmoKindName(kind)}{(deltaA > 0 ? "+" : "")}{deltaA} (地形)", new Color(0.9f, 0.7f, 0.2f));
        }

        if (fixedAmmoChangePerTurn == 0) return;
        if (!unit.hasPrimaryWeapon || !unit.primaryHasLimitedAmmo || unit.maxPrimaryAmmo >= 99) return;
        if (fixedAmmoChangePerTurn > 0)
        {
            int oldAmmo = unit.currentPrimaryAmmo;
            if (ammoCanOverMax) unit.currentPrimaryAmmo += fixedAmmoChangePerTurn;
            else unit.currentPrimaryAmmo = Mathf.Min(unit.maxPrimaryAmmo, unit.currentPrimaryAmmo + fixedAmmoChangePerTurn);
            if (unit.currentPrimaryAmmo - oldAmmo > 0)
                ShowTerrainDamageEffect(unit, $"Ammo+{unit.currentPrimaryAmmo - oldAmmo} (地形)", new Color(0.9f, 0.7f, 0.2f));
        }
        else
        {
            int consumeAmount = Mathf.Abs(fixedAmmoChangePerTurn);
            unit.currentPrimaryAmmo -= consumeAmount;
            if (!ammoCanReachZero) unit.currentPrimaryAmmo = Mathf.Max(1, unit.currentPrimaryAmmo);
            else unit.currentPrimaryAmmo = Mathf.Max(0, unit.currentPrimaryAmmo);
            ShowTerrainDamageEffect(unit, $"Ammo-{consumeAmount} (地形)", new Color(1, 0.5f, 0.2f));
        }
    }

    public void ApplyGridFuelChange(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit) || fixedFuelChangePerTurn == 0 || !unit.consumeFuel) return;
        if (fixedFuelChangePerTurn > 0)
        {
            int oldFuel = unit.fuel;
            if (fuelCanOverMax) unit.fuel += fixedFuelChangePerTurn;
            else unit.fuel = Mathf.Min(unit.maxFuel, unit.fuel + fixedFuelChangePerTurn);
            if (unit.fuel - oldFuel > 0)
                ShowTerrainDamageEffect(unit, $"Fuel+{unit.fuel - oldFuel} (地形)", new Color(0.2f, 0.8f, 0.9f));
        }
        else
        {
            int consumeAmount = Mathf.Abs(fixedFuelChangePerTurn);
            unit.fuel -= consumeAmount;
            if (!fuelCanReachZero) unit.fuel = Mathf.Max(1, unit.fuel);
            else unit.fuel = Mathf.Max(0, unit.fuel);
            ShowTerrainDamageEffect(unit, $"Fuel-{consumeAmount} (地形)", new Color(0.3f, 0.5f, 1f));
        }
    }

    public void LockHealthIfNeeded(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit) || canDestroyUnit || unit.health > 0) return;
        unit.health = 1; unit.UpdateHpLabel();
    }

    private int CalculateDamageThroughDefense(Infantry unit, int baseDamage)
    {
        return Mathf.Max(0, baseDamage - unit.GetEffectiveDefense(this));
    }

    /// <summary>✦优化：每回合最大值变化（血量/弹药上限/燃油上限）。先于实际值变化执行。</summary>
    public void ApplyGridMaxStatChanges(Infantry unit)
    {
        if (unit == null || !IsInstanceValid(unit)) return;

        // 最大血量
        if (fixedMaxHealthChangePerTurn != 0)
        {
            int oldMax = unit.maxHealth;
            unit.maxHealth = Mathf.Max(1, oldMax + fixedMaxHealthChangePerTurn);
            if (!canOverMaxHealth && unit.health > unit.maxHealth) unit.health = unit.maxHealth; // 缩容时钳制当前值
            unit.UpdateHpLabel();
            if (unit.maxHealth != oldMax)
                ShowTerrainDamageEffect(unit, $"MAX血量{(unit.maxHealth > oldMax ? "+" : "")}{unit.maxHealth - oldMax} (地形)", new Color(0.8f, 0.5f, 1f));
        }

        // 各弹药种类上限
        foreach (Infantry.AmmoKind kind in Infantry.AllAmmoKinds)
        {
            int mc = GetAmmoKindMaxChange(kind);
            if (mc == 0) continue;
            int baseMax = unit.GetBaseMaxAmmo(kind);
            if (baseMax <= 0 && mc < 0) continue; // 无此弹药系统则不缩容
            int newMax = Mathf.Max(0, baseMax + mc);
            if (newMax == baseMax) continue;
            unit.SetMaxAmmoRaw(kind, newMax);
            if (!ammoCanOverMax && unit.GetAmmo(kind) > newMax)
                unit.SetAmmoRaw(kind, newMax); // 缩容后钳制当前值
            ShowTerrainDamageEffect(unit, $"MAX{GetAmmoKindName(kind)}{(newMax > baseMax ? "+" : "")}{newMax - baseMax} (地形)", new Color(0.9f, 0.6f, 0.3f));
        }

        // 燃油上限
        if (fixedMaxFuelChangePerTurn != 0 && unit.consumeFuel)
        {
            int oldMaxF = unit.maxFuel;
            unit.maxFuel = Mathf.Max(0, oldMaxF + fixedMaxFuelChangePerTurn);
            if (!fuelCanOverMax && unit.fuel > unit.maxFuel) unit.fuel = unit.maxFuel;
            if (unit.maxFuel != oldMaxF)
                ShowTerrainDamageEffect(unit, $"MAX燃油{(unit.maxFuel > oldMaxF ? "+" : "")}{unit.maxFuel - oldMaxF} (地形)", new Color(0.5f, 0.7f, 1f));
        }
    }

    /// <summary>✦优化：弹药种类中文名（与Infantry.AmmoKind一一对应）</summary>
    public static string GetAmmoKindName(Infantry.AmmoKind kind) => kind switch
    {
        Infantry.AmmoKind.Primary => "主弹药",
        Infantry.AmmoKind.Explode => "自爆弹",
        Infantry.AmmoKind.Flare => "照明弹",
        Infantry.AmmoKind.StealthBullet => "隐身弹",
        Infantry.AmmoKind.RevealBullet => "显形弹",
        Infantry.AmmoKind.CamoBullet => "迷彩弹",
        Infantry.AmmoKind.Production => "生产弹",
        Infantry.AmmoKind.Construction => "建造弹",
        _ => kind.ToString(),
    };

    /// <summary>✦优化：读取某弹药种类的每回合增减</summary>
    public int GetAmmoKindChange(Infantry.AmmoKind kind) => kind switch
    {
        Infantry.AmmoKind.Primary => ammoChangePrimary,
        Infantry.AmmoKind.Explode => ammoChangeExplode,
        Infantry.AmmoKind.Flare => ammoChangeFlare,
        Infantry.AmmoKind.StealthBullet => ammoChangeStealth,
        Infantry.AmmoKind.RevealBullet => ammoChangeReveal,
        Infantry.AmmoKind.CamoBullet => ammoChangeCamo,
        Infantry.AmmoKind.Production => ammoChangeProduction,
        Infantry.AmmoKind.Construction => ammoChangeConstruction,
        _ => 0,
    };

    /// <summary>✦优化：读取某弹药种类的上限每回合增减</summary>
    public int GetAmmoKindMaxChange(Infantry.AmmoKind kind) => kind switch
    {
        Infantry.AmmoKind.Primary => ammoMaxChangePrimary,
        Infantry.AmmoKind.Explode => ammoMaxChangeExplode,
        Infantry.AmmoKind.Flare => ammoMaxChangeFlare,
        Infantry.AmmoKind.StealthBullet => ammoMaxChangeStealth,
        Infantry.AmmoKind.RevealBullet => ammoMaxChangeReveal,
        Infantry.AmmoKind.CamoBullet => ammoMaxChangeCamo,
        Infantry.AmmoKind.Production => ammoMaxChangeProduction,
        Infantry.AmmoKind.Construction => ammoMaxChangeConstruction,
        _ => 0,
    };

    /// <summary>✦优化：写入某弹药种类的每回合增减（编辑器用）</summary>
    public void SetAmmoKindChange(Infantry.AmmoKind kind, int value)
    {
        switch (kind)
        {
            case Infantry.AmmoKind.Primary: ammoChangePrimary = value; break;
            case Infantry.AmmoKind.Explode: ammoChangeExplode = value; break;
            case Infantry.AmmoKind.Flare: ammoChangeFlare = value; break;
            case Infantry.AmmoKind.StealthBullet: ammoChangeStealth = value; break;
            case Infantry.AmmoKind.RevealBullet: ammoChangeReveal = value; break;
            case Infantry.AmmoKind.CamoBullet: ammoChangeCamo = value; break;
            case Infantry.AmmoKind.Production: ammoChangeProduction = value; break;
            case Infantry.AmmoKind.Construction: ammoChangeConstruction = value; break;
        }
    }

    /// <summary>✦优化：写入某弹药种类的上限每回合增减（编辑器用）</summary>
    public void SetAmmoKindMaxChange(Infantry.AmmoKind kind, int value)
    {
        switch (kind)
        {
            case Infantry.AmmoKind.Primary: ammoMaxChangePrimary = value; break;
            case Infantry.AmmoKind.Explode: ammoMaxChangeExplode = value; break;
            case Infantry.AmmoKind.Flare: ammoMaxChangeFlare = value; break;
            case Infantry.AmmoKind.StealthBullet: ammoMaxChangeStealth = value; break;
            case Infantry.AmmoKind.RevealBullet: ammoMaxChangeReveal = value; break;
            case Infantry.AmmoKind.CamoBullet: ammoMaxChangeCamo = value; break;
            case Infantry.AmmoKind.Production: ammoMaxChangeProduction = value; break;
            case Infantry.AmmoKind.Construction: ammoMaxChangeConstruction = value; break;
        }
    }

    private void CheckUnitDeath(Infantry unit, string source)
    {
        if (unit.health <= 0 && canDestroyUnit)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.RemovePiece(unit);
            unit.OnDestroyed();
            if (unit.grid != null)
            {
                unit.grid.infantries.Remove(unit);
                if (unit.grid.infantry == unit) unit.grid.infantry = unit.grid.infantries.Count > 0 ? unit.grid.infantries[0] : null;
            }
            unit.QueueFree();
        }
    }

    private void ShowTerrainDamageEffect(Infantry unit, string text, Color color)
    {
        var label = new Label();
        label.Text = text; label.Modulate = color;
        label.Position = unit.Position + new Godot.Vector2(0, -20);
        label.ZIndex = 300;
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        GetTree()?.CurrentScene?.AddChild(label);
        var tween = CreateTween();
        tween.TweenProperty(label, "position", label.Position + new Godot.Vector2(0, -40), 1.0f);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.5f);
        tween.TweenCallback(Callable.From(() => label.QueueFree()));
    }

    public float TerrainDefenseBonus
    {
        
        get
        {
                    var extraSave = ExtraSave.Instance;
        if (extraSave != null && extraSave.CustomTerrainTypes.TryGetValue(gridType.ToString(), out var customTerrain))
        {
            return customTerrain.defenseBonus;
        }
            return gridType switch
            {
                GridType.GROUND => 0.10f, GridType.FOREST => 0.20f,
                GridType.HILL => 0.40f, GridType.ROAD => 0.0f, GridType.SEA => 0.0f,
                GridType.RIVER => 0.0f, GridType.BEACH => 0.10f, GridType.TP => 0.0f,
                GridType.REEF => 0.10f, GridType.WHIRLPOOL => 0.10f, GridType.LAVASIDE => 0.10f,
                GridType.SEAFOG => 0.50f, GridType.LANDFOG => 0.50f, GridType.WATERFALL => 0.20f,
                GridType.CLIFF => 0.30f, GridType.SLOPE => 0.20f, GridType.CAVE => 0.40f,
                GridType.HOLE => 0.40f, GridType.PIPESEAM => 0.10f, GridType.METEORITE => 0.10f,
                GridType.TRACK => 0.0f, GridType.STATION => 0.20f, GridType.BRIDGE => 0.0f,
                GridType.LAVABRIDGE => 0.0f, GridType.PASSABLEPIPE => 0.10f, GridType.SHIPGATE => 0.10f,
                GridType.OVERPASS => 0.30f, GridType.BROKENPIPE => 0.20f, GridType.RUINS => 0.30f,
                GridType.BROKENTRACK => 0.20f, GridType.LAVAFOG => 0.50f,
                GridType.WALL => 0.0f, GridType.WEAK_WALL => 0.0f, GridType.WASTELAND => 0.20f, GridType.RUINS2 => 0.30f, GridType.RUINS3 => 0.30f, GridType.DESERT_GROUND => 0.10f, GridType.SNOW_GROUND => 0.10f, GridType.WASTE_GROUND => 0.10f, GridType.DESERT_ROAD => 0.0f, GridType.SNOW_ROAD => 0.0f, GridType.WASTE_ROAD => 0.0f, GridType.DESERT_FOREST => 0.20f, GridType.SNOW_FOREST => 0.20f, GridType.WASTE_FOREST => 0.20f, GridType.DESERT_HILL => 0.40f, GridType.SNOW_HILL => 0.40f, GridType.WASTE_HILL => 0.40f, GridType.DESERT_RUINS => 0.30f, GridType.SNOW_RUINS => 0.30f, GridType.WASTE_RUINS => 0.30f, GridType.DESERT_WASTELAND => 0.20f, GridType.SNOW_WASTELAND => 0.20f, GridType.WASTE_WASTELAND => 0.20f, GridType.DESERT_PIPE => 0.0f, GridType.SNOW_PIPE => 0.0f, GridType.WASTE_PIPE => 0.0f, GridType.DESERT_PIPESEAM => 0.10f, GridType.SNOW_PIPESEAM => 0.10f, GridType.WASTE_PIPESEAM => 0.10f, GridType.DESERT_BROKENPIPE => 0.20f, GridType.SNOW_BROKENPIPE => 0.20f, GridType.WASTE_BROKENPIPE => 0.20f, _ => 0.0f
            };
        }
    }

    public string GetTerrainDefenseDescription() { float bonus = TerrainDefenseBonus * 100; return bonus > 0 ? $"{bonus:F0}%" : TranslateGroup.T("无"); }

    public List<Infantry> GetEnemyInfantries(string team)
    {
        var list = infantries.Where(i => i != null && TeamHelper.IsEnemyForAttacker(team, i.team)).ToList();
        // ✅ issue#1 多格机动单位：足印占据者视为本格敌方单位（目标解析+高亮统一入口；锚点格去重）
        var occ = multiTileOccupant;
        if (occ != null && !list.Contains(occ) && IsInstanceValid(occ) && !occ.IsQueuedForDeletion()
            && TeamHelper.IsEnemyForAttacker(team, occ.team))
            list.Add(occ);
        return list;
    }

    private double lastClickTime = 0;
    private const double DOUBLE_CLICK_WINDOW = 0.3;
    public bool HasEnemyInfantry(string team)
    {
        if (infantries.Any(i => i != null && IsInstanceValid(i) && TeamHelper.IsEnemyForAttacker(team, i.team))) return true;
        // ✅ issue#1 多格机动单位：足印占据者也算敌方单位
        var occ = multiTileOccupant;
        return occ != null && IsInstanceValid(occ) && !occ.IsQueuedForDeletion() && TeamHelper.IsEnemyForAttacker(team, occ.team);
    }
    public static bool IsForceActionMode = false;

    public void ClickGrid(Node viewport, InputEvent inputs, int shape_index)
    {
        if (inputs is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Left)
            {
                var terrainEditor = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
                if (terrainEditor != null && terrainEditor.IsEditMode)
                { terrainEditor.OnGridClickedForEdit(this); GetViewport().SetInputAsHandled(); return; }

                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                if (IsForceActionMode && OnClickGrid != null) { OnClickGrid.Invoke(this); GetViewport().SetInputAsHandled(); return; }

                if (gm?.selectedInfantry != null && gm.selectedInfantry.state != UnitState.Acted &&
                    gm.gridManager != null && gm.gridManager.moveRange.Contains(this) && gm.selectedInfantry.grid != this)
                { if (OnClickGrid != null) { OnClickGrid.Invoke(this); GetViewport().SetInputAsHandled(); return; } }

                if (weapons.Count > 0)
                {
                    var w = weapons[0];
                    if (gm != null && gm.IsTurnPhaseValid(w.team) && !w.hasActed) { w.OnClickWeapon?.Invoke(w); return; }
                }

                if (infantries == null) infantries = new List<Infantry>();
                infantries.RemoveAll(i => !IsInstanceValid(i));
                if (infantries.Count > 0)
                {
                    if (gm != null)
                    {
                        var selectableUnits = infantries.Where(i => TeamHelper.CanOperateTeam(gm.turnPhase, i.team) && !i.isMoved).OrderByDescending(i => i.health).ToList();
                        if (selectableUnits.Count > 0)
                        {
                            var selectedUnit = selectableUnits[0];
                            if (selectedUnit.grid == null)
                            {
                                if (gm.unitManager != null) gm.unitManager.BindUnitToGrid(selectedUnit, true);
                                else { var um = GetNodeOrNull<UnitManager>("/root/Main/UnitManager"); um?.BindUnitToGrid(selectedUnit, true); }
                            }
                            selectedUnit.InputClick(viewport, inputs, shape_index);
                            return;
                        }
                    }
                }

                // ✅ 检查是否为可旋转的瞭望塔（己方或P-1公共设施）
                if (gridVisionMode != GridVisionMode.None && towerCanRotate && city != null && !string.IsNullOrEmpty(city.facilityTeam))
                {
                    string currentTeam = gm?.turnPhase == 1 ? "Player1" : "Player2";
                    bool isOwnTower = city.facilityTeam == currentTeam;
                    bool isPublicTower = city.facilityTeam == TeamHelper.Player || city.isPublicFacility;

                    // ✅ 己方瞭望塔或P-1公共设施瞭望塔都可以被旋转
                    if ((isOwnTower || isPublicTower) && gm?.selectedInfantry == null)
                    {
                        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                        actionMenu?.ShowGridRotateMenu(this);
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                }

                // 非生产设施的手动赋能/解能入口；生产设施在生产菜单内显示同名按钮。
                if (city != null && !(city.canProduce && city.producibleUnitNames.Count > 0)
                    && gm != null && gm.selectedInfantry == null && TeamHelper.CanOperateTeam(gm.turnPhase, city.facilityTeam)
                    && EmpowermentRuntime.CanManualUse(city))
                {
                    (GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu)?.BeginEmpowermentSelection(city);
                    GetViewport().SetInputAsHandled();
                    return;
                }

                // ✅ 检查设施生产功能
                if (city != null && city.canProduce && city.producibleUnitNames.Count > 0 && gm?.selectedInfantry == null)
                {
                    string currentTeam = gm?.turnPhase == 1 ? "Player1" : "Player2";
                    if (city.CanBeProducedBy(currentTeam))
                    {
                        gm?.ShowProductionMenu(city, this);
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                }

                double now = Time.GetTicksMsec() / 1000.0;
                if (now - lastClickTime < DOUBLE_CLICK_WINDOW) { ShowTerrainInfo(); lastClickTime = 0; GetViewport().SetInputAsHandled(); return; }
                lastClickTime = now;
                if (OnClickGrid != null) OnClickGrid.Invoke(this); else OnClickEmpty?.Invoke();
            }
        }
    }

    private void ShowTerrainInfo()
    {
        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
        if (actionMenu == null) return;
        string info = $"地形: {gridType}\n防御加成: {GetTerrainDefenseDescription()}\n坐标: ({GridIndex.X}, {GridIndex.Y})\n";
        if (city != null) { info += $"归属: {city.facilityTeam}\n占领所需: {city.capturePointsRequired}点\n"; }
        if (gridVisionMode != GridVisionMode.None)
        {
            info += $"\n=== 格子视野 ===\n模式: {gridVisionMode}\n射程: {gridVisionRange}\n";
            if (gridVisionMode == GridVisionMode.TowerCone) { info += $"方向: {towerDirection}\n可旋转: {(towerCanRotate ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n"; }
            else if (gridVisionMode == GridVisionMode.TowerRay) { info += $"角度: {towerRayAngle}°\n"; }
            info += $"无视地形: {(gridVisionIgnoreTerrain ? TranslateGroup.T("是") : TranslateGroup.T("否"))}\n";
        }
        if (visionBonus != 0) info += $"视野加成: {visionBonus}\n";
        if (requiresAdjacentVision) info += $"需要紧邻视野: 是\n";
        if (fixedDamagePerTurn != 0) info += $"回合伤害: {fixedDamagePerTurn}\n";
        if (fixedAttackPerTurn != 0) info += $"回合攻击: {fixedAttackPerTurn}\n";
        if (fixedAmmoChangePerTurn != 0) info += $"弹药变化: {fixedAmmoChangePerTurn}\n";
        if (fixedFuelChangePerTurn != 0) info += $"燃料变化: {fixedFuelChangePerTurn}\n";
        actionMenu.unitInfoLabel.Text = info;
        actionMenu.unitInfoLabel.Visible = true;
        actionMenu.closeInfoButton.Visible = true;
        var timer = GetTree().CreateTimer(3.0);
        timer.Timeout += () => { if (actionMenu != null && IsInstanceValid(actionMenu)) { actionMenu.unitInfoLabel.Visible = false; actionMenu.closeInfoButton.Visible = false; } };
    }

    public List<Infantry> GetOtherInfantries(Infantry exclude) { return infantries.Where(i => i != exclude && IsInstanceValid(i)).ToList(); }
    // ✅ 有效敌方目标：排除自身/已失效/阵亡/运输中/同队（P-1/P0 也不打自己队）
    public static bool IsLiveEnemyOf(Infantry reference, Infantry i)
        => i != null && i != reference && IsInstanceValid(i) && !i.IsQueuedForDeletion() && i.health > 0 && !i.isTransported
           && TeamHelper.IsEnemyForAttacker(reference.team, i.team, reference.canAttackFriendlies);
    public List<Infantry> GetEnemyInfantries(Infantry reference)
    {
        var list = infantries.Where(i => IsLiveEnemyOf(reference, i)).ToList();
        // ✅ issue#1 多格机动单位：足印占据者视为本格敌方单位（锚点格去重）
        var occ = multiTileOccupant;
        if (occ != null && !list.Contains(occ) && IsLiveEnemyOf(reference, occ))
            list.Add(occ);
        return list;
    }
    public bool HasEnemyInfantry(Infantry reference) { return infantries.Any(i => i != reference && IsInstanceValid(i) && TeamHelper.IsEnemyForAttacker(reference.team, i.team)); }

    // 格子上是否存在攻击者实际能造成伤害的敌方单位（伤害矩阵中有可用武器，含弹药检查）
    public bool HasAttackableEnemyInfantry(Infantry attacker, List<Grids> allGrids = null)
    {
        // allGrids 非空时追加感知判定（隐身/林地隐蔽中看不见的不算目标，防按钮泄漏位置）
        return GetEnemyInfantries(attacker).Any(e => attacker.SelectWeaponByMatrix(e) != WeaponType.None
            && (allGrids == null || Infantry.IsVisibleTo(e, attacker.team, allGrids)));
    }

    // ========== 地形绑定图标系统 ==========
    private void InitializeTerrainIcons()
    {
        if (terrainIconTextures == null || terrainIconTextures.Count == 0) return;

        foreach (var kvp in terrainIconTextures)
        {
            if (kvp.Value == null) continue;

            // 若已存在则复用（热重载或重复调用）
            string spriteName = $"TerrainIcon_{kvp.Key}";
            var existing = GetNodeOrNull<Sprite2D>(spriteName);
            if (existing != null)
            {
                existing.Texture = kvp.Value;
                existing.Visible = false;
                terrainIconSprites[kvp.Key] = existing;
                continue;
            }

            var sprite = new Sprite2D
            {
                Name = spriteName,
                Texture = kvp.Value,
                Visible = false,
                Centered = false,
                ZIndex = terrainIconZIndex
            };
            AddChild(sprite);
            terrainIconSprites[kvp.Key] = sprite;
        }
    }

    /// <summary>地形外观刷新(巨雷破坏/还原地板后调用)</summary>
    public void UpdateTileSprite()
    {
        UpdateTerrainIcon();
        ApplyCustomTerrainVisual();
    }

    public void UpdateTerrainIcon()
    {
        foreach (var kvp in terrainIconSprites)
        {
            if (IsInstanceValid(kvp.Value))
                kvp.Value.Visible = (kvp.Key == gridType);
        }
        UpdateDirectionVariant();
        UpdateCowTerrainIcon();
    }

    // ========== ✅ CoW 地形风格渲染(调色板查表) ==========
    /// <summary>✅ 全图换风格: 强制覆盖本格烘焙风格并重渲染(游戏内全局功能, 非编辑器画笔)</summary>
    public void RebakeCowStyle(int biome, int style)
    {
        cowBakedBiome = biome;
        cowBakedStyle = style;
        UpdateCowTerrainIcon();
    }

    private Sprite2D _cowTerrainSprite = null;
    private Sprite2D _cowBaseSprite = null;

    /// <summary>CoW 风格开启且该地形有映射时, 用编码贴图+调色板材质渲染并压住旧贴图</summary>
    public void UpdateCowTerrainIcon()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        // ✅ 懒烘焙: 从未烘焙的格子(-1, 三板斧/快速切换/旧档放置)先按当前全局风格补烘焙, 防自动优化渲染回默认
        if (cowBakedStyle < 0 || cowBakedBiome < 0) BakeCowBrush();
        bool cowOn = gm == null || gm.enableCowTerrainStyle; // 取不到gm(孤立预览)时默认开
        if (cowOn) TerrainStyleManager.ApplyFromGameManager(gm);

        // ✅ 烘焙冻结：已放地块保持放置时的风格；未烘焙(旧地图)用当前画笔并即刻冻结
        if (cowOn)
        {
            if (cowBakedBiome < 0) cowBakedBiome = (int)TerrainStyleManager.CurrentBiome;
            if (cowBakedStyle < 0) cowBakedStyle = (int)TerrainStyleManager.CurrentStyle;
        }

        // ✅ 基底层(不空底): 特征地形下垫基础地形; 无论主层是否走CoW(漩涡等旧贴图也垫)
        UpdateCowBaseLayer(cowOn);

        if (!cowOn || !TerrainStyleManager.TryGetCowMapping(gridType, out _, out _))
        {
            if (_cowTerrainSprite != null && IsInstanceValid(_cowTerrainSprite))
                _cowTerrainSprite.Visible = false;
            return;
        }

        string texPath = !string.IsNullOrEmpty(manualCowTexturePath) && ResourceLoader.Exists(manualCowTexturePath)
            ? manualCowTexturePath
            : TerrainStyleManager.ResolveCowTexturePath(gridType, terrainDirection);
        var mat = TerrainStyleManager.GetMaterialFor(gridType,
            (TerrainStyleManager.Biome)cowBakedBiome,
            (TerrainStyleManager.Style)cowBakedStyle);
        var tex = texPath != null ? ResourceLoader.Load<Texture2D>(texPath) : null;
        if (tex == null || mat == null)
        {
            if (_cowTerrainSprite != null && IsInstanceValid(_cowTerrainSprite))
                _cowTerrainSprite.Visible = false;
            return; // 解析失败: 保持旧贴图兜底
        }

        // 隐藏旧渲染(基础图标+旧方向变体)
        foreach (var kvp in terrainIconSprites)
            if (IsInstanceValid(kvp.Value)) kvp.Value.Visible = false;
        if (_directionVariantSprite != null && IsInstanceValid(_directionVariantSprite))
            _directionVariantSprite.Visible = false;

        if (_cowTerrainSprite == null || !IsInstanceValid(_cowTerrainSprite))
        {
            _cowTerrainSprite = new Sprite2D
            {
                Name = "CowTerrainSprite",
                Centered = false,
                ZIndex = terrainIconZIndex,
            };
            AddChild(_cowTerrainSprite);
        }
        // ✅ 底部对齐: 山地等超格高贴图向上延伸(CoW惯例), 水平居中 —— 修山地严重偏移
        _cowTerrainSprite.Position = new Godot.Vector2((32f - tex.GetWidth()) * 0.5f, 32f - tex.GetHeight());
        _cowTerrainSprite.Texture = tex;
        _cowTerrainSprite.Material = mat;
        _cowTerrainSprite.Visible = true;
    }

    /// <summary>✅ 编辑器放置/修改地形时调用: 把当前画笔风格烘焙进本格(覆盖旧烘焙)</summary>
    public void BakeCowBrush()
    {
        cowBakedBiome = (int)TerrainStyleManager.CurrentBiome;
        cowBakedStyle = (int)TerrainStyleManager.CurrentStyle;
    }

    /// <summary>基底层: 管道/树林/山地垫草地, 海滩/漩涡/陨石垫海洋(对齐CoW loadBaseTerrain)</summary>
    private void UpdateCowBaseLayer(bool cowOn)
    {
        var baseType = cowOn ? TerrainStyleManager.GetCowBaseTerrain(gridType) : null;
        if (baseType == null)
        {
            if (_cowBaseSprite != null && IsInstanceValid(_cowBaseSprite))
                _cowBaseSprite.Visible = false;
            return;
        }
        // 海基底用"开阔海"变体(邻接掩码全陆地→land=0)
        int baseDir = baseType == GridType.SEA ? 0b1111 : 0;
        string texPath = TerrainStyleManager.ResolveCowTexturePath(baseType.Value, baseDir);
        var mat = TerrainStyleManager.GetMaterialFor(gridType,
            (TerrainStyleManager.Biome)cowBakedBiome,
            (TerrainStyleManager.Style)cowBakedStyle);
        var tex = texPath != null ? ResourceLoader.Load<Texture2D>(texPath) : null;
        if (tex == null || mat == null)
        {
            if (_cowBaseSprite != null && IsInstanceValid(_cowBaseSprite))
                _cowBaseSprite.Visible = false;
            return;
        }
        if (_cowBaseSprite == null || !IsInstanceValid(_cowBaseSprite))
        {
            _cowBaseSprite = new Sprite2D
            {
                Name = "CowBaseSprite",
                Centered = false,
                ZIndex = terrainIconZIndex - 1, // 垫在主层/旧贴图之下
            };
            AddChild(_cowBaseSprite);
        }
        _cowBaseSprite.Texture = tex;
        _cowBaseSprite.Material = mat;
        _cowBaseSprite.Visible = true;
    }

    /// <summary>
    /// ✅ 多方向地形：根据 terrainDirection 位掩码加载方向变体贴图。
    /// 支持 PIPE / ROAD / BRIDGE；其它地形不显示变体。
    /// </summary>
    public void UpdateDirectionVariant()
    {
        string texPath = GetDirectionVariantPath();
        if (texPath == null)
        {
            if (_directionVariantSprite != null && IsInstanceValid(_directionVariantSprite))
                _directionVariantSprite.Visible = false;
            return;
        }

        if (_directionVariantSprite == null || !IsInstanceValid(_directionVariantSprite))
        {
            _directionVariantSprite = new Sprite2D
            {
                Name = "DirectionVariantSprite",
                Centered = false,
                ZIndex = terrainIconZIndex,
            };
            AddChild(_directionVariantSprite);
        }

        var tex = ResourceLoader.Load<Texture2D>(texPath);
        if (tex != null)
        {
            _directionVariantSprite.Texture = tex;
            _directionVariantSprite.Visible = true;
        }
        else
        {
            _directionVariantSprite.Visible = false;
        }
    }

    /// <summary>解析当前地形+方向对应的变体贴图路径；null=该地形无变体</summary>
    private string GetDirectionVariantPath()
    {
        string prefix = gridType switch
        {
            GridType.PIPE => "Pipe",
            GridType.ROAD => "Road",
            GridType.BRIDGE => "Bridge",
            GridType.RIVER => "River",
            GridType.PLASMA => "Plasma",   // ✅ 等离子：多方向变体（复用CW素材）
            GridType.CREEPER => "Creeper", // ✅ 蔓生藤蔓：多方向变体（复用CW素材）
            GridType.WALL => "Wall",           // ✅ 墙：多方向变体（复用CW素材）
            GridType.WEAK_WALL => "WeakWall",  // ✅ 脆弱墙：多方向变体
            GridType.DESERT_ROAD => "DesRoad",  // ✅ 三气候公路
            GridType.SNOW_ROAD => "SnowRoad",
            GridType.WASTE_ROAD => "WasteRoad",
            GridType.DESERT_PIPE => "DesPipe", // ✅ 三气候管道
            GridType.SNOW_PIPE => "SnowPipe",
            GridType.WASTE_PIPE => "WastePipe",
            _ => null
        };
        if (prefix == null) return null;

        if (gridType == GridType.BRIDGE)
        {
            bool hasH = (terrainDirection & 2) != 0 || (terrainDirection & 8) != 0;
            bool hasV = (terrainDirection & 1) != 0 || (terrainDirection & 4) != 0;
            return $"res://art/Grids/地形/{(hasV && !hasH ? "BridgeV" : "BridgeH")}.png";
        }

        string s = "";
        if ((terrainDirection & 1) != 0) s += "N";
        if ((terrainDirection & 2) != 0) s += "E";
        if ((terrainDirection & 4) != 0) s += "S";
        if ((terrainDirection & 8) != 0) s += "W";
        if (string.IsNullOrEmpty(s)) s = "0";
        return $"res://art/Grids/地形/{prefix}_{s}.png";
    }

    /// <summary>
    /// ✅ 从邻格自动计算方向位掩码（同类地形才算连通）。
    /// map: 全地图二维数组；map[x,y] 为 Grids 或 null。
    /// </summary>
    public static int ComputeDirectionFromNeighbors(Grids[,] map, int x, int y, GridType type)
    {
        if (type == GridType.BEACH) return ComputeBeachDirectionMask(map, x, y);
        int dir = 0, w = map.GetLength(0), h = map.GetLength(1);
        if (y > 0 && map[x, y - 1] != null && map[x, y - 1].gridType == type) dir |= 1;
        if (x < w - 1 && map[x + 1, y] != null && map[x + 1, y].gridType == type) dir |= 2;
        if (y < h - 1 && map[x, y + 1] != null && map[x, y + 1].gridType == type) dir |= 4;
        if (x > 0 && map[x - 1, y] != null && map[x - 1, y].gridType == type) dir |= 8;
        return dir;
    }
    private static bool IsSeaBased(GridType t) =>
        t is GridType.SEA or GridType.REEF or GridType.WHIRLPOOL or GridType.METEORITE or GridType.SEAFOG or GridType.BEACH;
    public static int ComputeBeachDirectionMask(Grids[,] map, int x, int y)
    {
        int w = map.GetLength(0), h = map.GetLength(1);
        Grids At(int gx, int gy) => (gx >= 0 && gx < w && gy >= 0 && gy < h) ? map[gx, gy] : null;
        int land = 0, beach = 0, diag = 0;
        var n=At(x,y-1); if(n!=null){if(!IsSeaBased(n.gridType))land|=1;if(n.gridType==GridType.BEACH)beach|=1;}
        var e=At(x+1,y); if(e!=null){if(!IsSeaBased(e.gridType))land|=2;if(e.gridType==GridType.BEACH)beach|=2;}
        var s=At(x,y+1); if(s!=null){if(!IsSeaBased(s.gridType))land|=4;if(s.gridType==GridType.BEACH)beach|=4;}
        var wg=At(x-1,y); if(wg!=null){if(!IsSeaBased(wg.gridType))land|=8;if(wg.gridType==GridType.BEACH)beach|=8;}
        var ne=At(x+1,y-1); if(ne!=null&&!IsSeaBased(ne.gridType))diag|=1;
        var nw=At(x-1,y-1); if(nw!=null&&!IsSeaBased(nw.gridType))diag|=2;
        var se=At(x+1,y+1); if(se!=null&&!IsSeaBased(se.gridType))diag|=4;
        var sw=At(x-1,y+1); if(sw!=null&&!IsSeaBased(sw.gridType))diag|=8;
        if(land!=0){
            if(land==4&&(beach&1)!=0)beach&=~1;if(land==1&&(beach&4)!=0)beach&=~4;
            if(land==2&&(beach&8)!=0)beach&=~8;if(land==8&&(beach&2)!=0)beach&=~2;
            return beach|(land<<4);
        }
        bool DiagOk(int d,int a,int b)=>diag==d&&(beach==0||beach==a||beach==b||beach==(a|b));
        if(DiagOk(1,1,2)||DiagOk(2,1,8)||DiagOk(4,2,4)||DiagOk(8,4,8))return beach|(diag<<8);
        bool SeaAt(int gx,int gy){var t=At(gx,gy);return t==null||IsSeaBased(t.gridType);}
        if(!SeaAt(x,y-2))beach&=~1;if(!SeaAt(x,y+2))beach&=~4;
        if(!SeaAt(x+2,y))beach&=~2;if(!SeaAt(x-2,y))beach&=~8;
        return beach;
    }
    public void RefreshDirectionFromNeighbors(Grids[,] map)
    {
        if (!autoOptimizeDirection) return;
        terrainDirection = ComputeDirectionFromNeighbors(map, GridIndex.X, GridIndex.Y, gridType);
    }

    public override void _Process(double delta)
    {
        if (gridType != _lastGridType || terrainDirection != _lastTerrainDirection)
        {
            _lastGridType = gridType;
            _lastTerrainDirection = terrainDirection;
            UpdateTerrainIcon();
            ApplyCustomTerrainVisual();
        }
    }

    /// <summary>
    /// 应用自定义地形颜色和贴图（如果配置了）
    /// </summary>
    public void ApplyCustomTerrainVisual()
    {
        var extraSave = ExtraSave.Instance;
        if (extraSave == null) return;

        string terrainKey = gridType.ToString();

        // 应用自定义颜色
        var customColor = extraSave.GetCustomTerrainColor(terrainKey);
        if (customColor.HasValue)
        {
            var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
            if (sprite != null)
            {
                sprite.Modulate = customColor.Value;
            }
        }

        // 应用自定义贴图
        var texturePath = extraSave.GetCustomTerrainTexturePath(terrainKey);
        if (!string.IsNullOrEmpty(texturePath) && Godot.FileAccess.FileExists(texturePath))
        {
            var image = new Image();
            if (image.Load(texturePath) == Error.Ok)
            {
                var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
                if (sprite != null)
                {
                    sprite.Texture = ImageTexture.CreateFromImage(image);
                }
            }
        }

        // ✅ 会话级贴图变体优先（编辑器"换贴图"功能，同型格子全局换肤）
        // 实际显示的地形图标是 terrainIconSprites(命名 TerrainIcon_{type}), grid.tscn 无 "Sprite2D" 子节点
        var sessionTex = TerrainEditor.GetSessionTerrainTexture(gridType);
        if (sessionTex != null)
        {
            if (terrainIconSprites.TryGetValue(gridType, out var sIcon) && IsInstanceValid(sIcon))
                sIcon.Texture = sessionTex;
        }
        else if (terrainIconSprites.TryGetValue(gridType, out var dIcon) && IsInstanceValid(dIcon)
                 && terrainIconTextures != null && terrainIconTextures.TryGetValue(gridType, out var defTex))
            dIcon.Texture = defTex; // 无会话变体时还原 grid.tscn 默认贴图
    }
}
