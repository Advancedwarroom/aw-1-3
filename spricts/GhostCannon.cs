// GhostCannon.cs - 幽灵炮（AW2/3 原作特殊兵器）
// 原作：只能被防空/反空中单位和空军/天空军单位攻击扣血，不能被陆军/海军单位攻击
// 模块：伤害类别限制（Weapon 基类可拼装 Export，本类预置原作默认值）
using Godot;

public partial class GhostCannon : Weapon
{
    // ========== 1. 是否开启 ==========
    [ExportGroup("幽灵炮模块")]
    [Export] public bool ghostEnabled = true;                  // 1.Export是否开启

    // ========== 2. 伤害单位类别限制（原作规则预置）==========
    // 原作：仅防空/反空中单位和空军/天空军单位能造成伤害；陆军/海军不能
    // ✅ 预置到基类 restrictAttackerCategories / allowAttacker* （Weapon.cs 可拼装 Export）
    // 本类默认值（_Ready 时写入基类，编辑器可覆盖）
    [ExportGroup("伤害类别限制（原作默认）")]
    [Export] public bool ghostRestrictCategories = true;      // 启用类别限制
    [Export] public bool ghostAllowAir = true;               // 空军/天空军可伤害
    [Export] public bool ghostAllowLand = false;             // 陆军不可伤害
    [Export] public bool ghostAllowNaval = false;            // 海军不可伤害
    [Export] public bool ghostAllowAntiAir = true;          // 防空/反空中可伤害

    // ========== ✅ CoW (R,G)编码贴图 → LUT解码(随势力换色) ==========
    protected override void ApplyPaletteMaterial()
    {
        if (animSprite == null || !IsInstanceValid(animSprite)) return;
        var lut = FactionPaletteManager.GetCowLutMaterial(team);
        if (lut == null) return;
        animSprite.Material = (ShaderMaterial)lut.Duplicate(); // 独立副本防 tone 污染共享缓存
    }

    // ========== 4. 朝向（决定显示哪张方向贴图，编辑器可设，运行时可转）==========
    public enum GhostDirection { Up, Right, Down, Left }
    [Export] public GhostDirection direction = GhostDirection.Up;

    // ========== 3. 血量（继承基类千分制 maxHealth/health）==========
    // maxHealth/health 继承 Weapon 基类

    public override void _Ready()
    {
        // ✅ 预置原作默认值到基类（可拼装，编辑器可覆盖）
        restrictAttackerCategories = ghostRestrictCategories;
        allowAttackerAirUnits = ghostAllowAir;
        allowAttackerLandUnits = ghostAllowLand;
        allowAttackerNavalUnits = ghostAllowNaval;
        allowAttackerAntiAirUnits = ghostAllowAntiAir;

        base._Ready();
        UpdateDirectionVisual();
    }

    // 方向贴图动画名（GhostCannonN/E/S/W）
    public override string GetAnimName()
    {
        string suffix = direction switch
        {
            GhostDirection.Up    => "N",
            GhostDirection.Down  => "S",
            GhostDirection.Left  => "W",
            GhostDirection.Right => "E",
            _ => "N"
        };
        return $"GhostCannon{suffix}";
    }

    public void UpdateDirectionVisual()
    {
        if (animSprite == null || animSprite.SpriteFrames == null) return;
        string anim = GetAnimName();
        if (animSprite.SpriteFrames.HasAnimation(anim))
            animSprite.Play(anim);
    }

    public override void RotateDirection()
    {
        if (isDestroyed || !canRotate) return;
        direction = (GhostDirection)(((int)direction + 1) % 4);
        UpdateDirectionVisual();
    }

    public override void RefreshTeamVisual()
    {
        base.RefreshTeamVisual();
        UpdateDirectionVisual();
    }
}
