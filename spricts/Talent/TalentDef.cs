// TalentDef.cs - 天赋定义 (开创人2026-11-14)
// 数据面首片:一个天赋=ID+稀有度+叠级上限+标签+专属CO+效果映射(复用赋能字典语义)
using Godot;

[GlobalClass]
public partial class TalentDef : Resource
{
    [Export] public string talentId = "";                        // 唯一ID
    [Export] public string displayName = "新天赋";
    [Export] public string description = "";
    [Export] public int rarity = 0;                              // 0=普通 1=稀有 2=金色
    [Export] public int maxStacks = 5;                           // 叠级上限(金色通常1)
    [Export] public string[] tags = new string[0];               // 空军/陆军/经济/CO/海军...
    [Export] public string coExclusiveId = "";                   // 专属CO档案ID(空=通用)
    [Export] public bool goldenSlotOnly = false;                  // 只能放金色槽
    // 效果层——与赋能/内部服务同一套字典语义,应用层下轮焊
    [Export] public Godot.Collections.Dictionary<string, string> exportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> exportDeltas = new();
}
