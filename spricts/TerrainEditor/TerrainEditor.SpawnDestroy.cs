using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · SpawnDestroy（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

// ========== ✅ 撤销系统辅助 ==========
private void PushEditorAction(EditorActionType type, Grids grid, GridType oldTerrain = GridType.GROUND,
    SavedUnitData oldUnit = null, SavedWeaponData oldWeapon = null, SavedFacilityData oldFacility = null)
{
    var helper = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;
    if (helper == null) return;
    var record = new EditorActionRecord
    {
        actionType = type,
        position = grid.GridIndex,
        oldTerrain = oldTerrain,
        newTerrain = grid.gridType,
        oldUnitData = oldUnit,
        oldWeaponData = oldWeapon,
        oldFacilityData = oldFacility
    };
    // ✅ 生成后调用：记录新状态
    if (grid.infantry != null) record.newUnitData = SerializeUnitForUndo(grid.infantry);
    if (grid.weapon != null) record.newWeaponData = SerializeWeaponForUndo(grid.weapon);
    if (grid.city != null) record.newFacilityData = SerializeFacilityForUndo(grid.city);
    helper.PushAction(record);
}

private SavedUnitData SerializeUnitForUndo(Infantry unit)
{
    if (unit == null) return null;
    return new SavedUnitData
    {
        scenePath = unit.SceneFilePath,
        typeName = unit.GetType().Name,
        team = unit.team
    };
}

private SavedWeaponData SerializeWeaponForUndo(Weapon weapon)
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
    if (weapon is BlackCannon bc) data.direction = (int)bc.direction;
    else if (weapon is DeathRay dr) data.direction = (int)dr.direction;
    else if (weapon is LargeCannon lc) data.direction = (int)lc.direction;
    return data;
}

private SavedFacilityData SerializeFacilityForUndo(Facility facility)
{
    if (facility == null) return null;
    return new SavedFacilityData
    {
        scenePath = facility.SceneFilePath,
        typeName = facility.GetType().Name,
        team = facility.facilityTeam
    };
}

private void SpawnUnitAtGrid(Grids grid, string scenePath, string team)
{
    if (string.IsNullOrEmpty(scenePath) || grid == null) return;

    var scene = GD.Load<PackedScene>(scenePath);
    if (scene == null)
    {
        spawnHintLabel.Text = $"❌ {TranslateGroup.T("错误：无法加载场景")}\n{scenePath}";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }

    var unit = scene.Instantiate<Infantry>();
    if (unit == null)
    {
        TranslateGroup.SetText(spawnHintLabel, "❌ 实例化失败");
        return;
    }

    // ✅ 开创人2026-11-11：模式0（Warside 自毁模式）单位不允许放置在非法地形——
    // 在移除旧单位「之前」检查：拒绝时地图原样。想做「山上的重坦」？把单位切宽容模式（默认即是）。
    if (unit.illegalTerrainBehavior == 0 && unit.GetMoveCostOnGrid(grid) >= 999)
    {
        unit.QueueFree();
        TranslateGroup.SetText(spawnHintLabel, "⛔ Warside 模式单位不能放置在非法地形（属性面板切「宽容」模式即可放置）");
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Orange);
        return;
    }

    try
    {
        // ✅ 无视一切直接放：兵器/设施不挡单位；同种类（单位）直接覆盖替换
        // ✅ 推送到撤销历史（生成后调用，但旧状态在生成前记录）
        var oldUnit = grid.infantry != null ? SerializeUnitForUndo(grid.infantry) : null;
        var oldWeapon = grid.weapon != null ? SerializeWeaponForUndo(grid.weapon) : null;
        var oldFacility = grid.city != null ? SerializeFacilityForUndo(grid.city) : null;
        int replacedUnits = 0;
        foreach (var old in grid.infantries.Where(u => u != null && IsInstanceValid(u)).ToList())
        {
            replacedUnits++;
            gameManager.RemoveUnit(old);
        }


        // ✅ 关键修复：强制重置所有回合状态，覆盖场景预设值
        unit.isMoved = false;
        unit.isAttacked = false;
        unit.hasActed = false;
        unit.state = UnitState.Idle;
        unit.movePoints = unit.defaultMovePoints;
        unit.originalGrid = null;

        unit.Position = grid.Position;
        unit.team = team;
        int unitNumber = gameManager.unitManager.AllUnits.Count + 1;
        string typeName = GetFriendlyTypeName(unit);
        unit.Name = $"{team}_{typeName}_{unitNumber}";

        gameManager.unitManager.units.AddChild(unit);

        bool bound = gameManager.unitManager.BindUnitToGrid(unit, true);
        if (!bound)
        {
            unit.QueueFree();
            // ✅ 从父节点中移除，防止 RefreshUnitList 遍历到已销毁对象
            if (unit.GetParent() == gameManager.unitManager.units)
            {
                gameManager.unitManager.units.RemoveChild(unit);
            }
            TranslateGroup.SetText(spawnHintLabel, "❌ 绑定格子失败");
            return;
        }

        // ✅ 关键修复：添加到 AllUnits 列表（编辑模式生成时缺少这一步）
        gameManager.unitManager.AllUnits.Add(unit);

        gameManager.RegisterUnit(unit, GetUnitCategory(unit));
        unit.OnClickPiece = gameManager.OnSelectPiece;
        unit.UpdateTeamVisual();
        unit.UpdateHpLabel();
        if (unit.sprite != null)
            unit.StartBreath();
        ApplySavedUnitData(unit);
        gameManager.UpdateUnitLists();
        gameManager.RefreshSpecializedUnitLists();

        spawnHintLabel.Text = $"✅ {TranslateGroup.T("生成成功")}: {unit.Name}"
            + (replacedUnits > 0 ? $"（{TranslateGroup.T("已替换")} {replacedUnits} {TranslateGroup.T("个原单位")}）" : "");
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

        // ✅ 生成后推送撤销记录（传入生成前记录的旧状态）
        PushEditorAction(EditorActionType.PlaceUnit, grid, grid.gridType, oldUnit, oldWeapon, oldFacility);

    }
    catch (Exception e)
    {
        spawnHintLabel.Text = $"❌ {TranslateGroup.T($"{TranslateGroup.T("生成失败")}")}: {e.Message}";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
    }
    // 导入时不在此处调用 RefreshUnitList，避免遍历已废弃对象
    // 统一在 ParseAndImportMap 结束后刷新
    gameManager.UpdateUnitLists();
}

