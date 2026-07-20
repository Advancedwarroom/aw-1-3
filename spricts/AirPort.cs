using Godot;
using System;

public partial class AirPort : City
{
    private AnimatedSprite2D animSprite;

    public override void _Ready()
    {
        base._Ready();

        // 补给配置：只支持 FlyBomb
        healMode = 1;      
        healPercent = 0.2f; 
        flareAmmoSupply = 999;
        explodeAmmoSupply = 999;
        primaryAmmoSupply = 999;
        fuelSupply = 999;
        supportedUnitTypesForHeal = new Godot.Collections.Array<string> { "FlyBomb" };
        supportedUnitTypesForFlare = new Godot.Collections.Array<string> { "FlyBomb" };
        supportedUnitTypesForExplode = new Godot.Collections.Array<string> { "FlyBomb" };
        supportedUnitTypesForPrimaryAmmo = new Godot.Collections.Array<string> { "FlyBomb" };
        supportedUnitTypesForFuel = new Godot.Collections.Array<string> { "FlyBomb" };

        animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (animSprite == null)
        {
            GD.PushError("[AirPort] 找不到 AnimatedSprite2D 节点！");
            return;
        }

        // 初始化生产模块默认值
        if (!canProduce && producibleUnitNames.Count == 0)
        {
            canProduce = true;
            producibleUnitNames = new Godot.Collections.Array<string> { "FlyBomb" };
        }

        UpdateCityVisual();
    }

    public override void UpdateCityVisual()
    {
        if (animSprite == null) return;

        string animName = facilityTeam switch
        {
            "Player1" => "airportteam1",
            "Player2" => "airportteam2",
            "Player0" => "P0AirPort",
            "Player" => "airportteam1",
            _ => "P0AirPort"
        };

        if (animSprite.SpriteFrames.HasAnimation(animName))
        {
            animSprite.Play(animName);
        }
        else if (animSprite.SpriteFrames.HasAnimation("P0AirPort"))
        {
            animSprite.Play("P0AirPort");
        }

        Color tint = (facilityTeam == TeamHelper.Player)
            ? TeamHelper.GetTeamColor(facilityTeam)
            : Colors.White;
        animSprite.Modulate = tint;

    }
}
