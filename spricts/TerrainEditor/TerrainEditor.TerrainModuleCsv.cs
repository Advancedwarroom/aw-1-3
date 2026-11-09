// TerrainEditor.TerrainModuleCsv.cs
// 地形模块CSV配置面板: 完整属性编辑器内直接输入配置(扩散黑名单/摧毁链/传送绑定/视野加成/隐蔽), 对标"万物可控"的数据直改
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class TerrainEditor
{
    private VBoxContainer moduleCsvContainer;
    private Grids moduleCsvTarget;
    private LineEdit spreadTerrainEdit, spreadWeaponEdit, spreadBuildingEdit, spreadUnitEdit;
    private LineEdit destroyTerrainEdit, destroyUnitEdit, destroyBuildingEdit, destroyWeaponEdit;
    private LineEdit visionBlacklistEdit;
    private SpinBox spreadCooldownSpin, spreadCapSpin, spreadManhattanSpin, visionMinSpin, visionMaxSpin, visionAmountSpin;
    private CheckBox spreadEnableCheck, spreadCapCheck, hideUnitsCheck, visionEnableCheck, cannotStopCheck;

    private LineEdit MakeCsvRow(string label, string value, VBoxContainer parent)
    {
        var row = new HBoxContainer();
        var lab = new Label { Text = TranslateGroup.T(label), CustomMinimumSize = new Vector2(118, 22) };
        lab.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(lab);
        var edit = new LineEdit { Text = value ?? "", CustomMinimumSize = new Vector2(160, 22) };
        edit.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(edit);
        parent.AddChild(row);
        return edit;
    }

    private SpinBox MakeNumRow(string label, int value, VBoxContainer parent)
    {
        var row = new HBoxContainer();
        var lab = new Label { Text = TranslateGroup.T(label), CustomMinimumSize = new Vector2(118, 22) };
        lab.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(lab);
        var spin = new SpinBox { MinValue = 0, MaxValue = 99999, Value = value, CustomMinimumSize = new Vector2(80, 22) };
        spin.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(spin);
        parent.AddChild(row);
        return spin;
    }

    private CheckBox MakeCheckRow(string label, bool value, VBoxContainer parent)
    {
        var row = new HBoxContainer();
        var lab = new Label { Text = TranslateGroup.T(label), CustomMinimumSize = new Vector2(118, 22) };
        lab.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(lab);
        var check = new CheckBox { ButtonPressed = value };
        row.AddChild(check);
        parent.AddChild(row);
        return check;
    }

    /// <summary>完整属性编辑器打开时构建CSV配置区(初始值回显自当前格子)</summary>
    private void BuildTerrainModuleCsvUI(Grids grid)
    {
        if (moduleCsvContainer != null) { moduleCsvContainer.QueueFree(); moduleCsvContainer = null; }
        if (grid == null) return;
        moduleCsvTarget = grid;
        moduleCsvContainer = new VBoxContainer();
        moduleCsvContainer.Position = new Vector2(280, 60);
        moduleCsvContainer.Size = new Vector2(260, 450);
        moduleCsvContainer.AddThemeConstantOverride("separation", 3);

        var title = new Label();
        TranslateGroup.SetText(title, "=== 地形模块(CSV可输入) ===");
        title.AddThemeFontSizeOverride("font_size", 13);
        title.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 0.9f));
        moduleCsvContainer.AddChild(title);

        spreadEnableCheck = MakeCheckRow("能扩散", grid.spreadEnabled, moduleCsvContainer);
        spreadTerrainEdit = MakeCsvRow("扩散黑名单地形", grid.spreadBlacklistTerrain, moduleCsvContainer);
        spreadWeaponEdit = MakeCsvRow("扩散黑名单兵器", grid.spreadBlacklistWeapon, moduleCsvContainer);
        spreadBuildingEdit = MakeCsvRow("扩散黑名单设施", grid.spreadBlacklistBuilding, moduleCsvContainer);
        spreadUnitEdit = MakeCsvRow("扩散黑名单单位", grid.spreadBlacklistUnit, moduleCsvContainer);
        spreadCooldownSpin = MakeNumRow("扩散冷却回合", grid.spreadCooldownTurns, moduleCsvContainer);
        spreadCapCheck = MakeCheckRow("全局次数上限", grid.spreadGlobalCapEnabled, moduleCsvContainer);
        spreadCapSpin = MakeNumRow("一局几次(只计实际)", grid.spreadGlobalCapCount, moduleCsvContainer);
        spreadManhattanSpin = MakeNumRow("最大曼哈顿格数", grid.spreadMaxManhattanFromOrigin, moduleCsvContainer);

        destroyTerrainEdit = MakeCsvRow("摧毁链白名单地形", grid.destroyChainTerrainWhitelist, moduleCsvContainer);
        destroyUnitEdit = MakeCsvRow("摧毁链白名单单位", grid.destroyChainUnitWhitelist, moduleCsvContainer);
        destroyBuildingEdit = MakeCsvRow("摧毁链白名单设施", grid.destroyChainBuildingWhitelist, moduleCsvContainer);
        destroyWeaponEdit = MakeCsvRow("摧毁链白名单兵器", grid.destroyChainWeaponWhitelist, moduleCsvContainer);

                                
        hideUnitsCheck = MakeCheckRow("地形隐蔽", grid.terrainHidesUnits, moduleCsvContainer);
        cannotStopCheck = MakeCheckRow("单位不能停靠(可穿过)", grid.cannotStopOnThisGrid, moduleCsvContainer);

        visionEnableCheck = MakeCheckRow("视野加成开关", grid.visionBonusEnabled, moduleCsvContainer);
        visionBlacklistEdit = MakeCsvRow("势力黑名单csv", JoinInts(grid.visionBonusFactionBlacklist), moduleCsvContainer);
        visionMinSpin = MakeNumRow("范围n(最小)", grid.visionBonusMinRange, moduleCsvContainer);
        visionMaxSpin = MakeNumRow("范围m(最大视野)", grid.visionBonusMaxRange, moduleCsvContainer);
        visionAmountSpin = MakeNumRow("加成量", grid.visionBonusAmount, moduleCsvContainer);

        var applyBtn = new Button();
        TranslateGroup.SetText(applyBtn, "应用地形模块配置");
        applyBtn.CustomMinimumSize = new Vector2(200, 28);
        applyBtn.AddThemeFontSizeOverride("font_size", 12);
        applyBtn.Pressed += ApplyTerrainModuleCsv;
        moduleCsvContainer.AddChild(applyBtn);

        terrainMenuPanel.AddChild(moduleCsvContainer);
    }

    private static string JoinTargets(System.Collections.Generic.IEnumerable<Vector2I> t)
        => t == null ? "" : string.Join(";", t.Select(v => $"{v.X},{v.Y}"));
    private static string JoinInts(int[] a)
        => a == null ? "" : string.Join(",", a);

    private void ApplyTerrainModuleCsv()
    {
        var g = moduleCsvTarget;
        if (g == null || !IsInstanceValid(g)) return;
        g.spreadEnabled = spreadEnableCheck.ButtonPressed;
        g.spreadBlacklistTerrain = spreadTerrainEdit.Text.Trim();
        g.spreadBlacklistWeapon = spreadWeaponEdit.Text.Trim();
        g.spreadBlacklistBuilding = spreadBuildingEdit.Text.Trim();
        g.spreadBlacklistUnit = spreadUnitEdit.Text.Trim();
        g.spreadCooldownTurns = (int)spreadCooldownSpin.Value;
        g.spreadGlobalCapEnabled = spreadCapCheck.ButtonPressed;
        g.spreadGlobalCapCount = (int)spreadCapSpin.Value;
        g.spreadMaxManhattanFromOrigin = (int)spreadManhattanSpin.Value;
        g.destroyChainTerrainWhitelist = destroyTerrainEdit.Text.Trim();
        g.destroyChainUnitWhitelist = destroyUnitEdit.Text.Trim();
        g.destroyChainBuildingWhitelist = destroyBuildingEdit.Text.Trim();
        g.destroyChainWeaponWhitelist = destroyWeaponEdit.Text.Trim();
        g.terrainHidesUnits = hideUnitsCheck.ButtonPressed;
        g.cannotStopOnThisGrid = cannotStopCheck.ButtonPressed;
        g.visionBonusEnabled = visionEnableCheck.ButtonPressed;
        g.visionBonusFactionBlacklist = ParseInts(visionBlacklistEdit.Text);
        g.visionBonusMinRange = (int)visionMinSpin.Value;
        g.visionBonusMaxRange = (int)visionMaxSpin.Value;
        g.visionBonusAmount = (int)visionAmountSpin.Value;
        g.UpdateTerrainIcon();
    }

    private static Vector2I[] ParseTargets(string s)
    {
        var list = new List<Vector2I>();
        foreach (var part in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var xy = part.Split(',', StringSplitOptions.TrimEntries);
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y))
                list.Add(new Vector2I(x, y));
        }
        return list.ToArray();
    }

    private static int[] ParseInts(string s)
    {
        var list = new List<int>();
        foreach (var part in (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out int v)) list.Add(v);
        return list.ToArray();
    }
}
