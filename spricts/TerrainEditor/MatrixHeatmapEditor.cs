// MatrixHeatmapEditor.cs — ✅ 开创人2026-11-16: 伤害矩阵热力图（自绘Canvas,轻量顺滑）
// 行=攻击方,列=防御方,色深=伤害高;点格选中→顶部编辑条改值;配色 灰→红,>100 加深紫。
using Godot;
using System;
using System.Collections.Generic;

public partial class MatrixHeatmap : Control
{
    public event Action<int, int> CellSelected;        // (攻击序号, 防御序号)
    public Dictionary<string, int> Matrix = new();
    public List<string> Attackers = new();
    public List<string> Defenders = new();
    public const int Cell = 24;
    public const int Head = 84;
    public int SelA = -1, SelD = -1;
    private bool _suppress;

    public void Load(Dictionary<string, int> m)
    {
        Matrix = m ?? new Dictionary<string, int>();
        var sa = new HashSet<string>(); var sd = new HashSet<string>();
        foreach (var k in Matrix.Keys)
        {
            var p = k.Split('_');
            if (p.Length < 2) continue;
            sa.Add(p[0]); sd.Add(p[1]);
        }
        Attackers = new List<string>(sa); Attackers.Sort();
        Defenders = new List<string>(sd); Defenders.Sort();
        CustomMinimumSize = new Vector2(Head + Defenders.Count * Cell + 20, Head + Attackers.Count * Cell + 20);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        int fs = 8;
        // 行列头
        for (int d = 0; d < Defenders.Count; d++)
            DrawString(font, new Vector2(Head + d * Cell + 2, Head - 4), Defenders[d].Length > 6 ? Defenders[d][..6] : Defenders[d], fontSize: fs, modulate: Colors.LightGray);
        for (int a = 0; a < Attackers.Count; a++)
            DrawString(font, new Vector2(4, Head + a * Cell + Cell / 2 + 3), Attackers[a].Length > 10 ? Attackers[a][..10] : Attackers[a], fontSize: fs, modulate: Colors.LightGray);
        // 单元格
        for (int a = 0; a < Attackers.Count; a++)
            for (int d = 0; d < Defenders.Count; d++)
            {
                int v = Matrix.TryGetValue(Attackers[a] + "_" + Defenders[d], out int x) ? x : 0;
                float t = Mathf.Clamp(v / 100f, 0f, 1f);
                var col = new Color(0.15f + 0.75f * t, 0.15f + 0.05f * t, 0.15f + (v > 100 ? 0.6f * (v - 100) / 100f : 0.05f * t));
                var rect = new Rect2(Head + d * Cell, Head + a * Cell, Cell - 1, Cell - 1);
                DrawRect(rect, col);
                DrawString(font, rect.Position + new Vector2(2, Cell - 7), v.ToString(), fontSize: 7, modulate: Colors.White);
            }
        // 选中框
        if (SelA >= 0 && SelD >= 0)
            DrawRect(new Rect2(Head + SelD * Cell - 1, Head + SelA * Cell - 1, Cell + 1, Cell + 1), new Color(1, 1, 0.2f), false, 2f);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int d = (int)((mb.Position.X - Head) / Cell);
            int a = (int)((mb.Position.Y - Head) / Cell);
            if (a >= 0 && a < Attackers.Count && d >= 0 && d < Defenders.Count)
            {
                SelA = a; SelD = d;
                CellSelected?.Invoke(a, d);
                QueueRedraw();
            }
        }
    }
}

public partial class MatrixHeatmapEditor : VBoxContainer
{
    public event Action<Dictionary<string, int>> ValueChanged;
    private Dictionary<string, int> _matrix = new();
    private MatrixHeatmap _map;
    private Label _selLabel;
    private SpinBox _val;
    private bool _suppress;

    public Dictionary<string, int> Matrix
    {
        get => _matrix;
        set { _matrix = value ?? new Dictionary<string, int>(); if (_map != null) _map.Load(_matrix); }
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        var head = new Label { Text = "伤害矩阵热力图（色深=伤害高;点格→下方改值）" };
        head.AddThemeFontSizeOverride("font_size", 11);
        AddChild(head);

        _map = new MatrixHeatmap();
        _map.CellSelected += (a, d) => OnSelect(a, d);
        var sc = new ScrollContainer { CustomMinimumSize = new Vector2(0, 320) };
        sc.AddChild(_map);
        AddChild(sc);
        _map.Load(_matrix);

        var bar = new HBoxContainer();
        _selLabel = new Label { Text = "点选单元格…", CustomMinimumSize = new Vector2(230, 0) };
        bar.AddChild(_selLabel);
        _val = new SpinBox { MinValue = 0, MaxValue = 999, Step = 5, CustomMinimumSize = new Vector2(90, 0) };
        _val.ValueChanged += v => Apply((int)v);
        bar.AddChild(_val);
        foreach (var (t, act) in new (string, Action)[] { ("=100", () => SetVal(100)), ("=0", () => SetVal(0)), ("=999", () => SetVal(999)) })
        {
            var b = new Button { Text = t };
            b.Pressed += act;
            bar.AddChild(b);
        }
        AddChild(bar);
    }

    private void OnSelect(int a, int d)
    {
        string key = _map.Attackers[a] + "_" + _map.Defenders[d];
        _selLabel.Text = $"{_map.Attackers[a]} → {_map.Defenders[d]}";
        _suppress = true;
        _val.Value = _matrix.TryGetValue(key, out int v) ? v : 0;
        _suppress = false;
    }

    private void SetVal(int v) { _suppress = false; _val.Value = v; Apply(v); }

    private void Apply(int v)
    {
        if (_suppress || _map.SelA < 0 || _map.SelD < 0) return;
        string key = _map.Attackers[_map.SelA] + "_" + _map.Defenders[_map.SelD];
        _matrix[key] = v;
        ValueChanged?.Invoke(new Dictionary<string, int>(_matrix));
        _map.QueueRedraw();
    }
}
