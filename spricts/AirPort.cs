using Godot;
using System;

public partial class AirPort : City
{
    // Source-art baseline measured from the Airport base layer (pink-red, not pure red).
    protected override float GetBaseArtHue() => 27.0f / 360.0f;

    // ✅ 检查器优先：默认补给兵种列表重写为空军（仅当检查器未配置时才使用）
    protected override string[] DefaultSupplyUnitTypes() => new string[]
    {
        "FlyBomb", "Fighter", "Bomber", "BCopter", "TCopter", "Stealth", "Duster", "Seaplane"
    };

    /// <summary>默认可生产兵种（子类可重写；空数组=不生产，如临时机场）</summary>
    protected virtual string[] DefaultProducibleUnits() => new string[]
    {
        "FlyBomb", "Fighter", "Bomber", "BCopter", "TCopter", "Stealth", "Duster"
    };

    public override void _Ready()
    {
        base._Ready();

        animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (animSprite == null)
        {
            GD.PushError(TranslateGroup.T("[AirPort] 找不到 AnimatedSprite2D 节点！"));
            return;
        }

        // 初始化生产模块默认值（检查器优先）
        if (producibleUnitNames.Count == 0)
        {
            var defs = DefaultProducibleUnits();
            if (defs.Length > 0)
            {
                canProduce = true;
                producibleUnitNames = new Godot.Collections.Array<string>(defs);
            }
        }

        UpdateCityVisual();
    }

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0AirPort", "airportteam1", "airportteam2");
}
