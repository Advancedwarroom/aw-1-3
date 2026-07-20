// FogOfWarManager.cs - 战争迷雾系统核心管理器（v4 - 格子视野+全局视野池重构版）
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum VisionMode { Normal, Independent }

// ========== ✅ 照明弹效果数据结构 ==========
public class IlluminationEffect
{
    public Grids centerGrid;
    public int minRange;
    public int maxRange;
    public int remainingTurns;  // 剩余大回合数

    public IlluminationEffect(Grids center, int minR, int maxR, int duration)
    {
        centerGrid = center;
        minRange = minR;
        maxRange = maxR;
        remainingTurns = duration;
    }
}

public partial class FogOfWarManager : Node
{
    [Export] public bool isFogOfWarEnabled = true;
    [Export] public Color fogColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);
    [Export] public Color revealedColor = new Color(1, 1, 1, 0);

    private Dictionary<Grids, Sprite2D> fogSprites = new();
    private HashSet<Grids> visibleGridsThisTurn = new();
    public List<IlluminationEffect> activeIlluminations = new(); // ✅ 活跃照明弹效果

    private GameManager gameManager;
    private GridManager gridManager;
    private UnitManager unitManager;
    private WeaponManager weaponManager;

    private string currentTeam = "Player1";
    private int initRetryCount = 0;
    private const int MAX_INIT_RETRIES = 20;
    public bool isInitialized = false;

    public override void _Ready()
    {
        AddToGroup("fog_of_war_manager");
        CallDeferred(nameof(DeferredInit));
    }

    public void DeferredInit()
    {
        gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gameManager == null)
        {
            initRetryCount++;
            if (initRetryCount < MAX_INIT_RETRIES) { CallDeferred(nameof(DeferredInit)); return; }
        }

        gridManager = gameManager.gridManager;
        unitManager = gameManager.unitManager;
        weaponManager = gameManager.weaponManager;

        if (gridManager?.grids == null || gridManager.grids.Count == 0)
        {
            initRetryCount++;
            if (initRetryCount < MAX_INIT_RETRIES) { CallDeferred(nameof(DeferredInit)); return; }
        }

        CreateFogOverlay();
        isInitialized = true;
        if (isFogOfWarEnabled) CallDeferred(nameof(DelayedRefresh));
        else ClearAllFog();

    }

    private void DelayedRefresh() { RefreshFog(); }

    private ImageTexture CreateSolidTexture(Color color)
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, color);
        return ImageTexture.CreateFromImage(image);
    }

    // ========== ✅ 核心：刷新迷雾（全局视野池模式）==========
    public void RefreshFog()
    {
        if (!isFogOfWarEnabled || gridManager == null) { ClearAllFog(); return; }

        currentTeam = gameManager?.turnPhase == 1 ? "Player1" : "Player2";

        // ✅ AI 操控时，切换为 P-1 视角（玩家只能看到公开领域）
        var aiManager = GetTree()?.GetFirstNodeInGroup("ai_manager") as AI_Manager;
        if (aiManager != null && aiManager.IsAITeam(currentTeam))
        {
            currentTeam = TeamHelper.Player;
        }

        // ✅ 新：计算全局视野池（单位视野 + 格子视野共享）
        visibleGridsThisTurn = CalculateGlobalVisionPool(currentTeam);

        // ✅ 添加照明弹照亮的格子（所有队伍都可见）
        var illuminatedGrids = CalculateIlluminatedGrids();
        visibleGridsThisTurn.UnionWith(illuminatedGrids);

        if (visibleGridsThisTurn.Count == 0)
        {
            var buildingVision = CalculateBuildingVision(currentTeam);
            visibleGridsThisTurn.UnionWith(buildingVision);
        }

        ApplyFogToGrids();
        UpdateUnitVisibility();

    }

    // ✅ 核心新增：计算全局视野池（所有单位+格子视野合并）
    public HashSet<Grids> CalculateGlobalVisionPool(string team)
    {
        var visible = new HashSet<Grids>();
        if (gameManager == null) return visible;

        // 1. 收集所有视野提供者（单位+兵器）
        var visionProviders = GetVisionProviders(team);
        foreach (var provider in visionProviders)
        {
            if (provider?.Grid == null) continue;
            var providerVision = CalculateProviderVision(provider);
            visible.UnionWith(providerVision);
        }

        // 2. 收集所有格子视野（建筑+瞭望塔）
        var gridVision = CalculateAllGridVision(team);
        visible.UnionWith(gridVision);

        // 3. 应用特殊格子规则（如需要紧邻视野）
        ApplySpecialGridRulesToSet(visible, visionProviders);

        return visible;
    }

    // ✅ 计算所有格子提供的视野（建筑基础+瞭望塔）
    private HashSet<Grids> CalculateAllGridVision(string team)
    {
        var visible = new HashSet<Grids>();
        if (gridManager?.grids == null) return visible;

        foreach (var grid in gridManager.grids)
        {
            if (grid == null || !IsInstanceValid(grid)) continue;

            // 己方或P-1占领的建筑/格子才提供视野
            if (grid.city == null || string.IsNullOrEmpty(grid.city.facilityTeam)) continue;
            if (grid.city.facilityTeam != team && grid.city.facilityTeam != TeamHelper.Player) continue;

            // 计算该格子提供的视野
            var gridVision = grid.CalculateGridVision(gameManager);
            visible.UnionWith(gridVision);
        }

        return visible;
    }

    // 兼容旧方法
    public HashSet<Grids> CalculateAllVision(string team)
    {
        return CalculateGlobalVisionPool(team);
    }

    public HashSet<Grids> CalculateProviderVision(IVisionProvider provider)
    {
        var visible = new HashSet<Grids>();
        if (provider?.Grid == null || gridManager?.map == null) return visible;

        int baseRange = provider.GetVisionRange();
        if (baseRange < 0) baseRange = 0;

        int bonus = provider.GetVisionBonus();
        int totalRange = baseRange + bonus;

        if (totalRange <= 0) { visible.Add(provider.Grid); return visible; }

        if (provider.VisionMode == VisionMode.Independent && provider is WeaponVisionProvider wvp)
            visible = CalculateIndependentVision(wvp.Weapon, totalRange);
        else
            visible = CalculateManhattanVision(provider.Grid.GridIndex, totalRange);

        ApplySpecialGridRules(visible, provider.Grid.GridIndex);
        return visible;
    }

    private HashSet<Grids> CalculateManhattanVision(Vector2I center, int range)
    {
        var visible = new HashSet<Grids>();
        if (IsValidGrid(center)) { var selfGrid = gridManager.map[center.X, center.Y]; if (selfGrid != null) visible.Add(selfGrid); }
        for (int dx = -range; dx <= range; dx++)
            for (int dy = -range; dy <= range; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > range) continue;
                var pos = new Vector2I(center.X + dx, center.Y + dy);
                if (IsValidGrid(pos)) { var grid = gridManager.map[pos.X, pos.Y]; if (grid != null) visible.Add(grid); }
            }
        return visible;
    }

    private bool IsValidGrid(Vector2I pos)
    {
        return pos.X >= 0 && pos.X < gridManager.searchRange.X &&
               pos.Y >= 0 && pos.Y < gridManager.searchRange.Y;
    }

    private List<IVisionProvider> GetVisionProviders(string team)
    {
        var providers = new List<IVisionProvider>();
        if (unitManager?.AllUnits != null)
            foreach (var unit in unitManager.AllUnits)
                if (unit != null && IsInstanceValid(unit) && unit.health > 0)
                    if (unit.team == team || unit.team == TeamHelper.Player)
                        providers.Add(new UnitVisionProvider(unit));

        if (weaponManager?.AllWeapons != null)
            foreach (var weapon in weaponManager.AllWeapons)
                if (weapon != null && IsInstanceValid(weapon) && weapon.health > 0)
                    if (weapon.team == team || weapon.team == TeamHelper.Player)
                        providers.Add(new WeaponVisionProvider(weapon));
        return providers;
    }

    private HashSet<Grids> CalculateIndependentVision(Weapon weapon, int range)
    {
        var visible = new HashSet<Grids>();
        if (weapon?.grid == null) return visible;
        if (weapon is Laser laser) visible = CalculateLaserVision(laser, range);
        else if (weapon is BlackCannon cannon) visible = CalculateCannonVision(cannon, range);
        else visible = CalculateManhattanVision(weapon.grid.GridIndex, range);
        return visible;
    }

    private HashSet<Grids> CalculateLaserVision(Laser laser, int range)
    {
        var visible = new HashSet<Grids>();
        var center = laser.grid.GridIndex;
        Vector2I[] dirs = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };
        foreach (var dir in dirs)
        {
            for (int i = 0; i <= range; i++)
            {
                var pos = center + dir * i;
                if (!IsValidGrid(pos)) break;
                var g = gridManager.map[pos.X, pos.Y];
                if (g != null) visible.Add(g);
                if (g?.gridType == GridType.METEORITE) break;
            }
        }
        visible.Add(laser.grid);
        return visible;
    }

    private HashSet<Grids> CalculateCannonVision(BlackCannon cannon, int range)
    {
        var visible = new HashSet<Grids>();
        if (cannon?.grid == null) return visible;
        var start = cannon.grid.GridIndex;
        var dir = cannon.direction switch
        {
            BlackCannon.CannonDirection.Up => new Vector2I(0, -1),
            BlackCannon.CannonDirection.Right => new Vector2I(1, 0),
            BlackCannon.CannonDirection.Down => new Vector2I(0, 1),
            BlackCannon.CannonDirection.Left => new Vector2I(-1, 0),
            _ => new Vector2I(0, -1)
        };
        var perp = new Vector2I(dir.Y, -dir.X);
        for (int depth = 0; depth <= range; depth++)
        {
            Vector2I center = start + dir * depth;
            int halfWidth = depth;
            for (int w = -halfWidth; w <= halfWidth; w++)
            {
                var pos = center + perp * w;
                if (IsValidGrid(pos)) { var g = gridManager.map[pos.X, pos.Y]; if (g != null) visible.Add(g); }
            }
        }
        return visible;
    }

    private void ApplySpecialGridRules(HashSet<Grids> visible, Vector2I providerPos)
    {
        var toRemove = new List<Grids>();
        foreach (var grid in visible)
        {
            if (grid == null) continue;
            if (grid.requiresAdjacentVision)
            {
                int dist = Mathf.Abs(grid.GridIndex.X - providerPos.X) + Mathf.Abs(grid.GridIndex.Y - providerPos.Y);
                if (dist > 1) toRemove.Add(grid);
            }
        }
        foreach (var grid in toRemove) visible.Remove(grid);
    }

    // ✅ 对集合应用特殊规则（新版本）
    private void ApplySpecialGridRulesToSet(HashSet<Grids> visible, List<IVisionProvider> providers)
    {
        var toRemove = new List<Grids>();
        foreach (var grid in visible)
        {
            if (grid == null || !grid.requiresAdjacentVision) continue;

            // 检查是否有任何视野提供者紧邻该格子
            bool hasAdjacentProvider = false;
            foreach (var provider in providers)
            {
                if (provider?.Grid == null) continue;
                int dist = Mathf.Abs(grid.GridIndex.X - provider.Grid.GridIndex.X) + 
                          Mathf.Abs(grid.GridIndex.Y - provider.Grid.GridIndex.Y);
                if (dist <= 1) { hasAdjacentProvider = true; break; }
            }

            // 也检查格子自身是否紧邻（格子视野提供者自身就在格子上）
            if (!hasAdjacentProvider)
            {
                int dist = Mathf.Abs(grid.GridIndex.X - grid.GridIndex.X) + 
                          Mathf.Abs(grid.GridIndex.Y - grid.GridIndex.Y);
                if (dist <= 1) hasAdjacentProvider = true; // 格子自身
            }

            if (!hasAdjacentProvider) toRemove.Add(grid);
        }
        foreach (var grid in toRemove) visible.Remove(grid);
    }

    private HashSet<Grids> CalculateBuildingVision(string team)
    {
        var visible = new HashSet<Grids>();
        if (gridManager?.grids == null) return visible;
        foreach (var grid in gridManager.grids)
        {
            if (grid == null || !IsInstanceValid(grid)) continue;
            if (grid.city != null && (grid.city.facilityTeam == team || grid.city.facilityTeam == TeamHelper.Player) && TeamHelper.DoesFacilityProvideVision(grid.city.facilityTeam)) visible.Add(grid);
            if (grid.isWatchtower && grid.city != null && (grid.city.facilityTeam == team || grid.city.facilityTeam == TeamHelper.Player) && TeamHelper.DoesFacilityProvideVision(grid.city.facilityTeam))
            {
                visible.Add(grid);
                int towerRange = 1 + grid.visionBonus;
                var towerVision = CalculateManhattanVision(grid.GridIndex, towerRange);
                visible.UnionWith(towerVision);
            }
        }
        return visible;
    }

    private void ApplyFogToGrids()
    {
        foreach (var kvp in fogSprites)
        {
            var grid = kvp.Key; var sprite = kvp.Value;
            if (grid == null || !IsInstanceValid(grid) || sprite == null || !IsInstanceValid(sprite)) continue;
            bool isVisible = visibleGridsThisTurn.Contains(grid);
            sprite.Visible = isFogOfWarEnabled && !isVisible;
        }
    }

    private void UpdateUnitVisibility()
    {
        if (unitManager?.AllUnits != null)
            foreach (var unit in unitManager.AllUnits)
                if (unit != null && IsInstanceValid(unit)) unit.Visible = ShouldShowUnit(unit);
        if (weaponManager?.AllWeapons != null)
            foreach (var weapon in weaponManager.AllWeapons)
                if (weapon != null && IsInstanceValid(weapon)) weapon.Visible = ShouldShowWeapon(weapon);
    }

    private bool ShouldShowUnit(Infantry unit)
    {
        if (unit.isTransported) return false;
        if (unit.team == currentTeam) return true;
        if (unit.grid == null) return false;
        return visibleGridsThisTurn.Contains(unit.grid);
    }

    private bool ShouldShowWeapon(Weapon weapon)
    {
        if (weapon.team == currentTeam) return true;
        if (weapon.grid == null) return false;
        return visibleGridsThisTurn.Contains(weapon.grid);
    }

    private void ClearAllFog()
    {
        foreach (var sprite in fogSprites.Values)
            if (sprite != null && IsInstanceValid(sprite)) sprite.Visible = false;
    }

    public void SetFogOfWarEnabled(bool enabled)
    {
        isFogOfWarEnabled = enabled;
        if (!isInitialized || fogSprites == null || fogSprites.Count == 0)
        {
            if (gridManager?.grids != null && gridManager.grids.Count > 0)
            {
                CreateFogOverlay();
                if (fogSprites.Count > 0) isInitialized = true; else return;
            }
            else return;
        }
        if (enabled) RefreshFog();
        else { ClearAllFog(); ShowAllUnits(); }
    }

    private void ShowAllUnits()
    {
        if (unitManager?.AllUnits != null)
            foreach (var unit in unitManager.AllUnits)
                if (unit != null && IsInstanceValid(unit)) unit.Visible = true;
        if (weaponManager?.AllWeapons != null)
            foreach (var weapon in weaponManager.AllWeapons)
                if (weapon != null && IsInstanceValid(weapon)) weapon.Visible = true;
    }

    public void ForceRefresh()
    {
        if (!isInitialized) { DeferredInit(); return; }
        if (isFogOfWarEnabled) RefreshFog();
    }

    private void CreateFogOverlay()
    {
        if (gridManager?.grids == null) return;
        foreach (var kvp in fogSprites)
            if (kvp.Value != null && IsInstanceValid(kvp.Value)) kvp.Value.QueueFree();
        fogSprites.Clear();

        var fogTexture = CreateSolidTexture(fogColor);
        foreach (var grid in gridManager.grids)
        {
            if (grid == null || !IsInstanceValid(grid)) continue;
            var existing = grid.GetNodeOrNull<Sprite2D>("FogOverlay");
            if (existing != null) existing.QueueFree();
            var fogSprite = new Sprite2D();
            fogSprite.Name = "FogOverlay";
            fogSprite.Texture = fogTexture;
            fogSprite.Modulate = fogColor;
            float scaleX = gridManager.gridSize.X;
            float scaleY = gridManager.gridSize.Y;
            fogSprite.Scale = new Vector2(scaleX, scaleY);
            fogSprite.Centered = false;
            fogSprite.ZIndex = 50;
            fogSprite.Position = Vector2.Zero;
            fogSprite.Visible = isFogOfWarEnabled;
            grid.AddChild(fogSprite);
            fogSprites[grid] = fogSprite;
        }
    }

    public bool IsGridVisible(Grids grid)
    {
        if (!isFogOfWarEnabled) return true;
        if (grid == null) return false;
        return visibleGridsThisTurn.Contains(grid);
    }

    public bool IsGridVisibleForTeam(Grids grid, string team)
    {
        if (!isFogOfWarEnabled) return true;
        if (grid == null) return false;
        var vision = CalculateGlobalVisionPool(team);
        return vision.Contains(grid);
    }

    public void OnTurnChanged() { if (isFogOfWarEnabled) RefreshFog(); }
    public void OnUnitMoved() { if (isFogOfWarEnabled) RefreshFog(); }

    public string GetGridVisionStatus(Grids grid, string team)
    {
        if (!isFogOfWarEnabled) return "无迷雾";
        var vision = CalculateGlobalVisionPool(team);
        if (vision.Contains(grid)) return "视野内";
        if (grid.city != null && grid.city.facilityTeam == team) return "建筑视野";
        return "战争迷雾";
    }


    // ✅ 计算所有照明弹照亮的格子
    private HashSet<Grids> CalculateIlluminatedGrids()
    {
        var visible = new HashSet<Grids>();
        if (gridManager?.map == null) return visible;

        foreach (var effect in activeIlluminations)
        {
            if (effect?.centerGrid == null) continue;
            
            // 使用 Manhattan 距离计算照明范围（maxRange 外圈 - minRange 内圈）
            int maxRange = effect.maxRange;
            int minRange = effect.minRange;
            var center = effect.centerGrid.GridIndex;
            
            for (int dx = -maxRange; dx <= maxRange; dx++)
            {
                for (int dy = -maxRange; dy <= maxRange; dy++)
                {
                    int dist = Mathf.Abs(dx) + Mathf.Abs(dy);
                    if (dist > maxRange) continue;
                    if (dist < minRange) continue; // 排除内圈（与UI黄色范围一致）
                    var pos = new Vector2I(center.X + dx, center.Y + dy);
                    if (pos.X >= 0 && pos.X < gridManager.searchRange.X &&
                        pos.Y >= 0 && pos.Y < gridManager.searchRange.Y)
                    {
                        var g = gridManager.map[pos.X, pos.Y];
                        if (g != null) visible.Add(g);
                    }
                }
            }
        }
        return visible;
    }

    // ✅ 大回合结束时处理照明弹效果过期
    public void OnBigTurnEnd()
    {
        if (activeIlluminations.Count == 0) return;

        var expired = new List<IlluminationEffect>();
        foreach (var effect in activeIlluminations)
        {
            effect.remainingTurns--;
            if (effect.remainingTurns <= 0)
            {
                expired.Add(effect);
            }
        }

        foreach (var e in expired)
        {
            activeIlluminations.Remove(e);
        }

        if (expired.Count > 0 && isFogOfWarEnabled)
        {
            RefreshFog();
        }
    }

    // ✅ 添加新的照明弹效果
    public void AddIllumination(Grids center, int minR, int maxR, int duration)
    {
        if (center == null || duration <= 0) return;
        activeIlluminations.Add(new IlluminationEffect(center, minR, maxR, duration));
        if (isFogOfWarEnabled) RefreshFog();
    }

