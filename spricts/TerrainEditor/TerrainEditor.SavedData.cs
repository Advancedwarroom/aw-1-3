using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · SavedData（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public enum MapShape { RECT, DIAMOND, CIRCLE, ELLIPSE, TRIANGLE_UP }   // 地图形状（开创人2026-11-07：菱/圆/椭圆/三角，方格近似）
public partial class TerrainEditor
{

    // ✅ 新增：生成新地图
    private void GenerateNewMap(int width, int height, int screens = 1, int[] screenWidths = null, int[] screenHeights = null, MapShape shape = MapShape.RECT)
    {
        if (gridManager == null)
        {
            ShowImportHint(TranslateGroup.T("❌ GridManager 未初始化"));
            return;
        }

        // 确认对话框
        var confirm = new ConfirmationDialog();
        confirm.Title = TranslateGroup.T("⚠️ 确认生成新地图");
        int dlgW = (screenWidths != null && screenWidths.Length == screens) ? screenWidths.Sum() + (screens - 1) * gridManager.screenGap : width * screens;
        int dlgH = (screenHeights != null && screenHeights.Length == screens) ? screenHeights.Max() : height;
        confirm.DialogText = $"{TranslateGroup.T("将生成")} {screens} {TranslateGroup.T("屏")}（{TranslateGroup.T("总宽")} {dlgW} × {TranslateGroup.T("总高")} {dlgH}）{TranslateGroup.T("的新地图，当前所有单位、兵器、设施将被清空！")}";
        confirm.Size = new Vector2I(400, 200);
        confirm.Confirmed += () => {
            confirm.QueueFree();
            ExecuteGenerateMap(width, height, screens, screenWidths, screenHeights, shape);
        };
        confirm.Canceled += () => confirm.QueueFree();
        AddChild(confirm);
        confirm.PopupCentered();
    }

