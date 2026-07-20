using Godot;
using System.Linq;
public partial class CannonFlash : Node2D
{
    private float life = 0f;
    private const float MAX_LIFE = 0.15f;
    public override void _Process(double delta)
    {
        life += (float)delta;
        if (life >= MAX_LIFE) { QueueFree(); return; }
        QueueRedraw();
    }
    public override void _Draw()
    {
        float a = 1f - life / MAX_LIFE;
        // 外层大光晕
        DrawCircle(Vector2.Zero, 28f, new Color(1, 0.8f, 0.2f, a * 0.7f));
        // 内层白热
        DrawCircle(Vector2.Zero, 12f, new Color(1, 1, 1, a));
        // 十字火花
        DrawLine(new Vector2(-20, 0), new Vector2(20, 0), new Color(1, 1, 0.5f, a), 2f);
        DrawLine(new Vector2(0, -20), new Vector2(0, 20), new Color(1, 1, 0.5f, a), 2f);
    }
}