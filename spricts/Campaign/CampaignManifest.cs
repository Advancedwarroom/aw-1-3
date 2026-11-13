// CampaignManifest.cs - 战役清单：进度门+双货币关卡奖励 (开创人2026-11-14)
using Godot;
[GlobalClass]
public partial class CampaignManifest : Resource
{
    [ExportGroup("战役清单")]
    [Export] public string campaignName = "战役";
    [Export] public int[] stageSlots = new int[0];
    [Export] public int[] stageProgressValues = new int[0];              // ≤0=永远开放
    [Export] public Godot.Collections.Array<bool> stageStrictHideAfterClear = new();   // 通关后隐藏(一次性剧情关)
    [Export] public double[] stageScoreRewards = new double[0];
    [Export] public double[] stageCrystalRewards = new double[0];        // 宝钞(BlackCrystals)
    public int StageCount => stageSlots.Length;
}
