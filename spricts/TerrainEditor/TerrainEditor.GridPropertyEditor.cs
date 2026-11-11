using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · GridPropertyEditor（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{


// 替换原有的 BuildGridPropertyEditor 方法
private void BuildGridPropertyEditor(Grids grid)
{
    // 基础属性
    var basicLabel = new Label();
    TranslateGroup.SetText(basicLabel, "=== 基础属性 ===");
    basicLabel.AddThemeFontSizeOverride("font_size", 14);
    basicLabel.AddThemeColorOverride("font_color", Colors.Yellow);
    propertyContainer.AddChild(basicLabel);

    CreateMemberControl(grid, "gridType", typeof(GridType), 
        () => grid.gridType, (v) => { grid.gridType = (GridType)v; grid.BakeCowBrush(); grid.UpdateTerrainIcon(); }); // ✅ 修改地形属性时烘焙当前画笔风格

    if (grid.city != null)
    {
        var cityLabel = new Label();
        TranslateGroup.SetText(cityLabel, "=== City 属性 ===");
        cityLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        propertyContainer.AddChild(cityLabel);

        CreateMemberControl(grid.city, "facilityTeam", typeof(string),
            () => grid.city.facilityTeam, (v) => { grid.city.facilityTeam = (string)v; grid.city.UpdateCityVisual(); }); // ✅ 开创人2026-11-11: 编辑模式改势力即时刷新贴图(P-1正典紫)——与PropertyEditor.wrappedSetter同权
        CreateMemberControl(grid.city, "healAmount", typeof(int),
            () => grid.city.healAmount, (v) => { grid.city.healAmount = (int)v; });
        CreateMemberControl(grid.city, "flareAmmoSupply", typeof(int),
            () => grid.city.flareAmmoSupply, (v) => { grid.city.flareAmmoSupply = (int)v; });
        CreateMemberControl(grid.city, "explodeAmmoSupply", typeof(int),
            () => grid.city.explodeAmmoSupply, (v) => { grid.city.explodeAmmoSupply = (int)v; });
        CreateMemberControl(grid.city, "primaryAmmoSupply", typeof(int),
            () => grid.city.primaryAmmoSupply, (v) => { grid.city.primaryAmmoSupply = (int)v; });
        CreateMemberControl(grid.city, "fuelSupply", typeof(int),
            () => grid.city.fuelSupply, (v) => { grid.city.fuelSupply = (int)v; });
        CreateMemberControl(grid.city, "capturePointsRequired", typeof(int),
            () => grid.city.capturePointsRequired, (v) => { grid.city.capturePointsRequired = (int)v; });

        // ✅ City 每回合资金收入
        if (grid.city is City city)
        {
            CreateMemberControl(city, "fundsPerTurn", typeof(int),
                () => city.fundsPerTurn, (v) => { city.fundsPerTurn = (int)v; });
        }

        // ✅ 生产模块
        var prodLabel = new Label();
        TranslateGroup.SetText(prodLabel, "=== 生产模块 ===");
        prodLabel.AddThemeFontSizeOverride("font_size", 14);
        prodLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        propertyContainer.AddChild(prodLabel);

        CreateMemberControl(grid.city, "canProduce", typeof(bool),
            () => grid.city.canProduce, (v) => { grid.city.canProduce = (bool)v; });
        CreateMemberControl(grid.city, "producibleUnitNames", typeof(Godot.Collections.Array<string>),
            () => grid.city.producibleUnitNames, (v) => { grid.city.producibleUnitNames = (Godot.Collections.Array<string>)v; });

        // ✅ 占领进度（只读显示）
        var captureLabel = new Label();
        captureLabel.Text = $"{TranslateGroup.T("当前占领中单位数")}: {grid.city.capturingUnits.Count}";
        captureLabel.AddThemeFontSizeOverride("font_size", 12);
        captureLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.6f));
        propertyContainer.AddChild(captureLabel);
    }

    // ✅ 格子视野系统
    var visionLabel = new Label();
    TranslateGroup.SetText(visionLabel, "=== 格子视野系统 ===");
    visionLabel.AddThemeFontSizeOverride("font_size", 14);
    visionLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
    propertyContainer.AddChild(visionLabel);

    CreateMemberControl(grid, "gridVisionMode", typeof(GridVisionMode),
        () => grid.gridVisionMode, (v) => { grid.gridVisionMode = (GridVisionMode)v; });
    CreateMemberControl(grid, "gridVisionRange", typeof(int),
        () => grid.gridVisionRange, (v) => { grid.gridVisionRange = (int)v; });
    CreateMemberControl(grid, "towerDirection", typeof(TowerDirection),
        () => grid.towerDirection, (v) => { grid.towerDirection = (TowerDirection)v; });
    CreateMemberControl(grid, "towerCanRotate", typeof(bool),
        () => grid.towerCanRotate, (v) => { grid.towerCanRotate = (bool)v; });
    CreateMemberControl(grid, "towerRayAngle", typeof(float),
        () => grid.towerRayAngle, (v) => { grid.towerRayAngle = (float)v; });
    CreateMemberControl(grid, "gridVisionIgnoreTerrain", typeof(bool),
        () => grid.gridVisionIgnoreTerrain, (v) => { grid.gridVisionIgnoreTerrain = (bool)v; });
    CreateMemberControl(grid, "visionBonus", typeof(int),
        () => grid.visionBonus, (v) => { grid.visionBonus = (int)v; });
    
    // ✅ 关键：requiresAdjacentVision — 用户提到的"不紧邻不显示"
    CreateMemberControl(grid, "requiresAdjacentVision", typeof(bool),
        () => grid.requiresAdjacentVision, (v) => { grid.requiresAdjacentVision = (bool)v; });
    
    // ✅ 新增：瞭望塔标识
    CreateMemberControl(grid, "isWatchtower", typeof(bool),
        () => grid.isWatchtower, (v) => { grid.isWatchtower = (bool)v; });

    // 自定义伤害
    var damageLabel = new Label();
    TranslateGroup.SetText(damageLabel, "=== 自定义伤害 ===");
    damageLabel.AddThemeFontSizeOverride("font_size", 14);
    damageLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
    propertyContainer.AddChild(damageLabel);

    CreateMemberControl(grid, "canDestroyUnit", typeof(bool),
        () => grid.canDestroyUnit, (v) => { grid.canDestroyUnit = (bool)v; });
    CreateMemberControl(grid, "fixedDamagePerTurn", typeof(int),
        () => grid.fixedDamagePerTurn, (v) => { grid.fixedDamagePerTurn = (int)v; });
    CreateMemberControl(grid, "fixedAttackPerTurn", typeof(int),
        () => grid.fixedAttackPerTurn, (v) => { grid.fixedAttackPerTurn = (int)v; });
    CreateMemberControl(grid, "canOverMaxHealth", typeof(bool),
        () => grid.canOverMaxHealth, (v) => { grid.canOverMaxHealth = (bool)v; });
    CreateMemberControl(grid, "fixedMaxHealthChangePerTurn", typeof(int), // ✦优化：最大血量增减
        () => grid.fixedMaxHealthChangePerTurn, (v) => { grid.fixedMaxHealthChangePerTurn = (int)v; });

    // 弹药/燃料
    var ammoLabel = new Label();
    TranslateGroup.SetText(ammoLabel, "=== 弹药/燃料 ===");
    ammoLabel.AddThemeFontSizeOverride("font_size", 14);
    ammoLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.7f, 1f));
    propertyContainer.AddChild(ammoLabel);

    CreateMemberControl(grid, "fixedAmmoChangePerTurn", typeof(int),
        () => grid.fixedAmmoChangePerTurn, (v) => { grid.fixedAmmoChangePerTurn = (int)v; });
    // ✦优化：全种类弹药增减/上限增减
    CreateMemberControl(grid, "ammoChangePrimary", typeof(int), () => grid.ammoChangePrimary, (v) => { grid.ammoChangePrimary = (int)v; });
    CreateMemberControl(grid, "ammoChangeExplode", typeof(int), () => grid.ammoChangeExplode, (v) => { grid.ammoChangeExplode = (int)v; });
    CreateMemberControl(grid, "ammoChangeFlare", typeof(int), () => grid.ammoChangeFlare, (v) => { grid.ammoChangeFlare = (int)v; });
    CreateMemberControl(grid, "ammoChangeStealth", typeof(int), () => grid.ammoChangeStealth, (v) => { grid.ammoChangeStealth = (int)v; });
    CreateMemberControl(grid, "ammoChangeReveal", typeof(int), () => grid.ammoChangeReveal, (v) => { grid.ammoChangeReveal = (int)v; });
    CreateMemberControl(grid, "ammoChangeCamo", typeof(int), () => grid.ammoChangeCamo, (v) => { grid.ammoChangeCamo = (int)v; });
    CreateMemberControl(grid, "ammoChangeProduction", typeof(int), () => grid.ammoChangeProduction, (v) => { grid.ammoChangeProduction = (int)v; });
    CreateMemberControl(grid, "ammoChangeConstruction", typeof(int), () => grid.ammoChangeConstruction, (v) => { grid.ammoChangeConstruction = (int)v; });
    CreateMemberControl(grid, "ammoMaxChangePrimary", typeof(int), () => grid.ammoMaxChangePrimary, (v) => { grid.ammoMaxChangePrimary = (int)v; });
    CreateMemberControl(grid, "ammoMaxChangeExplode", typeof(int), () => grid.ammoMaxChangeExplode, (v) => { grid.ammoMaxChangeExplode = (int)v; });
    CreateMemberControl(grid, "ammoMaxChangeFlare", typeof(int), () => grid.ammoMaxChangeFlare, (v) => { grid.ammoMaxChangeFlare = (int)v; });
    CreateMemberControl(grid, "ammoMaxChangeStealth", typeof(int), () => grid.ammoMaxChangeStealth, (v) => { grid.ammoMaxChangeStealth = (int)v; });
    CreateMemberControl(grid, "ammoMaxChangeReveal", typeof(int), () => grid.ammoMaxChangeReveal, (v) => { grid.ammoMaxChangeReveal = (int)v; });
    CreateMemberControl(grid, "ammoMaxChangeCamo", typeof(int), () => grid.ammoMaxChangeCamo, (v) => { grid.ammoMaxChangeCamo = (int)v; });
    CreateMemberControl(grid, "ammoMaxChangeProduction", typeof(int), () => grid.ammoMaxChangeProduction, (v) => { grid.ammoMaxChangeProduction = (int)v; });
    CreateMemberControl(grid, "ammoMaxChangeConstruction", typeof(int), () => grid.ammoMaxChangeConstruction, (v) => { grid.ammoMaxChangeConstruction = (int)v; });
    CreateMemberControl(grid, "ammoCanOverMax", typeof(bool),
        () => grid.ammoCanOverMax, (v) => { grid.ammoCanOverMax = (bool)v; });
    CreateMemberControl(grid, "ammoCanReachZero", typeof(bool),
        () => grid.ammoCanReachZero, (v) => { grid.ammoCanReachZero = (bool)v; });
    CreateMemberControl(grid, "fixedFuelChangePerTurn", typeof(int),
        () => grid.fixedFuelChangePerTurn, (v) => { grid.fixedFuelChangePerTurn = (int)v; });
    CreateMemberControl(grid, "fixedMaxFuelChangePerTurn", typeof(int), // ✦优化：燃油上限增减
        () => grid.fixedMaxFuelChangePerTurn, (v) => { grid.fixedMaxFuelChangePerTurn = (int)v; });
    CreateMemberControl(grid, "fuelCanOverMax", typeof(bool),
        () => grid.fuelCanOverMax, (v) => { grid.fuelCanOverMax = (bool)v; });
    CreateMemberControl(grid, "fuelCanReachZero", typeof(bool),
        () => grid.fuelCanReachZero, (v) => { grid.fuelCanReachZero = (bool)v; });
    
    // ✅ 新增：格子上的单位/兵器列表（只读）
    var unitsLabel = new Label();
    TranslateGroup.SetText(unitsLabel, "=== 当前格子内容 ===");
    unitsLabel.AddThemeFontSizeOverride("font_size", 14);
    unitsLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 1f));
    propertyContainer.AddChild(unitsLabel);
    
    var unitCountLabel = new Label();
    unitCountLabel.Text = $"{TranslateGroup.T("单位")}: {grid.infantries.Count}{TranslateGroup.T($"{TranslateGroup.T("个")}")} | {TranslateGroup.T("兵器")}: {grid.weapons.Count}{TranslateGroup.T($"{TranslateGroup.T("个")}")}";
    unitCountLabel.AddThemeFontSizeOverride("font_size", 11);
    unitCountLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
    propertyContainer.AddChild(unitCountLabel);
}

    // ✅ 新增：地形视野加成矩阵编辑器
    private Control CreateTerrainBonusMatrixEditor(Godot.Collections.Dictionary<GridType, int> dict, Action<Godot.Collections.Dictionary<GridType, int>> onChanged)
    {
        var mainVbox = new VBoxContainer();
        mainVbox.AddThemeConstantOverride("separation", 4);

        var hintLabel = new Label();
        TranslateGroup.SetText(hintLabel, "每个地形的视野加成（可正可负）");
        hintLabel.AddThemeFontSizeOverride("font_size", 10);
        hintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.6f));
        mainVbox.AddChild(hintLabel);

        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(340, 200);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var container = new VBoxContainer();
        container.AddThemeConstantOverride("separation", 2);

        var terrainTypes = Enum.GetValues(typeof(GridType));

        foreach (GridType terrain in terrainTypes)
        {
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 8);
            hbox.CustomMinimumSize = new Vector2(0, 28);

            var nameLabel = new Label();
            nameLabel.Text = terrain.ToString();
            nameLabel.CustomMinimumSize = new Vector2(120, 24);
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            hbox.AddChild(nameLabel);

            int currentValue = dict.GetValueOrDefault(terrain, 0);

            var spinBox = new SpinBox();
            spinBox.MinValue = -10;
            spinBox.MaxValue = 10;
            spinBox.Value = currentValue;
            spinBox.CustomMinimumSize = new Vector2(70, 26);
            spinBox.AddThemeFontSizeOverride("font_size", 11);

            GridType capturedTerrain = terrain;
            spinBox.ValueChanged += (newVal) => {
                dict[capturedTerrain] = (int)newVal;
                onChanged(dict);
            };

            hbox.AddChild(spinBox);

            var signLabel = new Label();
            string sign = currentValue > 0 ? "+" : "";
            signLabel.Text = $"({sign}{currentValue})";
            signLabel.AddThemeFontSizeOverride("font_size", 10);
            signLabel.AddThemeColorOverride("font_color",
                currentValue > 0 ? new Color(0.3f, 1, 0.3f) :
                currentValue < 0 ? new Color(1, 0.3f, 0.3f) : new Color(0.7f, 0.7f, 0.7f));
            hbox.AddChild(signLabel);

            container.AddChild(hbox);
        }

        scroll.AddChild(container);
        mainVbox.AddChild(scroll);

        return mainVbox;
    }
}
