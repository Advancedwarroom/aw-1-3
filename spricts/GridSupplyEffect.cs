// GridSupplyEffect.cs - 新建文件
using Godot;

public partial class GridSupplyEffect : Node2D
{
    private float lifetime = 0f;
    private const float MAX_LIFETIME = 2f;
    private float pulseSpeed = 8f;
    
    public override void _Ready()
    {
        ZIndex = 250;
        Position = Godot.Vector2.Zero; // 相对于格子中心
    }
    
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
        
        // ===== 1. 六边形基地脉冲（CITY的感觉）=====
        DrawHexagonPulse(alpha, t);
        
        // ===== 2. 上升的能量柱 =====
        DrawEnergyColumn(alpha, t);
        
        // ===== 3. 扩散的圆环 =====
        DrawExpandingRings(alpha, t);
        
        // ===== 4. 飘升的加号（医疗感）=====
        DrawRisingPlusSigns(alpha, t);
    }
    
    private void DrawHexagonPulse(float alpha, float t)
    {
        float baseSize = 20f;
        float pulse = 1f + Mathf.Sin(t * pulseSpeed) * 0.15f;
        float size = baseSize * pulse;
        
        // 外框
        Color hexColor = new Color(0.2f, 0.8f, 0.3f, alpha * 0.6f);
        DrawHexagon(Godot.Vector2.Zero, size, hexColor, 3f);
        
        // 内框（反向）
        Color innerHexColor = new Color(0.4f, 1f, 0.5f, alpha * 0.4f);
        DrawHexagon(Godot.Vector2.Zero, size * 0.7f, innerHexColor, 2f);
        
        // 填充（半透明）
        DrawFilledHexagon(Godot.Vector2.Zero, size * 0.5f, new Color(0.3f, 0.9f, 0.4f, alpha * 0.2f));
    }
    
    private void DrawHexagon(Godot.Vector2 center, float radius, Color color, float width)
    {
        Godot.Vector2[] points = new Godot.Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Pi / 3;
            points[i] = center + new Godot.Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        
        for (int i = 0; i < 6; i++)
        {
            DrawLine(points[i], points[(i + 1) % 6], color, width);
        }
    }
    
// GridSupplyEffect.cs - 修复 DrawFilledHexagon 方法
private void DrawFilledHexagon(Godot.Vector2 center, float radius, Color color)
{
    Godot.Vector2[] points = new Godot.Vector2[6];
    for (int i = 0; i < 6; i++)
    {
        float angle = i * Mathf.Pi / 3;
        points[i] = center + new Godot.Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
    
    // 每个顶点一个颜色（都是同一个颜色）
    Color[] colors = new Color[6];
    colors[0] = color;
    colors[1] = color;
    colors[2] = color;
    colors[3] = color;
    colors[4] = color;
    colors[5] = color;
    
    DrawColoredPolygon(points, color);
}
    
    private void DrawEnergyColumn(float alpha, float t)
    {
        float height = 60f * t;
        float width = 16f * (1f - t * 0.5f);
        
        // 能量柱主体
        Rect2 rect = new Rect2(-width/2, -height, width, height);
        Color columnColor = new Color(0.2f, 0.9f, 0.3f, alpha * 0.4f);
        DrawRect(rect, columnColor);
        
        // 能量柱高光
        Rect2 highlightRect = new Rect2(-width/4, -height, width/2, height);
        Color highlightColor = new Color(0.5f, 1f, 0.6f, alpha * 0.6f);
        DrawRect(highlightRect, highlightColor);
    }
    
    private void DrawExpandingRings(float alpha, float t)
    {
        for (int i = 0; i < 3; i++)
        {
            float ringT = (t * 2f + i * 0.33f) % 1f;
            float ringRadius = 10f + ringT * 40f;
            float ringAlpha = (1f - ringT) * alpha * 0.5f;
            
            Color ringColor = new Color(0.3f, 1f, 0.4f, ringAlpha);
            DrawArc(Godot.Vector2.Zero, ringRadius, 0, Mathf.Pi * 2, 32, ringColor, 2f);
        }
    }
    
    private void DrawRisingPlusSigns(float alpha, float t)
    {
        // 医疗加号向上飘
        for (int i = 0; i < 4; i++)
        {
            float plusT = (t * 1.5f + i * 0.25f) % 1f;
            float y = -plusT * 50f;
            float x = Mathf.Sin(plusT * Mathf.Pi * 2 + i) * 15f;
            float plusAlpha = (1f - plusT) * alpha;
            float size = 6f * (1f - plusT * 0.3f);
            
            if (plusAlpha <= 0) continue;
            
            Color plusColor = Colors.White with { A = plusAlpha };
            Godot.Vector2 pos = new Godot.Vector2(x, y);
            
            // 绘制加号
            DrawLine(pos - new Godot.Vector2(size, 0), pos + new Godot.Vector2(size, 0), plusColor, 2f);
            DrawLine(pos - new Godot.Vector2(0, size), pos + new Godot.Vector2(0, size), plusColor, 2f);
        }
    }
}