// GrandBolt.VisualParts.cs
// 巨雷分部件视觉系统: 基体+四弱点独立图层, 不规则尺寸, 四向离线预渲染查表, 弱点状态机, CoW LUT势力换色(mask=中立原色, 蓝绿橙粉黑紫全自动)
// 资产: gb_base_{S,W,N,E}.png(编码版4帧条) + gb_points.png + gb_core.png + gb_layout.json (asscets/Weapons/GrandBolt/)
using Godot;
using System;

public partial class GrandBoltVisual : Node2D
{
    [Export] public bool enableRotation = true;
    [Export] public int direction = 0;                   // 0=S 1=W 2=N 3=E
    [Export] public double animFps = 2.0;

    private Sprite2D baseSprite;
    private readonly Sprite2D[] pointSprites = new Sprite2D[4];
    private readonly bool[] pointDead = new bool[4];
    private Texture2D[] baseTex;
    private Texture2D pointsTex, coreTex;
    private double acc;
    private int frame;
    private string lastTeam;

    private static readonly int[,] PTS = {
        { 41,145,14,14 },
        { 137,177,14,14 },
        { 233,145,14,14 },
        { 116,70,56,54 },
    };
    private const int PAD = 1;
    private static readonly string[] DIRN = { "S", "W", "N", "E" };
    private static readonly int[] FW = { 288, 192, 288, 192 };
    private static readonly int[] FH = { 192, 288, 192, 288 };

    public override void _Ready()
    {
        baseTex = new Texture2D[4];
        for (int k = 0; k < 4; k++)
            baseTex[k] = GD.Load<Texture2D>($"res://asscets/Weapons/GrandBolt/gb_base_{DIRN[k]}.png");
        pointsTex = GD.Load<Texture2D>("res://asscets/Weapons/GrandBolt/gb_points.png");
        coreTex = GD.Load<Texture2D>("res://asscets/Weapons/GrandBolt/gb_core.png");
        var old = GetParent()?.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (old != null) old.Visible = false;
        baseSprite = new Sprite2D { Name = "GB_Base", Centered = true, RegionEnabled = true };
        AddChild(baseSprite);
        for (int i = 0; i < 4; i++)
        {
            pointSprites[i] = new Sprite2D { Name = $"GB_Point{i}", Centered = true, RegionEnabled = true, ZIndex = 1 };
            AddChild(pointSprites[i]);
        }
        ApplyDirection();
        ApplyTeamMaterial(true);
    }

    private static void RotRect(int x, int y, int w, int h, int k, out int ox, out int oy, out int ow, out int oh)
    {
        ox = x; oy = y; ow = w; oh = h;
        int W = 288, H = 192;
        for (int i = 0; i < k; i++)
        {
            int nx = H - oy - oh, ny = ox, nw = oh, nh = ow;
            ox = nx; oy = ny; ow = nw; oh = nh;
            int t = W; W = H; H = t;
        }
    }

    /// <summary>CoW正统换色: 编码资产+(R,G)查colortable LUT; team变更多节点同步重套材质</summary>
    private ShaderMaterial lutCache;
    /// <summary>✅ 每帧重断言: Weapon基类换色流程会事后覆盖子节点材质(GetBaseLayerMaterial等), 早退式缓存会让GB_Base被覆盖成中立lut——改为每帧强制重挂当前势力lut(查表仍按team缓存)</summary>
    private void ApplyTeamMaterial(bool force = false)
    {
        var w = GetParent() as Weapon;
        string t = w?.team ?? "neutral";
        if (t != lastTeam || lutCache == null || force)
        {
            lastTeam = t;
            lutCache = FactionPaletteManager.GetCowLutMaterial(t);
        }
        if (lutCache == null) return;
        baseSprite.Material = (ShaderMaterial)lutCache.Duplicate();
        foreach (var p in pointSprites)
            if (p != null) p.Material = (ShaderMaterial)lutCache.Duplicate();
    }

    private void ApplyDirection()
    {
        int k = enableRotation ? (direction & 3) : 0;
        baseSprite.Texture = baseTex[k];
        baseSprite.Position = new Vector2(FW[k] / 2f, FH[k] / 2f);
        baseSprite.RegionRect = new Rect2(frame * FW[k], 0, FW[k], FH[k]);
        for (int i = 0; i < 4; i++)
        {
            RotRect(PTS[i,0], PTS[i,1], PTS[i,2], PTS[i,3], k, out int rx, out int ry, out int rw, out int rh);
            int pw = (i == 3) ? (k % 2 == 1 ? 56 : 58) : (rw + 2 * PAD);
            int ph = (i == 3) ? (k % 2 == 1 ? 58 : 56) : (rh + 2 * PAD);
            pointSprites[i].Position = new Vector2(rx - PAD + pw / 2f, ry - PAD + ph / 2f);
            ApplyPointFrame(i, k);
        }
    }

    private void ApplyPointFrame(int i, int k)
    {
        if (i < 3)
        {
            int st = pointDead[i] ? 1 : 0;
            int si = k * 24 + i * 8 + st * 4 + frame;
            pointSprites[i].Texture = pointsTex;
            pointSprites[i].RegionRect = new Rect2((si % 12) * 16, (si / 12) * 16, 16, 16);
        }
        else
        {
            int st = pointDead[3] ? 1 : 0;
            int ci = k * 8 + st * 4 + frame;
            const int SLOT = 60;
            int pw = (k % 2 == 1) ? 56 : 58;
            int ph = (k % 2 == 1) ? 58 : 56;
            pointSprites[3].Texture = coreTex;
            pointSprites[3].RegionRect = new Rect2((ci % 4) * SLOT + (SLOT - pw) / 2, (ci / 4) * SLOT + (SLOT - ph) / 2, pw, ph);
        }
    }

    public override void _Process(double delta)
    {
        if (baseSprite == null) return;
        ApplyTeamMaterial();
        if (animFps <= 0) return;
        acc += delta;
        double step = 1.0 / animFps;
        if (acc < step) return;
        acc -= step;
        frame = (frame + 1) % 4;
        int k = enableRotation ? (direction & 3) : 0;
        baseSprite.RegionRect = new Rect2(frame * FW[k], 0, FW[k], FH[k]);
        for (int i = 0; i < 4; i++) ApplyPointFrame(i, k);
    }

    /// <summary>编辑模式面板API: 旋转开关/方向/帧率(实时应用到本体+全部弱点图层)</summary>
    public void SetEnableRotation(bool v) { enableRotation = v; ApplyDirection(); }
    public void SetDirection(int k) { direction = k & 3; ApplyDirection(); }
    public void SetAnimFps(double f) { animFps = f; }

    public void SetWeakPointDead(int id, bool dead = true)
    {
        if (id < 0 || id > 3) return;
        pointDead[id] = dead;
        if (baseSprite != null) ApplyPointFrame(id, enableRotation ? (direction & 3) : 0);
    }
    public bool IsWeakPointDead(int id) => id >= 0 && id <= 3 && pointDead[id];
}
