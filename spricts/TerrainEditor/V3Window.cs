using Godot;
using System;

// V3Window.cs - ✅ 开创人2026-11-25: 可拖动浮窗(标题栏拖拽+✕关闭,历史/模板/卡片三弹窗共用)
public partial class V3Window : PanelContainer
{
    public string Title { set { _titleLabel.Text = $"⋮⋮ {value}"; } }
    public Control Body => _body;
    public event Action Closed;

    private readonly Label _titleLabel;
    private readonly Control _body;
    private bool _dragging;
    private Vector2 _dragOff;

    public V3Window(Vector2 minSize)
    {
        Visible = false;
        CustomMinimumSize = minSize;
        ZIndex = 100;
        var vb = new VBoxContainer();

        var bar = new PanelContainer();
        bar.CustomMinimumSize = new Vector2(0, 44);   // HIG触屏高度
        var barBox = new HBoxContainer();
        _titleLabel = new Label { Text = "⋮⋮", SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        var closeBtn = new Button { Text = "✕", CustomMinimumSize = new Vector2(44, 44), TooltipText = "关闭" };
        closeBtn.Pressed += () => { Visible = false; Closed?.Invoke(); };
        barBox.AddChild(_titleLabel);
        barBox.AddChild(closeBtn);
        bar.AddChild(barBox);
        bar.GuiInput += OnBarGui;
        vb.AddChild(bar);

        _body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var sc = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        sc.AddChild(_body);
        vb.AddChild(sc);
        AddChild(vb);
    }

    private void OnBarGui(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _dragging = mb.Pressed;
            _dragOff = GetGlobalMousePosition() - GlobalPosition;
        }
        else if (e is InputEventMouseMotion && _dragging)
        {
            GlobalPosition = GetGlobalMousePosition() - _dragOff;
        }
    }

    public void ShowCentered()
    {
        Visible = true;
        Callable.From(() =>
        {
            var vp = GetViewportRect().Size;
            GlobalPosition = new Vector2(Mathf.Max(0, (vp.X - Size.X) / 2), Mathf.Max(0, (vp.Y - Size.Y) / 2));
        }).CallDeferred();
        MoveToFront();
    }
}
