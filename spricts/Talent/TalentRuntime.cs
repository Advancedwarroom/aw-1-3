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

    // ✅ 单体应用:中途生产的单位当场吃build(开创人2026-11-14)
    public void ApplyEffectsToUnit(Infantry u)
    {
        if (u == null || !GodotObject.IsInstanceValid(u) || Pool == null || Owned.Count == 0) return;
        foreach (var kv in Owned)
        {
            var def = FindDef(kv.Key); if (def == null) continue;
            foreach (var d in def.exportDeltas) SetFieldDelta(u, d.Key, d.Value * kv.Value);
            foreach (var o in def.exportOverrides) SetFieldOverride(u, o.Key, o.Value);
        }
    }

    // ===== 三选一面板 + 效果应用 (开创人2026-11-14) =====
    private Control _draftRoot;
    private RichTextLabel _draftTitle;
    private Button _refreshBtn;
    private readonly System.Collections.Generic.List<Button> _draftBtns = new();

    // ✅ 三选一选秀面板:三张卡(稀有度配色/已拥等级/叠级预览)+刷新钮,选中回调onComplete
    public void ShowDraft(System.Action onComplete)
    {
        var three = DraftThree();
        if (three.Count == 0) { onComplete?.Invoke(); return; }
        EnsureDraftUI();
        foreach (var b in _draftBtns) b.QueueFree();
        _draftBtns.Clear();
        var vp = GetViewport(); if (vp == null) { onComplete?.Invoke(); return; }
        var size = vp.GetVisibleRect().Size;
        _draftTitle.Text = "[center][font_size=40]每赢一关 · 三选一拿天赋[/font_size][/center]";
        string[] colors = { "#DDDDDD", "#6FB7FF", "#FFD24A" };
        string[] rname = { "普通", "稀有", "金色" };
        int i = 0;
        foreach (var def in three)
        {
            int lv = GetLevel(def.talentId);
            string col = colors[Mathf.Clamp(def.rarity, 0, 2)];
            var btn = new Button
            {
                Text = $"[{rname[Mathf.Clamp(def.rarity, 0, 2)]}]{def.displayName}" + (lv > 0 ? $"  Lv{lv}→{lv + 1}" : "  NEW") + "\n{def.description}",
                Position = new Vector2(size.X / 2 - 240 + i * 250, size.Y * 0.4f),
                Size = new Vector2(230, 150),
                Modulate = new Color(col)
            };
            var captured = def;
            btn.Pressed += () =>
            {
                CommitPick(captured);
                _draftRoot.Visible = false;
                onComplete?.Invoke();
            };
            _draftRoot.AddChild(btn);
            _draftBtns.Add(btn);
            i++;
        }
        _refreshBtn.Visible = RefreshLeft > 0;
        _refreshBtn.Text = $"刷新 · 剩{RefreshLeft}";
        _refreshBtn.Pressed += () => { ConsumeRefresh(); ShowDraft(onComplete); };   // 重抽
        _draftRoot.Visible = true;
        _draftRoot.Modulate = new Color(1, 1, 1, 1);
    }

    private void EnsureDraftUI()
    {
        if (_draftRoot != null && IsInstanceValid(_draftRoot)) return;
        var vp = GetViewport(); if (vp == null) return;
        var size = vp.GetVisibleRect().Size;
        _draftRoot = new Control { Position = Vector2.Zero, Size = size, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _draftRoot.AddChild(new ColorRect { Color = new Color(0.03f, 0.05f, 0.1f, 0.95f), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore });
        _draftTitle = new RichTextLabel { Position = new Vector2(0, size.Y * 0.24f), Size = new Vector2(size.X, 70), BbcodeEnabled = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _refreshBtn = new Button { Position = new Vector2(size.X - 170, 24), Size = new Vector2(150, 36) };
        _draftRoot.AddChild(_draftTitle); _draftRoot.AddChild(_refreshBtn);
        vp.AddChild(_draftRoot);
    }

    // ✅ 效果应用:拥有栈×等级 → 反射注入玩家全军Export字段。换图后由SurvivalSession调本方法重挂。
    public void ApplyEffectsToUnits(string team)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var units = gm?.unitManager?.AllUnits;
        if (units == null || Pool == null || Owned.Count == 0) return;
        int applied = 0;
        foreach (var u in units)
        {
            if (u == null || !GodotObject.IsInstanceValid(u) || u.team != team) continue;
            foreach (var kv in Owned)
            {
                var def = FindDef(kv.Key); if (def == null) continue;
                int lv = kv.Value;
                foreach (var d in def.exportDeltas) SetFieldDelta(u, d.Key, d.Value * lv);
                foreach (var o in def.exportOverrides) SetFieldOverride(u, o.Key, o.Value);
            }
            applied++;
        }
        if (applied > 0) GD.Print($"[Talent] 效果已注入 {applied} 个己方单位");
    }

    private static readonly System.Collections.Generic.HashSet<string> _warned = new();

    private static void WarnMissing(object target, string name)
    {
        if (_warned.Contains(name)) return;
        _warned.Add(name);
        GD.PushWarning($"[Talent] 效果字段 '{name}' 在 {target.GetType().Name} 上不存在——检查TalentDef拼写");
    }

    private static void SetFieldDelta(object target, string name, double delta)
    {
        var f = target.GetType().GetField(name);
        if (f == null) { WarnMissing(target, name); return; }
        object cur = f.GetValue(target);
        object nv = cur switch
        {
            int i => (object)(i + (int)delta),
            float fl => fl + (float)delta,
            double db => db + delta,
            _ => null
        };
        if (nv != null) f.SetValue(target, nv);
    }

    private static void SetFieldOverride(object target, string name, string val)
    {
        var f = target.GetType().GetField(name);
        if (f == null) { WarnMissing(target, name); return; }
        object cur = f.GetValue(target);
        object nv = cur switch
        {
            bool => val == "true" || val == "1",
            int => int.TryParse(val, out var iv) ? iv : null,
            float => float.TryParse(val, out var fv) ? fv : null,
            double => double.TryParse(val, out var dv) ? dv : null,
            string => val,
            _ => null
        };
        if (nv != null) f.SetValue(target, nv);
    }

    public void ResetRun() { Owned.Clear(); if (FileAccess.FileExists(SavePath)) DirAccess.RemoveAbsolute(SavePath); }
}
