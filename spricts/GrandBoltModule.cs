// GrandBoltModule.cs
// 模块化巨雷(学CoW多件分置的独立新版, 不动整体版GrandBolt/GrandBoltVisual):
// 弱点A/B/C + 核心 四个独立可放置模块, 各自live/dead贴图独立切换, 摧毁后锁血显dead, CoW LUT势力换色
using Godot;
using System;

public partial class GrandBoltModule : Weapon
{
    public enum ModuleType { WeakA = 0, WeakB = 1, WeakC = 2, Core = 3 }
    [Export] public ModuleType moduleType = ModuleType.WeakA;
    [Export] public double animFps = 2.0;

    private static readonly string[] TEX = {
        "res://asscets/Weapons/GrandBolt/mod_a.png",
        "res://asscets/Weapons/GrandBolt/mod_b.png",
        "res://asscets/Weapons/GrandBolt/mod_c.png",
        "res://asscets/Weapons/GrandBolt/mod_core.png",
    };
    private Sprite2D modSprite;
    private Texture2D strip;
    private int fw, fh, frame;
    private bool dead;
    private string lastTeam;

    public override void _Ready()
    {
        base._Ready();
        strip = GD.Load<Texture2D>(TEX[(int)moduleType]);
        fw = (int)strip.GetWidth() / 8;
        fh = (int)strip.GetHeight();
        modSprite = new Sprite2D { Name = "ModuleSprite", Centered = true, RegionEnabled = true, Position = new Vector2(fw / 2f, fh / 2f) };
        AddChild(modSprite);
        ApplyFrame();
        ApplyLut(true);
        var timer = new Timer { Name = "AnimTimer", WaitTime = 0.5, Autostart = true };
        timer.Timeout += OnAnimTick;
        AddChild(timer);
    }

    private void ApplyLut(bool force = false)
    {
        if (modSprite == null || (team == lastTeam && !force)) return;
        lastTeam = team;
        var lut = FactionPaletteManager.GetCowLutMaterial(team);
        if (lut != null) modSprite.Material = (ShaderMaterial)lut.Duplicate();
    }

    public bool IsModuleDead() => dead;
    /// <summary>模块摧毁状态: dead=true 换dead贴图并锁血(不再QueueFree, 学CoW多件各自留尸)</summary>
    public void SetModuleDead(bool value)
    {
        dead = value;
        healthLocked = dead;
        ApplyFrame();
    }

    public override void TakeDamage(int damage)
    {
        if (dead) return;
        base.TakeDamage(damage);
        if (health <= 0) SetModuleDead(true);
    }

    private void ApplyFrame()
    {
        if (modSprite == null) return;
        modSprite.Texture = strip;
        modSprite.RegionRect = new Rect2(((dead ? 4 : 0) + frame) * fw, 0, fw, fh);
    }

    private void OnAnimTick()
    {
        ApplyLut();
        if (animFps <= 0) return;
        var timer = GetNodeOrNull<Timer>("AnimTimer");
        if (timer != null) timer.WaitTime = 1.0 / animFps;
        frame = (frame + 1) % 4;
        ApplyFrame();
    }
}
