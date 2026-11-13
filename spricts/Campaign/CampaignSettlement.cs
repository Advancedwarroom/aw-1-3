// CampaignSettlement.cs - 战役结算：胜利时刻统一记账(摧毁/占领/接管) (开创人2026-11-14)
// 原则:全部在胜利瞬间结算,失败过程摧毁不加分;仅ResetRun后激活,普通对战零影响。
using Godot;
using System.Collections.Generic;
public partial class CampaignSettlement : Node
{
    public static CampaignSettlement Instance { get; private set; }
    public bool Active { get; private set; } = false;
    public CampaignManifest Manifest { get; private set; }
    public int CurrentSlot { get; private set; } = -1;
    private readonly Dictionary<string, (double score, double crystals)> _killLedger = new();
    public override void _Ready() { Instance = this; AddToGroup("campaign_settlement"); }
    public void ResetRun(CampaignManifest m, int slot) { Manifest = m; CurrentSlot = slot; _killLedger.Clear(); Active = m != null; }
    // ✅ 接线点(下一轮换锚):单位/兵器/设施死亡入口调用 RecordKill(受害者的两个Export值, 凶手team)
    public void RecordKill(double scoreValue, double crystalValue, string killerTeam)
    {
        if (!Active || killerTeam == "P0" || killerTeam == "Player0" || string.IsNullOrEmpty(killerTeam)) return;
        _killLedger.TryGetValue(killerTeam, out var cur);
        _killLedger[killerTeam] = (CampaignProfile.Round4(cur.score + scoreValue), CampaignProfile.Round4(cur.crystals + crystalValue));
    }
    public void OnVictoryDeclared(string winner)
    {
        if (!Active || CampaignProfile.Instance == null) { Active = false; return; }
        foreach (var kv in _killLedger) CampaignProfile.Instance.AddKillRewards(kv.Value.score, kv.Value.crystals);
        var root = GetTree()?.Root; if (root == null) { Active = false; return; }
        foreach (var node in root.FindChildren("*", "Facility", true, false))
        {
            var f = node as Facility; if (f == null || !f.IsInsideTree()) continue;
            double os = Num(f, "campaignOccupierScoreValue"), oc = Num(f, "campaignOccupierCrystalValue");
            if (os != 0 || oc != 0) CampaignProfile.Instance.AddKillRewards(os, oc);   // 终局占领者得账(不看胜负)
            string occ = f.facilityTeam;
            if (occ == winner || occ == "P-1" || occ == "Player0") continue;
            if (f.Get("takeoverEnabled").VariantType == Variant.Type.Bool && (bool)f.Get("takeoverEnabled")) f.facilityTeam = winner; f.UpdateCityVisual();   // 设施换主=直写+刷新(全库惯例)
        }
        foreach (var node in root.FindChildren("*", "Weapon", true, false))
        {
            var w = node as Weapon; if (w == null || !w.IsInsideTree()) continue;
            if (w.team == winner || w.team == "P-1" || w.team == "Player0") continue;
            if (w.Get("takeoverEnabled").VariantType == Variant.Type.Bool && (bool)w.Get("takeoverEnabled")) w.Set("team", winner);
        }
        if (Manifest != null && CurrentSlot >= 0)
        {
            int idx = System.Array.IndexOf(Manifest.stageSlots, CurrentSlot);
            if (idx >= 0)
            {
                double s = idx < Manifest.stageScoreRewards.Length ? Manifest.stageScoreRewards[idx] : 0;
                double c = idx < Manifest.stageCrystalRewards.Length ? Manifest.stageCrystalRewards[idx] : 0;
                int pv = idx < Manifest.stageProgressValues.Length ? Manifest.stageProgressValues[idx] : 0;
                bool strict = idx < Manifest.stageStrictHideAfterClear.Count && Manifest.stageStrictHideAfterClear[idx];
                CampaignProfile.Instance.RecordStageClear(CurrentSlot, pv, strict, s, c);
                GD.Print($"[Campaign] 通关 slot={CurrentSlot} 进度→{CampaignProfile.Instance.CurrentProgress} 分+{s} 宝钞+{c}");
            }
        }
        Active = false;
    }
    private static double Num(GodotObject o, string prop)
    { var v = o.Get(prop); return v.VariantType == Variant.Type.Nil ? 0.0 : (double)v; }
}