    private void ExecuteGenerateMap(int width, int height, int screens = 1, int[] screenWidths = null, int[] screenHeights = null, MapShape shape = MapShape.RECT)
    {
        try
        {
            // 清空所有实体
            ClearAllMapEntities();

            // ✅ N屏：先设屏数再建图（横向条带均分）
            gridManager.screenCount = screens <= 0 ? 1 : screens;
            // ✅ 每屏独立宽：总宽=各屏宽之和；空数组=均分(旧行为)
            bool wMismatch = screenWidths != null && screenWidths.Length != gridManager.screenCount;
            bool hMismatch = screenHeights != null && screenHeights.Length != gridManager.screenCount;
            if (wMismatch || hMismatch)
                GD.PrintErr($"[多屏] ⚠️ 分屏设置数量与屏数不符（宽:{screenWidths?.Length ?? 0}/{gridManager.screenCount} 高:{screenHeights?.Length ?? 0}/{gridManager.screenCount}），已回退均分");
            gridManager.screenWidths = (screenWidths != null && screenWidths.Length == gridManager.screenCount) ? screenWidths : new int[0];
            gridManager.screenHeights = (screenHeights != null && screenHeights.Length == gridManager.screenCount) ? screenHeights : new int[0];
            int gap = gridManager.screenGap;
            int totalWidth = 0;
            if (gridManager.screenWidths.Length > 0) { foreach (var sw in gridManager.screenWidths) totalWidth += Math.Max(1, sw); }
            else totalWidth = width * gridManager.screenCount;
            totalWidth += gap * (gridManager.screenCount - 1);   // ✅ 屏间间隔带宽度（真分离）
            // 总高=各屏高度最大值（不拿全局高兜底——开创人2026-11-07：全局兜底导致矮屏被拉成一致高）
            int totalHeight = (gridManager.screenHeights.Length > 0) ? 1 : height;
            foreach (var sh in gridManager.screenHeights) totalHeight = Math.Max(totalHeight, sh);

            // 调整地图大小
            gridManager.ResizeMap(totalWidth, totalHeight);
            gridManager.Init();
            gridManager.RefreshScreenIds();   // ✅ 生成后立即刷新屏归属（间隔带无格子）

            // 所有格子默认设为平原（间隔带与矮屏下方天然无格子=真空）
            foreach (var grid in gridManager.grids)
            {
                if (grid != null)
                {
                    grid.gridType = GridType.GROUND;
                    UpdateGridVisual(grid);
                }
            }


            // ✅ 屏外挖真空：以引擎自己的 IsInsideAnyScreen 为准——间隔带/矮屏下方/Y偏移悬空区一律无格子（真物理分离）
            if (gridManager.screenCount > 1)
            {
                var outside = new System.Collections.Generic.List<Grids>();
                foreach (var g in gridManager.grids)
                    if (g != null && !gridManager.IsInsideAnyScreen(g.GridIndex.X, g.GridIndex.Y))
                        outside.Add(g);
                foreach (var g in outside)
                {
                    gridManager.grids.Remove(g);
                    gridManager.map[g.GridIndex.X, g.GridIndex.Y] = null;
                    g.QueueFree();
                }
                if (outside.Count > 0) gridManager.RefreshScreenIds();   // 挖空后重刷屏归属
            }


            // ✅ 形状挖空：菱/圆/椭圆/三角（中心=地图中心，半径=内切撑满；三角顶点朝上）
            if (shape != MapShape.RECT)
            {
                float cx = (totalWidth - 1) / 2f, cy = (totalHeight - 1) / 2f;
                float rx = totalWidth / 2f, ry = totalHeight / 2f;
                var shapeRemove = new System.Collections.Generic.List<Grids>();
                foreach (var g in gridManager.grids)
                {
                    if (g == null) continue;
                    float nx = (g.GridIndex.X - cx) / rx, ny = (g.GridIndex.Y - cy) / ry;
                    bool inside = shape switch
                    {
                        MapShape.DIAMOND => Mathf.Abs(nx) + Mathf.Abs(ny) <= 1f,
                        MapShape.CIRCLE or MapShape.ELLIPSE => nx * nx + ny * ny <= 1f,
                        MapShape.TRIANGLE_UP => ny >= -1f && ny <= 1f && Mathf.Abs(nx) <= (ny + 1f) / 2f,
                        _ => true,
                    };
                    if (!inside) shapeRemove.Add(g);
                }
                foreach (var g in shapeRemove)
                {
                    gridManager.grids.Remove(g);
                    gridManager.map[g.GridIndex.X, g.GridIndex.Y] = null;
                    g.QueueFree();
                }
            }

            // 重置游戏状态
            gameManager?.ResetGameState();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();

            ShowImportHint($"✅ {TranslateGroup.T("地图生成成功")}！\n{TranslateGroup.T("总尺寸")}: {totalWidth}×{totalHeight} × {gridManager.screenCount} {TranslateGroup.T("屏")}");
        }
        catch (Exception ex)
        {
            ShowImportHint($"❌ {TranslateGroup.T("生成失败")}: {ex.Message}");
        }
    }
        /// <summary>
    /// 从 ExtraSave 的保存槽位加载单位数据并应用到实例
    /// </summary>
    public void SpawnSavedUnitAtGrid(Grids grid, int slotIndex, string teamOverride = null)
    {
        var data = ExtraSave.Instance?.GetSavedUnit(slotIndex);
        if (data == null || string.IsNullOrEmpty(data.scenePath)) return;
        SpawnSavedUnitAtGrid(grid, data, teamOverride);
    }

    /// <summary>
    /// 从SavedUnitData直接生成单位
    /// </summary>
    public void SpawnSavedUnitAtGrid(Grids grid, SavedUnitData data, string teamOverride = null)
    {
        if (data == null || string.IsNullOrEmpty(data.scenePath)) return;

        var scene = GD.Load<PackedScene>(data.scenePath);
        if (scene == null) return;

        var unit = scene.Instantiate<Infantry>();
        if (unit == null) return;

        // 重置状态
        unit.isMoved = false;
        unit.isAttacked = false;
        unit.hasActed = false;
        unit.state = UnitState.Idle;
        unit.movePoints = unit.defaultMovePoints;
        unit.originalGrid = null;

        unit.Position = grid.Position;
        unit.team = teamOverride ?? data.team;
        unit.Name = data.displayName ?? $"SavedUnit";

        // 应用字段覆盖
        ApplyFieldValues(unit, data.fieldValues);

        // 应用攻防表
        if (data.attackMatrix != null && data.attackMatrix.Count > 0)
            unit.attackMatrix = ToGodotDict(data.attackMatrix);
        if (data.secondaryAttackMatrix != null && data.secondaryAttackMatrix.Count > 0)
            unit.secondaryAttackMatrix = ToGodotDict(data.secondaryAttackMatrix);

        // 应用自定义贴图
        if (data.useCustomTexture && !string.IsNullOrEmpty(data.customTexturePath))
        {
            ApplyCustomTextureToUnit(unit, data.customTexturePath);
        }

        gameManager.unitManager.units.AddChild(unit);
        gameManager.unitManager.BindUnitToGrid(unit, true);
        gameManager.unitManager.AllUnits.Add(unit);
        gameManager.RegisterUnit(unit, GetUnitCategory(unit));
        unit.OnClickPiece = gameManager.OnSelectPiece;
        unit.UpdateTeamVisual();
        unit.UpdateHpLabel();
        unit.StartBreath();

        gameManager.UpdateUnitLists();
        gameManager.RefreshSpecializedUnitLists();
    }

