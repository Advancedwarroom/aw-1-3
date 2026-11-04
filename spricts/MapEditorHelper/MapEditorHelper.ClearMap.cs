// MapEditorHelper.cs - 编辑器大更新：图形填充、区域填充、地形填充、覆盖全图、选区复制粘贴（矩形+不规则）、对称放置
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// MapEditorHelper · ClearMap（由 MapEditorHelper.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class MapEditorHelper
{

    // ========== 覆盖全图状态 ==========
    public bool IsClearMapMode { get; private set; } = false;
    private GridType clearMapTerrain = GridType.GROUND;
    private bool clearMapRemoveEntities = true;

    // ========== 覆盖全图 ==========
    private void StartClearMapMode()
    {
        var dialog = new AcceptDialog();
        dialog.Title = TranslateGroup.T("⚠️ 确认覆盖全图?");
        dialog.DialogText = $"{TranslateGroup.T("这将把所有格子变为")} {clearMapTerrain}，{TranslateGroup.T("并删除所有单位")}/{TranslateGroup.T("兵器")}/{TranslateGroup.T("设施！")}\n\n{TranslateGroup.T("确定要继续吗？")}";
        dialog.Size = new Vector2I(400, 200);
        dialog.GetOkButton().Text = TranslateGroup.T("确认覆盖");
        dialog.GetOkButton().AddThemeColorOverride("font_color", Colors.Red);
        dialog.Confirmed += () => { ApplyClearMap(); dialog.QueueFree(); };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void ApplyClearMap()
    {
        if (gridManager?.map == null) return;
        int w = gridManager.map.GetLength(0);
        int h = gridManager.map.GetLength(1);
        var batchRecord = new EditorActionRecord
        {
            actionType = EditorActionType.ClearMap,
            subActions = new List<EditorActionRecord>()
        };

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                var g = gridManager.map[x, y];
                if (g == null) continue;
                var sub = new EditorActionRecord
                {
                    actionType = EditorActionType.ClearMap,
                    position = g.GridIndex,
                    oldTerrain = g.gridType
                };
                if (g.infantry != null) sub.oldUnitData = SerializeUnit(g.infantry);
                if (g.weapon != null) sub.oldWeaponData = SerializeWeapon(g.weapon);
                if (g.city != null) sub.oldFacilityData = SerializeFacility(g.city);
                batchRecord.subActions.Add(sub);

                if (clearMapRemoveEntities)
                {
                    if (g.infantry != null) { g.infantry.QueueFree(); g.infantry = null; g.infantries.Clear(); }
                    if (g.weapon != null) { g.weapon.QueueFree(); g.weapon = null; g.weapons.Clear(); }
                    if (g.city != null) { g.city.QueueFree(); g.city = null; }
                }
                g.gridType = clearMapTerrain;
                g.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
                g.UpdateTerrainIcon();
            }
        }
        PushAction(batchRecord);
        UpdateStatusHint($"{TranslateGroup.T("全图已覆盖为")} {clearMapTerrain}");
    }
}
