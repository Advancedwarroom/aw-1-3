using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · MultiTileWeapon（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ========== ✅ 多格兵器放置预览模式 ==========
    private bool isMultiTilePreviewMode = false;
    private Weapon pendingMultiTileWeapon = null;
    private List<Grids> previewMultiTileGrids = new();
    private Vector2I pendingMultiTileAnchorPos = new Vector2I(-1, -1);
    private string pendingMultiTileScenePath = "";
    private string pendingMultiTileTeam = "";
    private BlackCannon.CannonDirection pendingCannonDirection = BlackCannon.CannonDirection.Up;
    public Grids selectedGrid = null;
    private object currentEditingTarget = null;
    private bool isMenuOpen = false;

    // ✅ 贴图应用模式
    private bool isTextureApplyMode = false;
    private string pendingTextureApplyPath = "";
    private string pendingTextureApplyTargetType = "";


    // ========== ✅ 多格兵器放置预览系统 ==========
    private void TryPreviewMultiTileWeapon(Grids grid, string scenePath, string team)
    {
        if (string.IsNullOrEmpty(scenePath) || grid == null) return;

        var scene = GD.Load<PackedScene>(scenePath);
        if (scene == null) return;

        var tempWeapon = scene.Instantiate<Weapon>();
        if (tempWeapon == null) return;

        // ✅ 关键修复：Instantiate 不会触发 _Ready，多格兵器的 isMultiTile 需要在 _Ready 中设置
        // 但临时实例化时 _Ready 还没执行，所以这里手动检测并设置
        if (tempWeapon is LargeCannon lc && !tempWeapon.isMultiTile)
        {
            tempWeapon.isMultiTile = true;
            tempWeapon.size = lc.multiTileSize;
        }
        if (tempWeapon is DeathRay dr && !tempWeapon.isMultiTile)
        {
            tempWeapon.isMultiTile = true;
            tempWeapon.size = dr.multiTileSize;
        }
        if (tempWeapon is BlackObelisk bo && !tempWeapon.isMultiTile)
        {
            tempWeapon.isMultiTile = true;
            tempWeapon.size = bo.multiTileSize;
        }

        // 如果不是多格兵器，直接生成（不走预览流程）
        if (!tempWeapon.isMultiTile)
        {
            tempWeapon.QueueFree();
            SpawnWeaponAtGrid(grid, scenePath, team);
            return;
        }

        // 计算预览范围
        var anchorPos = grid.GridIndex;
        var occupiedIndices = tempWeapon.GetOccupiedIndices(anchorPos);
        tempWeapon.QueueFree();

        // 检查是否超出地图限制
        var gm = gameManager;
        if (gm?.gridManager == null) return;
        foreach (var idx in occupiedIndices)
        {
            if (idx.X < 0 || idx.X >= gm.gridManager.searchRange.X ||
                idx.Y < 0 || idx.Y >= gm.gridManager.searchRange.Y)
            {
                TranslateGroup.SetText(spawnHintLabel, "❌ 超出地图范围，无法放置");
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
        }

        // ✅ 检查目标区域是否已被占据（兵器/非重叠单位）
        foreach (var idx in occupiedIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            // 已有兵器
            if (g.weapon != null && IsInstanceValid(g.weapon))
            {
                TranslateGroup.SetText(spawnHintLabel, "❌ 该区域已有兵器占据，无法放置");
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
            // 非重叠单位
            var blockingUnit = g.infantries.FirstOrDefault(u =>
                u != null && IsInstanceValid(u) && u.overlapType == UnitOverlapType.NonOverlapping);
            if (blockingUnit != null)
            {
                spawnHintLabel.Text = $"❌ {TranslateGroup.T("该区域被")} {blockingUnit.Name} {TranslateGroup.T("占据")}";
                spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
                return;
            }
        }

        // 清除之前的预览
        ClearMultiTilePreview();

        // 显示预览范围（蓝色攻击范围图标）
        previewMultiTileGrids.Clear();
        foreach (var idx in occupiedIndices)
        {
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g != null)
            {
                previewMultiTileGrids.Add(g);
                g.attackRangeIcon?.Show();
                if (g.attackRangeIcon != null)
                    g.attackRangeIcon.Modulate = new Color(0.3f, 0.6f, 1.0f, 0.8f); // 蓝色预览
            }
        }

        // 保存预览状态
        isMultiTilePreviewMode = true;
        pendingMultiTileAnchorPos = anchorPos;
        pendingMultiTileScenePath = scenePath;
        pendingMultiTileTeam = team;

        spawnHintLabel.Text = $"🔵 {TranslateGroup.T("预览")} {tempWeapon.GetType().Name} {occupiedIndices.Count}{TranslateGroup.T("格范围")}\n{TranslateGroup.T("再次点击确认放置，按")}ESC{TranslateGroup.T("取消")}";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1f));
    }

    private void ConfirmMultiTilePlacement()
    {
        if (!isMultiTilePreviewMode || string.IsNullOrEmpty(pendingMultiTileScenePath)) return;

        var gm = gameManager;
        if (gm?.gridManager == null) return;

        var anchorGrid = gm.gridManager.map[pendingMultiTileAnchorPos.X, pendingMultiTileAnchorPos.Y];
        if (anchorGrid == null)
        {
            ClearMultiTilePreview();
            return;
        }

        var scene = GD.Load<PackedScene>(pendingMultiTileScenePath);
        if (scene == null)
        {
            ClearMultiTilePreview();
            return;
        }

        var weapon = scene.Instantiate<Weapon>();
        if (weapon == null)
        {
            ClearMultiTilePreview();
            return;
        }

        // 摧毁预览范围内的所有单位、兵器、设施
        foreach (var g in previewMultiTileGrids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            // 摧毁单位
            foreach (var unit in g.infantries.ToList())
            {
                if (unit != null && IsInstanceValid(unit))
                {
                    gameManager.RemoveUnit(unit);
                }
            }
            // 摧毁兵器（排除即将放置的）
            foreach (var w in g.weapons.ToList())
            {
                if (w != null && IsInstanceValid(w) && w != weapon)
                {
                    gameManager.weaponManager.RemoveWeapon(w);
                }
            }
            // 摧毁设施
            if (g.city != null && IsInstanceValid(g.city))
            {
                g.city.QueueFree();
                g.city = null;
            }
        }

        weapon.Position = anchorGrid.Position;
        weapon.team = pendingMultiTileTeam;
        weapon.Name = $"{pendingMultiTileTeam}_{weapon.GetType().Name}_{gameManager.weaponManager.AllWeapons.Count}";

        gameManager.weaponManager.weaponsNode.AddChild(weapon);

        bool bound = gameManager.weaponManager.BindWeaponToGrid(weapon, true);
        if (!bound)
        {
            weapon.QueueFree();
            if (weapon.GetParent() == gameManager.weaponManager.weaponsNode)
                gameManager.weaponManager.weaponsNode.RemoveChild(weapon);
            TranslateGroup.SetText(spawnHintLabel, "❌ 多格兵器绑定失败");
            spawnHintLabel.AddThemeColorOverride("font_color", Colors.Red);
            ClearMultiTilePreview();
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
        if (weapon is DeathRay dr)
        {
            dr.direction = pendingCannonDirection;
            dr.UpdateDirectionVisual();
            dr.UpdateAmmoVisual();
            dr.UpdateMultiTileVisual();
        }
        if (weapon is LargeCannon lc)
        {
            lc.direction = pendingCannonDirection;
            lc.UpdateDirectionVisual();
            lc.UpdateAmmoVisual();
            lc.UpdateMultiTileVisual();
        }
        if (weapon is BlackObelisk bo2)
        {
            bo2.direction = BlackCannon.CannonDirection.Up; // 黑尖塔固定朝向
            bo2.UpdateDirectionVisual();
            bo2.UpdateMultiTileVisual();
        }

        gameManager.UpdateUnitLists();

        spawnHintLabel.Text = $"✅ {TranslateGroup.T("生成多格兵器")}: {weapon.Name} ({weapon.size.X}×{weapon.size.Y})";
        spawnHintLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));

        ClearMultiTilePreview();
    }

    private void ClearMultiTilePreview()
    {
        foreach (var g in previewMultiTileGrids)
        {
            if (g != null && IsInstanceValid(g))
            {
                g.attackRangeIcon?.Hide();
                if (g.attackRangeIcon != null)
                    g.attackRangeIcon.Modulate = new Color(1, 0.2f, 0.2f, 0.9f);
            }
        }
        previewMultiTileGrids.Clear();
        isMultiTilePreviewMode = false;
        pendingMultiTileWeapon = null;
        pendingMultiTileAnchorPos = new Vector2I(-1, -1);
        pendingMultiTileScenePath = "";
        pendingMultiTileTeam = "";
    }

    /// <summary>
    /// 绑定多格兵器到多个格子
    /// </summary>
    private void BindMultiTileWeapon(Weapon weapon, Grids anchorGrid)
    {
        if (weapon == null || anchorGrid == null || !weapon.isMultiTile) return;

        var gm = gameManager;
        if (gm?.gridManager == null) return;

        var anchorPos = anchorGrid.GridIndex;
        var occupied = weapon.GetOccupiedIndices(anchorPos);

        weapon.occupiedGrids.Clear();
        foreach (var idx in occupied)
        {
            if (idx.X >= 0 && idx.X < gm.gridManager.map.GetLength(0) && 
                idx.Y >= 0 && idx.Y < gm.gridManager.map.GetLength(1))
            {
                var g = gm.gridManager.map[idx.X, idx.Y];
                if (g != null)
                {
                    g.weapons.Add(weapon);
                    g.weapon = weapon;
                    weapon.occupiedGrids.Add(g);
                }
            }
        }

        weapon.grid = anchorGrid;
    }
}
