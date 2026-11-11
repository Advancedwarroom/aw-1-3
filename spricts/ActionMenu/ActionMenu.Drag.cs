// ActionMenu.Drag.cs - 开创人2026-11-12: 菜单整体可拖动——菜单弹出常压住临格/攻击范围, 拖开再点
using Godot;

public partial class ActionMenu
{
    private bool _dragging;
    private Vector2 _dragOffset;

    public override void _GuiInput(InputEvent e)
    {
        base._GuiInput(e);

        // 鼠标左键拖
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _dragging = true;
                _dragOffset = GetGlobalMousePosition() - GlobalPosition;
                AcceptEvent();
            }
            else
                _dragging = false;
        }
        else if (e is InputEventMouseMotion && _dragging)
        {
            _ApplyDrag(GetGlobalMousePosition());
            AcceptEvent();
        }
        // 触屏拖
        else if (e is InputEventScreenTouch st)
        {
            if (st.Pressed)
            {
                _dragging = true;
                _dragOffset = GetGlobalMousePosition() - GlobalPosition;
                AcceptEvent();
            }
            else
                _dragging = false;
        }
        else if (e is InputEventScreenDrag && _dragging)
        {
            _ApplyDrag(GetGlobalMousePosition());
            AcceptEvent();
        }
    }

    private void _ApplyDrag(Vector2 globalMouse)
    {
        var vp = GetViewportRect().Size;
        var target = globalMouse - _dragOffset;
        // 钳在视口内, 拖不丢
        target.X = Mathf.Clamp(target.X, -Size.X * 0.5f, vp.X - Size.X * 0.5f);
        target.Y = Mathf.Clamp(target.Y, 0f, vp.Y - Size.Y * 0.3f);
        GlobalPosition = target;
    }
}
