// ShieldWeakPointEditor.cs — ✅ 开创人2026-11-16: GrandBolt护盾弱点索引·序号行编辑器
// shieldWeakPointIndices与shieldSourceOffsets同序对齐（第N行=第N个护盾源保护此弱点）。
// 行号与BindOffsetsEditor的行号一一对应——两个面板并排看，N:M关系一目了然。
using Godot;
using System;

public partial class ShieldWeakPointEditor : VBoxContainer
{
    public event Action<int[]> ValueChanged;
    private int[] _indices = Array.Empty<int>();
    private VBoxContainer _list;
    private bool _suppress;

    public int[] Indices
    {
        get => _indices;
        set { _indices = value ?? Array.Empty<int>(); Rebuild(); }
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 3);
        var head = new Label { Text = "护盾弱点索引（行号=护盾源序号,与shieldSourceOffsets同序对齐）" };
        head.AddThemeFontSizeOverride("font_size", 11);
        AddChild(head);
        _list = new VBoxContainer();
        AddChild(_list);
        var add = new Button { Text = "＋ 添加绑定行" };
        add.Pressed += () => { var l = new System.Collections.Generic.List<int>(_indices) { 0 }; Commit(l.ToArray()); Rebuild(); };
        AddChild(add);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_list == null) return;
        foreach (Node c in _list.GetChildren()) c.QueueFree();
        for (int i = 0; i < _indices.Length; i++)
        {
            int idx = i;
            var row = new HBoxContainer();
            var no = new Label { Text = $"源#{i + 1} → 弱点", CustomMinimumSize = new Vector2(120, 0) };
            no.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(no);
            var sp = new SpinBox { MinValue = 0, MaxValue = 7, Value = _indices[i], CustomMinimumSize = new Vector2(70, 0) };
            sp.ValueChanged += v => { var l = new System.Collections.Generic.List<int>(_indices); l[idx] = (int)v; Commit(l.ToArray()); };
            row.AddChild(sp);
            var del = new Button { Text = "✕" };
            del.Pressed += () => { var l = new System.Collections.Generic.List<int>(_indices); l.RemoveAt(idx); Commit(l.ToArray()); Rebuild(); };
            row.AddChild(del);
            _list.AddChild(row);
        }
    }

    private void Commit(int[] v) { _indices = v; if (!_suppress) ValueChanged?.Invoke(v); }
}
