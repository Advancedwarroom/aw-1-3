// ExportPersistenceCodec.cs - 新模块集合类型的统一存档/地图diff编码。Base64避免与diff分号冲突。
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

public static class ExportPersistenceCodec
{
    private const string Prefix = "g1:";

    public sealed class PushChainRuleData
    {
        public int teamPolicy { get; set; }
        public bool targetInfantry { get; set; }
        public bool targetWeapons { get; set; }
        public bool targetFacilities { get; set; }
        public bool targetEnemies { get; set; }
        public bool targetFriendlies { get; set; }
        public bool targetAllies { get; set; }
        public string[] whitelist { get; set; } = Array.Empty<string>();
        public string[] blacklist { get; set; } = Array.Empty<string>();
    }

    public static bool IsSupportedType(Type type)
    {
        return type == typeof(Godot.Collections.Dictionary<string, string>)
            || type == typeof(Godot.Collections.Dictionary<string, float>)
            || type == typeof(Godot.Collections.Dictionary<string, int>)
            || type == typeof(Godot.Collections.Array<string>)
            || type == typeof(Godot.Collections.Array<UnitCategory>)
            || type == typeof(Godot.Collections.Array<PushChainRule>)
            || type == typeof(string[]);
    }

    public static bool TrySerialize(object value, out string encoded)
    {
        encoded = null;
        if (value == null || !IsSupportedType(value.GetType())) return false;
        try
        {
            object plain = value switch
            {
                Godot.Collections.Dictionary<string, string> d => d.ToDictionary(p => p.Key, p => p.Value),
                Godot.Collections.Dictionary<string, float> d => d.ToDictionary(p => p.Key, p => p.Value),
                Godot.Collections.Dictionary<string, int> d => d.ToDictionary(p => p.Key, p => p.Value),
                Godot.Collections.Array<string> a => a.ToList(),
                Godot.Collections.Array<UnitCategory> a => a.Select(v => (int)v).ToList(),
                Godot.Collections.Array<PushChainRule> a => a.Select(rule => rule == null ? null : new PushChainRuleData
                {
                    teamPolicy = rule.teamPolicy,
                    targetInfantry = rule.targetInfantry,
                    targetWeapons = rule.targetWeapons,
                    targetFacilities = rule.targetFacilities,
                    targetEnemies = rule.targetEnemies,
                    targetFriendlies = rule.targetFriendlies,
                    targetAllies = rule.targetAllies,
                    whitelist = rule.whitelist ?? Array.Empty<string>(),
                    blacklist = rule.blacklist ?? Array.Empty<string>()
                }).ToList(),
                string[] a => a.ToList(),
                _ => null
            };
            if (plain == null) return false;
            encoded = Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plain)));
            return true;
        }
        catch { return false; }
    }

    public static bool TryParse(string text, Type targetType, out object value)
    {
        value = null;
        if (text == null || !text.StartsWith(Prefix) || !IsSupportedType(targetType)) return false;
        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(Prefix.Length)));
            if (targetType == typeof(Godot.Collections.Dictionary<string, string>))
            {
                var result = new Godot.Collections.Dictionary<string, string>();
                foreach (var p in JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new()) result[p.Key] = p.Value;
                value = result;
            }
            else if (targetType == typeof(Godot.Collections.Dictionary<string, float>))
            {
                var result = new Godot.Collections.Dictionary<string, float>();
                foreach (var p in JsonSerializer.Deserialize<Dictionary<string, float>>(json) ?? new()) result[p.Key] = p.Value;
                value = result;
            }
            else if (targetType == typeof(Godot.Collections.Dictionary<string, int>))
            {
                var result = new Godot.Collections.Dictionary<string, int>();
                foreach (var p in JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new()) result[p.Key] = p.Value;
                value = result;
            }
            else if (targetType == typeof(Godot.Collections.Array<string>))
            {
                var result = new Godot.Collections.Array<string>();
                foreach (string item in JsonSerializer.Deserialize<List<string>>(json) ?? new()) result.Add(item);
                value = result;
            }
            else if (targetType == typeof(Godot.Collections.Array<UnitCategory>))
            {
                var result = new Godot.Collections.Array<UnitCategory>();
                foreach (int item in JsonSerializer.Deserialize<List<int>>(json) ?? new()) result.Add((UnitCategory)item);
                value = result;
            }
            else if (targetType == typeof(Godot.Collections.Array<PushChainRule>))
            {
                var result = new Godot.Collections.Array<PushChainRule>();
                foreach (var item in JsonSerializer.Deserialize<List<PushChainRuleData>>(json) ?? new())
                {
                    if (item == null)
                    {
                        result.Add(null);
                        continue;
                    }
                    result.Add(new PushChainRule
                    {
                        teamPolicy = item.teamPolicy,
                        targetInfantry = item.targetInfantry,
                        targetWeapons = item.targetWeapons,
                        targetFacilities = item.targetFacilities,
                        targetEnemies = item.targetEnemies,
                        targetFriendlies = item.targetFriendlies,
                        targetAllies = item.targetAllies,
                        whitelist = item.whitelist ?? Array.Empty<string>(),
                        blacklist = item.blacklist ?? Array.Empty<string>()
                    });
                }
                value = result;
            }
            else if (targetType == typeof(string[])) value = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
            return value != null;
        }
        catch { value = null; return false; }
    }
}

public static class EmpowermentPersistence
{
    public static void AddRuntimeFields(Node source, Dictionary<string, string> fields)
    {
        if (source == null || fields == null) return;
        var type = source.GetType();
        if (type.GetField("empowermentEnabled")?.GetValue(source) is not true) return;
        fields["empowermentManualActive"] = (type.GetField("empowermentManualActive")?.GetValue(source) is true).ToString().ToLowerInvariant();
        fields["empowermentUsesThisTurn"] = (type.GetField("empowermentUsesThisTurn")?.GetValue(source) ?? 0).ToString();
        fields["empowermentManualTargetX"] = (type.GetField("empowermentManualTargetX")?.GetValue(source) ?? -1).ToString();
        fields["empowermentManualTargetY"] = (type.GetField("empowermentManualTargetY")?.GetValue(source) ?? -1).ToString();
    }
}
