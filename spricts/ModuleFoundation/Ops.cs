// Ops.cs - 三界公共操作名册（模块通用化基座的一部分）
// 单位/兵器/设施所有模块的"操作对表"统一用这里登记的名字。
// 约定（开创人定）：填不存在的操作名 = 该规则默认不调用；留空 = 走默认/不附加。
public static class Ops
{
    // ── 单位层 ──
    public const string Attack = "Attack";
    public const string Move = "Move";
    public const string Wait = "Wait";
    public const string Capture = "Capture";            // 含远程占领/舱内占领
    public const string TearFlag = "TearFlag";          // 拆旗/舱内撕旗
    public const string Supply = "Supply";
    public const string Repair = "Repair";
    public const string Explode = "Explode";
    public const string Flare = "Flare";               // 照明弹投射
    public const string Illuminate = "Illuminate";       // 照明覆盖
    public const string Stealth = "Stealth";
    public const string Scent = "Scent";                // 嗅探
    public const string Load = "Load";                 // 搭载
    public const string Unload = "Unload";
    public const string Merge = "Merge";                // 合流
    public const string Teleport = "Teleport";
    public const string Rotate = "Rotate";             // 多格旋转
    public const string Production = "Production";       // 生产
    public const string Construction = "Construction";   // 建造/部署建筑
    public const string LoadedAttack = "LoadedAttack";   // 舱内系列
    public const string LoadedCapture = "LoadedCapture";
    public const string LoadedTear = "LoadedTear";
    public const string LoadedStealth = "LoadedStealth";
    public const string Shelter = "Shelter";            // 掩蔽
    public const string ForceSupport = "ForceSupport";   // 兵力支援
    public const string PaidResupply = "PaidResupply";   // 自费补给
    public const string PushAction = "PushAction";       // 推动独立行动
    public const string CoPower = "CoPower";            // COP/SCOP

    // ── 兵器层 ──
    public const string WeaponAttack = "WeaponAttack";
    public const string WeaponWait = "WeaponWait";
    public const string WeaponRotate = "WeaponRotate";   // 塔旋转
    public const string ArcStrike = "ArcStrike";
    public const string SiloStrike = "SiloStrike";
    public const string EmpBurst = "EmpBurst";
    public const string Wriggle = "Wriggle";            // 巨雷蠕动
    public const string Heal = "Heal";                  // 黑水晶治疗
    public const string ShieldToggle = "ShieldToggle";

    // ── 设施层 ──
    public const string FacilityCapture = "FacilityCapture";
    public const string FacilityRepair = "FacilityRepair";
    public const string FacilityProduce = "FacilityProduce";

    public static readonly string[] All =
    {
        Attack, Move, Wait, Capture, TearFlag, Supply, Repair, Explode, Flare, Illuminate,
        Stealth, Scent, Load, Unload, Merge, Teleport, Rotate, Production, Construction,
        LoadedAttack, LoadedCapture, LoadedTear, LoadedStealth, Shelter, ForceSupport,
        PaidResupply, PushAction, CoPower,
        WeaponAttack, WeaponWait, WeaponRotate, ArcStrike, SiloStrike, EmpBurst, Wriggle, Heal, ShieldToggle,
        FacilityCapture, FacilityRepair, FacilityProduce
    };
}