private void SpawnWeaponAtGrid(Grids grid, string scenePath, string team)
{
    if (string.IsNullOrEmpty(scenePath) || grid == null) return;

    var scene = GD.Load<PackedScene>(scenePath);
    if (scene == null)
    {
        return;
    }

    // ✅ 旧地图兼容：卫星等步兵系（Infantry）单位曾被收录在兵器段，
    //    根节点不是 Weapon 时 Instantiate<Weapon> 会失败——自动改走单位层生成
    try
    {
        var probe = scene.Instantiate();
        bool isInfantryRoot = probe is Infantry;
        probe.QueueFree();
        if (isInfantryRoot)
        {
            SpawnUnitAtGrid(grid, scenePath, team);
            return;
        }
    }
    catch (Exception) { /* 探测失败则继续按兵器处理 */ }

    try
    {
        // ✅ 无视一切直接放：单位/设施不挡兵器；同种类（兵器）直接覆盖替换
        // ✅ 推送到撤销历史（生成后调用，但旧状态在生成前记录）
        var oldUnitW = grid.infantry != null ? SerializeUnitForUndo(grid.infantry) : null;
        var oldWeaponW = grid.weapon != null ? SerializeWeaponForUndo(grid.weapon) : null;
        var oldFacilityW = grid.city != null ? SerializeFacilityForUndo(grid.city) : null;

        var weapon = scene.Instantiate<Weapon>();
        if (weapon == null)
        {
            TranslateGroup.SetText(spawnHintLabel, "❌ 兵器实例化失败");
            return;
        }

        // ✅ 关键修复：重置兵器行动状态，覆盖场景预设值
        weapon.hasActed = false;

        // ✅ 关键修复：Instantiate 不会立即触发 _Ready，多格兵器属性需在 _Ready 前手动设置
        if (weapon is LargeCannon lc && !weapon.isMultiTile)
        {
            weapon.isMultiTile = true;
            weapon.size = lc.multiTileSize;
        }
        else if (weapon is DeathRay dr && !weapon.isMultiTile)
        {
            weapon.isMultiTile = true;
            weapon.size = dr.multiTileSize;
        }

        weapon.Position = grid.Position;
        weapon.team = team;
        weapon.Name = $"{team}_{weapon.GetType().Name}_{gameManager.weaponManager.AllWeapons.Count}";

        // ✅ 同种类覆盖替换：清掉新兵器将占据的所有格子上的旧兵器（多格兵器整体移除）
        // ✅ 撤销支持：移除前记录每个旧兵器的原锚点+序列化数据（多格兵器按原锚点完整恢复）
        var replacedWeapons = new List<Weapon>();
        var replacedAnchors = new List<Vector2I>();
        var replacedData = new List<SavedWeaponData>();
        var replacedOldUnits = new List<SavedUnitData>();
        var replacedOldFacilities = new List<SavedFacilityData>();
        foreach (var idx in weapon.GetOccupiedIndices(grid.GridIndex))
        {
            if (idx.X < 0 || idx.Y < 0) continue;
            if (gridManager?.map == null || idx.X >= gridManager.map.GetLength(0) || idx.Y >= gridManager.map.GetLength(1)) continue;
            var og = gridManager.map[idx.X, idx.Y];
            if (og == null) continue;
            foreach (var oldW in og.weapons.Where(w => w != null && IsInstanceValid(w)).ToList())
                if (!replacedWeapons.Contains(oldW)) replacedWeapons.Add(oldW);
        }
        foreach (var oldW in replacedWeapons)
        {
            // 记录旧兵器原锚点及其格子上的单位/设施（撤销时一并恢复）
            var oldAnchorGrid = oldW.grid;
            replacedAnchors.Add(oldAnchorGrid != null ? oldAnchorGrid.GridIndex : grid.GridIndex);
            replacedData.Add(SerializeWeaponForUndo(oldW));
            replacedOldUnits.Add(oldAnchorGrid != null && oldAnchorGrid.infantry != null ? SerializeUnitForUndo(oldAnchorGrid.infantry) : null);
            replacedOldFacilities.Add(oldAnchorGrid != null && oldAnchorGrid.city != null ? SerializeFacilityForUndo(oldAnchorGrid.city) : null);
            if (oldW.isMultiTile)
            {
                foreach (var og2 in oldW.occupiedGrids.ToList())
                {
                    if (og2 == null || !IsInstanceValid(og2)) continue;
                    og2.weapons.Remove(oldW);
                    if (og2.weapon == oldW) og2.weapon = og2.weapons.Count > 0 ? og2.weapons[0] : null;
                }
            }
            else if (oldW.grid != null)
            {
                oldW.grid.weapons.Remove(oldW);
                if (oldW.grid.weapon == oldW) oldW.grid.weapon = null;
            }
            oldW.occupiedGrids?.Clear();
            gameManager.weaponManager.AllWeapons.Remove(oldW);
            if (gameManager.selectedWeapon == oldW) gameManager.selectedWeapon = null;
            if (gameManager.weapons != null) gameManager.weapons.Remove(oldW);
            oldW.QueueFree();
        }

        gameManager.weaponManager.weaponsNode.AddChild(weapon);

        bool bound = gameManager.weaponManager.BindWeaponToGrid(weapon, true);
        if (!bound)
        {
            weapon.QueueFree();
            // ✅ 从父节点中移除，防止遍历到已销毁对象
            if (weapon.GetParent() == gameManager.weaponManager.weaponsNode)
            {
                gameManager.weaponManager.weaponsNode.RemoveChild(weapon);
            }
            TranslateGroup.SetText(spawnHintLabel, "❌ 兵器绑定格子失败");
            return;
        }
        gameManager.weaponManager.AllWeapons.Add(weapon);
        weapon.OnClickWeapon = gameManager.OnSelectWeapon;
        weapon.UpdateHpLabel();
        if (weapon is BlackCannon cannon)
        {
            cannon.direction = pendingCannonDirection;
            cannon.UpdateDirectionVisual();
            cannon.UpdateAmmoVisual();
        }
        if (weapon is GhostCannon gc)   // ✅ 幽灵炮: 方向选择器四向
        {
            gc.direction = pendingCannonDirection switch
            {
                BlackCannon.CannonDirection.Right => GhostCannon.GhostDirection.Right,
                BlackCannon.CannonDirection.Down  => GhostCannon.GhostDirection.Down,
                BlackCannon.CannonDirection.Left  => GhostCannon.GhostDirection.Left,
                _                                  => GhostCannon.GhostDirection.Up,
            };
            gc.UpdateDirectionVisual();
        }
        if (weapon is SuperFactory sf)   // ✅ BHF: 方向选择器四向（原缺分支，放置永远默认 Down）
            sf.direction = pendingCannonDirection;
        ApplySavedWeaponData(weapon);
        gameManager.UpdateUnitLists();

        spawnHintLabel.Text = $"✅ {TranslateGroup.T("生成兵器")}: {weapon.Name}"
            + (replacedWeapons.Count > 0 ? $"（{TranslateGroup.T("已替换")} {replacedWeapons.Count} {TranslateGroup.T("个旧兵器")}）" : "");
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

        // ✅ 生成后推送撤销记录（传入生成前记录的旧状态）
        if (replacedWeapons.Count == 0)
        {
            PushEditorAction(EditorActionType.PlaceWeapon, grid, grid.gridType, oldUnitW, oldWeaponW, oldFacilityW);
        }
        else
        {
            // ✅ 覆盖替换的完整撤销：每个被替换旧兵器一条子记录（在其原锚点，多格兵器按原锚点整体恢复）
            //    新兵器锚点子记录放最后：撤销时最后清掉新兵器并恢复锚点旧兵器，重做时最后重建新兵器
            var helper = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;
            if (helper != null)
            {
                var batch = new EditorActionRecord
                {
                    actionType = EditorActionType.PlaceWeapon,
                    position = grid.GridIndex,
                    oldTerrain = grid.gridType,
                    newTerrain = grid.gridType,
                    subActions = new List<EditorActionRecord>()
                };
                for (int i = 0; i < replacedWeapons.Count; i++)
                {
                    if (replacedAnchors[i] == grid.GridIndex) continue;   // 新锚点上的旧兵器并入锚点子记录
                    Grids ag = null;
                    if (replacedAnchors[i].X >= 0 && replacedAnchors[i].Y >= 0
                        && replacedAnchors[i].X < gridManager.map.GetLength(0) && replacedAnchors[i].Y < gridManager.map.GetLength(1))
                        ag = gridManager.map[replacedAnchors[i].X, replacedAnchors[i].Y];
                    var sub = new EditorActionRecord
                    {
                        actionType = EditorActionType.PlaceWeapon,
                        position = replacedAnchors[i],
                        oldTerrain = ag != null ? ag.gridType : grid.gridType,
                        newTerrain = ag != null ? ag.gridType : grid.gridType,
                        oldWeaponData = replacedData[i],
                        oldUnitData = replacedOldUnits[i],
                        oldFacilityData = replacedOldFacilities[i],
                        newUnitData = replacedOldUnits[i],
                        newFacilityData = replacedOldFacilities[i]
                    };
                    batch.subActions.Add(sub);
                }
                // 新兵器锚点子记录（最后执行）：旧=锚点上原有的兵器/单位/设施，新=刚放的新兵器
                SavedWeaponData anchorOld = oldWeaponW;
                for (int i = 0; i < replacedWeapons.Count; i++)
                    if (replacedAnchors[i] == grid.GridIndex) { anchorOld = replacedData[i]; break; }
                batch.subActions.Add(new EditorActionRecord
                {
                    actionType = EditorActionType.PlaceWeapon,
                    position = grid.GridIndex,
                    oldTerrain = grid.gridType,
                    newTerrain = grid.gridType,
                    oldUnitData = oldUnitW,
                    oldWeaponData = anchorOld,
                    oldFacilityData = oldFacilityW,
                    newWeaponData = SerializeWeaponForUndo(weapon)
                });
                helper.PushAction(batch);
            }
            else
            {
                PushEditorAction(EditorActionType.PlaceWeapon, grid, grid.gridType, oldUnitW, oldWeaponW, oldFacilityW);
            }
        }

    }
    catch (Exception e)
    {
        spawnHintLabel.Text = $"❌ {TranslateGroup.T($"{TranslateGroup.T("生成失败")}")}: {e.Message}";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
    }
}

