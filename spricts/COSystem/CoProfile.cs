// CoProfile.cs - ✅ CO 三十模块体系 · 数据层
// 每个 CO 一份 JSON（co/<id>.json）：基础信息三件套 + None/COP/SCOP/Zone 四槽位 + 模块组合
// 结构设计见 docs/CO模块体系设计.md
using Godot;
using System.Collections.Generic;

public enum CoPowerMode { Off = 0, Cop = 1, Scop = 2, Tag = 3 }

/// <summary>单个 CO 模块条目（30 大模块之一的一条实例）</summary>
public class CoModuleEntry
{
    public int module;             // 模块号 1..30（见 docs/CO模块体系设计.md 第一节）
    public string name;            // 模块显示名（如 "伤害加成"）
    // 三档+Zone 四档数值（键自由：offBonus/defBonus/moveBonus/healHp/...，由运行层按模块号解释）
    public Godot.Collections.Dictionary<string, float> d2d = new();
    public Godot.Collections.Dictionary<string, float> cop = new();
    public Godot.Collections.Dictionary<string, float> scop = new();
    public Godot.Collections.Dictionary<string, float> zone = new();   // 模块30：区域内效果（区域外用 d2d）
    // 模块20 时限参数
    public string duration = "turns";  // turns=按回合过期 / permanent=永久 / instant=瞬时
    public int durationTurns = 1;      // COP/SCOP 默认 1 个己方回合（AW 标准）
    public int maxStacks = 1;          // 可叠加层数（1=不可叠加）
    // 模块29 特殊限定：条件（可组合；空=无条件）
    public List<string> unitTypes = new();    // 限定单位类型（空=全部）
    public List<string> terrainTypes = new(); // 限定地形（空=全部）
    public bool directOnly = false;    // 仅直接攻击
    public bool indirectOnly = false; // 仅间接攻击

    public Godot.Collections.Dictionary<string, float> GetPhaseValues(CoPowerMode mode)
        => mode == CoPowerMode.Cop ? cop : mode == CoPowerMode.Scop || mode == CoPowerMode.Tag ? scop : d2d;
}

/// <summary>能量条积累配置（模块 27 附属）</summary>
public class CoChargeConfig
{
    public float damageDealtWeight = 1f;    // 我方造成伤害 → 能量
    public float damageTakenWeight = 1f;    // 我方被造成伤害 → 能量
    public float unitLostWeight = 0f;       // 我方损失单位数 → 能量
    public float unitProducedWeight = 0f;   // 我方生产单位数 → 能量
    public float enemyTurnWeight = 0f;      // 敌方坚持回合数 → 能量
    public float fundsSpentWeight = 0f;     // 我方消耗资金 → 能量
    public float incomeWeight = 0f;         // 现有/初始资金 → 能量
    public float chargeDivisor = 900f;     // 能量分母（伤害类：900 伤害=1★ 的量级）
    public bool globalScope = true;         // true=全局统计；false=仅 CO 单位 Zone 范围内
}

/// <summary>CO 档案（基础信息三件套 + 四槽位技能）</summary>
public class CoProfile
{
    public string id;               // 如 "andy"
    public string name = "";        // 自定义名称
    public string avatar = "";      // 头像路径（res://co/avatars/xxx.png，可玩家上传）
    public string bio = "";         // 自定义介绍
    public string hits = "";
    public string miss = "";
    public string army = "";         // OS/BM/GE/YC/BH/...（阵营皮肤参考）
    public string copName = "";
    public string scopName = "";
    public string copDesc = "";
    public string scopDesc = "";
    public int powerStars = 3;      // COP 所需星数
    public int superStars = 3;     // SCOP 所需星数
    public int zoneRange = -1;      // -1=全局 D2D（AW1-3 系）；>=0=CO Zone 模式（DoR 系）
    public List<string> coUnits = new();  // 模块19：CO 专属单位
    public List<CoModuleEntry> modules = new();
    public CoChargeConfig charge = new();

    static Godot.Collections.Dictionary<string, float> ToVals(Variant v)
    {
        var d = new Godot.Collections.Dictionary<string, float>();
        if (v.VariantType == Variant.Type.Dictionary)
            foreach (var kv in v.AsGodotDictionary())
                d[kv.Key.AsString()] = Mathf.Max(-999f, Mathf.Min(999f, kv.Value.AsSingle()));
        return d;
    }

