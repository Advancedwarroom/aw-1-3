// BindOffsetsEditor.cs — ✅ 开创人2026-11-16: 绑定偏移·图形化行编辑器 + 全图点格拾取服务
// 每行=(x,y)偏移+✕；「点图添加」进入拾取模式（BindPickService,由编辑器点击流消费）。
// 适配字段：forceFieldGridOffsets / escortGridOffsets / boundFactoryOffsets / shieldSourceOffsets
using Godot;
using System;
using System.Collections.Generic;

/// <summary>全图点格拾取服务——编辑器点击流顶部消费一行（参谋二接线点见文件尾注释）。</summary>
public static class BindPickService
{
    private static readonly Queue<Func<Vector2I, bool>> _pending = new();
    public static bool IsPicking => _pending.Count > 0;
    public static void RequestOnce(Func<Vector2I, bool> onPick) => _pending.Enqueue(onPick);
    /// <summary>编辑器点击分发处首行调用：返回true=本次点击已被拾取消费。</summary>
    public static bool ConsumeClick(Vector2I gridIndex)
        => _pending.Count > 0 && _pending.Dequeue()(gridIndex);
}

public partial class BindOffsetsEditor : VBoxContainer
{
    public event Action<Array<Vector2I>> ValueChanged;
    private Array<Vector2I> _offsets = new();
    private VBoxContainer _list;
    private Button _pickBtn;
    private bool _suppress;

    public Array<Vector2I> Offsets
    {
        get => _offsets;
        set { _offsets = value ?? new Array<Vector2I>(); Rebuild(); }
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 3);
        var head = new Label { Text = "绑定偏移（相对锚点格；点图添加=全图拾取）" };
        head.AddThemeFontSizeOverride("font_size", 11);
        AddChild(head);
        _list = new VBoxContainer();
        AddChild(_list);
        var btns = new HBoxContainer();
        _pickBtn = new Button { Text = "＋ 点图添加" };
        _pickBtn.Pressed += () =>
        {
            _pickBtn.Text = "点击地图格子…";
            BindPickService.RequestOnce(idx =>
            {
                var l = new Array<Vector2I>(_offsets) { idx };
                Commit(l);
                _pickBtn.Text = "＋ 点图添加";
                return true;
            });
        };
        btns.AddChild(_pickBtn);
        var add = new Button { Text = "＋ 手动(0,0)" };
        add.Pressed += () => { var l = new Array<Vector2I>(_offsets) { new Vector2I(0, 0) }; Commit(l); Rebuild(); };
        btns.AddChild(add);
        AddChild(btns);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_list == null) return;
        foreach (Node c in _list.GetChildren()) c.QueueFree();
        for (int i = 0; i < _offsets.Count; i++)
        {
            int idx = i;
            var row = new HBoxContainer();
            var x = new SpinBox { MinValue = -99, MaxValue = 99, Value = _offsets[i].X, CustomMinimumSize = new Vector2(70, 0) };
            x.ValueChanged += v => { var l = new Array<Vector2I>(_offsets); var e = l[idx]; e.X = (int)v; l[idx] = e; Commit(l); };
            var y = new SpinBox { MinValue = -99, MaxValue = 99, Value = _offsets[i].Y, CustomMinimumSize = new Vector2(70, 0) };
            y.ValueChanged += v => { var l = new Array<Vector2I>(_offsets); var e = l[idx]; e.Y = (int)v; l[idx] = e; Commit(l); };
            var del = new Button { Text = "✕" };
            del.Pressed += () => { var l = new Array<Vector2I>(_offsets); l.RemoveAt(idx); Commit(l); Rebuild(); };
            row.AddChild(new Label { Text = "偏移" });
            row.AddChild(x); row.AddChild(y); row.AddChild(del);
            _list.AddChild(row);
        }
    }

    private void Commit(Array<Vector2I> v) { _offsets = v; if (!_suppress) ValueChanged?.Invoke(v); Rebuild(); }
}