private void DestroyTargetAtGrid(Grids grid)
{
    if (grid == null) return;

    // 检查是否点击了多格兵器
    var multiTileWeapon = grid.weapons.FirstOrDefault(w => w != null && IsInstanceValid(w) && w.isMultiTile);
    if (multiTileWeapon != null)
    {
        // 多格兵器：弹出整体确认销毁对话框
        ShowMultiTileDestroyConfirm(multiTileWeapon);
        return;
    }

    var allTargets = new List<object>();

    foreach (var unit in grid.infantries.ToList())
    {
        if (unit != null && IsInstanceValid(unit))
            allTargets.Add(unit);
    }

    foreach (var weapon in grid.weapons.ToList())
    {
        if (weapon != null && IsInstanceValid(weapon))
            allTargets.Add(weapon);
    }

    if (grid.city != null && IsInstanceValid(grid.city))
    {
        allTargets.Add(grid.city);
    }

    if (allTargets.Count == 0)
    {
        TranslateGroup.SetText(spawnHintLabel, "⚠️ 该格子没有可销毁的目标");
        return;
    }

    if (allTargets.Count == 1)
    {
        ExecuteDestroy(allTargets[0]);
    }
    else
    {
        ShowDestroySelectionMenu(grid, allTargets);
    }
}

    // 多格兵器整体确认销毁对话框
    private void ShowMultiTileDestroyConfirm(Weapon weapon)
    {
        var dialog = new Control();
        dialog.Name = "MultiTileDestroyDialog";
        dialog.ZIndex = 600;
        dialog.SetAnchorsPreset(Control.LayoutPreset.Center);

        var bg = new ColorRect();
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.Color = new Color(0, 0, 0, 0.6f);
        dialog.AddChild(bg);

        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(360, 200);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.98f);
        style.SetCornerRadiusAll(12);
        style.SetBorderWidthAll(2);
        style.BorderColor = new Color(0.8f, 0.2f, 0.2f);
        panel.AddThemeStyleboxOverride("panel", style);
        dialog.AddChild(panel);

        var title = new Label();
        title.Text = $"💀 {TranslateGroup.T("确认销毁多格兵器")}?";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
        title.Position = new Vector2(0, 20);
        title.Size = new Vector2(360, 35);
        panel.AddChild(title);

        var info = new Label();
        info.Text = $"{weapon.Name}\n{TranslateGroup.T("占据")} {weapon.size.X}×{weapon.size.Y} {TranslateGroup.T("格")}\n{TranslateGroup.T("摧毁后不可通行")}";
        info.HorizontalAlignment = HorizontalAlignment.Center;
        info.AddThemeFontSizeOverride("font_size", 12);
        info.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        info.Position = new Vector2(0, 60);
        info.Size = new Vector2(360, 50);
        panel.AddChild(info);

        var hbox = new HBoxContainer();
        hbox.Position = new Vector2(60, 130);
        hbox.Size = new Vector2(240, 40);
        hbox.AddThemeConstantOverride("separation", 20);

        var confirmBtn = new Button();
        TranslateGroup.SetText(confirmBtn, "💀 确认销毁");
        confirmBtn.CustomMinimumSize = new Vector2(100, 36);
        confirmBtn.Pressed += () => {
            dialog.QueueFree();
            ExecuteDestroy(weapon);
        };
        hbox.AddChild(confirmBtn);

        var cancelBtn = new Button();
        TranslateGroup.SetText(cancelBtn, "❌ 取消");
        cancelBtn.CustomMinimumSize = new Vector2(100, 36);
        cancelBtn.Pressed += () => dialog.QueueFree();
        hbox.AddChild(cancelBtn);

        panel.AddChild(hbox);
        GetTree().CurrentScene.AddChild(dialog);
    }

