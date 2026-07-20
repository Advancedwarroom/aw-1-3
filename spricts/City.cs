using Godot;

public partial class City : Facility
{
    [Export] public int fundsPerTurn = 1000;
    private AnimatedSprite2D animSprite;

    public override void _Ready()
    {
        base._Ready();

        // 补给配置：支持所有陆军单位
        healMode = 1;       // 百分比模式
        healPercent = 0.2f; // 20%最大HP
        flareAmmoSupply = 999;
        explodeAmmoSupply = 999;
        primaryAmmoSupply = 999;
        fuelSupply = 999;
        supportedUnitTypesForHeal = new Godot.Collections.Array<string>
        {
            "Infantry", "Mech", "Bike", "Oozium",
            "LightTank", "Artillery", "APC", "AntiAir",
            "AntiTank", "MdTank", "Recon", "Flare"
        };
        supportedUnitTypesForFlare = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
        supportedUnitTypesForExplode = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
        supportedUnitTypesForPrimaryAmmo = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
        supportedUnitTypesForFuel = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);

        animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (animSprite == null)
        {
            GD.PushError("[City] 找不到 AnimatedSprite2D 节点！");
            return;
        }

        UpdateCityVisual();
    }

    public override void UpdateCityVisual()
    {
        if (animSprite == null) return;

        string animName = facilityTeam switch
        {
            "Player1" => "cityteam1",
            "Player2" => "cityteam2",
            "Player0" => "P0City",
            "Player" => "cityteam1",
            _ => "cityteam1"
        };

        if (animSprite.SpriteFrames.HasAnimation(animName))
        {
            animSprite.Play(animName);
        }
        else
        {
            GD.PushWarning($"[City] 动画 '{animName}' 不存在于 SpriteFrames 中");
        }

        // P-1 用势力色调（浅紫），P0/P1/P2 用原始贴图
        Color tint = (facilityTeam == TeamHelper.Player)
            ? TeamHelper.GetTeamColor(facilityTeam)
            : Colors.White;
        animSprite.Modulate = tint;

    }
}
