// CampaignProfile.cs - 战役档案：进度/通关集/双货币总账,user://持久化 (开创人2026-11-14)
using Godot;
using System.Collections.Generic;
public partial class CampaignProfile : Node
{
    public static CampaignProfile Instance { get; private set; }
    public int CurrentProgress { get; private set; } = 0;
    public HashSet<int> ClearedSlots { get; } = new();
    public double TotalScore { get; private set; } = 0;
    public double TotalBlackCrystals { get; private set; } = 0;   // 中文:宝钞 | EN:BlackCrystals | JP:Black Crystal复用
    public override void _Ready() { Instance = this; AddToGroup("campaign_profile"); Load(); }
    public static double Round4(double v) => System.Math.Round(v, 4);
    public bool IsStageUnlocked(int progressValue, bool strictHide, int slot)
        => (progressValue <= 0 || progressValue <= CurrentProgress) && !(strictHide && ClearedSlots.Contains(slot));
    public void RecordStageClear(int slot, int progressValue, bool strictHide, double score, double crystals)
    {
        if (strictHide) ClearedSlots.Add(slot);
        if (progressValue > CurrentProgress) CurrentProgress = progressValue;
        TotalScore = Round4(TotalScore + Round4(score));
        TotalBlackCrystals = Round4(TotalBlackCrystals + Round4(crystals));
        Save();
    }
    public void AddKillRewards(double score, double crystals)
    { TotalScore = Round4(TotalScore + Round4(score)); TotalBlackCrystals = Round4(TotalBlackCrystals + Round4(crystals)); }
    private const string SavePath = "user://campaign_profile.json";
    private void Save()
    {
        var d = new Godot.Collections.Dictionary { ["progress"] = CurrentProgress, ["score"] = TotalScore, ["crystals"] = TotalBlackCrystals, ["cleared"] = string.Join(",", ClearedSlots) };
        using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        f.StoreString(Json.Stringify(d));
    }
    private void Load()
    {
        if (!Godot.FileAccess.FileExists(SavePath)) return;
        var d = Json.ParseString(Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read).GetAsText()).AsGodotDictionary();
        CurrentProgress = (int)d["progress"]; TotalScore = (double)d["score"]; TotalBlackCrystals = (double)d["crystals"];
        foreach (var s in ((string)d["cleared"]).Split(",", System.StringSplitOptions.RemoveEmptyEntries)) ClearedSlots.Add(int.Parse(s));
    }
}