private void ExecuteDestroy(object target)
{
    try
    {
        if (target is Infantry unit)
        {
            string name = unit.Name;
            var grid = unit.grid;
            // ✅ 删除前记录旧状态
            var oldUnit = grid != null && grid.infantry != null ? SerializeUnitForUndo(grid.infantry) : null;
            var oldWeapon = grid != null && grid.weapon != null ? SerializeWeaponForUndo(grid.weapon) : null;
            var oldFacility = grid != null && grid.city != null ? SerializeFacilityForUndo(grid.city) : null;
            gameManager.RemoveUnit(unit);
            if (grid != null)
                PushEditorAction(EditorActionType.DeleteUnit, grid, grid.gridType, oldUnit, oldWeapon, oldFacility);
            spawnHintLabel.Text = $"💀 {TranslateGroup.T("已销毁单位")}: {name}";
        }
        else if (target is Weapon weapon)
        {
            string name = weapon.Name;
            if (weapon.isMultiTile)
            {
                // ✅ 多格兵器：记录所有占据格子的撤销
                var batchRecord = new EditorActionRecord
                {
                    actionType = EditorActionType.DeleteWeapon,
                    subActions = new List<EditorActionRecord>()
                };
                foreach (var og in weapon.occupiedGrids.ToList())
                {
                    if (og != null && IsInstanceValid(og))
                    {
                        var sub = new EditorActionRecord
                        {
                            actionType = EditorActionType.DeleteWeapon,
                            position = og.GridIndex,
                            oldTerrain = og.gridType,
                            newTerrain = og.gridType,
                            oldWeaponData = og.weapon == weapon ? SerializeWeaponForUndo(weapon) : null
                        };
                        batchRecord.subActions.Add(sub);
                        og.weapons.Remove(weapon);
                        if (og.weapon == weapon)
                            og.weapon = og.weapons.Count > 0 ? og.weapons[0] : null;
                    }
                }
                var helper = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;
                helper?.PushAction(batchRecord);
                weapon.occupiedGrids.Clear();
                
                // 从 GameManager 清理引用
                var gm = gameManager;
                if (gm != null)
                {
                    gm.weapons.Remove(weapon);
                    if (gm.selectedWeapon == weapon) gm.selectedWeapon = null;
                    if (gm.weaponManager?.AllWeapons.Contains(weapon) == true)
                        gm.weaponManager.AllWeapons.Remove(weapon);
                }
                
                weapon.QueueFree();
                spawnHintLabel.Text = $"💀 {TranslateGroup.T("已销毁多格兵器")}: {name}";
            }
            else
            {
                var grid = weapon.grid;
                // ✅ 删除前记录旧状态
                var oldUnit = grid != null && grid.infantry != null ? SerializeUnitForUndo(grid.infantry) : null;
                var oldWeapon = grid != null && grid.weapon != null ? SerializeWeaponForUndo(grid.weapon) : null;
                var oldFacility = grid != null && grid.city != null ? SerializeFacilityForUndo(grid.city) : null;
                gameManager.RemoveWeapon(weapon);
                if (grid != null)
                    PushEditorAction(EditorActionType.DeleteWeapon, grid, grid.gridType, oldUnit, oldWeapon, oldFacility);
                spawnHintLabel.Text = $"💀 {TranslateGroup.T("已销毁兵器")}: {name}";
            }
        }
        else if (target is Facility facility)
        {
            string name = facility.Name;
            var grid = facility.ParentGrid;
            // ✅ 删除前记录旧状态
            var oldUnit = grid != null && grid.infantry != null ? SerializeUnitForUndo(grid.infantry) : null;
            var oldWeapon = grid != null && grid.weapon != null ? SerializeWeaponForUndo(grid.weapon) : null;
            var oldFacility = grid != null && grid.city != null ? SerializeFacilityForUndo(grid.city) : null;
            if (grid != null)
            {
                grid.city = null;
            }
            facility.QueueFree(); // 编辑模式只删除摆图对象，不触发战斗死亡/尸化。
            if (grid != null)
                PushEditorAction(EditorActionType.DeleteFacility, grid, grid.gridType, oldUnit, oldWeapon, oldFacility);
            spawnHintLabel.Text = $"💀 {TranslateGroup.T("已销毁设施")}: {name}";
            gameManager.UpdateUnitLists();
        }

        spawnHintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        gameManager.UpdateUnitLists();
    }
    catch (Exception e)
    {
        spawnHintLabel.Text = $"❌ {TranslateGroup.T("销毁失败")}: {e.Message}";
    }
}

