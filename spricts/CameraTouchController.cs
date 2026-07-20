using Godot;

public partial class CameraTouchController : Camera2D
{
    [Export] public float minZoom = 0.3f;  // 最小缩放（最远处）
    [Export] public float maxZoom = 3.0f;  // 最大缩放（最近处）
    [Export] public float zoomSensitivity = 0.1f;  // 鼠标滚轮灵敏度
    [Export] public float dragSpeed = 1.0f;  // 拖拽速度
    [Export] public float smoothSpeed = 10.0f;  // 平滑插值速度

    // 当前目标缩放值（用于平滑插值）
    private Vector2 targetZoom = Vector2.One;
    private bool isDragging = false;
    private Vector2 dragStartPos = Vector2.Zero;
    private Vector2 dragStartCameraPos = Vector2.Zero;

    // 触摸状态
    private Godot.Collections.Dictionary<int, Vector2> activeTouches = new();
    private float touchStartPinchDist = 0f;
    private Vector2 touchStartZoom = Vector2.One;
    private bool isPinching = false;

    // 对外暴露当前缩放百分比
    public float ZoomPercent => Mathf.Clamp((Zoom.X - minZoom) / (maxZoom - minZoom) * 100f, 1f, 100f);

    public override void _Ready()
    {
        targetZoom = Zoom;
    }

    public override void _Process(double delta)
    {
        // 平滑插值到目标缩放
        Zoom = Zoom.Lerp(targetZoom, (float)delta * smoothSpeed);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ===== 鼠标滚轮缩放 =====
        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomAtPoint(GetGlobalMousePosition(), zoomSensitivity);
            }
            else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomAtPoint(GetGlobalMousePosition(), -zoomSensitivity);
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Middle)
            {
                if (mouseBtn.Pressed)
                {
                    isDragging = true;
                    dragStartPos = GetViewport().GetMousePosition();
                    dragStartCameraPos = Position;
                }
                else
                {
                    isDragging = false;
                }
            }
        }

        // ===== 鼠标中键拖拽平移 =====
        if (@event is InputEventMouseMotion mouseMotion && isDragging)
        {
            var currentPos = GetViewport().GetMousePosition();
            var delta = currentPos - dragStartPos;
            Position = dragStartCameraPos - new Vector2(delta.X / Zoom.X, delta.Y / Zoom.X);
        }

        // ===== 触摸处理 =====
        HandleTouchInput(@event);
    }

    private void HandleTouchInput(InputEvent @event)
    {
        // 屏幕触摸开始
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                activeTouches[touch.Index] = touch.Position;

                // 双指开始
                if (activeTouches.Count == 2)
                {
                    isPinching = true;
                    touchStartPinchDist = GetPinchDistance();
                    touchStartZoom = targetZoom;
                }
            }
            else
            {
                activeTouches.Remove(touch.Index);
                if (activeTouches.Count < 2)
                {
                    isPinching = false;
                }
            }
        }

        // 屏幕触摸移动
        if (@event is InputEventScreenDrag drag)
        {
            activeTouches[drag.Index] = drag.Position;

            // 双指缩放
            if (isPinching && activeTouches.Count == 2)
            {
                float currentDist = GetPinchDistance();
                if (touchStartPinchDist > 0)
                {
                    float ratio = currentDist / touchStartPinchDist;
                    Vector2 newZoom = touchStartZoom * ratio;
                    newZoom.X = Mathf.Clamp(newZoom.X, minZoom, maxZoom);
                    newZoom.Y = Mathf.Clamp(newZoom.Y, minZoom, maxZoom);
                    targetZoom = newZoom;
                }
            }
            // 单指拖拽（如果只有一个触摸点）
            else if (activeTouches.Count == 1)
            {
                Position -= new Vector2(drag.Relative.X / Zoom.X, drag.Relative.Y / Zoom.X);
            }
        }
    }

    private float GetPinchDistance()
    {
        var values = activeTouches.Values;
        if (values.Count < 2) return 0f;

        var it = values.GetEnumerator();
        it.MoveNext();
        Vector2 p1 = it.Current;
        it.MoveNext();
        Vector2 p2 = it.Current;
        return p1.DistanceTo(p2);
    }

    // 在指定点缩放（保持该点位置不变）
    private void ZoomAtPoint(Vector2 worldPos, float delta)
    {
        Vector2 oldZoom = targetZoom;
        Vector2 newZoom = oldZoom * (1f + delta);
        newZoom.X = Mathf.Clamp(newZoom.X, minZoom, maxZoom);
        newZoom.Y = Mathf.Clamp(newZoom.Y, minZoom, maxZoom);

        // 计算需要偏移的位置，使缩放中心对准鼠标/触摸点
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        Vector2 screenPos = (worldPos - GlobalPosition) * oldZoom + viewportSize / 2f;
        Vector2 offset = (screenPos - viewportSize / 2f) * new Vector2(1f / oldZoom.X - 1f / newZoom.X, 1f / oldZoom.Y - 1f / newZoom.Y);

        targetZoom = newZoom;
        Position += offset;
    }

    // 外部设置缩放百分比（0-100）
    public void SetZoomPercent(float percent)
    {
        float t = Mathf.Clamp(percent, 1f, 100f) / 100f;
        float zoomValue = Mathf.Lerp(minZoom, maxZoom, t);
        targetZoom = new Vector2(zoomValue, zoomValue);
    }

    // 外部设置缩放（直接值）
    public void SetZoomValue(float zoomValue)
    {
        zoomValue = Mathf.Clamp(zoomValue, minZoom, maxZoom);
        targetZoom = new Vector2(zoomValue, zoomValue);
    }
}
