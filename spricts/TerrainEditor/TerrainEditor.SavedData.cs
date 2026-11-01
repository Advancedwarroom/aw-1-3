using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · SavedData（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ✅ 新增：生成新地图
    private void GenerateNewMap(int width, int height)
    {
        if (gridManager == null)
        {
            ShowImportHint(TranslateGroup.T("❌ GridManager 未初始化"));
            return;
        }

        // 确认对话框
        var confirm = new ConfirmationDialog();
        confirm.Title = TranslateGroup.T("⚠️ 确认生成新地图");
        confirm.DialogText = $"{TranslateGroup.T("将生成")} {width}×{height} {TranslateGroup.T("的新地图，")}\n{TranslateGroup.T("当前所有单位、兵器、设施将被清空！")}\n\n{TranslateGroup.T("是否继续？")}";
        confirm.Size = new Vector2I(400, 200);
        confirm.Confirmed += () => {
            confirm.QueueFree();
            ExecuteGenerateMap(width, height);
        };
        confirm.Canceled += () => confirm.QueueFree();
        AddChild(confirm);
        confirm.PopupCentered();
    }

    private void ExecuteGenerateMap(int width, int height)
    {
        try
        {
            // 清空所有实体
            ClearAllMapEntities();

            // 调整地图大小
            gridManager.ResizeMap(width, height);
            gridManager.Init();

            // 所有格子默认设为平原
            foreach (var grid in gridManager.grids)
            {
                if (grid != null)
                {
                    grid.gridType = GridType.GROUND;
                    UpdateGridVisual(grid);
                }
            }

            // 重置游戏状态
            gameManager?.ResetGameState();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();

            ShowImportHint($"✅ {TranslateGroup.T("地图生成成功")}！\n尺寸: {width}×{height}");
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
        grid.fixedAmmoChangePerTurn = data.fixedAmmoChangePerTurn;
        grid.ammoCanOverMax = data.ammoCanOverMax;
        grid.ammoCanReachZero = data.ammoCanReachZero;
        grid.fixedFuelChangePerTurn = data.fixedFuelChangePerTurn;
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