private void ShowDestroySelectionMenu(Grids grid, List<object> targets)
{
    foreach (var child in selectionButtonContainer.GetChildren())
        child.QueueFree();

    selectionTitleLabel.Text = $"💀 {TranslateGroup.T("选择要销毁的目标")} ({targets.Count}{TranslateGroup.T("个")})";

    foreach (var target in targets)
    {
        string displayName = "";
        Color color = Colors.White;
        Action onClick = null;

        if (target is Infantry unit)
        {
            string teamEmoji = unit.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            displayName = $"{teamEmoji} [{TranslateGroup.T("单位")}:{unit.GetType().Name}] {unit.Name} HP:{Mathf.CeilToInt(unit.health/10f)}";
            color = new Color(0.8f, 0.3f, 0.3f);
            onClick = () => {
                ExecuteDestroy(unit);
                CloseSelectionMenu();
            };
        }
        else if (target is Weapon weapon)
        {
            string teamEmoji = weapon.team switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            displayName = $"{teamEmoji} [{TranslateGroup.T("兵器")}:{weapon.GetType().Name}] {weapon.Name} HP:{Mathf.CeilToInt(weapon.health/10f)}";
            color = new Color(0.6f, 0.2f, 0.2f);
            onClick = () => {
                ExecuteDestroy(weapon);
                CloseSelectionMenu();
            };
        }
        else if (target is Facility facility)
        {
            string teamEmoji = facility.facilityTeam switch { "Player1" => "🔴", "Player2" => "🔵", "Player0" => "⚪", "Player" => "🟣", _ => "⚪" };
            displayName = $"{teamEmoji} [{TranslateGroup.T("设施")}:{facility.GetType().Name}] {facility.Name} (Team:{facility.facilityTeam})";
            color = new Color(0.8f, 0.5f, 0.2f);
            onClick = () => {
                ExecuteDestroy(facility);
                CloseSelectionMenu();
            };
        }

        var btn = CreateSelectionButton(displayName, color, onClick);
        AddLiveObjectIcon(btn, target);   // ✅ 销毁候选按钮文字前加对象实际 sprite 首帧小图标
        selectionButtonContainer.AddChild(btn);
    }

    var cancelBtn = CreateSelectionButton(TranslateGroup.T("❌ 取消"), new Color(0.5f, 0.5f, 0.5f), CloseSelectionMenu);
    selectionButtonContainer.AddChild(cancelBtn);

    var viewportSize = GetViewport().GetVisibleRect().Size;
    selectionMenuPanel.Position = new Vector2(
        (viewportSize.X - selectionMenuPanel.Size.X) / 2,
        (viewportSize.Y - selectionMenuPanel.Size.Y) / 2
    );
    selectionMenuPanel.Visible = true;
    isMenuOpen = true;
}

