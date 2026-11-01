// CoModuleSystem.cs - ✅ CO 三十模块体系 · 运行层
// 势力(team) → CO 状态（档案/能量条/当前 PowerMode），30 模块统一查询入口
// 已接线模块：1 伤害 / 2 防御 / 3 射程 / 4 视野 / 5 移动 / 13 运气 / 18 占领 / 19 专属单位 / 27 能量条 / 30 Zone
using Godot;
using System.Collections.Generic;
using System.Linq;

public class CoTeamState
{
    public CoProfile profile;
    public List<CoProfile> partners = new();  // ✅ 配合模块：Tag Power 搭档 CO（合体技）
    public float charge = 0f;            // 能量条（星数 = charge；全队共享）
    public CoPowerMode powerMode = CoPowerMode.Off;
    public int powerTurnsLeft = 0;      // 大招剩余己方回合数
    public bool powerUsedThisTurn = false;  // 一回合只能放一次
    public bool tagExtraTurnUsed = false;    // Tag Power 的全军再动是否已用（一次合体只再动一次）
    public int coLevel = 0;                // ✅ 模块17（CO四级）：0-3，放大招升级
    public bool stealthAllActive = false;  // ✅ 模块14：本波大招是否开了全军隐身（到期解除）
}

public static class CoModuleSystem
{
    // 势力 → CO 状态（未注册的势力 = 无 CO，查询全部返回 0）
    static readonly Dictionary<string, CoTeamState> Teams = new();
    // 档案缓存（id → profile），供选 CO 界面/注册用
    static readonly Dictionary<string, CoProfile> Profiles = new();
    // ✅ Tag 搭配加成表（CoW Tagpower.js 搬运：co/tag_synergy.json，180 组）
    static readonly Dictionary<(string, string), (float attack, float luck)> TagSynergy = new();
    static bool loaded = false;