// ========== 视野提供者接口 ==========
public interface IVisionProvider
{
    Grids Grid { get; }
    int GetVisionRange();
    int GetVisionBonus();
    VisionMode VisionMode { get; }
}

public class UnitVisionProvider : IVisionProvider
{
    private Infantry unit;
    public Grids Grid => unit?.grid;
    public VisionMode VisionMode => VisionMode.Normal;

    public UnitVisionProvider(Infantry unit) { this.unit = unit; }

    public int GetVisionRange()
    {
        if (unit == null) return 0;
        return unit.ActualVisionRange;
    }

    public int GetVisionBonus()
    {
        if (unit == null || unit.grid == null) return 0;
        // ✅ 使用单位专属的地形加成（或全局回退）
        int bonus = 0;
        bonus += unit.GetTerrainVisionBonus(unit.grid.gridType);  // 单位×地形加成
        bonus += unit.grid.visionBonus;                           // 格子额外加成
        bonus += VisionConfig.GetUnitTypeVisionBonus(unit.GetType().Name); // 单位类型加成
        return bonus;
    }
}

public class WeaponVisionProvider : IVisionProvider
{
    private Weapon weapon;
    public Grids Grid => weapon?.grid;
    public Weapon Weapon => weapon;

    public VisionMode VisionMode
    {
        get
        {
            if (weapon == null) return VisionMode.Normal;
            if (weapon.visionMode == VisionMode.Independent) return VisionMode.Independent;
            if (VisionConfig.IsWeaponIndependentMode(weapon.GetType().Name)) return VisionMode.Independent;
            return VisionMode.Normal;
        }
    }

    public WeaponVisionProvider(Weapon weapon) { this.weapon = weapon; }

    public int GetVisionRange()
    {
        if (weapon == null) return 0;
        return weapon.ActualVisionRange;
    }

    public int GetVisionBonus()
    {
        if (weapon == null || weapon.grid == null) return 0;
        int bonus = 0;
        // ✅ 兵器也使用专属地形加成
        if (weapon is BlackCannon bc && bc.overrideGlobalTerrainBonus && bc.weaponTerrainVisionBonus != null)
        {
            if (bc.weaponTerrainVisionBonus.TryGetValue(weapon.grid.gridType, out int b)) bonus += b;
        }
        else if (weapon is Laser lz && lz.overrideGlobalTerrainBonus && lz.weaponTerrainVisionBonus != null)
        {
            if (lz.weaponTerrainVisionBonus.TryGetValue(weapon.grid.gridType, out int b)) bonus += b;
        }
        else
        {
            bonus += VisionConfig.GetWeaponTerrainVisionBonus(weapon.GetType().Name, weapon.grid.gridType);
        }
        bonus += weapon.grid.visionBonus;
        return bonus;
    }
}
}