private UnitCategory GetUnitCategory(Infantry unit)
{
    return unit switch
    {
        Mech _ => UnitCategory.Mech,
        Oozium _ => UnitCategory.Oozium,
        LightTank _ => UnitCategory.Tank,
        Recon _ => UnitCategory.Recon,
        APC _ => UnitCategory.APC,
        Flare _ => UnitCategory.Vehicle,
        Bike _ => UnitCategory.Infantry,
        PipeRunner _ => UnitCategory.PipeRunner,
        Artillery _ => UnitCategory.Artillery,
        Rocket _ => UnitCategory.Rocket,
        FlyBomb _ => UnitCategory.FlyBomb,
                Fighter _ => UnitCategory.Fighter,
                Bomber _ => UnitCategory.Bomber,
                BCopter _ => UnitCategory.BCopter,
                TCopter _ => UnitCategory.TCopter,
                AntiAirMissile _ => UnitCategory.AntiAirMissile,
                Stealth _ => UnitCategory.Stealth,
                Dog _ => UnitCategory.Dog,
                MdTank _ => UnitCategory.MdTank,
        AntiAir _ => UnitCategory.AntiAir,
        AntiTank _ => UnitCategory.AntiTank,
        RepairBoat _ => UnitCategory.RepairBoat,
        Battleship _ => UnitCategory.Battleship,
        Cruiser _ => UnitCategory.Cruiser,
        Submarine _ => UnitCategory.Submarine,
        Lander _ => UnitCategory.Lander,
        Carrier _ => UnitCategory.Carrier,
        Gunboat _ => UnitCategory.Gunboat,
        Neotank _ => UnitCategory.Neotank,
        Megatank _ => UnitCategory.Megatank,
        WarTank _ => UnitCategory.WarTank,
        Rig _ => UnitCategory.Rig,
        Duster _ => UnitCategory.Duster,
        Seaplane _ => UnitCategory.Seaplane,
        ProducibleBoat _ => UnitCategory.ProducibleBoat,
        KamikazeBoat _ => UnitCategory.KamikazeBoat,
        Infantry _ => UnitCategory.Infantry,
        _ => UnitCategory.Other
    };
}

