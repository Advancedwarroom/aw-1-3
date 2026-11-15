// PatternEditorGrid.cs — ✅ 开创人2026-11-16: 多格攻击模式·图形化编辑器
// 9×9格板：中心★=目标零点(锚点,可勾选可取消)；点击任意格=增删"dx,dy"；天然支持空格间隙与任意拓展。
using Godot;
using System;
using System.Collections.Generic;

public partial class PatternEditorGrid : VBoxContainer
{
    public event Action<string[]> CellsChanged;
    private string[] _cells = Array.Empty<string>();
    private readonly CheckBox[,] _checks = new CheckBox[9, 9];   // [dy+4, dx+4]
    private bool _suppress;

    public string[] Cells
    {
        get => _cells;
        set { _cells = value ?? Array.Empty<string>(); RefreshChecks(); }
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        var head = new Label { Text = "多格攻击模式（★=目标零点锚点，点击勾选/取消）" };
        head.AddThemeFontSizeOverride("font_size", 11);
        AddChild(head);

        var grid = new GridContainer { Columns = 10 };
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        AddChild(grid);

        grid.AddChild(new Control());   // 左上角空
        for (int dx = -4; dx <= 4; dx++)
            grid.AddChild(MakeHead(dx.ToString()));
        for (int dy = -4; dy <= 4; dy++)
        {
            grid.AddChild(MakeHead(dy.ToString()));
            for (int dx = -4; dx <= 4; dx++)
            {
                var cb = new CheckBox();
                cb.CustomMinimumSize = new Vector2(22, 22);
                if (dx == 0 && dy == 0)
                {
                    cb.Text = "★";
                    cb.AddThemeFontSizeOverride("font_size", 9);
                }
                int cx = dx, cy = dy;
                cb.Toggled += _ => OnToggle(cx, cy);
                _checks[dy + 4, dx + 4] = cb;
                grid.AddChild(cb);
            }
        }
        RefreshChecks();
    }

    private static Label MakeHead(string t) => new Label { Text = t, CustomMinimumSize = new Vector2(22, 14), AddThemeFontSizeOverride = { ["font_size"] = 8 } };

    private void OnToggle(int dx, int dy)
    {
        if (_suppress) return;
        var set = new HashSet<string>(_cells ?? Array.Empty<string>());
        string key = dx + "," + dy;
        if (!set.Add(key)) set.Remove(key);
        _cells = new List<string>(set) { "0,0" }.Contains("0,0") ? ListToArray(set) : ListToArray(set);
        CellsChanged?.Invoke(_cells);
    }

    private static string[] ListToArray(HashSet<string> s)
    {
        var l = new List<string>(s);
        l.Sort((a, b) => { var pa = a.Split(','); var pb = b.Split(','); int c = int.Parse(pa[0]).CompareTo(int.Parse(pb[0])); return c != 0 ? c : int.Parse(pa[1]).CompareTo(int.Parse(pb[1])); });
        return l.ToArray();
    }

    private void RefreshChecks()
    {
        _suppress = true;
        var set = new HashSet<string>(_cells ?? Array.Empty<string>());
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
                if (_checks[dy + 4, dx + 4] != null)
                    _checks[dy + 4, dx + 4].ButtonPressed = set.Contains(dx + "," + dy);
        _suppress = false;
    }
}
