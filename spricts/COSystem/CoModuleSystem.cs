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
}

public static class CoModuleSystem
{
    // 势力 → CO 状态（未注册的势力 = 无 CO，查询全部返回 0）
    static readonly Dictionary<string, CoTeamState> Teams = new();
    // 档案缓存（id → profile），供选 CO 界面/注册用
    static readonly Dictionary<string, CoProfile> Profiles = new();
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
        => attacker == null ? 0 : Sum(attacker.team, 1, "offBonus", attacker);

    public static float GetDefenseBonusPercent(Infantry defender)
        => defender == null ? 0 : Sum(defender.team, 2, "defBonus", defender);

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

    /// <summary>模块13 运气：本攻击的运气浮动（±luckMax）</summary>
    public static int GetLuckRoll(Infantry attacker, System.Random rng = null)
    {
        rng ??= new System.Random();
        float max = Sum(attacker?.team, 13, "luckMax", attacker);
        float min = Sum(attacker?.team, 13, "luckMin", attacker);
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
        GD.Print($"[CO] {team} 发动 COP：{st.profile.copName}");
        return true;
    }

    public static bool ActivateSuperPower(string team)
    {
        var st = GetState(team);
        if (!CanUseSuperPower(team)) return false;
        st.powerMode = CoPowerMode.Scop;
        st.powerTurnsLeft = PowerDuration(st, CoPowerMode.Scop);
        st.powerUsedThisTurn = true;
        st.charge = 0f;
        GD.Print($"[CO] {team} 发动 SCOP：{st.profile.scopName}");
        return true;
    }

    // ---------- 配合模块：Tag Power（多 CO 合体技） ----------
    /// <summary>Tag 所需星数 = 主 CO SCOP★ + 全部搭档 COP★（合体消耗）</summary>
    public static float GetTagPowerNeeded(string team)
    {
        var st = GetState(team);
        if (st?.profile == null || st.partners.Count == 0) return float.MaxValue;
        return st.profile.superStars + st.partners.Sum(p => p.powerStars);
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
        st.powerTurnsLeft = PowerDuration(st, CoPowerMode.Tag);
        st.powerUsedThisTurn = true;
        st.charge = 0f;
        st.tagExtraTurnUsed = false;
        GrantTeamExtraTurn(team);   // ✅ 模块6（多次移动）：合体技全军再动
        st.tagExtraTurnUsed = true;
        GD.Print($"[CO] {team} 发动 Tag Power：{st.profile.scopName} × {st.partners.Count + 1} CO 合体");
        return true;
    }

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
                return Mathf.Max(1, m.durationTurns);
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
        }
    }

    /// <summary>清档（重开局）</summary>
    public static void ResetAll()
    {
        Teams.Clear();
        loaded = false;
    }
}
