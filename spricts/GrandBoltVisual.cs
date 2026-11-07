// GrandBolt.VisualParts.cs
// 巨雷分部件视觉系统(终稿19x12@2x): 基体+四弱点独立图层, 四向离线预渲染查表, 弱点状态机, CoW LUT势力换色
// 终稿: 304x194整帧=19x12(16px/tile), 显示2x(32px格); 弱点 A(3,9) B(9,11) C(15,9) 核心(8,5,3x2)
using Godot;
using System;

public partial class GrandBoltVisual : Node2D
{
    [Export] public bool enableRotation = true;
    [Export] public int direction = 0;
    [Export] public double animFps = 2.0;

    private Sprite2D baseSprite;
    private readonly Sprite2D[] pointSprites = new Sprite2D[4];
    private readonly bool[] pointDead = new bool[4];
    private Texture2D[] baseTex;
    private Texture2D pointsTex, coreTex;
    private double acc;
    private int frame;
    private string lastTeam;
    private ShaderMaterial lutCache;

    private static readonly int[,] PTS = {
        { 48,144,16,16 },
        { 144,176,16,16 },
        { 240,144,16,16 },
        { 128,80,48,32 },
    };
    private const int PAD = 1;
    private const int SCALE = 2;
    private static readonly string[] DIRN = { "S", "W", "N", "E" };
    private static readonly int[] FW = { 608, 388, 608, 388 };
    private static readonly int[] FH = { 388, 608, 388, 608 };

    public override void _Ready()
    {
        baseTex = new Texture2D[4];
        for (int k = 0; k < 4; k++)
            baseTex[k] = GD.Load<Texture2D>($"res://asscets/Weapons/GrandBolt/gb_base_{DIRN[k]}.png");
        pointsTex = GD.Load<Texture2D>("res://asscets/Weapons/GrandBolt/gb_points.png");
        coreTex = GD.Load<Texture2D>("res://asscets/Weapons/GrandBolt/gb_core.png");
        if (GetParent() != null)
            foreach (var n in GetParent().GetChildren())
                if (n is AnimatedSprite2D a) a.Visible = false;
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
        int W = 304, H = 194;
        for (int i = 0; i < k; i++)
        {
            int nx = H - oy - oh, ny = ox, nw = oh, nh = ow;
            ox = nx; oy = ny; ow = nw; oh = nh;
            int t = W; W = H; H = t;
        }
    }

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
            int pw = (i == 3) ? (k % 2 == 1 ? 68 : 100) : (rw + 2 * PAD) * SCALE;
            int ph = (i == 3) ? (k % 2 == 1 ? 100 : 68) : (rh + 2 * PAD) * SCALE;
            pointSprites[i].Position = new Vector2((rx - PAD) * SCALE + pw / 2f, (ry - PAD) * SCALE + ph / 2f);
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
            pointSprites[i].RegionRect = new Rect2((si % 12) * 36, (si / 12) * 36, 36, 36);
        }
        else
        {
            int st = pointDead[3] ? 1 : 0;
            int ci = k * 8 + st * 4 + frame;
            const int SLOT = 108;
            int pw = (k % 2 == 1) ? 68 : 100;
            int ph = (k % 2 == 1) ? 100 : 68;
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
        if (acc < 1.0 / animFps) return;
        acc -= 1.0 / animFps;
        frame = (frame + 1) % 4;
        int k = enableRotation ? (direction & 3) : 0;
        baseSprite.RegionRect = new Rect2(frame * FW[k], 0, FW[k], FH[k]);
        for (int i = 0; i < 4; i++) ApplyPointFrame(i, k);
    }

    public void SetWeakPointDead(int id, bool dead = true)
    {
        if (id < 0 || id > 3) return;
        pointDead[id] = dead;
        if (baseSprite != null) ApplyPointFrame(id, enableRotation ? (direction & 3) : 0);
    }
    public bool IsWeakPointDead(int id) => id >= 0 && id <= 3 && pointDead[id];
    public void SetEnableRotation(bool v) { enableRotation = v; ApplyDirection(); }
    public void SetDirection(int kk) { direction = kk & 3; ApplyDirection(); }
    public void SetAnimFps(double f) { animFps = f; }
}
