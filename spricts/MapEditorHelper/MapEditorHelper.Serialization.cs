// MapEditorHelper.cs - 编辑器大更新：图形填充、区域填充、地形填充、覆盖全图、选区复制粘贴（矩形+不规则）、对称放置
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// MapEditorHelper · Serialization（由 MapEditorHelper.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class MapEditorHelper
{

    // ========== 序列化辅助 ==========
    private SavedUnitData SerializeUnit(Infantry unit)
    {
        if (unit == null) return null;
        return new SavedUnitData
        {
            scenePath = unit.SceneFilePath,
            typeName = unit.GetType().Name,
            team = unit.team,
            // 复制/粘贴保留全部 Join Export 与编组号；独立单位模板才在 SaveUnit 中归零编组号。
            fieldValues = ExtraSave.Instance?.SerializeObjectFields(unit) ?? new Dictionary<string, string>()
        };
    }

    private SavedWeaponData SerializeWeapon(Weapon weapon)
    {
        if (weapon == null) return null;
        var data = new SavedWeaponData
        {
            scenePath = weapon.SceneFilePath,
            typeName = weapon.GetType().Name,
            team = weapon.team,
            isMultiTile = weapon.isMultiTile,
            sizeX = weapon.size.X,
            sizeY = weapon.size.Y
        };
        var dirField = weapon.GetType().GetField("direction", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (dirField != null)
            data.direction = (int)dirField.GetValue(weapon);
        return data;
    }

    private SavedFacilityData SerializeFacility(Facility facility)
    {
        if (facility == null) return null;
        return new SavedFacilityData
        {
            scenePath = facility.SceneFilePath,
            typeName = facility.GetType().Name,
            team = facility.facilityTeam
        };
    }

    private void SpawnUnitFromData(Grids grid, SavedUnitData data)
    {
        if (string.IsNullOrEmpty(data.scenePath)) return;
        var scene = GD.Load<PackedScene>(data.scenePath);
        if (scene == null) return;
        var unit = scene.Instantiate<Infantry>();
        if (unit == null) return;
        unit.team = data.team;
        terrainEditor?.ApplyFieldValues(unit, data.fieldValues);

        // ✅ 正规流程：加到 units 节点 → 通过 UnitManager 绑定格子 → 注册到 AllUnits
        if (gameManager?.unitManager?.units != null)
            gameManager.unitManager.units.AddChild(unit);
        else
            grid.AddChild(unit); // 备用

        gameManager?.unitManager?.BindUnitToGrid(unit, true);

        if (gameManager?.unitManager?.AllUnits != null && !gameManager.unitManager.AllUnits.Contains(unit))
            gameManager.unitManager.AllUnits.Add(unit);

        unit.GlobalPosition = grid.GlobalPosition;
        gameManager?.RefreshJoinVisuals(grid);
    }

    private void SpawnWeaponFromData(Grids grid, SavedWeaponData data)
    {
        if (string.IsNullOrEmpty(data.scenePath)) return;

        // ✅ 多格兵器走 TerrainEditor 的正规绑定流程（避免重复实例化）
        if (data.isMultiTile && terrainEditor != null)
        {
            terrainEditor.SpawnSavedWeaponAtGrid(grid, data);
            return;
        }

        var scene = GD.Load<PackedScene>(data.scenePath);
        if (scene == null) return;
        var weapon = scene.Instantiate<Weapon>();
        if (weapon == null) return;
        weapon.team = data.team;
        weapon.isMultiTile = data.isMultiTile;
        weapon.size = new Vector2I(data.sizeX, data.sizeY);
        if (weapon is BlackCannon bc && data.direction != 0) bc.direction = (BlackCannon.CannonDirection)data.direction;
        else if (weapon is DeathRay dr && data.direction != 0) dr.direction = (DeathRay.CannonDirection)data.direction;
        else if (weapon is LargeCannon lc && data.direction != 0) lc.direction = (LargeCannon.CannonDirection)data.direction;

        // ✅ 单格兵器正规流程：加到 weaponsNode → 通过 WeaponManager 绑定格子 → 注册到 AllWeapons
        if (gameManager?.weaponManager?.weaponsNode != null)
            gameManager.weaponManager.weaponsNode.AddChild(weapon);
        else
            grid.AddChild(weapon); // 备用

        gameManager?.weaponManager?.BindWeaponToGrid(weapon, true);

        if (gameManager?.weaponManager?.AllWeapons != null && !gameManager.weaponManager.AllWeapons.Contains(weapon))
            gameManager.weaponManager.AllWeapons.Add(weapon);

        weapon.GlobalPosition = grid.GlobalPosition;
    }

    private void SpawnFacilityFromData(Grids grid, SavedFacilityData data)
    {
        if (string.IsNullOrEmpty(data.scenePath)) return;
        var scene = GD.Load<PackedScene>(data.scenePath);
        if (scene == null) return;
        var facility = scene.Instantiate<Facility>();
        if (facility == null) return;
        facility.facilityTeam = data.team;

        // ✅ 设施直接加到格子下
        grid.AddChild(facility);
        grid.city = facility;
        facility.GlobalPosition = grid.GlobalPosition;
    }
}
