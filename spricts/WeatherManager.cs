// WeatherManager.cs - 区域天气系统（格战模块二十四）
// 需求源：Advancedwarroom/Commander_Wars issue#1（区域天气）
// 设计：全球基底天气（baseWeatherId）+ 格子级天气栈（同类不叠=续时长，异类最多4层，
//       单层可标记"替代基底为晴"）。AI 与玩家走同一套静态查询，效果只读不改实际数值。
// 注意：本文件只做"数据与查询"；效果接入点见 docs 侧《INTEGRATION_天气.md》（grep 口诀清单）。
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>天气类型定义（注册表条目；mods 可 RegisterWeatherType 追加）</summary>
public class WeatherTypeDef
{
    public int id;
    public string name = "";
    // 千分制百分比（1000=100%），攻防为乘区
    public int attackPercent = 1000;
    public int defensePercent = 1000;
    // 增量类（可为负）
    public int moveCostDelta = 0;     // 每格移动开销增量（结算后最小1）
    public int movePointsDelta = 0;   // 移动力增量
    public int fireRangeDelta = 0;    // 最大射程增量
    public int minFireRangeDelta = 0; // 最小射程增量
    public int visionDelta = 0;       // 视野增量
    public int fuelCostDelta = 0;     // 每格燃料消耗增量
    // 该区域层是否把本格基底天气视为晴（issue：替代基底的晴选项）
    public bool replaceBaseWithSunny = false;
    // 生效/失效回调（mod 扩展用，可空）
    public Action onActivate;
    public Action onDeactivate;
}

/// <summary>格子上的单层天气</summary>
public class WeatherLayer
{
    public int typeId;
    public int remainingTurns;   // 剩余回合数；-1 = 永久
    public bool replaceBaseSunny;
}

public partial class WeatherManager : Node
{
    public static WeatherManager Instance { get; private set; }

    [ExportGroup("区域天气总开关")]
    [Export] public bool localWeatherEnabled = false;  // 地图设置勾选（= issue 的 ☑️ Enable Local Weather）
    [Export] public int baseWeatherId = 0;             // 全球基底天气（0=晴）
    [Export] public bool weatherAffectsAI = true;      // AI 寻路/决策计入天气（issue 要求；关闭则 AI 按晴算）

    // 类型注册表 + 格子级天气栈
    private readonly Dictionary<int, WeatherTypeDef> types = new();
    private readonly Dictionary<Vector2I, List<WeatherLayer>> cellStacks = new();
    public const int MaxStackLayers = 4;               // issue：不同天气最多叠 4 层

    public override void _Ready()
    {
        Instance = this;
        AddToGroup("weather_manager");
        RegisterBuiltinTypes();
    }

    // ========== 内置五种（默认值按 AW 系惯例；mod 可在 _Ready 后覆写注册项）==========
    private void RegisterBuiltinTypes()
    {
        RegisterWeatherType(new WeatherTypeDef { id = 0, name = "晴" });
        RegisterWeatherType(new WeatherTypeDef { id = 1, name = "雨", defensePercent = 1100, visionDelta = -1 });
        RegisterWeatherType(new WeatherTypeDef { id = 2, name = "雪", moveCostDelta = 1, movePointsDelta = -1, fuelCostDelta = 1 });
        RegisterWeatherType(new WeatherTypeDef { id = 3, name = "沙暴", attackPercent = 900, visionDelta = -2 });
        RegisterWeatherType(new WeatherTypeDef { id = 4, name = "雾", visionDelta = -3, fireRangeDelta = -1 });
    }

    public void RegisterWeatherType(WeatherTypeDef t) => types[t.id] = t;
    public WeatherTypeDef GetWeatherType(int id) => types.TryGetValue(id, out var t) ? t : null;

    // ========== 增删查 ==========
    /// <summary>给格子加一层天气：同类不叠（续时长），异类≤4层。turns=-1 永久。</summary>
    public bool AddWeather(Vector2I cell, int typeId, int turns, bool replaceSunny = false)
    {
        if (!localWeatherEnabled || typeId == 0) return false;
        var type = GetWeatherType(typeId);
        if (type == null) return false;
        if (!cellStacks.TryGetValue(cell, out var stack))
        {
            stack = new List<WeatherLayer>();
            cellStacks[cell] = stack;
        }
        var existing = stack.FirstOrDefault(l => l.typeId == typeId);
        if (existing != null)
        {
            existing.remainingTurns = Mathf.Max(existing.remainingTurns, turns);  // 同类不叠=续时长
            existing.replaceBaseSunny |= replaceSunny;
            return true;
        }
        if (stack.Count >= MaxStackLayers) return false;   // 异类≤4层
        stack.Add(new WeatherLayer { typeId = typeId, remainingTurns = turns, replaceBaseSunny = replaceSunny });
        type.onActivate?.Invoke();
        return true;
    }

    public void RemoveWeather(Vector2I cell, int typeId)
    {
        if (!cellStacks.TryGetValue(cell, out var stack)) return;
        var layer = stack.FirstOrDefault(l => l.typeId == typeId);
        if (layer == null) return;
        stack.Remove(layer);
        GetWeatherType(typeId)?.onDeactivate?.Invoke();
        if (stack.Count == 0) cellStacks.Remove(cell);
    }

    public void ClearAllWeather()
    {
        foreach (var stack in cellStacks.Values)
            foreach (var layer in stack)
                GetWeatherType(layer.typeId)?.onDeactivate?.Invoke();
        cellStacks.Clear();
    }

