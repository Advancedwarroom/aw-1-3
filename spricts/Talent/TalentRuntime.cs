// TalentRuntime.cs - 天赋运行时(autoload "talent_runtime") (开创人2026-11-14)
// 拥有栈/三选一抽取(排除满级,金槽未开不入金)/刷新计数/选中升档/存档。效果应用下轮焊(赋能/CO模块注入)。
using Godot;
using System.Collections.Generic;

public partial class TalentRuntime : Node
{
    public static TalentRuntime Instance { get; private set; }
    public TalentPool Pool { get; private set; }
    public Godot.Collections.Dictionary<string, int> Owned { get; } = new();  // talentId → 等级
    public int RefreshLeft { get; private set; }
    public int GoldenSlotOpen { get; private set; } = 1;   // 金槽数(可随关卡/成就扩)

    [Signal] public delegate void TalentChangedEventHandler();
    [Signal] public delegate void DraftRequestedEventHandler();   // UI挂钩点:弹出三选一

    public override void _Ready() { Instance = this; AddToGroup("talent_runtime"); ProcessMode = ProcessModeEnum.Always; }

    public void BeginRun(TalentPool pool)
    {
        Pool = pool;
        Owned.Clear();
        GoldenSlotOpen = pool?.goldenSlots ?? 1;
        RefreshLeft = pool?.refreshPerDraft ?? 0;
    }

    public void OnStageVictory() => RefreshLeft = Pool?.refreshPerDraft ?? 0;   // 每关补刷新

    // ✅ 三选一:按稀有度权重,排除已满级;金槽未开或金色槽位占满则金不入池
    public Godot.Collections.Array<TalentDef> DraftThree()
    {
        var result = new Godot.Collections.Array<TalentDef>();
        if (Pool == null) return result;
        var rng = new RandomNumberGenerator { Seed = (ulong)Time.GetTicksUsec() };
        var bag = BuildBag(rng);
        while (result.Count < Pool.draftCount && bag.Count > 0)
        {
            int idx = rng.RandiRange(0, bag.Count - 1);
            var pick = bag[idx]; bag.RemoveAt(idx);
            if (result.Contains(pick)) continue;
            result.Add(pick);
        }
        return result;
    }

    private List<TalentDef> BuildBag(RandomNumberGenerator rng)
    {
        var bag = new List<TalentDef>();
        void AddPool(IEnumerable<TalentDef> pool, double weight)
        {
            foreach (var d in pool)
            {
                if (d == null || string.IsNullOrEmpty(d.talentId)) continue;
                if (GetLevel(d.talentId) >= d.maxStacks) continue;                       // 已满级
                if (d.rarity >= 2 && (GoldenSlotOpen <= CountGoldenOwned())) continue;     // 金槽满不入金
                int w = Mathf.Max(1, (int)weight);
                for (int i = 0; i < w; i++) bag.Add(d);   // 权重=重复占位
            }
        }
        AddPool(Pool.commonPool, 100);
        AddPool(Pool.rarePool, Pool.rareWeight);
        AddPool(Pool.goldenPool, Pool.goldenWeight);
        return bag;
    }

    private int CountGoldenOwned()
    {
        int n = 0;
        foreach (var kv in Owned) { var d = FindDef(kv.Key); if (d != null && d.rarity >= 2) n++; }
        return n;
    }

    public TalentDef FindDef(string id)
    {
        if (Pool == null) return null;
        foreach (var arr in new[] { Pool.commonPool, Pool.rarePool, Pool.goldenPool })
            foreach (var d in arr) if (d != null && d.talentId == id) return d;
        return null;
    }

    // ✅ 选中:新拿1级,已有+1(不超上限)
    public void CommitPick(TalentDef def)
    {
        if (def == null) return;
        if (Owned.ContainsKey(def.talentId)) Owned[def.talentId] = Mathf.Min(def.maxStacks, Owned[def.talentId] + 1);
        else Owned[def.talentId] = 1;
        EmitSignal(SignalName.TalentChanged);
        GD.Print($"[Talent] 获得 {def.displayName} Lv{GetLevel(def.talentId)}");
    }

    public void ConsumeRefresh() { if (RefreshLeft > 0) RefreshLeft--; }
    public int GetLevel(string id) => Owned.TryGetValue(id, out var v) ? v : 0;

    // ===== 存档(随生存局) =====
    private const string SavePath = "user://talent_run.json";
    public void SaveRun()
    {
        var dict = new Godot.Collections.Dictionary { ["poolName"] = Pool?.poolName ?? "", ["goldenSlotOpen"] = GoldenSlotOpen };
        var owned = new Godot.Collections.Dictionary();
        foreach (var kv in Owned) owned[kv.Key] = kv.Value;
        dict["owned"] = owned;
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        f?.StoreString(Json.Stringify(dict));
    }

    public void LoadRun()
    {
        if (!FileAccess.FileExists(SavePath)) return;
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        var parsed = Json.ParseString(f?.GetAsText() ?? "").AsGodotDictionary();
        if (parsed.Count == 0) return;
        Owned.Clear();
        foreach (var kv in parsed["owned"].AsGodotDictionary()) Owned[kv.Key.ToString()] = (int)kv.Value.AsInt64();
        GoldenSlotOpen = parsed.ContainsKey("goldenSlotOpen") ? (int)parsed["goldenSlotOpen"].AsInt64() : 1;
        EmitSignal(SignalName.TalentChanged);
    }

    public void ResetRun() { Owned.Clear(); if (FileAccess.FileExists(SavePath)) DirAccess.RemoveAbsolute(SavePath); }
}