    /// <summary>
    /// 从 ExtraSave 的保存槽位加载兵器数据并应用到实例
    /// </summary>
    public void SpawnSavedWeaponAtGrid(Grids grid, int slotIndex, string teamOverride = null)
    {
        var data = ExtraSave.Instance?.GetSavedWeapon(slotIndex);
        if (data == null || string.IsNullOrEmpty(data.scenePath)) return;
        SpawnSavedWeaponAtGrid(grid, data, teamOverride);
    }

    /// <summary>
    /// 从SavedWeaponData直接生成兵器（支持多格兵器）
    /// </summary>
    public void SpawnSavedWeaponAtGrid(Grids grid, SavedWeaponData data, string teamOverride = null)
    {
        if (data == null || string.IsNullOrEmpty(data.scenePath)) return;

        var scene = GD.Load<PackedScene>(data.scenePath);
        if (scene == null) return;

        var weapon = scene.Instantiate<Weapon>();
        if (weapon == null) return;

        // 设置多格属性（在BindWeaponToGrid之前）
        if (data.isMultiTile)
        {
            weapon.isMultiTile = true;
            weapon.size = new Vector2I(data.sizeX, data.sizeY);

            if (weapon is LargeCannon lc)
            {
                lc.multiTileSize = new Vector2I(data.sizeX, data.sizeY);
            }
            else if (weapon is DeathRay dr)
            {
                dr.multiTileSize = new Vector2I(data.sizeX, data.sizeY);
            }
            else if (weapon is BlackObelisk bo)
            {
                bo.multiTileSize = new Vector2I(data.sizeX, data.sizeY);
            }
        }

        // 设置方向
        if (weapon is BlackCannon bc)
            bc.direction = (BlackCannon.CannonDirection)data.direction;
        else if (weapon is DeathRay dr2)
            dr2.direction = (BlackCannon.CannonDirection)data.direction;
        else if (weapon is LargeCannon lc2)
            lc2.direction = (BlackCannon.CannonDirection)data.direction;
        else if (weapon is SuperFactory sf2)   // ✅ BHF: 开局/加载恢复存档方向（原缺，开局永远 Down）
            sf2.direction = (BlackCannon.CannonDirection)data.direction;
        else if (weapon is GrandBolt gb2)   // ✅ 巨雷: 恢复方向——_Ready 时 RebindForDirection 按它旋足印/弱点
            gb2.direction = (BlackCannon.CannonDirection)data.direction;

        weapon.hasActed = false;
        weapon.Position = grid.Position;
        weapon.team = teamOverride ?? data.team;
        weapon.Name = data.displayName ?? $"SavedWeapon";

        // 应用字段覆盖
        ApplyFieldValues(weapon, data.fieldValues);

        // 应用自定义贴图
        if (data.useCustomTexture && !string.IsNullOrEmpty(data.customTexturePath))
        {
            ApplyCustomTextureToWeapon(weapon, data.customTexturePath);
        }

        gameManager.weaponManager.weaponsNode.AddChild(weapon);

        // 多格兵器需要特殊绑定
        if (weapon.isMultiTile)
        {
            BindMultiTileWeapon(weapon, grid);
        }
        else
        {
            gameManager.weaponManager.BindWeaponToGrid(weapon, true);
        }

        gameManager.weaponManager.AllWeapons.Add(weapon);
        weapon.OnClickWeapon = gameManager.OnSelectWeapon;
        weapon.UpdateHpLabel();

        // 更新方向视觉
        if (weapon is BlackCannon bc3) bc3.UpdateDirectionVisual();
        else if (weapon is DeathRay dr3) dr3.UpdateDirectionVisual();
        else if (weapon is LargeCannon lc3) lc3.UpdateDirectionVisual();

        gameManager.UpdateUnitLists();
    }

    /// <summary>
    /// 从 ExtraSave 的保存槽位加载设施数据并应用到实例
    /// </summary>
    public void SpawnSavedFacilityAtGrid(Grids grid, int slotIndex, string teamOverride = null)
    {
        var data = ExtraSave.Instance?.GetSavedFacility(slotIndex);
        if (data == null || string.IsNullOrEmpty(data.scenePath)) return;
        SpawnSavedFacilityAtGrid(grid, data, teamOverride);
    }