// ✅ 设施更改菜单：生成/摧毁/编辑设施
private void OpenFacilityMenu(Grids grid)
{
    CloseSelectionMenu();
    selectedGrid = grid;
    currentEditingTarget = grid.city;
    
    propertyEditorTitle.Text = $"{TranslateGroup.T("设施更改")} - {TranslateGroup.T("格子")} {grid.GridIndex}";
    
    // 清空属性容器
    foreach (var child in propertyContainer.GetChildren())
        child.QueueFree();
    
    if (grid.city == null)
    {
        // 没有设施：显示生成按钮
        var noFacilityLabel = new Label();
        TranslateGroup.SetText(noFacilityLabel, "当前格子没有设施");
        noFacilityLabel.AddThemeFontSizeOverride("font_size", 14);
        noFacilityLabel.AddThemeColorOverride("font_color", Colors.Gray);
        propertyContainer.AddChild(noFacilityLabel);
        
        var spawnBtn = new Button();
        TranslateGroup.SetText(spawnBtn, "🏙️ 生成 City 设施");
        spawnBtn.CustomMinimumSize = new Vector2(200, 40);
        spawnBtn.Pressed += () => SpawnFacilityAtGrid(grid);
        propertyContainer.AddChild(spawnBtn);
    }
    else
    {
        // 有设施：显示编辑 + 摧毁按钮
        var facilityLabel = new Label();
        facilityLabel.Text = $"{TranslateGroup.T("当前设施")}: {grid.city.GetType().Name}";
        facilityLabel.AddThemeFontSizeOverride("font_size", 14);
        facilityLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        propertyContainer.AddChild(facilityLabel);
        
        // 编辑设施属性
        BuildPropertyEditor(grid.city);
        
        // 摧毁按钮
        var destroyBtn = new Button();
        TranslateGroup.SetText(destroyBtn, "💥 摧毁设施");
        destroyBtn.CustomMinimumSize = new Vector2(200, 40);
        destroyBtn.AddThemeColorOverride("font_color", Colors.Red);
        destroyBtn.Pressed += () => DestroyFacilityAtGrid(grid);
        propertyContainer.AddChild(destroyBtn);
    }
    
    propertyEditorPanel.Visible = true;
}

