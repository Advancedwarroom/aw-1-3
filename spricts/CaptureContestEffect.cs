// CaptureContestEffect.cs - 争夺特效（红蓝双方闪烁对抗）
using Godot;
using System;

public partial class CaptureContestEffect : Node2D
{
    private float lifetime = 0f;
    private const float MAX_LIFETIME = 2.0f;
    
    // 双方队伍颜色
    public Color Team1Color { get; set; } = new Color(1f, 0.2f, 0.2f);  // 红
    public Color Team2Color { get; set; } = new Color(0.2f, 0.4f, 1f);  // 蓝
    
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
        float alpha = 1f - (t * 0.5f);  // 慢慢淡出
        
        // ===== 1. 闪电效果（双方对抗）=====
        DrawLightningBattle(alpha);
        
        // ===== 2. 闪烁的双方标志 =====
        drawTeamFlags(alpha);
        
        // ===== 3. "争夺中!"文字 =====
        DrawContestText(alpha);
        
        // ===== 4. 冲击波 =====
        DrawShockWave(alpha, t);
    }
    
    private void DrawLightningBattle(float alpha)
    {
        // 快速闪烁的闪电
        float flashSpeed = 15f;
        float flash = Mathf.Sin(lifetime * flashSpeed);
        bool showTeam1 = flash > 0;
        
        // 左侧闪电（队伍1）
        if (showTeam1)
        {
            DrawLightningBolt(new Vector2(-20, -30), new Vector2(-5, 0), Team1Color with { A = alpha }, 3f);
        }
        
        // 右侧闪电（队伍2）
        else
        {
            DrawLightningBolt(new Vector2(20, -30), new Vector2(5, 0), Team2Color with { A = alpha }, 3f);
        }
        
        // 中间碰撞点
        float collisionIntensity = Mathf.Abs(flash);
        Color collisionColor = Colors.White with { A = alpha * collisionIntensity };
        DrawCircle(Vector2.Zero, 8f + collisionIntensity * 4f, collisionColor);
    }
    
    private void DrawLightningBolt(Vector2 start, Vector2 end, Color color, float width)
    {
        // 锯齿状闪电
        Vector2 dir = (end - start).Normalized();
        float dist = start.DistanceTo(end);
        int segments = 5;
        
        Vector2[] points = new Vector2[segments + 1];
        points[0] = start;
        
        for (int i = 1; i < segments; i++)
        {
            float t = i / (float)segments;
            Vector2 basePos = start.Lerp(end, t);
            // 随机偏移
            float offset = Mathf.Sin(t * 10f + lifetime * 20f) * 8f;
            Vector2 perp = new Vector2(-dir.Y, dir.X) * offset;
            points[i] = basePos + perp;
        }
        
        points[segments] = end;
        
        // 绘制
        for (int i = 0; i < segments; i++)
        {
            DrawLine(points[i], points[i + 1], color, width);
            // 发光效果
            DrawLine(points[i], points[i + 1], color with { A = color.A * 0.3f }, width + 2f);
        }
    }
    
    private void drawTeamFlags(float alpha)
    {
        float wave = Mathf.Sin(lifetime * 5f) * 3f;
        
        // 左侧红旗
        DrawFlag(new Vector2(-25, -10 + wave), Team1Color with { A = alpha }, -1f);
        
        // 右侧蓝旗
        DrawFlag(new Vector2(25, -10 - wave), Team2Color with { A = alpha }, 1f);
    }
    
    private void DrawFlag(Vector2 pos, Color color, float direction)
    {
		float t = lifetime / MAX_LIFETIME;
        float alpha = 1f - (t * 0.5f);
        // 旗杆
        Vector2 poleTop = pos + new Vector2(0, -15);
        Vector2 poleBottom = pos;
        DrawLine(poleBottom, poleTop, Colors.Gray with { A = alpha }, 2f);
        
        // 飘动的旗帜
        float wave = Mathf.Sin(lifetime * 8f + pos.X) * 2f;
        Vector2[] flagPoints = new Vector2[]
        {
            poleTop,
            poleTop + new Vector2(12 * direction, wave),
            poleTop + new Vector2(12 * direction, 8 + wave * 0.5f),
            poleTop + new Vector2(0, 8)
        };
        
        DrawColoredPolygon(flagPoints, color);
    }
    
    private void DrawContestText(float alpha)
    {
        string text = "争夺中!";
        
        // 剧烈闪烁
        float flash = (Mathf.Sin(lifetime * 12f) + 1f) * 0.5f;
        Color textColor = Colors.Yellow with { A = alpha * (0.5f + flash * 0.5f) };
        
        // 阴影
        DrawString(
            ThemeDB.FallbackFont,
            new Vector2(2, -42),
            text,
            HorizontalAlignment.Center,
            -1,
            16,
            Colors.Red with { A = alpha * 0.5f }
        );
        
        // 主文字
        DrawString(
            ThemeDB.FallbackFont,
            new Vector2(0, -44),
            text,
            HorizontalAlignment.Center,
            -1,
            16,
            textColor
        );
    }
    
    private void DrawShockWave(float alpha, float t)
    {
        // 扩散的冲击波圆环
        for (int i = 0; i < 3; i++)
        {
            float ringT = (t * 2f + i * 0.33f) % 1f;
            float radius = 10f + ringT * 40f;
            float ringAlpha = (1f - ringT) * alpha * 0.3f;
            
            // 交替颜色
            Color ringColor = (i % 2 == 0) 
                ? new Color(Team1Color.R, Team1Color.G, Team1Color.B, ringAlpha)
                : new Color(Team2Color.R, Team2Color.G, Team2Color.B, ringAlpha);
            
            DrawArc(Vector2.Zero, radius, 0, Mathf.Pi * 2, 32, ringColor, 2f);
        }
    }
}