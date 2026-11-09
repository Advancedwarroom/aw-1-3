// ProductionPricing.cs - 实例级生产价格。正数=降价百分比，负数=加价百分比。
using Godot;

public static class ProductionPricing
{
    public static int Calculate(int baseCost, int generalDiscountPercent,
        Godot.Collections.Dictionary<string, int> perUnitDiscountPercent,
        string unitName, int runtimeModifierPercent = 0)
    {
        int unitDiscount = 0;
        if (!string.IsNullOrEmpty(unitName) && perUnitDiscountPercent != null)
            perUnitDiscountPercent.TryGetValue(unitName, out unitDiscount);
        int finalPercent = 100 + runtimeModifierPercent - generalDiscountPercent - unitDiscount;
        return Mathf.Max(1, Mathf.RoundToInt(baseCost * finalPercent / 100f));
    }
}

public partial class Facility
{
    [ExportGroup("实例生产降价")]
    [Export] public int productionDiscountPercent = 0;
    [Export] public Godot.Collections.Dictionary<string, int> productionDiscountByUnit = new();

    public int GetProductionCost(int baseCost, string unitName)
        => ProductionPricing.Calculate(baseCost, productionDiscountPercent, productionDiscountByUnit, unitName, productionSuppressPercent);
}

public partial class Weapon
{
    [ExportGroup("实例生产降价")]
    [Export] public int productionDiscountPercent = 0;
    [Export] public Godot.Collections.Dictionary<string, int> productionDiscountByUnit = new();

    public int GetInstanceProductionCost(int baseCost, string unitName)
        => ProductionPricing.Calculate(baseCost, productionDiscountPercent, productionDiscountByUnit, unitName);
}

public partial class Infantry
{
    [ExportGroup("实例生产降价")]
    [Export] public int productionDiscountPercent = 0;
    [Export] public Godot.Collections.Dictionary<string, int> productionDiscountByUnit = new();

    public int GetInstanceProductionCost(int baseCost, string unitName)
        => ProductionPricing.Calculate(baseCost, productionDiscountPercent, productionDiscountByUnit, unitName);
}
