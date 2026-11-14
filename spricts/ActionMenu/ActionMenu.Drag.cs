// ActionMenu.Drag.cs - 开创人: 菜单整体可拖动(鼠标+触屏, 视口内钳制)——根面板 + 所有浮出子容器
using Godot;

public partial class ActionMenu
{
    private bool _dragging;
    private Vector2 _dragOffset;
    private Control _subDrag;
    private Vector2 _subDragOffset;

    public override void _GuiInput(InputEvent e)
    {
        base._GuiInput(e);

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
            _ApplyDrag(this, GetGlobalMousePosition(), _dragOffset);
            AcceptEvent();
        }
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
            _ApplyDrag(this, GetGlobalMousePosition(), _dragOffset);
            AcceptEvent();
        }
    }

    // ✅ 开创人2026-11-15: 浮出子菜单(卸载列表/生产/CO弹窗/远占/庇护)同样可拖动——子容器按住背景拖走
    public void AttachDrag(Control c)
    {
        if (c == null) return;
        c.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _subDrag = c;
                    _subDragOffset = GetGlobalMousePosition() - c.GlobalPosition;
                    AcceptEvent();
                }
                else if (_subDrag == c) _subDrag = null;
            }
            else if (e is InputEventMouseMotion && _subDrag == c)
            {
                _ApplyDrag(c, GetGlobalMousePosition(), _subDragOffset);
                AcceptEvent();
            }
            else if (e is InputEventScreenTouch st)
            {
                if (st.Pressed)
                {
                    _subDrag = c;
                    _subDragOffset = GetGlobalMousePosition() - c.GlobalPosition;
                    AcceptEvent();
                }
                else if (_subDrag == c) _subDrag = null;
            }
            else if (e is InputEventScreenDrag && _subDrag == c)
            {
                _ApplyDrag(c, GetGlobalMousePosition(), _subDragOffset);
                AcceptEvent();
            }
        };
    }

    private void _ApplyDrag(Control target, Vector2 globalMouse, Vector2 offset)
    {
        var vp = GetViewportRect().Size;
        var t = globalMouse - offset;
        t.X = Mathf.Clamp(t.X, 0f, Mathf.Max(0f, vp.X - target.Size.X));
        t.Y = Mathf.Clamp(t.Y, 0f, Mathf.Max(0f, vp.Y - target.Size.Y * 0.3f));
        target.GlobalPosition = t;
    }
}
