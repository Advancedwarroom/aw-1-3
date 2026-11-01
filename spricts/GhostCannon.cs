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
    }
}
