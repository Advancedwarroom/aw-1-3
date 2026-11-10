using Godot;
using System;

public partial class Base : City
{
    // Base light/dark base frames share a near-zero-degree red source.
    protected override float GetBaseArtHue() => 0.7f / 360.0f;

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0Base", "baseteam1", "baseteam2");

    public override void _Ready()
    {
        base._Ready();

        // 初始化生产模块默认值
        if (!canProduce && producibleUnitNames.Count == 0)
        {
            canProduce = true;
            producibleUnitNames = new Godot.Collections.Array<string>
            {
                "Infantry", "Mech", "Bike", "Oozium",
                "LightTank", "MdTank", "Rocket", "Artillery",
                "APC", "AntiAir", "Recon", "AntiTank", "Flare", "PipeRunner", "AntiAirMissile", "Dog",
                "Neotank", "Megatank", "WarTank", "Rig",
            };
        }
    }
}