    public IReadOnlyList<WeatherLayer> GetLayersAt(Vector2I cell)
    {
        if (!localWeatherEnabled) return Array.Empty<WeatherLayer>();
        return cellStacks.TryGetValue(cell, out var s) ? s : (IReadOnlyList<WeatherLayer>)Array.Empty<WeatherLayer>();
    }

    /// <summary>有效天气判定（CO 天气检查走这里）：区域层优先，无层回退基底；显式转晴返回0</summary>
    public int GetEffectiveWeatherIdAt(Vector2I cell)
    {
        if (!localWeatherEnabled) return baseWeatherId;
        var layers = GetLayersAt(cell);
        if (layers.Count == 0) return baseWeatherId;
        if (layers.Any(l => l.replaceBaseSunny)) return 0;   // 替代基底=晴
        return layers[0].typeId;                              // 顶层优先
    }

    // ========== 效果聚合（基底 + 各层一起算；显式转晴则剔除基底）==========
    private List<WeatherTypeDef> GetActiveTypesAt(Vector2I cell, bool forAI)
    {
        var result = new List<WeatherTypeDef>();
        if (forAI && !weatherAffectsAI) return result;   // AI 开关：关闭=按无天气算
        if (!localWeatherEnabled) return result;
        var layers = GetLayersAt(cell);
        bool baseReplaced = layers.Any(l => l.replaceBaseSunny);
        if (!baseReplaced && baseWeatherId != 0)
        {
            var b = GetWeatherType(baseWeatherId);
            if (b != null) result.Add(b);
        }
        foreach (var l in layers)
        {
            var t = GetWeatherType(l.typeId);
            if (t != null) result.Add(t);
        }
        return result;
    }

    public int GetAttackPercent(Vector2I cell, bool forAI = false)
    {
        int p = 1000;
        foreach (var t in GetActiveTypesAt(cell, forAI)) p = p * t.attackPercent / 1000;
        return p;
    }

    public int GetDefensePercent(Vector2I cell, bool forAI = false)
    {
        int p = 1000;
        foreach (var t in GetActiveTypesAt(cell, forAI)) p = p * t.defensePercent / 1000;
        return p;
    }

    public int GetMoveCostDelta(Vector2I cell, bool forAI = false)
        => GetActiveTypesAt(cell, forAI).Sum(t => t.moveCostDelta);

    public int GetMovePointsDelta(Vector2I cell, bool forAI = false)
        => GetActiveTypesAt(cell, forAI).Sum(t => t.movePointsDelta);

    public int GetFireRangeDelta(Vector2I cell, bool forAI = false)
        => GetActiveTypesAt(cell, forAI).Sum(t => t.fireRangeDelta);

    public int GetMinFireRangeDelta(Vector2I cell, bool forAI = false)
        => GetActiveTypesAt(cell, forAI).Sum(t => t.minFireRangeDelta);

    public int GetVisionDelta(Vector2I cell, bool forAI = false)
        => GetActiveTypesAt(cell, forAI).Sum(t => t.visionDelta);

    public int GetFuelCostDelta(Vector2I cell, bool forAI = false)
        => GetActiveTypesAt(cell, forAI).Sum(t => t.fuelCostDelta);

    /// <summary>移动开销结算口：AI寻路/范围预览/截断统一走这个（在 GetMoveCostOnGrid 末尾调用）</summary>
    public int ApplyMoveCost(Vector2I cell, int cost, bool forAI = false)
        => Mathf.Max(1, cost + GetMoveCostDelta(cell, forAI));

    // ========== 回合驱动（挂在 GameManager 回合开始，同 PerformTurnSupply 的位置）==========
    public void OnTurnStart()
    {
        var expired = new List<(Vector2I cell, int typeId)>();
        foreach (var kv in cellStacks)
        {
            foreach (var layer in kv.Value)
            {
                if (layer.remainingTurns < 0) continue;    // 永久
                layer.remainingTurns--;
                if (layer.remainingTurns <= 0) expired.Add((kv.Key, layer.typeId));
            }
        }
        foreach (var (cell, typeId) in expired) RemoveWeather(cell, typeId);
    }

    // ========== 存档（ExtraSave 挂点；格式 x,y,typeId,turns,replaceSunny;... ）==========
    public string SerializeStacks()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in cellStacks)
            foreach (var l in kv.Value)
                sb.Append($"{kv.Key.X},{kv.Key.Y},{l.typeId},{l.remainingTurns},{(l.replaceBaseSunny ? 1 : 0)};");
        return sb.ToString();
    }

    public void DeserializeStacks(string data)
    {
        ClearAllWeather();
        if (string.IsNullOrEmpty(data)) return;
        foreach (var rec in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = rec.Split(',');
            if (f.Length < 5) continue;
            var cell = new Vector2I(int.Parse(f[0]), int.Parse(f[1]));
            AddWeather(cell, int.Parse(f[2]), int.Parse(f[3]), f[4] == "1");
        }
    }

    // ========== UI 指示（Info Bar 小图标文字；StatsPanel 调用）==========
    public string GetCellWeatherText(Vector2I cell)
    {
        if (!localWeatherEnabled) return "";
        var layers = GetLayersAt(cell);
        if (layers.Count == 0) return baseWeatherId != 0 ? GetWeatherType(baseWeatherId)?.name : "";
        return string.Join("+", layers.Select(l =>
        {
            var t = GetWeatherType(l.typeId);
            string n = t?.name ?? "?";
            return l.remainingTurns >= 0 ? $"{n}{l.remainingTurns}" : n;
        }));
    }
}
