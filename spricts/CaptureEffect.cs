// CaptureEffect.cs - 占领特效（进度环+旗帜飘动效果）
using Godot;
using System;

public partial class CaptureEffect : Node2D
{
    private float lifetime = 0f;
    private const float MAX_LIFETIME = 3.0f;
    
    // 占领进度 0-100
    public float CaptureProgress { get; set; } = 0f;
    public Color TeamColor { get; set; } = Colors.White;
    public bool IsCapturing { get; set; } = true; // true=占领中, false=占领完成
    public int CapturePower { get; set; } = 10;
    
public override void _Process(double delta)
{
    lifetime += (float)delta;
    
    float maxTime = IsCapturing ? MAX_LIFETIME : MAX_LIFETIME + 0.5f;
    
    if (lifetime >= maxTime)
    {
        QueueFree();
        return;
    }
    QueueRedraw();
}
    private void DrawPowerBadge(float alpha)
{
    Vector2 badgePos = new Vector2(18, -18);
    DrawCircle(badgePos, 10f, TeamColor with { A = alpha * 0.8f });
    DrawCircle(badgePos, 8f, Colors.White with { A = alpha * 0.9f });
    
    string powerText = CapturePower.ToString();
    DrawString(ThemeDB.FallbackFont, badgePos + new Vector2(0, 4), 
        powerText, HorizontalAlignment.Center, -1, 12, Colors.Black with { A = alpha });
}
    public override void _Draw()
    {
    float t = lifetime / MAX_LIFETIME;
    float alpha = IsCapturing ? (1f - t) : Mathf.Max(0f, 1f - t * 2f);
        
        // ===== 1. 绘制进度环 =====
        DrawProgressRing(alpha);
        
        // ===== 2. 绘制飘动的旗帜 =====
        DrawWavingFlag(alpha);
        
        // ===== 3. 绘制占领文字 =====
        DrawCaptureText(alpha);
        
        // ===== 4. 粒子效果 =====
        DrawSparkles(alpha);
        DrawPowerBadge(alpha);
    }
    
    private void DrawProgressRing(float alpha)
    {
        float radius = 25f;
        float thickness = 4f;
        int segments = 32;
        
        // 背景环（灰色）
        Color bgColor = new Color(0.3f, 0.3f, 0.3f, alpha * 0.5f);
        DrawCircleArc(Vector2.Zero, radius, 0, Mathf.Pi * 2, bgColor, thickness, segments);
        
        // 进度环（队伍颜色）
        float progressAngle = (CaptureProgress / 100f) * Mathf.Pi * 2;
        Color progressColor = TeamColor with { A = alpha };
        DrawCircleArc(Vector2.Zero, radius, -Mathf.Pi / 2, -Mathf.Pi / 2 + progressAngle, progressColor, thickness, segments);
        
        // 中心显示百分比
        if (IsCapturing)
        {
            string percentText = $"{Mathf.RoundToInt(CaptureProgress)}%";
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(0, 5),
                percentText,
                HorizontalAlignment.Center,
                -1,
                14,
                Colors.White with { A = alpha }
            );
        }
    }
    
    private void DrawCircleArc(Vector2 center, float radius, float startAngle, float endAngle, Color color, float width, int segments)
    {
        Vector2 prevPoint = center + new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * radius;
        
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(startAngle, endAngle, t);
            Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            DrawLine(prevPoint, point, color, width);
            prevPoint = point;
        }
    }
    
    private void DrawWavingFlag(float alpha)
    {
        // 旗帜杆
        Vector2 poleTop = new Vector2(0, -35);
        Vector2 poleBottom = new Vector2(0, -15);
        DrawLine(poleBottom, poleTop, Colors.Gray with { A = alpha }, 2f);
        
        // 飘动的旗帜（正弦波）
        float wave = Mathf.Sin(lifetime * 8f) * 3f;
        Color flagColor = TeamColor with { A = alpha };
        
        Vector2[] flagPoints = new Vector2[]
        {
            poleTop,
            poleTop + new Vector2(15, wave),
            poleTop + new Vector2(15, 10 + wave * 0.5f),
            poleTop + new Vector2(0, 10)
        };
        
        DrawColoredPolygon(flagPoints, flagColor);
        
        // 旗面高光
        DrawLine(poleTop + new Vector2(2, 2), poleTop + new Vector2(13, wave + 2), Colors.White with { A = alpha * 0.5f }, 1f);
    }
    
    private void DrawCaptureText(float alpha)
    {
        string text = IsCapturing ? "占领中..." : "占领完成!";
        Color textColor = IsCapturing ? Colors.Yellow : Colors.Green;
        
        // 文字阴影
        DrawString(
            ThemeDB.FallbackFont,
            new Vector2(2, -42),
            text,
            HorizontalAlignment.Center,
            -1,
            12,
            Colors.Black with { A = alpha * 0.5f }
        );
        
        // 主文字
        DrawString(
            ThemeDB.FallbackFont,
            new Vector2(0, -44),
            text,
            HorizontalAlignment.Center,
            -1,
            12,
            textColor with { A = alpha }
        );
    }
    
    private void DrawSparkles(float alpha)
    {
        // 随机闪烁的星星
        uint seed = (uint)(GlobalPosition.X + GlobalPosition.Y);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = seed;
        
        for (int i = 0; i < 5; i++)
        {
            float angle = rng.Randf() * Mathf.Pi * 2;
            float dist = 30f + rng.Randf() * 15f;
            Vector2 pos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
            
            float sparklePhase = (lifetime * 2f + i * 0.3f) % 1f;
            float sparkleAlpha = Mathf.Sin(sparklePhase * Mathf.Pi) * alpha;
            
            if (sparkleAlpha > 0)
            {
                DrawCircle(pos, 2f, Colors.White with { A = sparkleAlpha });
            }
        }
    }
}