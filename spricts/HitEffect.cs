using Godot;
using System;
public partial class HitEffect : Node2D
{
    private float t = 0f;
    private const float DURATION = 0.3f;
    public override void _Process(double delta)
    {
        t += (float)delta;
        if (t >= DURATION) { QueueFree(); return; }
        QueueRedraw();
    }
    public override void _Draw()
    {
        float a = 1f - t / DURATION;
        float r = 10f + t * 40f;          // 向外扩散
        // 红圈
        DrawArc(Vector2.Zero, r, 0, Mathf.Pi * 2, 32, new Color(1, 0, 0, a * 0.8f), 3f);
        // 中心白点
        DrawCircle(Vector2.Zero, 6f, new Color(1, 1, 1, a));
    }
}