// InternalServiceMapper.cs - 进驻期间生效的 Export 映射；离开/卸载时恢复快照。
using Godot;
using System.Collections.Generic;
using System.Linq;

public static class InternalServiceMapper
{
    private sealed class ServiceState
    {
        public Node Host;
        public Infantry Unit;
        public Dictionary<string, object> OriginalValues = new();
    }
    private static readonly List<ServiceState> States = new();

    public static void Apply(Node host, Infantry unit, bool enabled, Godot.Collections.Array<string> whitelist,
        Godot.Collections.Dictionary<string, string> overrides, Godot.Collections.Dictionary<string, float> deltas)
    {
        if (host == null || unit == null || !GodotObject.IsInstanceValid(unit)) return;
        var existing = States.FirstOrDefault(s => s.Host == host && s.Unit == unit);
        if (!enabled || (whitelist != null && whitelist.Count > 0 && !whitelist.Contains(unit.UnitTypeId) && !whitelist.Contains(unit.GetType().Name)))
        {
            if (existing != null) Remove(host, unit);
            return;
        }
        if (existing != null) return; // 持续效果，不是每回合重复叠加
        var names = new List<string>();
        if (overrides != null) names.AddRange(overrides.Keys);
        if (deltas != null) names.AddRange(deltas.Keys);
        names = names.Distinct().ToList();
        var state = new ServiceState { Host = host, Unit = unit, OriginalValues = ExportFieldMapper.Capture(unit, names) };
        States.Add(state);
        ExportFieldMapper.ApplyOverrides(unit, overrides);
        ExportFieldMapper.ApplyNumericDeltas(unit, deltas);
        unit.UpdateHpLabel();
    }

    public static void Remove(Node host, Infantry unit)
    {
        for (int i = States.Count - 1; i >= 0; i--)
        {
            var state = States[i];
            if (state.Host != host || state.Unit != unit) continue;
            if (state.Unit != null && GodotObject.IsInstanceValid(state.Unit))
            {
                ExportFieldMapper.RestoreCaptured(state.Unit, state.OriginalValues);
                state.Unit.UpdateHpLabel();
            }
            States.RemoveAt(i);
        }
    }

    public static void RemoveAll(Node host)
    {
        foreach (var unit in States.Where(s => s.Host == host).Select(s => s.Unit).ToList()) Remove(host, unit);
    }
}

public partial class Infantry
{
    [ExportGroup("内部服务：通用Export映射")]
    [Export] public bool internalExportServiceEnabled = false;
    [Export] public Godot.Collections.Array<string> internalExportServiceWhitelist = new();
    [Export] public Godot.Collections.Dictionary<string, string> internalExportServiceOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> internalExportServiceDeltas = new();

    public void ApplyInternalExportService(Infantry unit)
        => InternalServiceMapper.Apply(this, unit, internalExportServiceEnabled, internalExportServiceWhitelist, internalExportServiceOverrides, internalExportServiceDeltas);

    public void RemoveInternalExportService(Infantry unit) => InternalServiceMapper.Remove(this, unit);
}
