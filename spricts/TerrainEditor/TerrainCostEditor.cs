// TerrainCostEditor.cs — ✅ 开创人2026-11-16: 地形移动消耗覆写·行编辑器
// 每行：地形名下拉 + 消耗SpinBox + 删除；底部添加按钮。对应字符串"TERRAIN=cost"。
using Godot;
using System;
using System.Collections.Generic;

public partial class TerrainCostEditor : VBoxContainer
{
    public event Action<string[]> ValueChanged;
    private string[] _rows = Array.Empty<string>();
    private VBoxContainer _list;
    private bool _suppress;

    public string[] Rows
    {
        get => _rows;
        set { _rows = value ?? Array.Empty<string>(); Rebuild(); }
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 3);
        var head = new Label { Text = "地形移动消耗覆写（逐行：地形=消耗）" };
        head.AddThemeFontSizeOverride("font_size", 11);
        AddChild(head);
        _list = new VBoxContainer();
        AddChild(_list);
        var add = new Button { Text = "＋ 添加地形" };
        add.Pressed += () => { var l = new List<string>(_rows) { "GROUND=1" }; Commit(l.ToArray()); Rebuild(); };
        AddChild(add);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_list == null) return;
        foreach (Node c in _list.GetChildren()) c.QueueFree();
        string[] names = Enum.GetNames(typeof(GridType));
        for (int i = 0; i < _rows.Length; i++)
        {
            int idx = i;
            var parts = _rows[i].Split('=');
            var row = new HBoxContainer();
            var opt = new OptionButton { CustomMinimumSize = new Vector2(140, 0) };
            foreach (var n in names) opt.AddItem(n);
            int cur = Array.IndexOf(names, parts[0].Trim());
            if (cur >= 0) opt.Select(cur);
            opt.ItemSelected += sel => { var l = new List<string>(_rows); var p2 = l[idx].Split('='); l[idx] = names[(int)sel] + "=" + (p2.Length > 1 ? p2[1] : "1"); Commit(l.ToArray()); };
            row.AddChild(opt);
            var sp = new SpinBox { MinValue = 1, MaxValue = 999, Step = 1, CustomMinimumSize = new Vector2(80, 0) };
            sp.Value = parts.Length > 1 && int.TryParse(parts[1], out int c0) ? c0 : 1;
            sp.ValueChanged += val => { var l = new List<string>(_rows); var p2 = l[idx].Split('='); l[idx] = p2[0] + "=" + (int)val; Commit(l.ToArray()); };
            row.AddChild(sp);
            var del = new Button { Text = "✕" };
            del.Pressed += () => { var l = new List<string>(_rows); l.RemoveAt(idx); Commit(l.ToArray()); Rebuild(); };
            row.AddChild(del);
            _list.AddChild(row);
        }
    }

    private void Commit(string[] v) { _rows = v; if (!_suppress) ValueChanged?.Invoke(v); }
}
