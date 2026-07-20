// SupplyEffect.cs - 新建文件，放在项目根目录
using Godot;
using System;

public partial class SupplyEffect : Node2D
{
    private float lifetime = 0f;
    private const float MAX_LIFETIME = 1.5f;
    
    // 特效参数
    private Godot.Vector2 startPos;
    private string effectText;
    private Color effectColor;
    private float floatSpeed = 40f;
    
    // 十字光芒旋转
    private float rotationAngle = 0f;
    
    public void Setup(Godot.Vector2 position, string text, Color color)
    {
        startPos = position;
        effectText = text;
        effectColor = color;
        Position = position;
        ZIndex = 300; // 确保在最上层
    }
    
    public override void _Process(double delta)
    {
        lifetime += (float)delta;
        float t = lifetime / MAX_LIFETIME;
        
        if (t >= 1f)
        {
            QueueFree();
            return;
        }
        
        // 向上飘动
        Position = startPos + new Godot.Vector2(0, -floatSpeed * lifetime);
        
        // 旋转光芒
        rotationAngle += (float)delta * 3f;
        
        // 重绘
        QueueRedraw();
    }
    
    public override void _Draw()
    {
        float t = lifetime / MAX_LIFETIME;
        float alpha = 1f - t; // 渐隐
        
        // ===== 1. 绘制旋转的十字光芒（补给感）=====
        DrawRotatingCross(alpha);
        
        // ===== 2. 绘制中心光点 =====
        DrawCenterGlow(alpha);
        
        // ===== 3. 绘制文字 =====
        DrawFloatingText(alpha);
        
        // ===== 4. 绘制小星星/粒子 =====
        DrawSparkles(alpha, t);
    }
    
    private void DrawRotatingCross(float alpha)
    {
        float size = 25f * (1f + Mathf.Sin(lifetime * 5f) * 0.2f); // 呼吸效果
        Color crossColor = effectColor with { A = alpha * 0.6f };
        
        // 主十字（旋转）
        for (int i = 0; i < 4; i++)
        {
            float angle = rotationAngle + i * Mathf.Pi / 2;
            Godot.Vector2 dir = new Godot.Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            
            DrawLine(
                -dir * size * 0.3f,
                dir * size,
                crossColor,
                3f
            );
        }
        
        // 小十字（反向旋转）
        float smallSize = size * 0.5f;
        Color smallCrossColor = Colors.White with { A = alpha * 0.4f };
        for (int i = 0; i < 4; i++)
        {
            float angle = -rotationAngle * 1.5f + i * Mathf.Pi / 2 + Mathf.Pi / 4;
            Godot.Vector2 dir = new Godot.Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            
            DrawLine(
                -dir * smallSize * 0.2f,
                dir * smallSize,
                smallCrossColor,
                2f
            );
        }
    }
    
    private void DrawCenterGlow(float alpha)
    {
        // 多层光晕
        for (int i = 3; i >= 0; i--)
        {
            float radius = 8f + i * 6f;
            Color glowColor = effectColor with { A = alpha * (0.3f - i * 0.05f) };
            DrawCircle(Godot.Vector2.Zero, radius, glowColor);
        }
        
        // 中心亮点
        DrawCircle(Godot.Vector2.Zero, 4f, Colors.White with { A = alpha });
    }
    
    private void DrawFloatingText(float alpha)
    {
        if (string.IsNullOrEmpty(effectText)) return;
        
        // 文字阴影
        DrawString(
            ThemeDB.FallbackFont,
            new Godot.Vector2(2, -28),
            effectText,
            HorizontalAlignment.Center,
            -1,
            16,
            Colors.Black with { A = alpha * 0.5f }
        );
        
        // 主文字（带描边效果）
        Color textColor = Colors.White with { A = alpha };
        Color outlineColor = effectColor with { A = alpha };
        
        // 描边
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                DrawString(
                    ThemeDB.FallbackFont,
                    new Godot.Vector2(x, -30 + y),
                    effectText,
                    HorizontalAlignment.Center,
                    -1,
                    16,
                    outlineColor
                );
            }
        }
        
        // 主文字
        DrawString(
            ThemeDB.FallbackFont,
            new Godot.Vector2(0, -30),
            effectText,
            HorizontalAlignment.Center,
            -1,
            16,
            textColor
        );
    }
    
    private void DrawSparkles(float alpha, float t)
    {
        // 随机分布的小星星
        uint seed = (uint)(startPos.X + startPos.Y);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = seed;
        
        for (int i = 0; i < 6; i++)
        {
            float angle = rng.Randf() * Mathf.Pi * 2;
            float dist = 20f + rng.Randf() * 30f;
            Godot.Vector2 sparklePos = new Godot.Vector2(
                Mathf.Cos(angle) * dist,
                Mathf.Sin(angle) * dist - lifetime * 20f // 也向上飘
            );
            
            float sparklePhase = (t * 3f + i * 0.5f) % 1f;
            float sparkleAlpha = Mathf.Sin(sparklePhase * Mathf.Pi) * alpha;
            float sparkleSize = 2f + Mathf.Sin(sparklePhase * Mathf.Pi * 2) * 1.5f;
            
            if (sparkleAlpha > 0)
            {
                DrawCircle(sparklePos, sparkleSize, Colors.White with { A = sparkleAlpha });
            }
        }
    }
}