    static CoModuleEntry ToModule(Godot.Collections.Dictionary m)
    {
        var e = new CoModuleEntry
        {
            module = m.ContainsKey("module") ? m["module"].AsInt32() : 0,
            name = m.ContainsKey("name") ? m["name"].AsString() : "",
            duration = m.ContainsKey("duration") ? m["duration"].AsString() : "turns",
            durationTurns = m.ContainsKey("durationTurns") ? m["durationTurns"].AsInt32() : 1,
            maxStacks = m.ContainsKey("maxStacks") ? m["maxStacks"].AsInt32() : 1,
            directOnly = m.ContainsKey("directOnly") && m["directOnly"].AsBool(),
            indirectOnly = m.ContainsKey("indirectOnly") && m["indirectOnly"].AsBool(),
        };
        foreach (string k in new[] { "unitTypes", "terrainTypes" })
            if (m.ContainsKey(k) && m[k].VariantType == Variant.Type.Array)
                foreach (var s in m[k].AsGodotArray()) (k == "unitTypes" ? e.unitTypes : e.terrainTypes).Add(s.AsString());
        if (m.ContainsKey("d2d")) e.d2d = ToVals(m["d2d"]);
        if (m.ContainsKey("cop")) e.cop = ToVals(m["cop"]);
        if (m.ContainsKey("scop")) e.scop = ToVals(m["scop"]);
        if (m.ContainsKey("zone")) e.zone = ToVals(m["zone"]);
        return e;
    }

    public static CoProfile FromDict(Godot.Collections.Dictionary d)
    {
        var p = new CoProfile { id = d.ContainsKey("id") ? d["id"].AsString() : "" };
        foreach (var kv in d)
        {
            switch (kv.Key.AsString())
            {
                case "name": p.name = kv.Value.AsString(); break;
                case "avatar": p.avatar = kv.Value.AsString(); break;
                case "bio": p.bio = kv.Value.AsString(); break;
                case "hits": p.hits = kv.Value.AsString(); break;
                case "miss": p.miss = kv.Value.AsString(); break;
                case "army": p.army = kv.Value.AsString(); break;
                case "copName": p.copName = kv.Value.AsString(); break;
                case "scopName": p.scopName = kv.Value.AsString(); break;
                case "copDesc": p.copDesc = kv.Value.AsString(); break;
                case "scopDesc": p.scopDesc = kv.Value.AsString(); break;
                case "powerStars": p.powerStars = kv.Value.AsInt32(); break;
                case "superStars": p.superStars = kv.Value.AsInt32(); break;
                case "zoneRange": p.zoneRange = kv.Value.AsInt32(); break;
                case "coUnits":
                    if (kv.Value.VariantType == Variant.Type.Array)
                        foreach (var s in kv.Value.AsGodotArray()) p.coUnits.Add(s.AsString());
                    break;
                case "modules":
                    if (kv.Value.VariantType == Variant.Type.Array)
                        foreach (var mo in kv.Value.AsGodotArray())
                            if (mo.VariantType == Variant.Type.Dictionary)
                                p.modules.Add(ToModule(mo.AsGodotDictionary()));
                    break;
                case "charge":
                    if (kv.Value.VariantType == Variant.Type.Dictionary)
                    {
                        var c = kv.Value.AsGodotDictionary();
                        var g = p.charge;
                        if (c.ContainsKey("damageDealtWeight")) g.damageDealtWeight = c["damageDealtWeight"].AsSingle();
                        if (c.ContainsKey("damageTakenWeight")) g.damageTakenWeight = c["damageTakenWeight"].AsSingle();
                        if (c.ContainsKey("unitLostWeight")) g.unitLostWeight = c["unitLostWeight"].AsSingle();
                        if (c.ContainsKey("unitProducedWeight")) g.unitProducedWeight = c["unitProducedWeight"].AsSingle();
                        if (c.ContainsKey("enemyTurnWeight")) g.enemyTurnWeight = c["enemyTurnWeight"].AsSingle();
                        if (c.ContainsKey("fundsSpentWeight")) g.fundsSpentWeight = c["fundsSpentWeight"].AsSingle();
                        if (c.ContainsKey("incomeWeight")) g.incomeWeight = c["incomeWeight"].AsSingle();
                        if (c.ContainsKey("chargeDivisor")) g.chargeDivisor = Mathf.Max(1f, c["chargeDivisor"].AsSingle());
                        if (c.ContainsKey("globalScope")) g.globalScope = c["globalScope"].AsBool();
                    }
                    break;
            }
        }
        return p;
    }

    /// <summary>从 JSON 文本解析</summary>
    public static CoProfile FromJson(string json)
    {
        var v = Json.ParseString(json);
        if (v.VariantType != Variant.Type.Dictionary) return null;
        return FromDict(v.AsGodotDictionary());
    }

    /// <summary>加载目录下全部 CO 档案（res://co/*.json）</summary>
    public static List<CoProfile> LoadAll(string dir = "res://co")
    {
        var list = new List<CoProfile>();
        var dirAccess = DirAccess.Open(dir);
        if (dirAccess == null) return list;
        foreach (string file in dirAccess.GetFiles())
        {
            if (!file.EndsWith(".json")) continue;
            using var f = FileAccess.Open($"{dir}/{file}", FileAccess.ModeFlags.Read);
            if (f == null) continue;
            var p = FromJson(f.GetAsText());
            if (p != null && !string.IsNullOrEmpty(p.id)) list.Add(p);
        }
        return list;
    }
}