    /// <summary>
    /// 从SavedFacilityData直接生成设施
    /// </summary>
    public void SpawnSavedFacilityAtGrid(Grids grid, SavedFacilityData data, string teamOverride = null)
    {
        if (data == null || string.IsNullOrEmpty(data.scenePath)) return;

        var scene = GD.Load<PackedScene>(data.scenePath);
        if (scene == null) return;

        var facility = scene.Instantiate<Facility>();
        if (facility == null) return;

        facility.Name = "Facility";
        grid.AddChild(facility);
        grid.city = facility;

        facility.facilityTeam = teamOverride ?? data.team;

        // 应用字段覆盖
        ApplyFieldValues(facility, data.fieldValues);

        // 应用自定义贴图
        if (data.useCustomTexture && !string.IsNullOrEmpty(data.customTexturePath))
        {
            ApplyCustomTextureToFacility(facility, data.customTexturePath);
        }

        facility.UpdateCityVisual();

        gameManager.UpdateUnitLists();
        gameManager.RefreshSpecializedUnitLists();
    }

    /// <summary>
    /// 从 ExtraSave 的保存槽位加载地形数据并应用到格子
    /// </summary>
    public void ApplySavedGridTerrain(Grids grid, int slotIndex)
    {
        var data = ExtraSave.Instance?.GetSavedGridTerrain(slotIndex);
        if (data == null || data.savedTimestamp == 0) return;
        ApplySavedGridTerrain(grid, data);
    }

    /// <summary>
    /// 从SavedGridTerrainData直接应用地形
    /// </summary>
    public void ApplySavedGridTerrain(Grids grid, SavedGridTerrainData data)
    {
        if (data == null || data.savedTimestamp == 0) return;

        grid.gridType = data.gridType;
        grid.canDestroyUnit = data.canDestroyUnit;
        grid.fixedDamagePerTurn = data.fixedDamagePerTurn;
        grid.fixedAttackPerTurn = data.fixedAttackPerTurn;
        grid.canOverMaxHealth = data.canOverMaxHealth;
        grid.fixedMaxHealthChangePerTurn = data.fixedMaxHealthChangePerTurn;
        grid.fixedAmmoChangePerTurn = data.fixedAmmoChangePerTurn;
        grid.ammoCanOverMax = data.ammoCanOverMax;
        grid.ammoCanReachZero = data.ammoCanReachZero;
        grid.ammoChangePrimary = data.ammoChangePrimary;
        grid.ammoChangeExplode = data.ammoChangeExplode;
        grid.ammoChangeFlare = data.ammoChangeFlare;
        grid.ammoChangeStealth = data.ammoChangeStealth;
        grid.ammoChangeReveal = data.ammoChangeReveal;
        grid.ammoChangeCamo = data.ammoChangeCamo;
        grid.ammoChangeProduction = data.ammoChangeProduction;
        grid.ammoChangeConstruction = data.ammoChangeConstruction;
        grid.ammoMaxChangePrimary = data.ammoMaxChangePrimary;
        grid.ammoMaxChangeExplode = data.ammoMaxChangeExplode;
        grid.ammoMaxChangeFlare = data.ammoMaxChangeFlare;
        grid.ammoMaxChangeStealth = data.ammoMaxChangeStealth;
        grid.ammoMaxChangeReveal = data.ammoMaxChangeReveal;
        grid.ammoMaxChangeCamo = data.ammoMaxChangeCamo;
        grid.ammoMaxChangeProduction = data.ammoMaxChangeProduction;
        grid.ammoMaxChangeConstruction = data.ammoMaxChangeConstruction;
        grid.fixedFuelChangePerTurn = data.fixedFuelChangePerTurn;
        grid.fixedMaxFuelChangePerTurn = data.fixedMaxFuelChangePerTurn;
        grid.fuelCanOverMax = data.fuelCanOverMax;
        grid.fuelCanReachZero = data.fuelCanReachZero;
        grid.gridVisionRange = data.gridVisionRange;
        grid.gridVisionMode = (GridVisionMode)data.gridVisionMode;
        grid.towerDirection = (TowerDirection)data.towerDirection;
        grid.towerCanRotate = data.towerCanRotate;
        grid.towerRayAngle = data.towerRayAngle;
        grid.gridVisionIgnoreTerrain = data.gridVisionIgnoreTerrain;
        grid.visionBonus = data.visionBonus;
        grid.requiresAdjacentVision = data.requiresAdjacentVision;
        grid.isWatchtower = data.isWatchtower;

        // 应用自定义贴图
        if (data.useCustomTexture && !string.IsNullOrEmpty(data.customTexturePath))
        {
            ApplyCustomTextureToGrid(grid, data.customTexturePath);
        }

        UpdateGridVisual(grid);
    }

