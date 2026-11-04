// MapEditorHelper.cs - 编辑器大更新：图形填充、区域填充、地形填充、覆盖全图、选区复制粘贴（矩形+不规则）、对称放置
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// MapEditorHelper · UndoRedo（由 MapEditorHelper.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class MapEditorHelper
{

    // ========== 撤销/前进 ==========
    private List<EditorActionRecord> actionHistory = new();
    private int currentHistoryIndex = -1;
    private const int MAX_HISTORY = 20;

    // ========== 撤销/前进系统 ==========
    public void PushAction(EditorActionRecord action)
    {
        if (currentHistoryIndex < actionHistory.Count - 1)
        {
            actionHistory.RemoveRange(currentHistoryIndex + 1, actionHistory.Count - currentHistoryIndex - 1);
        }
        action.timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        actionHistory.Add(action);
        if (actionHistory.Count > MAX_HISTORY)
        {
            actionHistory.RemoveAt(0);
        }
        else
        {
            currentHistoryIndex++;
        }
        UpdateHistoryLabel();
    }

    public void Undo()
    {
        if (currentHistoryIndex < 0) return;
        var action = actionHistory[currentHistoryIndex];
        UndoAction(action);
        currentHistoryIndex--;
        UpdateHistoryLabel();
        UpdateStatusHint($"{TranslateGroup.T("已撤销")}: {action.actionType}");
    }

    public void Redo()
    {
        if (currentHistoryIndex >= actionHistory.Count - 1) return;
        currentHistoryIndex++;
        var action = actionHistory[currentHistoryIndex];
        RedoAction(action);
        UpdateHistoryLabel();
        UpdateStatusHint($"{TranslateGroup.T("已前进")}: {action.actionType}");
    }

    private void UndoAction(EditorActionRecord action)
    {
        if (action.subActions != null && action.subActions.Count > 0)
        {
            foreach (var sub in action.subActions)
                UndoSingleAction(sub);
        }
        else
        {
            UndoSingleAction(action);
        }
    }

    private void RedoAction(EditorActionRecord action)
    {
        if (action.subActions != null && action.subActions.Count > 0)
        {
            foreach (var sub in action.subActions)
                RedoSingleAction(sub);
        }
        else
        {
            RedoSingleAction(action);
        }
    }

    private void UndoSingleAction(EditorActionRecord record)
    {
        if (gridManager?.map == null) return;
        int x = record.position.X, y = record.position.Y;
        if (x < 0 || x >= gridManager.map.GetLength(0) || y < 0 || y >= gridManager.map.GetLength(1))
            return;
        var g = gridManager.map[x, y];
        if (g == null) return;

        g.gridType = record.oldTerrain;
        g.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
        g.UpdateTerrainIcon();

        // ✅ 安全清理当前格子上的所有单位/兵器/设施（多格兵器特殊处理）
        ClearGridEntities(g);

        // ✅ 再从记录中恢复旧状态
        if (record.oldUnitData != null) SpawnUnitFromData(g, record.oldUnitData);
        if (record.oldWeaponData != null) SpawnWeaponFromData(g, record.oldWeaponData);
        if (record.oldFacilityData != null) SpawnFacilityFromData(g, record.oldFacilityData);
    }

    private void RedoSingleAction(EditorActionRecord record)
    {
        if (gridManager?.map == null) return;
        int x = record.position.X, y = record.position.Y;
        if (x < 0 || x >= gridManager.map.GetLength(0) || y < 0 || y >= gridManager.map.GetLength(1))
            return;
        var g = gridManager.map[x, y];
        if (g == null) return;

        g.gridType = record.newTerrain;
        g.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
        g.UpdateTerrainIcon();

        // ✅ 安全清理当前格子上的所有单位/兵器/设施（多格兵器特殊处理）
        ClearGridEntities(g);

        // ✅ 再从记录中恢复新状态（redo 后的状态）
        if (record.newUnitData != null) SpawnUnitFromData(g, record.newUnitData);
        if (record.newWeaponData != null) SpawnWeaponFromData(g, record.newWeaponData);
        if (record.newFacilityData != null) SpawnFacilityFromData(g, record.newFacilityData);
    }

    /// <summary>
    /// 安全清理格子上的所有实体。多格兵器会清理所有 occupiedGrids 的引用。
    /// </summary>
    private void ClearGridEntities(Grids g)
    {
        // 单位
        if (g.infantry != null) { g.infantry.QueueFree(); g.infantry = null; }
        g.infantries.Clear();

        // 兵器 - 多格兵器需要清理所有占据格子的引用
        if (g.weapon != null)
        {
            var wp = g.weapon;
            if (wp.isMultiTile && wp.occupiedGrids != null && wp.occupiedGrids.Count > 0)
            {
                foreach (var og in wp.occupiedGrids.ToList())
                {
                    if (og != null && IsInstanceValid(og))
                    {
                        og.weapons.Remove(wp);
                        if (og.weapon == wp)
                            og.weapon = og.weapons.Count > 0 ? og.weapons[0] : null;
                    }
                }
                wp.occupiedGrids.Clear();
            }
            wp.QueueFree();
            g.weapon = null;
        }
        g.weapons.Clear();

        // 设施
        if (g.city != null) { g.city.QueueFree(); g.city = null; }
    }

    private void UpdateHistoryLabel()
    {
        var label = helperPanel?.GetNodeOrNull<Label>("HelperVBox/HistoryLabel");
        if (label != null)
            label.Text = $"{TranslateGroup.T("历史")}: {currentHistoryIndex + 1}/{actionHistory.Count}";
    }
}
