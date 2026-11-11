using Godot;

public partial class City : Facility
{
    // City base art's dominant team-red pixels are approximately 1 degree.
    protected override float GetBaseArtHue() => 58.0f / 360.0f;


    /// <summary>默认补给/维修的兵种列表（子类可重写；仅当检查器未配置时才使用）</summary>
    protected virtual string[] DefaultSupplyUnitTypes() => new string[]
    {
        "Infantry", "Mech", "Bike", "Oozium",
        "LightTank", "Artillery", "APC", "AntiAir",
        "AntiTank", "MdTank", "Recon", "Flare",
        "Neotank", "Megatank", "WarTank", "Rig"
    };

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0City", "cityteam1", "cityteam2");

    public override void _Ready()
    {
        base._Ready();

        // 补给配置：支持默认兵种列表（✅ 检查器优先：仅当未配置时补默认，改检查器不再被覆盖）
        if (supportedUnitTypesForHeal.Count == 0)
        {
            healMode = 1;       // 百分比模式
            healPercent = 0.2f; // 20%最大HP
            flareAmmoSupply = 999;
            explodeAmmoSupply = 999;
            primaryAmmoSupply = 999;
            fuelSupply = 999;
            supportedUnitTypesForHeal = new Godot.Collections.Array<string>(DefaultSupplyUnitTypes());
            supportedUnitTypesForFlare = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
            supportedUnitTypesForExplode = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
            supportedUnitTypesForPrimaryAmmo = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
            supportedUnitTypesForFuel = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
            supportedUnitTypesForProductionAmmo = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
            supportedUnitTypesForConstructionAmmo = new Godot.Collections.Array<string>(supportedUnitTypesForHeal);
        }
    }
}