// ✅ 在格子生成 City 设施（OpenFacilityMenu 用，默认 Player0）
private void SpawnFacilityAtGrid(Grids grid)
{
    SpawnFacilityAtGrid(grid, "res://Prefabs/City.tscn", "Player0");
    OpenFacilityMenu(grid);
}

// ✅ 在格子生成设施（spawnMenu 用，支持模板参数）
private void SpawnFacilityAtGrid(Grids grid, string scenePath, string team)
{
    // ✅ 无视一切直接放：单位/兵器不挡设施；同种类（设施）直接覆盖替换
    if (string.IsNullOrEmpty(scenePath))
    {
        TranslateGroup.SetText(spawnHintLabel, "❌ 场景路径为空");
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }
    
    var scene = GD.Load<PackedScene>(scenePath);
    if (scene == null)
    {
        GD.PushError($"[TerrainEditor] {TranslateGroup.T("无法加载场景")}: {scenePath}");
        spawnHintLabel.Text = $"❌ {TranslateGroup.T("无法加载场景")}: {scenePath}";
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }
    
    // ✅ 推送到撤销历史（生成后调用，但旧状态在生成前记录）
    var oldUnitF = grid.infantry != null ? SerializeUnitForUndo(grid.infantry) : null;
    var oldWeaponF = grid.weapon != null ? SerializeWeaponForUndo(grid.weapon) : null;
    var oldFacilityF = grid.city != null ? SerializeFacilityForUndo(grid.city) : null;
    
    // ✅ 同种类覆盖替换：旧设施直接替换
    bool replacedFacility = grid.city != null && IsInstanceValid(grid.city);
    if (replacedFacility)
    {
        grid.city.QueueFree();
        grid.city = null;
    }
    
    var facility = scene.Instantiate<Facility>();
    if (facility == null)
    {
        TranslateGroup.SetText(spawnHintLabel, "❌ 设施实例化失败");
        spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
        return;
    }
    
    facility.Name = "Facility";
    grid.AddChild(facility);
    grid.city = facility;
    
    // 设置势力
    facility.facilityTeam = team;
    facility.healAmount = 20;facility.flareAmmoSupply = 999;facility.explodeAmmoSupply = 999;facility.primaryAmmoSupply = 999;facility.fuelSupply = 999;
    facility.capturePointsRequired = 20;
    
    if (facility is City city)
    {
        city.UpdateCityVisual();
    }
    else
    {
        facility.UpdateCityVisual();
    }
    
    ApplySavedFacilityData(facility);
    spawnHintLabel.Text = $"✅ {TranslateGroup.T("生成设施")}: {facility.GetType().Name} ({team})"
        + (replacedFacility ? $"（{TranslateGroup.T("已替换旧设施")}）" : "");
    spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
    
    // ✅ 生成后推送撤销记录（传入生成前记录的旧状态）
    PushEditorAction(EditorActionType.PlaceFacility, grid, grid.gridType, oldUnitF, oldWeaponF, oldFacilityF);
    
    gameManager?.UpdateUnitLists();
    gameManager?.RefreshSpecializedUnitLists();
}

// ✅ 对称放置：在镜像位置生成相同实体
private void TrySpawnSymmetric(Grids originGrid, string scenePath, string team, SpawnMode mode)
{
    var mapHelper = GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;
    if (mapHelper == null || !mapHelper.IsSymmetryMode || mapHelper.symmetryAxisLine < 0)
        return;

    var mirroredPos = mapHelper.GetMirroredPosition(originGrid.GridIndex);
    if (mirroredPos == originGrid.GridIndex)
        return;

    int w = gridManager?.map?.GetLength(0) ?? 0;
    int h = gridManager?.map?.GetLength(1) ?? 0;
    if (mirroredPos.X < 0 || mirroredPos.X >= w || mirroredPos.Y < 0 || mirroredPos.Y >= h)
        return;

    var mirroredGrid = gridManager.map[mirroredPos.X, mirroredPos.Y];
    if (mirroredGrid == null)
        return;

    switch (mode)
    {
        case SpawnMode.Unit:
            SpawnUnitAtGrid(mirroredGrid, scenePath, team);
            break;
        case SpawnMode.Weapon:
            SpawnWeaponAtGrid(mirroredGrid, scenePath, team);
            break;
        case SpawnMode.Facility:
            SpawnFacilityAtGrid(mirroredGrid, scenePath, team);
            break;
    }
}

// ✅ 摧毁格子上的设施
private void DestroyFacilityAtGrid(Grids grid)
{
    if (grid.city == null) return;
    
    grid.city.QueueFree(); // 编辑模式不触发尸化。
    grid.city = null;
    
    
    // 刷新菜单
    OpenFacilityMenu(grid);
}
}
