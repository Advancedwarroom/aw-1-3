// TalentPool.cs - 天赋池 (开创人2026-11-14)
using Godot;

[GlobalClass]
public partial class TalentPool : Resource
{
    [Export] public string poolName = "默认池";
    [Export] public TalentDef[] commonPool = new TalentDef[0];   // 普通
    [Export] public TalentDef[] rarePool = new TalentDef[0];     // 稀有(专属CO挂这)
    [Export] public TalentDef[] goldenPool = new TalentDef[0];   // 金色(改玩法定律)
    [Export] public TalentDef[] uraniumPool = new TalentDef[0];   // ✅ 铀核(紫·精英阶)
    [ExportGroup("选秀参数")]
    [Export] public int draftCount = 3;           // 每次三选一
    [Export] public int refreshPerDraft = 1;      // 每关刷新次数
    [Export] public int normalSlots = 4;            // 普通槽
    [Export] public int goldenSlots = 1;            // 金色槽
    [Export] public double commonWeight = 100;      // 普通池权重(原先硬编码100)
    [Export] public double rareWeight = 40;         // 稀有权重(普通=100基准)
    [Export] public double goldenWeight = 8;        // 金色权重
    [Export] public double uraniumWeight = 3;      // ✅ 铀核权重(比反物质更稀罕)
}
