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
}

public enum GridVisionMode { None, TowerCone, TowerRay }
public enum TowerDirection { Up, Right, Down, Left }

public partial class Grids : Node2D
{
    public Sprite2D attackRangeIcon;
    public Vector2I GridIndex;
    public Sprite2D pathIcon;
    [Export] public GridType gridType;
    public List<Infantry> infantries = new();
    public Infantry infantry;
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

    [ExportGroup("格子弹药/燃料系统")]
    [Export] public int fixedAmmoChangePerTurn = 0;
    [Export] public bool ammoCanOverMax = false;
    [Export] public bool ammoCanReachZero = true;
    [Export] public int fixedFuelChangePerTurn = 0;
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
    private int _lastTerrainDirection = -1;
    private Sprite2D _directionVariantSprite = null;

    [Export] public int gridVisionRange = 1;
    [Export] public TowerDirection towerDirection = TowerDirection.Up;
    [Export] public bool towerCanRotate = false;
    [Export] public float towerRayAngle = 0f;
    [Export] public bool gridVisionIgnoreTerrain = true;

    public Action OnMouseEnteredAttackGrid;
    public Action OnMouseExitedAttackGrid;

    public bool HasEnemyWeapon(string myTeam)
    {
        return weapons.Any(w => IsInstanceValid(w) && !w.IsQueuedForDeletion() && w.team != myTeam);
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
                bool isExemptLava = unit.moveType == MoveType.AirPlane || unit.moveType == MoveType.AirShip ||
                    unit.moveType == MoveType.AeroSpacer || unit.moveType == MoveType.SpaceShiper ||
                    unit.moveType == MoveType.LAVARUNNER || unit.moveType == MoveType.LAVAHOVER;
                if (unit.moveType == MoveType.Drone || unit.moveType == MoveType.HeliCopter) isExemptLava = false;
                if (!isExemptLava) { isPercentage = true; percentage = 0.10f; damageSource = "LAVA"; }
                break;
            case GridType.LAVASIDE:
                bool isExemptLavaSide = unit.moveType == MoveType.LAVAHOVER || unit.moveType == MoveType.AirPlane ||
                    unit.moveType == MoveType.AirShip || unit.moveType == MoveType.AeroSpacer ||
                    unit.moveType == MoveType.HeliCopter || unit.moveType == MoveType.SpaceShiper ||
                    unit.moveType == MoveType.LAVARUNNER;
                if (!isExemptLavaSide) { damage = 98; damageSource = "LAVASIDE"; }
                break;
            case GridType.LAVAFOG:
                bool isExemptLavaFOG = unit.moveType == MoveType.AeroSpacer || unit.moveType == MoveType.SpaceShiper;
                if (unit.moveType == MoveType.Drone || unit.moveType == MoveType.HeliCopter) isExemptLavaFOG = false;
                if (!isExemptLavaFOG) { isPercentage = true; percentage = 0.20f; damageSource = "LAVAFOG"; }
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
        if (unit == null || !IsInstanceValid(unit) || fixedAmmoChangePerTurn == 0) return;
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
                GridType.BROKENTRACK => 0.20f, GridType.LAVAFOG => 0.50f, _ => 0.0f
            };
        }
    }

    public string GetTerrainDefenseDescription() { float bonus = TerrainDefenseBonus * 100; return bonus > 0 ? $"{bonus:F0}%" : TranslateGroup.T("无"); }

    public List<Infantry> GetEnemyInfantries(string team) { return infantries.Where(i => i != null && TeamHelper.IsEnemyForAttacker(team, i.team)).ToList(); }

    private double lastClickTime = 0;
    private const double DOUBLE_CLICK_WINDOW = 0.3;
    public bool HasEnemyInfantry(string team) { return infantries.Any(i => i != null && IsInstanceValid(i) && TeamHelper.IsEnemyForAttacker(team, i.team)); }
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
           && i.team != reference.team && TeamHelper.IsEnemyForAttacker(reference.team, i.team);
    public List<Infantry> GetEnemyInfantries(Infantry reference) { return infantries.Where(i => IsLiveEnemyOf(reference, i)).ToList(); }
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

    public void UpdateTerrainIcon()
    {
        foreach (var kvp in terrainIconSprites)
        {
            if (IsInstanceValid(kvp.Value))
                kvp.Value.Visible = (kvp.Key == gridType);
        }
        UpdateDirectionVariant();
    }

    /// <summary>
    /// ✅ 多方向地形：根据 terrainDirection 位掩码加载方向变体贴图。
    /// 支持 PIPE / ROAD / BRIDGE；其它地形不显示变体。
    /// </summary>
    private void UpdateDirectionVariant()
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
        int dir = 0;
        int w = map.GetLength(0), h = map.GetLength(1);
        // N
        if (y > 0 && map[x, y - 1] != null && map[x, y - 1].gridType == type) dir |= 1;
        // E
        if (x < w - 1 && map[x + 1, y] != null && map[x + 1, y].gridType == type) dir |= 2;
        // S
        if (y < h - 1 && map[x, y + 1] != null && map[x, y + 1].gridType == type) dir |= 4;
        // W
        if (x > 0 && map[x - 1, y] != null && map[x - 1, y].gridType == type) dir |= 8;
        return dir;
    }

    /// <summary>刷新方向并更新渲染（地图加载/编辑后调用）</summary>
    public void RefreshDirectionFromNeighbors(Grids[,] map)
    {
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
    }
}