    // ==================== 辅助方法 ====================

    private void ApplyFieldValues(object target, Dictionary<string, string> fieldValues)
    {
        if (target == null || fieldValues == null || fieldValues.Count == 0) return;

        var type = target.GetType();
        foreach (var kvp in fieldValues)
        {
            var field = type.GetField(kvp.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                try
                {
                    object value = ParseValueFromString(kvp.Value, field.FieldType);
                    if (value != null) field.SetValue(target, value);
                }
                catch (System.Exception ex) { GD.PushWarning($"[TerrainEditor] {ex.GetType().Name}: {ex.Message}"); }
            }
            else
            {
                var prop = type.GetProperty(kvp.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        object value = ParseValueFromString(kvp.Value, prop.PropertyType);
                        if (value != null) prop.SetValue(target, value);
                    }
                    catch (System.Exception ex) { GD.PushWarning($"[TerrainEditor] {ex.GetType().Name}: {ex.Message}"); }
                }
            }
        }
    }

    private object ParseValueFromString(string str, Type targetType)
    {
        if (targetType == typeof(string)) return str;
        if (targetType == typeof(int) && int.TryParse(str, out int i)) return i;
        if (targetType == typeof(float) && float.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float f)) return f;
        if (targetType == typeof(bool))
        {
            if (bool.TryParse(str, out bool b)) return b;
            if (str == "1" || str.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (str == "0" || str.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (targetType.IsEnum && int.TryParse(str, out int e)) return e;
        if (targetType == typeof(Color)) return new Color(str);
        return null;
    }

    private Godot.Collections.Dictionary<string, int> ToGodotDict(Dictionary<string, int> dict)
    {
        var result = new Godot.Collections.Dictionary<string, int>();
        foreach (var kvp in dict) result[kvp.Key] = kvp.Value;
        return result;
    }

    private void ApplyCustomTextureToUnit(Infantry unit, string path)
    {
        if (unit.sprite != null && Godot.FileAccess.FileExists(path))
        {
            var image = new Image();
            if (image.Load(path) == Error.Ok)
                unit.sprite.Texture = ImageTexture.CreateFromImage(image);
        }
    }

    private void ApplyCustomTextureToWeapon(Weapon weapon, string path)
    {
        // 根据 Weapon 的实际贴图组件调整
        if (weapon.animSprite != null && Godot.FileAccess.FileExists(path))
        {
            var image = new Image();
            if (image.Load(path) == Error.Ok)
            {
                // 如果是 GIF 可能需要 AnimatedTexture，这里简化处理
                // weapon.animSprite.SpriteFrames = ... 
            }
        }
    }

    private void ApplyCustomTextureToFacility(Facility facility, string path)
    {
        // 根据 Facility 的实际贴图组件调整
    }

    private void ApplyCustomTextureToGrid(Grids grid, string path)
    {
        if (Godot.FileAccess.FileExists(path))
        {
            var image = new Image();
            if (image.Load(path) == Error.Ok)
            {
                var sprite = grid.GetNodeOrNull<Sprite2D>("Sprite2D");
                if (sprite != null)
                    sprite.Texture = ImageTexture.CreateFromImage(image);
            }
        }
    }


    // ==================== 保存数据应用方法 ====================

    /// <summary>
    /// 从 ExtraSave 的保存数据应用到单位实例
    /// </summary>
    private void ApplySavedUnitData(Infantry unit)
    {
        if (unit == null) return;
        // 占位实现 - 可根据需要从 ExtraSave 加载数据
    }

    /// <summary>
    /// 从 ExtraSave 的保存数据应用到兵器实例
    /// </summary>
    private void ApplySavedWeaponData(Weapon weapon)
    {
        if (weapon == null) return;
        // 占位实现 - 可根据需要从 ExtraSave 加载数据
    }

    /// <summary>
    /// 从 ExtraSave 的保存数据应用到设施实例
    /// </summary>
    private void ApplySavedFacilityData(Facility facility)
    {
        if (facility == null) return;
        // 占位实现 - 可根据需要从 ExtraSave 加载数据
    }
}
