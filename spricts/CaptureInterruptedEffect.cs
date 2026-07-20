// CaptureInterruptedEffect.cs - 占领中断特效（进度条碎裂效果）
using Godot;
using System;
public partial class CaptureInterruptedEffect : Node2D
{
    private float lifetime = 0f;
    private const float MAX_LIFETIME = 1.0f;
    
    public override void _Process(double delta)
    {
        lifetime += (float)delta;
        if (lifetime >= MAX_LIFETIME)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }
    
    public override void _Draw()
    {
        float t = lifetime / MAX_LIFETIME;
        float alpha = 1f - t;
        
        // 碎裂的进度环
        DrawShatteredRing(alpha, t);
        
        // "占领中断"文字
        DrawInterruptedText(alpha);
        
        // 碎片飞散
        DrawDebris(alpha, t);
    }
    
    private void DrawShatteredRing(float alpha, float t)
    {
        float radius = 25f;
        int segments = 8;
        float breakOffset = t * 10f;  // 裂缝扩大
        
        for (int i = 0; i < segments; i++)
        {
            float startAngle = i * Mathf.Pi * 2 / segments;
            float endAngle = (i + 0.8f) * Mathf.Pi * 2 / segments;
            
            // 每段向外飞散
            float flyDistance = t * 20f;
            float flyAngle = (startAngle + endAngle) / 2;
            Vector2 offset = new Vector2(Mathf.Cos(flyAngle), Mathf.Sin(flyAngle)) * flyDistance;
            
            Color color = new Color(0.8f, 0.2f, 0.2f, alpha);  // 红色表示失败
            
            DrawArc(offset, radius, startAngle, endAngle, 8, color, 3f);
        }
    }
    
    private void DrawInterruptedText(float alpha)
    {
        string text = "占领中断!";
        float t = lifetime / MAX_LIFETIME;
        // 抖动效果
        float shake = Mathf.Sin( t * 20f) * 2f;
        
        DrawString(
            ThemeDB.FallbackFont,
            new Vector2(shake, -40),
            text,
            HorizontalAlignment.Center,
            -1,
            14,
            Colors.Red with { A = alpha }
        );
    }
    
    private void DrawDebris(float alpha, float t)
    {
        // 随机碎片
        uint seed = 12345;
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = seed;
        
        for (int i = 0; i < 6; i++)
        {
            float angle = rng.Randf() * Mathf.Pi * 2;
            float speed = 30f + rng.Randf() * 20f;
            Vector2 pos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed * t;
            
            float size = 3f * (1f - t);
            Color color = new Color(0.6f, 0.1f, 0.1f, alpha);
            
            DrawCircle(pos, size, color);
        }
    }
}
