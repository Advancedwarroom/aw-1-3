using Godot;
using System;

public partial class Base : City
{
    private AnimatedSprite2D animSprite;

    public override void _Ready()
    {
        base._Ready();

        animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (animSprite == null)
        {
            GD.PushError("[Base] 找不到 AnimatedSprite2D 节点！");
            return;
        }

        // 初始化生产模块默认值
        if (!canProduce && producibleUnitNames.Count == 0)
        {
            canProduce = true;
            producibleUnitNames = new Godot.Collections.Array<string>
            {
                "Infantry", "Mech", "Bike", "Oozium",
                "LightTank", "MdTank", "Rocket", "Artillery",
                "APC", "AntiAir", "Recon", "AntiTank", "Flare", "PipeRunner",
            };
        }

        UpdateCityVisual();
    }

    public override void UpdateCityVisual()
    {
        if (animSprite == null) return;

        string animName = facilityTeam switch
        {
            "Player1" => "baseteam1",
            "Player2" => "baseteam2",
            "Player0" => "P0Base",
            "Player" => "baseteam1",
            _ => "P0Base"
        };

        if (animSprite.SpriteFrames.HasAnimation(animName))
        {
            animSprite.Play(animName);
        }
        else if (animSprite.SpriteFrames.HasAnimation("P0Base"))
        {
            animSprite.Play("P0Base");
        }

        Color tint = (facilityTeam == TeamHelper.Player)
            ? TeamHelper.GetTeamColor(facilityTeam)
            : Colors.White;
        animSprite.Modulate = tint;

    }
}