    // ---------- 初始化 ----------
    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        foreach (var p in CoProfile.LoadAll())
            Profiles[p.id] = p;
        // ✅ 势力绑定：co/bindings.json（{"Player1":"andy"} 或 {"Player1":["andy","eagle"]} 后者=主CO+Tag搭档）
        var bf = FileAccess.Open("res://co/bindings.json", FileAccess.ModeFlags.Read);
        if (bf != null)
        {
            var v = Json.ParseString(bf.GetAsText());
            if (v.VariantType == Variant.Type.Dictionary)
                foreach (var kv in v.AsGodotDictionary())
                {
                    string team = kv.Key.AsString();
                    // 支持单 CO 字符串 或 [主, 搭档...] 数组
                    if (kv.Value.VariantType == Variant.Type.Array)
                    {
                        var arr = kv.Value.AsGodotArray();
                        if (arr.Count == 0) continue;
                        string mainId = arr[0].AsString();
                        if (!Profiles.ContainsKey(mainId)) continue;
                        Teams[team] = new CoTeamState { profile = Profiles[mainId] };
                        for (int i = 1; i < arr.Count; i++)
                        {
                            string pid = arr[i].AsString();
                            if (Profiles.ContainsKey(pid) && !Teams[team].partners.Contains(Profiles[pid]))
                                Teams[team].partners.Add(Profiles[pid]);
                        }
                    }
                    else
                    {
                        string coId = kv.Value.AsString();
                        if (Profiles.ContainsKey(coId)) Teams[team] = new CoTeamState { profile = Profiles[coId] };
                    }
                }
            GD.Print("[CO] bindings.json 已绑定势力");
        }
        // ✅ Tag 搭配加成表（无上限：任意数量 CO 两两组合求和）
        var sf = FileAccess.Open("res://co/tag_synergy.json", FileAccess.ModeFlags.Read);
        if (sf != null)
        {
            var sv = Json.ParseString(sf.GetAsText());
            if (sv.VariantType == Variant.Type.Array)
                foreach (var e in sv.AsGodotArray())
                {
                    if (e.VariantType != Variant.Type.Dictionary) continue;
                    var d = e.AsGodotDictionary();
                    if (!d.ContainsKey("a") || !d.ContainsKey("b")) continue;
                    string a = d["a"].AsString(), b = d["b"].AsString();
                    float atk = d.ContainsKey("attack") ? d["attack"].AsSingle() : 0;
                    float lk = d.ContainsKey("luck") ? d["luck"].AsSingle() : 0;
                    TagSynergy[(a, b)] = (atk, lk);   // 已按 id 排序存储
                    TagSynergy[(b, a)] = (atk, lk);
                }
            GD.Print($"[CO] tag_synergy.json 加载：{TagSynergy.Count / 2} 组搭配");
        }
        GD.Print($"[CO] 档案加载完成：{Profiles.Count} 个，已绑定势力：{Teams.Count} 个");
    }

    public static List<CoProfile> AllProfiles()
    {
        EnsureLoaded();
        return Profiles.Values.OrderBy(p => p.id).ToList();
    }

    /// <summary>给势力绑定 CO（开局/选将时调用）</summary>
    public static void Register(string team, string coId)
    {
        EnsureLoaded();
        if (!Profiles.TryGetValue(coId, out var p))
        {
            GD.PrintErr($"[CO] 档案不存在：{coId}");
            return;
        }
        Teams[team] = new CoTeamState { profile = p };
        GD.Print($"[CO] {team} 绑定 CO：{p.name}（{p.id}）");
    }

    /// <summary>配合模块：给势力追加 Tag Power 搭档 CO</summary>
    public static void RegisterPartner(string team, string coId)
    {
        EnsureLoaded();
        var st = GetState(team);
        if (st == null)
        {
            GD.PrintErr($"[CO] {team} 未绑定主 CO，无法追加搭档");
            return;
        }
        if (!Profiles.TryGetValue(coId, out var p) || p == st.profile || st.partners.Contains(p)) return;
        st.partners.Add(p);
        GD.Print($"[CO] {team} 追加搭档 CO：{p.name}（{p.id}）");
    }

    public static void ClearPartners(string team) => GetState(team)?.partners.Clear();
    public static IReadOnlyList<CoProfile> GetPartners(string team) => GetState(team)?.partners ?? (IReadOnlyList<CoProfile>)System.Array.Empty<CoProfile>();

    public static void Unregister(string team) => Teams.Remove(team);
    public static bool HasCo(string team) => team != null && Teams.TryGetValue(team, out var s) && s.profile != null;
    public static CoTeamState GetState(string team) => string.IsNullOrEmpty(team) ? null
        : Teams.TryGetValue(team, out var s) ? s : null;
    public static CoProfile GetProfile(string team) => GetState(team)?.profile;

    // ---------- 模块取值核心 ----------
    /// <summary>取该势力当前 PowerMode 下某模块的全部数值（含 Zone 判定与 Tag 多 CO 合并）</summary>
    static IEnumerable<CoModuleEntry> ActiveModules(string team, int moduleId, Infantry unit = null)
    {
        var st = GetState(team);
        if (st?.profile == null) yield break;
        // ✅ 配合模块：Tag Power 时主 CO + 全部搭档 CO 的 SCOP 一并生效
        var profiles = st.powerMode == CoPowerMode.Tag && st.partners.Count > 0
            ? new[] { st.profile }.Concat(st.partners)
            : new[] { st.profile };
        foreach (var profile in profiles)
        {
            foreach (var m in profile.modules)
            {
                if (m.module != moduleId) continue;
                // ✅ 模块29 特殊限定
                if (unit != null)
                {
                    if (m.unitTypes.Count > 0 && !m.unitTypes.Contains(unit.UnitTypeId)) continue;
                    if (m.terrainTypes.Count > 0 && (unit.grid == null || !m.terrainTypes.Contains(unit.grid.gridType.ToString()))) continue;
                    bool indirect = !unit.canAttackAfterMoving;   // 移动后不能攻击=间接攻击型（Artillery/Rocket 等）
                    if (m.directOnly && indirect) continue;
                    if (m.indirectOnly && !indirect) continue;
                }
                // ✅ 模块17（CO四级）+ 模块29：等级区间门控（不在区间=本条不生效）
                if (m.minCoLevel > st.coLevel || m.maxCoLevel < st.coLevel) continue;
                // ✅ 模块30 Zone 判定：zoneRange>=0 时，非 Zone 内单位只吃 d2d 档
                if (st.profile.zoneRange >= 0 && unit != null && unit.grid != null)
                {
                    bool inZone = IsInCoZone(team, unit);
                    if (m.module == 30 && !inZone) continue;               // 30 号模块区域外无效
                    if (inZone && m.zone.Count > 0 && st.powerMode == CoPowerMode.Off)
                    {
                        // Zone CO 常态：区域内用 zone 档（调用方处理，这里仍返回模块）
                    }
                }
                yield return m;
            }
        }
    }

    /// <summary>模块30：单位是否在该势力 CO Zone 内（zoneRange=-1 视为全域）</summary>
    public static bool IsInCoZone(string team, Infantry unit)
    {
        var st = GetState(team);
        if (st?.profile == null) return false;
        if (st.profile.zoneRange < 0) return true;  // 全局 CO（AW1-3 系）
        if (unit?.grid == null) return false;
        // Zone 载体：该势力所有 isCoUnit 单位（含搭载 CO 的载具）
        foreach (var n in unit.GetTree().GetNodesInGroup("infantry"))
        {
            if (n is not Infantry i || i.team != team || !i.isCoUnit || i.grid == null) continue;
            int dist = Mathf.Abs(i.grid.GridIndex.X - unit.grid.GridIndex.X)
                     + Mathf.Abs(i.grid.GridIndex.Y - unit.grid.GridIndex.Y);
            if (dist <= st.profile.zoneRange) return true;
        }
        return false;
    }

    static float Sum(string team, int moduleId, string key, Infantry unit = null, bool zoneOnly = false)
    {
        float sum = 0f;
        foreach (var m in ActiveModules(team, moduleId, unit))
        {
            var st = GetState(team);
            if (zoneOnly) sum += m.zone.TryGetValue(key, out var zv) ? zv : 0;
            else
            {
                var vals = m.GetPhaseValues(st.powerMode);
                sum += vals.TryGetValue(key, out var v) ? v : 0;
                // Zone CO 常态：区域内额外吃 zone 档
                if (!zoneOnly && st.profile.zoneRange >= 0 && unit != null && IsInCoZone(team, unit)
                    && st.powerMode == CoPowerMode.Off)
                    sum += m.zone.TryGetValue(key, out var zv2) ? zv2 : 0;
            }
        }
        return sum;
    }

    // ---------- 对外查询（模块 1/2/3/4/5/13/18） ----------
    public static float GetOffenseBonusPercent(Infantry attacker)
    {
        if (attacker == null) return 0;
        float bonus = Sum(attacker.team, 1, "offBonus", attacker);
        // ✅ 模块16（升级）：单位等级攻/防加成（每级+4%）
        bonus += (attacker.Level - 1) * 4f;
        // ✅ 模块25（状态系统）：虚弱降攻
        if (attacker.weakTurnsRemaining > 0) bonus -= attacker.weakPercent;
        // ✅ Tag 搭配加成（合体技期间）
        if (GetState(attacker.team)?.powerMode == CoPowerMode.Tag)
            bonus += GetTagSynergyBonus(attacker.team).attack;
        return bonus;
    }

    public static float GetDefenseBonusPercent(Infantry defender)
        => defender == null ? 0 : Sum(defender.team, 2, "defBonus", defender) + (defender.Level - 1) * 4f;

    public static float GetTerrainDefenseBonus(Infantry defender)
        => defender == null ? 0 : Sum(defender.team, 2, "terrainStars", defender);

    public static int GetFireRangeBonus(Infantry unit)
        => unit == null ? 0 : Mathf.RoundToInt(Sum(unit.team, 3, "rangeBonus", unit));

    public static int GetMinFireRangeBonus(Infantry unit)
        => unit == null ? 0 : Mathf.RoundToInt(Sum(unit.team, 3, "minRangeBonus", unit));

    public static int GetVisionBonus(Infantry unit)
        => unit == null ? 0 : Mathf.RoundToInt(Sum(unit.team, 4, "visionBonus", unit));

    public static int GetMovementBonus(Infantry unit)
        => unit == null ? 0 : Mathf.RoundToInt(Sum(unit.team, 5, "moveBonus", unit));

    public static int GetMovementCostReduction(Infantry unit, Grids to)
        => unit == null ? 0 : Mathf.RoundToInt(Sum(unit.team, 5, "moveCostReduction", unit));

    public static int GetCaptureBonus(Infantry unit)
        => unit == null ? 0 : Mathf.RoundToInt(Sum(unit.team, 18, "captureBonus", unit));

    /// <summary>模块13 运气：本攻击的运气浮动（±luckMax，Tag 合体技期间叠加搭配运气加成）</summary>
    public static int GetLuckRoll(Infantry attacker, System.Random rng = null)
    {
        rng ??= new System.Random();
        float max = Sum(attacker?.team, 13, "luckMax", attacker);
        float min = Sum(attacker?.team, 13, "luckMin", attacker);
        if (GetState(attacker?.team)?.powerMode == CoPowerMode.Tag)
        {
            var syn = GetTagSynergyBonus(attacker.team);
            max += syn.luck;   // 搭配运气加成只抬上限（CoW 同款）
        }
        return rng.Next(Mathf.RoundToInt(Mathf.Min(min, max)), Mathf.RoundToInt(Mathf.Max(min, max)) + 1);
    }

    /// <summary>模块12 生产价格系数（返回百分比增减，如 -20 表示 -20%）</summary>
    public static float GetCostModifierPercent(string team)
        => Sum(team, 12, "costPercent");

    /// <summary>模块8 回血（COP/SCOP 常见：全军回复 N HP）</summary>
    public static int GetHealHp(string team)
        => Mathf.RoundToInt(Sum(team, 8, "healHp"));

    /// <summary>模块11 收入系数（百分比增减）</summary>
    public static float GetIncomeModifierPercent(string team)
        => Sum(team, 11, "incomePercent");

    /// <summary>模块19（CO 专属单位）：该势力可生产的专属单位列表（主 CO + 搭档，去重）</summary>
    public static IReadOnlyList<string> GetExclusiveUnits(string team)
    {
        var st = GetState(team);
        if (st?.profile == null) return System.Array.Empty<string>();
        var list = new List<string>(st.profile.coUnits);
        foreach (var p in st.partners)
            foreach (var u in p.coUnits)
                if (!list.Contains(u)) list.Add(u);
        return list;
    }

    /// <summary>模块19：某单位是否为该势力 CO 的专属单位（生产时用于 isCoUnit 标记）</summary>
    public static bool IsExclusiveUnit(string team, string unitTypeId)
        => GetExclusiveUnits(team).Contains(unitTypeId);

    /// <summary>模块14（隐藏/反隐藏）：该势力是否拥有反隐（antiStealth，看破并可直接攻击隐身单位）</summary>
    public static bool GetAntiStealth(string team)
        => Sum(team, 14, "antiStealth") > 0f;

    /// <summary>模块10（逆转/联动）：该单位是否拥有先制反击（firstStrike）</summary>
    public static bool GetFirstStrike(Infantry unit)
        => unit != null && Sum(unit.team, 10, "firstStrike", unit) > 0f;

    /// <summary>模块16（升级）：击杀获取的经验值（默认100，可被模块 xpPerKill 改写）</summary>
    public static int GetKillXp(Infantry killer)
    {
        if (killer == null) return 0;
        float v = Sum(killer.team, 16, "xpPerKill", killer);
        return v > 0 ? Mathf.RoundToInt(v) : 100;
    }

    /// <summary>模块15（天气/战争迷雾）：该单位是否免疫天气惩罚（weatherImmune）</summary>
    public static bool GetWeatherImmune(Infantry unit)
        => unit != null && Sum(unit.team, 15, "weatherImmune", unit) > 0f;

    // ---------- 发动时效果（COP/SCOP/Tag 统一入口） ----------
    /// <summary>
    /// 大招发动时的即时效果：模块7 生成单位、8 全军回血、9 区域导弹、15 改天气、24 建筑状态、25 状态效果。
    /// </summary>
    static void ExecuteActivationEffects(string team)
    {
        var st = GetState(team);
        if (st?.profile == null) return;
        var tree = Engine.GetMainLoop() as SceneTree;
        var gm = tree?.GetFirstNodeInGroup("game_manager") as GameManager;

        // ✅ 模块8：全军回血
        int healHp = GetHealHp(team);
        if (healHp != 0 && tree != null)
            foreach (var n in tree.GetNodesInGroup("infantry"))
            {
                if (n is not Infantry i || i.team != team) continue;
                i.health = Mathf.Clamp(i.health + healHp, 1, i.GetEffectiveMaxHealth());
                i.UpdateHpLabel();
            }

        // ✅ 模块15：改变天气（0=晴 1=雨 2=雪 3=沙暴 4=雾）
        int weather = Mathf.RoundToInt(Sum(team, 15, "setWeather"));
        if (weather is >= 1 and <= 4)
        {
            int wDays = Mathf.RoundToInt(Sum(team, 15, "weatherTurns"));
            WeatherSystem.SetWeather((WeatherType)weather, Mathf.Max(1, wDays));
        }

        // ✅ 模块7（生成单位）：在自己的城市上刷兵（Sensei 式：spawnUnitId + spawnHp）
        foreach (var m7 in ActiveModules(team, 7))
        {
            var vals = m7.GetPhaseValues(st.powerMode);
            if (!vals.TryGetValue("spawnUnitId", out float uid)) continue;
            string unitId = SpawnUnitIds.TryGetValue(Mathf.RoundToInt(uid), out var s) ? s : "Infantry";
            int spawnHp = vals.TryGetValue("spawnHp", out float h) ? Mathf.RoundToInt(h) : 9;   // HP 1-10
            int maxCount = vals.TryGetValue("spawnCount", out float c) ? Mathf.RoundToInt(c) : 999;
            if (gm?.gridManager?.grids == null) continue;
            int spawned = 0;
            foreach (var g in gm.gridManager.grids)
            {
                if (g?.city == null || g.infantry != null || spawned >= maxCount) continue;
                if (g.city.facilityTeam != team) continue;   // 自己的城市
                if (SpawnCoUnit(g, unitId, team, spawnHp) != null) spawned++;
            }
            GD.Print($"[CO] {team} 模块7 生成单位：{unitId}×{spawned}（{spawnHp}HP）");
        }

        // ✅ 模块9（区域导弹）：对敌军最密集处发射导弹（missileCount/missileDamage/blastRadius）
        int missileCount = Mathf.RoundToInt(Sum(team, 9, "missileCount"));
        if (missileCount > 0 && gm?.gridManager?.grids != null)
        {
            int dmg = Mathf.RoundToInt(Sum(team, 9, "missileDamage"));               // HP 1-10
            int radius = Mathf.RoundToInt(Sum(team, 9, "blastRadius"));
            if (radius <= 0) radius = 2;
            var grids = gm.gridManager.grids;
            var hitCenters = new List<Grids>();
            for (int mi = 0; mi < missileCount; mi++)
            {
                // 目标：blast 半径内敌军数最多的格子（避开已打击中心）
                Grids best = null; int bestScore = 0;
                foreach (var g in grids)
                {
                    if (g == null || hitCenters.Contains(g)) continue;
                    int score = 0;
                    foreach (var g2 in grids)
                    {
                        if (g2?.infantry == null || !IsEnemy(team, g2.infantry.team)) continue;
                        int d = Mathf.Abs(g2.GridIndex.X - g.GridIndex.X) + Mathf.Abs(g2.GridIndex.Y - g.GridIndex.Y);
                        if (d <= radius) score++;
                    }
                    if (score > bestScore) { bestScore = score; best = g; }
                }
                if (best == null || bestScore == 0) break;
                hitCenters.Add(best);
                foreach (var g2 in grids)
                {
                    if (g2?.infantry == null || !IsEnemy(team, g2.infantry.team)) continue;
                    int d = Mathf.Abs(g2.GridIndex.X - best.GridIndex.X) + Mathf.Abs(g2.GridIndex.Y - best.GridIndex.Y);
                    if (d > radius) continue;
                    var u = g2.infantry;
                    u.health = Mathf.Max(1, u.health - dmg * 10);   // 千分制
                    u.UpdateHpLabel();
                }
            }
            GD.Print($"[CO] {team} 模块9 区域导弹：{hitCenters.Count} 发（伤害{dmg}HP 半径{radius}）");
        }

        // ✅ 模块24（建筑状态）：给敌方设施上状态（facStatus 1-4 + facStatusTurns）
        int facStatus = Mathf.RoundToInt(Sum(team, 24, "facStatus"));
        if (facStatus is >= 1 and <= 4 && gm?.gridManager?.grids != null)
        {
            int facTurns = Mathf.RoundToInt(Sum(team, 24, "facStatusTurns"));
            facTurns = facTurns > 0 ? facTurns : Mathf.Max(1, st.powerTurnsLeft);
            foreach (var g in gm.gridManager.grids)
            {
                if (g?.city == null || !IsEnemy(team, g.city.facilityTeam)) continue;
                g.city.AddStatus((FacilityStatusType)facStatus, facTurns, team);
            }
            GD.Print($"[CO] {team} 模块24 建筑状态：{(FacilityStatusType)facStatus} ×{facTurns}回合");
        }

        // ✅ 模块25（状态系统）：燃烧/麻痹/虚弱施加给全部敌军（持续=大招时长）
        float burn = Sum(team, 25, "burnHp");
        float stun = Sum(team, 25, "stunTurns");
        float weak = Sum(team, 25, "weakPercent");
        if ((burn != 0 || stun != 0 || weak != 0) && tree != null)
        {
            int dur = Mathf.Max(1, st.powerTurnsLeft);
            foreach (var n in tree.GetNodesInGroup("infantry"))
            {
                if (n is not Infantry u || !IsEnemy(team, u.team)) continue;
                if (burn > 0) { u.burnDamage = Mathf.RoundToInt(burn); u.burnTurnsRemaining = dur; }
                if (stun > 0) u.ApplyStun(Mathf.RoundToInt(stun));
                if (weak > 0) { u.weakPercent = Mathf.RoundToInt(weak); u.weakTurnsRemaining = dur; }
            }
            GD.Print($"[CO] {team} 模块25 状态：burn={burn} stun={stun} weak={weak} ×{dur}回合");
        }

        // ✅ 模块14（隐藏）：发动时全军隐身（stealthAll，大招到期自动解除）
        if (Sum(team, 14, "stealthAll") > 0f && tree != null)
        {
            st.stealthAllActive = true;
            foreach (var n in tree.GetNodesInGroup("infantry"))
            {
                if (n is not Infantry u || u.team != team) continue;
                u.isStealthed = true;
            }
            GD.Print($"[CO] {team} 模块14：全军隐身开启");
        }
    }

    /// <summary>模块7 单位编号 → aw-1-3 兵种 ID</summary>
    static readonly Dictionary<int, string> SpawnUnitIds = new()
    {
        { 1, "Infantry" }, { 2, "Mech" }, { 3, "Bike" }, { 4, "Recon" }, { 5, "LightTank" },
    };

    /// <summary>敌对判定（模块9/24/25 目标过滤）</summary>
    static bool IsEnemy(string fromTeam, string otherTeam)
        => otherTeam != fromTeam && TeamHelper.IsEnemyForAttacker(fromTeam, otherTeam);

    /// <summary>模块7：在指定格生成单位（复用生产管线：场景实例化+绑格+注册）</summary>
    static Infantry SpawnCoUnit(Grids g, string unitId, string team, int hp)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var gm = tree?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.unitManager == null || g == null || !UnitProductionDatabase.HasUnit(unitId)) return null;
        var info = UnitProductionDatabase.GetInfo(unitId);
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null) return null;
        var unit = scene.Instantiate<Infantry>();
        if (unit == null) return null;
        unit.Position = g.Position;
        unit.team = team;
        unit.Name = $"{team}_{unitId}_{gm.unitManager.AllUnits.Count + 1}";
        gm.unitManager.units.AddChild(unit);
        if (!gm.unitManager.BindUnitToGrid(unit, true)) { unit.QueueFree(); return null; }
        gm.unitManager.AllUnits.Add(unit);
        gm.RegisterUnit(unit, gm.InferCategory(unit));
        unit.OnClickPiece = gm.OnSelectPiece;
        unit.UpdateTeamVisual();
        unit.UpdateHpLabel();
        if (hp > 0 && hp < 10)
        {
            unit.health = Mathf.Clamp(hp * 10, 1, unit.maxHealth);   // HP→千分制
            unit.UpdateHpLabel();
        }
        return unit;
    }

    // ---------- 能量条（模块 27 附属） ----------
    public static float GetCharge(string team) => GetState(team)?.charge ?? 0f;
    public static float GetChargeNeeded(string team, bool scop)
    {
        var p = GetProfile(team);
        return (p == null) ? float.MaxValue : (scop ? p.superStars : p.powerStars);
    }
    public static bool CanUsePower(string team) => GetState(team) is { } s && s.profile != null
        && s.powerMode == CoPowerMode.Off && s.charge >= s.profile.powerStars && !s.powerUsedThisTurn;
    public static bool CanUseSuperPower(string team) => GetState(team) is { } s && s.profile != null
        && s.powerMode == CoPowerMode.Off && s.charge >= s.profile.superStars && !s.powerUsedThisTurn;

    /// <summary>能量条事件打点（伤害/损失/生产/回合/资金）——由各结算点调用</summary>
    public static void GainCharge(string team, float amount)
    {
        var st = GetState(team);
        if (st?.profile == null || amount <= 0) return;
        // ✅ 上限：无搭档=主CO SCOP★；有搭档=Tag 所需星数（否则攒不到合体技）
        float cap = st.partners.Count > 0 ? GetTagPowerNeeded(team) : st.profile.superStars;
        st.charge = Mathf.Min(st.charge + amount, cap);
    }

    public static void OnDamageDealt(string team, float damage)
    {
        var st = GetState(team);
        if (st?.profile == null) return;
        GainCharge(team, damage * st.profile.charge.damageDealtWeight / st.profile.charge.chargeDivisor);
    }
    public static void OnDamageTaken(string team, float damage)
    {
        var st = GetState(team);
        if (st?.profile == null) return;
        GainCharge(team, damage * st.profile.charge.damageTakenWeight / st.profile.charge.chargeDivisor);
    }
    public static void OnUnitLost(string team)
    {
        var st = GetState(team);
        if (st?.profile == null) return;
        GainCharge(team, st.profile.charge.unitLostWeight);
    }
    public static void OnUnitProduced(string team)
    {
        var st = GetState(team);
        if (st?.profile == null) return;
        GainCharge(team, st.profile.charge.unitProducedWeight);
    }
    public static void OnFundsSpent(string team, int funds)
    {
        var st = GetState(team);
        if (st?.profile == null) return;
        GainCharge(team, funds * st.profile.charge.fundsSpentWeight / st.profile.charge.chargeDivisor);
    }

    // ---------- 大招发动/过期 ----------
    /// <summary>发动 COP（返回是否成功）。能量不清零（AW 标准为清零，SCOP 扣除所需）</summary>
    public static bool ActivatePower(string team)
    {
        var st = GetState(team);
        if (!CanUsePower(team)) return false;
        st.powerMode = CoPowerMode.Cop;
        st.powerTurnsLeft = PowerDuration(st, CoPowerMode.Cop);
        st.powerUsedThisTurn = true;
        st.charge = 0f;
        ExecuteActivationEffects(team);   // ✅ 发动时效果（模块8回血/模块15天气……）
        GD.Print($"[CO] {team} 发动 COP：{st.profile.copName}");
        return true;
    }

    public static bool ActivateSuperPower(string team)
    {
        var st = GetState(team);
        if (!CanUseSuperPower(team)) return false;
        st.powerMode = CoPowerMode.Scop;
        st.coLevel = Mathf.Min(3, st.coLevel + 1);   // ✅ 模块17（CO四级）：放大招升级
        st.powerTurnsLeft = PowerDuration(st, CoPowerMode.Scop);
        st.powerUsedThisTurn = true;
        st.charge = 0f;
        ExecuteActivationEffects(team);   // ✅ 发动时效果（模块8回血/模块15天气……）
        GD.Print($"[CO] {team} 发动 SCOP：{st.profile.scopName}");
        return true;
    }

    // ---------- 配合模块：Tag Power（多 CO 合体技，无上限） ----------
    /// <summary>Tag 所需星数 = 主 CO SCOP★ + 全部搭档 COP★（合体消耗，搭档数量不限）</summary>
    public static float GetTagPowerNeeded(string team)
    {
        var st = GetState(team);
        if (st?.profile == null || st.partners.Count == 0) return float.MaxValue;
        return st.profile.superStars + st.partners.Sum(p => p.powerStars);
    }

    /// <summary>
    /// ✅ Tag 搭配加成（CoW Tagpower.js 搬运）：主 CO + 全部搭档两两组合，攻击%与运气加成求和。
    /// 搭档数量无上限——3 个 CO 就吃 3 对组合加成，4 个吃 6 对，以此类推。
    /// </summary>
    public static (float attack, float luck) GetTagSynergyBonus(string team)
    {
        var st = GetState(team);
        if (st?.profile == null || st.partners.Count == 0) return (0, 0);
        float atk = 0, luck = 0;
        var all = new[] { st.profile }.Concat(st.partners).Select(p => p.id).ToList();
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
                if (TagSynergy.TryGetValue((all[i], all[j]), out var s))
                {
                    atk += s.attack;
                    luck += s.luck;
                }
        return (atk, luck);
    }

    public static bool CanUseTagPower(string team) => GetState(team) is { } s && s.profile != null
        && s.partners.Count > 0 && s.powerMode == CoPowerMode.Off
        && s.charge >= GetTagPowerNeeded(team) && !s.powerUsedThisTurn;

    /// <summary>发动 Tag Power：主 CO + 搭档 CO 的 SCOP 一并生效 + 全军可再行动一次（模块6）</summary>
    public static bool ActivateTagPower(string team)
    {
        var st = GetState(team);
        if (!CanUseTagPower(team)) return false;
        st.powerMode = CoPowerMode.Tag;
        st.coLevel = Mathf.Min(3, st.coLevel + 1);   // ✅ 模块17（CO四级）：合体技也升级
        st.powerTurnsLeft = PowerDuration(st, CoPowerMode.Tag);
        st.powerUsedThisTurn = true;
        st.charge = 0f;
        st.tagExtraTurnUsed = false;
        GrantTeamExtraTurn(team);   // ✅ 模块6（多次移动）：合体技全军再动
        st.tagExtraTurnUsed = true;
        ExecuteActivationEffects(team);   // ✅ 发动时效果（模块8回血/模块15天气……）
        GD.Print($"[CO] {team} 发动 Tag Power：{st.profile.scopName} × {st.partners.Count + 1} CO 合体");
        return true;
    }

    // ---------- 模块26（AI模块）：AI 自动放大 ----------
    /// <summary>AI 势力回合开始时调用：能量够就放大招（优先 Tag/SCOP，再 COP）</summary>
    public static void AIAutoPower(string team)
    {
        if (!HasCo(team)) return;
        try
        {
            if (CanUseTagPower(team)) ActivateTagPower(team);
            else if (CanUseSuperPower(team)) ActivateSuperPower(team);
            else if (CanUsePower(team)) ActivatePower(team);
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[CO] AI 自动放大失败：{e.Message}");
        }
    }

    // ---------- 模块21/22/23：脚本 API（操作权/势力/外交） ----------
    /// <summary>模块21（操作权改变）：单位移交（team 变更+待机+迷雾刷新）</summary>
    public static void TransferUnit(Infantry unit, string toTeam)
    {
        if (unit == null || !IsInstanceValid(unit) || string.IsNullOrEmpty(toTeam)) return;
        unit.team = toTeam;
        unit.hasActed = true;            // 移交后当场待机
        unit.state = UnitState.Acted;
        unit.UpdateTeamVisual();
        ((Engine.GetMainLoop() as SceneTree)?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager)?.OnUnitMoved();
    }

    /// <summary>模块22（势力改变）：设施/城市移交（全局）</summary>
    public static void TransferCity(Facility f, string toTeam)
    {
        if (f == null || !IsInstanceValid(f) || string.IsNullOrEmpty(toTeam)) return;
        f.facilityTeam = toTeam;
        f.UpdateCityVisual();
        ((Engine.GetMainLoop() as SceneTree)?.GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager)?.OnUnitMoved();
    }

    /// <summary>模块22（势力改变）：某势力全部单位+设施整体移交</summary>
    public static void TransferFaction(string fromTeam, string toTeam)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        foreach (var n in tree.GetNodesInGroup("infantry"))
        {
            if (n is Infantry u && u.team == fromTeam) TransferUnit(u, toTeam);
        }
        var gm = tree.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.grids != null)
            foreach (var g in gm.gridManager.grids)
            {
                if (g?.city != null && g.city.facilityTeam == fromTeam) TransferCity(g.city, toTeam);
            }
    }

    /// <summary>模块11/23（资金转移）：赠款</summary>
    public static void GiftFunds(string fromTeam, string toTeam, int amount)
    {
        if (amount <= 0) return;
        var gm = (Engine.GetMainLoop() as SceneTree)?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;
        int avail = gm.GetTeamFunds(fromTeam);
        int give = Mathf.Min(avail, amount);
        if (give <= 0) return;
        gm.SetTeamFunds(fromTeam, avail - give);
        gm.SetTeamFunds(toTeam, gm.GetTeamFunds(toTeam) + give);
    }

    /// <summary>模块23（外交）：结盟/解盟（运行时全局生效）</summary>
    public static void SetAlliance(string a, string b, bool allied) => TeamHelper.SetAlliance(a, b, allied);

    /// <summary>模块6（多次移动加成）：全势力单位恢复可行动（Tag Power / 特殊模块用）</summary>
    public static void GrantTeamExtraTurn(string team)
    {
        var tree = (Engine.GetMainLoop() as SceneTree);
        if (tree == null) return;
        foreach (var n in tree.GetNodesInGroup("infantry"))
        {
            if (n is not Infantry i || i.team != team) continue;
            i.hasActed = false;
            i.isMoved = false;
            i.isAttacked = false;
            i.state = UnitState.Idle;
            i.hasTeleportedThisTurn = false;   // 合体再动回合传送也重置（限一次按回合算）
            i.SetWaitVisual(false);
            i.RestoreAnimAndBreath();
        }
    }

    static int PowerDuration(CoTeamState st, CoPowerMode mode)
    {
        // 默认：持续到下一次己方回合结束（AW 标准 = 1 个己方回合）
        foreach (var m in st.profile.modules)
            if (m.module is 1 or 2 or 3 or 4 or 5 or 13 or 18)
            {
                // ✅ 模块20（时限）：permanent=持续整局 / instant=瞬时（发动时效果已结算）
                if (m.duration == "permanent") return 999;
                if (m.duration == "instant") return 1;
                return Mathf.Max(1, m.durationTurns);
            }
        return 1;
    }

    /// <summary>该势力回合结束时调用（GameManager.NextPhase）：过期判定 + 每回合重置</summary>
    public static void OnTeamTurnEnd(string team)
    {
        var st = GetState(team);
        if (st?.profile == null) return;
        st.powerUsedThisTurn = false;
        st.tagExtraTurnUsed = false;
        if (st.powerTurnsLeft > 0 && --st.powerTurnsLeft <= 0 && st.powerMode != CoPowerMode.Off)
        {
            GD.Print($"[CO] {team} 的 {st.profile.name} 大招效果结束");
            st.powerMode = CoPowerMode.Off;
            // ✅ 模块14：大招到期解除全军隐身
            if (st.stealthAllActive)
            {
                st.stealthAllActive = false;
                var tree = Engine.GetMainLoop() as SceneTree;
                if (tree != null)
                    foreach (var n in tree.GetNodesInGroup("infantry"))
                    {
                        if (n is not Infantry u || u.team != team || !u.isStealthed) continue;
                        u.RevealStealth();
                    }
            }
        }
    }

    /// <summary>清档（重开局）</summary>
    public static void ResetAll()
    {
        Teams.Clear();
        loaded = false;
    }
}
