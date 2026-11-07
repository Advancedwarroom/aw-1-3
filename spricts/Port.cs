using Godot;
using System;

// ✅ 港口（原作 AW Port）：可生产海军单位、维修/补给舰船、每回合 1000 收入、可占领。
//    检查器优先：默认值只在「未配置」时补上（场景 .tscn / 检查器里写好的值不会被覆盖）。
//    舰船可靠泊（海军移动开销覆盖见 Infantry.GetMoveCostOnGrid）。
public partial class Port : City
{
    // 港口默认可生产的舰船（含既有维修艇）
    private static readonly string[] DefaultNavalUnits =
    {
        "Battleship", "Cruiser", "Submarine", "Lander", "Carrier", "Gunboat", "RepairBoat",
        "ProducibleBoat", "KamikazeBoat"
    };

    /// <summary>默认可生产兵种（子类可重写；空数组=不生产，如临时港口）</summary>
    protected virtual string[] DefaultProducibleUnits() => DefaultNavalUnits;

    // ✅ 检查器优先：默认补给兵种列表重写为海军
    protected override string[] DefaultSupplyUnitTypes() => DefaultNavalUnits;

    public override void _Ready()
    {
        base._Ready();

        // ✅ 新弹药：生产/建造弹（仅当未配置时补默认）
        if (productionAmmoSupply == 0) productionAmmoSupply = 999;
        if (constructionAmmoSupply == 0) constructionAmmoSupply = 999;

        // 生产模块默认值（检查器优先）
        if (producibleUnitNames.Count == 0)
        {
            var defs = DefaultProducibleUnits();
            if (defs.Length > 0)
            {
                canProduce = true;
                producibleUnitNames = new Godot.Collections.Array<string>(defs);
            }
        }

        animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (animSprite == null)
        {
            GD.PushError(TranslateGroup.T("[Port] 找不到 AnimatedSprite2D 节点！"));
            return;
        }

        UpdateCityVisual();
    }

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0Port", "portteam1", "portteam2");
}
