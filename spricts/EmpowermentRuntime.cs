// EmpowermentRuntime.cs - 每格多层赋能状态机、统一映射、冲突裁决与精确解能。
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class EmpowermentRuntime
{
    private sealed class SourceView
    {
        public Node Node;
        public string Team;
        public Grids Grid;
        public bool Enabled, Automatic, ConsumesAction, IncludeSelf;
        public bool TargetUnits, TargetWeapons, TargetFacilities;
        public int OperationType, Shape, RangeMin, RangeMax, CastMin, CastMax;
        public int TeamPolicy, MaxStacks, Weight, DispelScope;
        public Vector2I Direction;
        public Godot.Collections.Array<string> Blacklist;
        public Godot.Collections.Dictionary<string, string> Overrides;
        public Godot.Collections.Dictionary<string, float> Deltas;
        public Godot.Collections.Array<string> StatusAdd, StatusRemove;
        public bool ManualActive;
        public Grids ManualTarget;
        public int Uses;

        public static SourceView From(Node node)
        {
            if (node == null || !GodotObject.IsInstanceValid(node)) return null;
            var view = new SourceView { Node = node };
            view.Enabled = Get(node, "empowermentEnabled", false);
            if (!view.Enabled) return view;
            view.OperationType = Get(node, "empowermentOperationType", 0);
            view.Automatic = Get(node, "empowermentAutomatic", true);
            view.ConsumesAction = Get(node, "empowermentConsumesAction", true);
            view.Shape = Get(node, "empowermentShape", 0);
            view.Direction = Get(node, "empowermentDirection", new Vector2I(1, 0));
            view.RangeMin = Get(node, "empowermentRangeMin", 0);
            view.RangeMax = Get(node, "empowermentRangeMax", 0);
            view.CastMin = Get(node, "empowermentCastRangeMin", 0);
            view.CastMax = Get(node, "empowermentCastRangeMax", 0);
            view.TargetUnits = Get(node, "empowermentTargetUnits", true);
            view.TargetWeapons = Get(node, "empowermentTargetWeapons", true);
            view.TargetFacilities = Get(node, "empowermentTargetFacilities", true);
            view.TeamPolicy = Get(node, "empowermentTeamPolicy", 0);
            view.Blacklist = Get(node, "empowermentBlacklist", new Godot.Collections.Array<string>());
            view.IncludeSelf = Get(node, "empowermentIncludeSelf", true);
            view.MaxStacks = Math.Max(1, Get(node, "empowermentMaxStacksPerCell", 1));
            view.Weight = Math.Clamp(Get(node, "empowermentWeight", 0), 0, 9999);
            view.Overrides = Get(node, "empowermentExportOverrides", new Godot.Collections.Dictionary<string, string>());
            view.Deltas = Get(node, "empowermentExportDeltas", new Godot.Collections.Dictionary<string, float>());
            view.StatusAdd = Get(node, "empowermentStatusAdd", new Godot.Collections.Array<string>());
            view.StatusRemove = Get(node, "empowermentStatusRemove", new Godot.Collections.Array<string>());
            view.DispelScope = Get(node, "empowermentDispelScope", 0);
            view.ManualActive = Get(node, "empowermentManualActive", false);
            view.ManualTarget = Get<Grids>(node, "empowermentManualTarget", null);
            view.Uses = Get(node, "empowermentUsesThisTurn", 0);
            if (node is Infantry unit) { view.Team = unit.team; view.Grid = unit.grid; }
            else if (node is Weapon weapon) { view.Team = weapon.team; view.Grid = weapon.grid; }
            else if (node is Facility facility)
            {
                view.Team = facility.facilityTeam;
                var gm = node.GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
                view.Grid = gm?.gridManager?.grids.FirstOrDefault(g => g?.city == facility);
            }
            if (view.ManualTarget == null && view.ManualActive)
            {
                int x = Get(node, "empowermentManualTargetX", -1);
                int y = Get(node, "empowermentManualTargetY", -1);
                var gm = node.GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
                if (gm?.unitManager != null && gm.unitManager.IsValidGrid(new Vector2I(x, y)))
                    view.ManualTarget = gm.gridManager.map[x, y];
            }
            return view;
        }

        public Grids Anchor => Automatic ? Grid : (ManualActive ? ManualTarget : null);
        public bool Active => Automatic || ManualActive;
    }

    private sealed class Layer
    {
        public SourceView Source;
        public Grids Cell;
        public long Sequence;
        public string Id;
    }

    private sealed class CellState
    {
        public readonly List<Layer> Layers = new();
    }

    private sealed class TargetState
    {
        public string Signature = "";
        public readonly Dictionary<string, object> OriginalOverrides = new();
        public readonly List<KeyValuePair<string, float>> AppliedDeltas = new();
        public readonly HashSet<FacilityStatusType> AddedFacilityStatuses = new();
        public readonly Dictionary<FacilityStatusType, int> RemovedFacilityStatuses = new();
    }

    private static readonly Dictionary<Grids, CellState> Cells = new();
    private static readonly Dictionary<Node, TargetState> Targets = new();
    private static readonly Dictionary<Node, long> SourceSequences = new();
    private static readonly HashSet<string> DispelledSourceCells = new();
    private static long nextSequence = 0;
    private static float timer = 0f;
    private static GameManager boundGame;

    // 存档必须写模块介入前的底值；否则读档重建层时会把 delta 再叠一次。
    public static Dictionary<string, object> GetBaseValuesForSave(Node target)
    {
        var result = new Dictionary<string, object>();
        if (target == null || !Targets.TryGetValue(target, out var state)) return result;
        foreach (var pair in state.OriginalOverrides) result[pair.Key] = pair.Value;
        foreach (var group in state.AppliedDeltas.GroupBy(d => d.Key))
        {
            if (result.ContainsKey(group.Key)) continue;
            float total = group.Sum(d => d.Value);
            object value = ExportFieldMapper.GetNumberAfterDelta(target, group.Key, -total);
            if (value != null) result[group.Key] = value;
        }
        return result;
    }

    public static void Tick(GameManager gm, double delta)
    {
        if (gm == null) return;
        timer += (float)delta;
        if (timer < 0.5f) return;
        timer = 0f;
        RefreshAll(gm);
    }

    public static int GetCellStackCount(Grids cell) => cell != null && Cells.TryGetValue(cell, out var state) ? state.Layers.Count : 0;

    public static void RefreshAll(GameManager gm)
    {
        if (gm?.gridManager == null) return;
        if (boundGame != gm)
        {
            ClearRuntime();
            boundGame = gm;
        }
        var views = EnumerateSources(gm).Select(SourceView.From)
            .Where(v => v?.Enabled == true && v.Active && v.Anchor != null).ToList();
        Cells.Clear(); // 每轮从仍存活的来源重建；离场/关闭/手动结束都会撤层
        foreach (var view in views.Where(v => v.OperationType == 0))
        {
            var source = view.Node;
            if (!SourceSequences.ContainsKey(source)) SourceSequences[source] = ++nextSequence;
            var covered = GetCoveredCells(gm, view.Anchor, view.Shape, view.Direction, view.RangeMin, view.RangeMax);
            foreach (var cell in covered)
            {
                if (cell == null || DispelledSourceCells.Contains(DispelKey(source, cell))) continue;
                AddLayer(cell, new Layer
                {
                    Source = view,
                    Cell = cell,
                    Sequence = SourceSequences[source],
                    Id = $"{source.GetInstanceId()}:{cell.GetInstanceId()}:{SourceSequences[source]}"
                }, TeamHelper.GetPhaseTeamName(gm.turnPhase));
            }
        }
        foreach (var view in views.Where(v => v.OperationType == 1))
            DispelCells(gm, view, GetCoveredCells(gm, view.Anchor, view.Shape, view.Direction, view.RangeMin, view.RangeMax));
        RebuildTargets(gm);
    }

    public static bool CanManualUse(Node source)
    {
        var view = SourceView.From(source);
        if (view?.Enabled != true || view.Automatic || view.Uses >= 1 || view.Grid == null) return false;
        if (!view.ConsumesAction) return true;
        if (source is Infantry unit) return !unit.hasActed && unit.state != UnitState.Acted;
        if (source is Weapon weapon) return !weapon.hasActed;
        return true;
    }

    public static bool Activate(Node source, Grids target)
    {
        var gm = source?.GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var view = SourceView.From(source);
        if (gm == null || view == null || target == null || !CanManualUse(source)) return false;
        int distance = Manhattan(view.Grid, target);
        if (distance < view.CastMin || distance > view.CastMax) return false;
        Set(source, "empowermentManualTarget", target);
        Set(source, "empowermentManualTargetX", target.GridIndex.X);
        Set(source, "empowermentManualTargetY", target.GridIndex.Y);
        Set(source, "empowermentManualActive", true);
        Set(source, "empowermentUsesThisTurn", view.Uses + 1);
        SourceSequences[source] = ++nextSequence;
        foreach (string key in DispelledSourceCells.Where(k => k.StartsWith(source.GetInstanceId() + ":")).ToList())
            DispelledSourceCells.Remove(key);
        if (view.ConsumesAction) ConsumeAction(source);
        RefreshAll(gm);
        return true;
    }

    public static void OnPhaseStart(GameManager gm, string team)
    {
        foreach (var source in EnumerateSources(gm))
        {
            var view = SourceView.From(source);
            if (view == null || view.Team != team) continue;
            Set(source, "empowermentUsesThisTurn", 0);
            Set(source, "empowermentManualActive", false);
            Set(source, "empowermentManualTarget", (Grids)null);
            Set(source, "empowermentManualTargetX", -1);
            Set(source, "empowermentManualTargetY", -1);
        }
        DispelledSourceCells.Clear();
        RefreshAll(gm);
    }

    private static void AddLayer(Grids cell, Layer layer, string currentTeam)
    {
        if (!Cells.TryGetValue(cell, out var state)) { state = new CellState(); Cells[cell] = state; }
        state.Layers.RemoveAll(l => l.Source.Node == layer.Source.Node);
        if (state.Layers.Count >= layer.Source.MaxStacks) return; // 到顶后拒绝新来源，不挤掉旧层
        state.Layers.Add(layer);
        state.Layers.Sort(new LayerPriority(currentTeam));
    }

    private sealed class LayerPriority : IComparer<Layer>
    {
        private readonly string currentTeam;
        public LayerPriority(string team) { currentTeam = team; }
        public int Compare(Layer a, Layer b)
        {
            int result = a.Sequence.CompareTo(b.Sequence); // 第一裁决：先操作者优先级低，后操作者后覆盖
            if (result != 0) return result;
            result = (a.Source.Team == currentTeam).CompareTo(b.Source.Team == currentTeam); // 第二：当前操作人势力优先
            if (result != 0) return result;
            return a.Source.Weight.CompareTo(b.Source.Weight); // 第三：高权重后覆盖
        }
    }

    private static void RebuildTargets(GameManager gm)
    {
        var desired = new Dictionary<Node, List<Layer>>();
        foreach (var cellState in Cells)
        {
            foreach (var layer in cellState.Value.Layers)
            {
                foreach (var target in TargetsOnCell(gm, cellState.Key, layer.Source))
                {
                    if (!desired.TryGetValue(target, out var list)) { list = new List<Layer>(); desired[target] = list; }
                    list.Add(layer);
                }
            }
        }

        foreach (var oldTarget in Targets.Keys.ToList())
            if (oldTarget == null || !GodotObject.IsInstanceValid(oldTarget) || !desired.ContainsKey(oldTarget))
            {
                if (oldTarget != null && GodotObject.IsInstanceValid(oldTarget)) ClearTarget(oldTarget, Targets[oldTarget]);
                Targets.Remove(oldTarget);
            }

        foreach (var pair in desired)
        {
            if (!Targets.TryGetValue(pair.Key, out var state)) { state = new TargetState(); Targets[pair.Key] = state; }
            string signature = ComputeSignature(pair.Value);
            if (state.Signature == signature) continue;
            ClearTarget(pair.Key, state);
            state.Signature = signature;
            ApplyLayers(pair.Key, pair.Value, state);
        }

        foreach (var invalid in Cells.Keys.Where(c => c == null || !GodotObject.IsInstanceValid(c)).ToList()) Cells.Remove(invalid);
    }

    private static void ApplyLayers(Node target, List<Layer> layers, TargetState state)
    {
        var overrideNames = new List<string>();
        foreach (var layer in layers)
        {
            if (layer.Source.Overrides != null) overrideNames.AddRange(layer.Source.Overrides.Keys);
            if (target is not Facility)
            {
                if (layer.Source.StatusAdd != null) overrideNames.AddRange(layer.Source.StatusAdd);
                if (layer.Source.StatusRemove != null) overrideNames.AddRange(layer.Source.StatusRemove);
            }
        }
        foreach (string name in overrideNames.Distinct())
            if (!state.OriginalOverrides.ContainsKey(name))
            {
                var captured = ExportFieldMapper.Capture(target, new[] { name });
                if (captured.ContainsKey(name)) state.OriginalOverrides[name] = captured[name];
            }

        foreach (var layer in layers)
        {
            ExportFieldMapper.ApplyOverrides(target, layer.Source.Overrides);
            if (target is not Facility)
            {
                foreach (string status in layer.Source.StatusRemove ?? new()) ExportFieldMapper.WriteValue(target, status, "false");
                foreach (string status in layer.Source.StatusAdd ?? new()) ExportFieldMapper.WriteValue(target, status, "true");
            }
            else ApplyFacilityStatusLayer(target as Facility, layer.Source, state);
            if (layer.Source.Deltas == null) continue;
            foreach (var delta in layer.Source.Deltas)
                if (ExportFieldMapper.AddNumber(target, delta.Key, delta.Value))
                    state.AppliedDeltas.Add(delta);
        }
        RefreshVisual(target);
    }

    private static void ClearTarget(Node target, TargetState state)
    {
        for (int i = state.AppliedDeltas.Count - 1; i >= 0; i--)
            ExportFieldMapper.AddNumber(target, state.AppliedDeltas[i].Key, -state.AppliedDeltas[i].Value);
        state.AppliedDeltas.Clear();
        ExportFieldMapper.RestoreCaptured(target, state.OriginalOverrides);
        state.OriginalOverrides.Clear();
        if (target is Facility facility)
        {
            foreach (var status in state.AddedFacilityStatuses) facility.RemoveStatus(status);
            foreach (var pair in state.RemovedFacilityStatuses) facility.AddStatus(pair.Key, pair.Value);
        }
        state.AddedFacilityStatuses.Clear();
        state.RemovedFacilityStatuses.Clear();
        state.Signature = "";
        RefreshVisual(target);
    }

    private static void ApplyFacilityStatusLayer(Facility facility, SourceView source, TargetState state)
    {
        foreach (string text in source.StatusRemove ?? new())
            if (TryStatus(text, out var status) && facility.HasStatus(status))
            {
                if (!state.RemovedFacilityStatuses.ContainsKey(status)) state.RemovedFacilityStatuses[status] = facility.GetStatusRemainingTurns(status);
                facility.RemoveStatus(status);
            }
        foreach (string text in source.StatusAdd ?? new())
            if (TryStatus(text, out var status) && !facility.HasStatus(status))
            {
                facility.AddStatus(status, -1, source.Team);
                state.AddedFacilityStatuses.Add(status);
            }
    }

    private static bool TryStatus(string text, out FacilityStatusType status)
        => Enum.TryParse(text, true, out status);

    private static IEnumerable<Node> TargetsOnCell(GameManager gm, Grids cell, SourceView source)
    {
        if (source.TargetUnits)
            foreach (var unit in cell.infantries.ToList())
                if (PassesTargetFilter(source, unit, unit.team, unit.UnitTypeId, unit.TemplateId)) yield return unit;
        if (source.TargetWeapons)
            foreach (var weapon in gm.weaponManager?.AllWeapons ?? new List<Weapon>())
                if (weapon.GetWeakPointGrid() == cell && PassesTargetFilter(source, weapon, weapon.team, weapon.GetType().Name, "")) yield return weapon;
        if (source.TargetFacilities && cell.city != null && PassesTargetFilter(source, cell.city, cell.city.facilityTeam, cell.city.GetType().Name, ""))
            yield return cell.city;
    }

    private static bool PassesTargetFilter(SourceView source, Node target, string targetTeam, string id, string template)
    {
        if (target == null || !GodotObject.IsInstanceValid(target)) return false;
        if (!source.IncludeSelf && target == source.Node) return false;
        bool friendly = source.Team == targetTeam || source.Team == TeamHelper.Player || targetTeam == TeamHelper.Player;
        if (source.TeamPolicy == 0 && !friendly) return false;
        if (source.TeamPolicy == 1 && friendly) return false;
        string type = target.GetType().Name;
        return source.Blacklist == null || (!source.Blacklist.Contains(id) && !source.Blacklist.Contains(template) && !source.Blacklist.Contains(type));
    }

    private static List<Grids> GetCoveredCells(GameManager gm, Grids anchor, int shape, Vector2I direction, int min, int max)
    {
        var result = new List<Grids>();
        if (anchor == null) return result;
        min = Math.Max(0, min); max = Math.Max(min, max);
        if (shape == 0)
        {
            foreach (var grid in gm.gridManager.grids)
            {
                int distance = Manhattan(anchor, grid);
                if (distance >= min && distance <= max) result.Add(grid);
            }
            return result;
        }
        var dir = new Vector2I(Math.Sign(direction.X), Math.Sign(direction.Y));
        if (dir == Vector2I.Zero) dir = Vector2I.Right;
        foreach (var grid in gm.gridManager.grids)
        {
            int dx = grid.GridIndex.X - anchor.GridIndex.X;
            int dy = grid.GridIndex.Y - anchor.GridIndex.Y;
            int forward = dx * dir.X + dy * dir.Y;
            int lateral = Math.Abs(dx * dir.Y - dy * dir.X);
            if (shape == 1 && forward >= min && forward <= max && lateral == 0) result.Add(grid);
            if (shape == 2 && forward >= min && forward <= max && lateral <= forward) result.Add(grid);
        }
        return result;
    }

    public static List<Grids> GetManualCastCells(Node source)
    {
        var gm = source?.GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var view = SourceView.From(source);
        if (gm == null || view?.Grid == null) return new List<Grids>();
        return gm.gridManager.grids.Where(g => { int d = Manhattan(view.Grid, g); return d >= view.CastMin && d <= view.CastMax; }).ToList();
    }

    private static void DispelCells(GameManager gm, SourceView source, List<Grids> cells)
    {
        foreach (var cell in cells)
        {
            if (!Cells.TryGetValue(cell, out var state)) continue;
            foreach (var layer in state.Layers.ToList())
            {
                if (source.DispelScope == 0 && layer.Source.Node != source.Node) continue;
                DispelledSourceCells.Add(DispelKey(layer.Source.Node, cell));
                state.Layers.Remove(layer);
            }
        }
    }

    private static string DispelKey(Node source, Grids cell) => $"{source.GetInstanceId()}:{cell.GetInstanceId()}";

    private static string ComputeSignature(List<Layer> layers)
    {
        return string.Join("|", layers.Select(l =>
        {
            string set = l.Source.Overrides == null ? "" : string.Join(",", l.Source.Overrides.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
            string delta = l.Source.Deltas == null ? "" : string.Join(",", l.Source.Deltas.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
            return l.Id + ":" + set + ":" + delta + ":" + string.Join(",", l.Source.StatusAdd ?? new()) + ":" + string.Join(",", l.Source.StatusRemove ?? new());
        }));
    }

    private static IEnumerable<Node> EnumerateSources(GameManager gm)
    {
        foreach (var unit in gm.unitManager?.AllUnits ?? new List<Infantry>()) if (GodotObject.IsInstanceValid(unit)) yield return unit;
        foreach (var weapon in gm.weaponManager?.AllWeapons ?? new List<Weapon>()) if (GodotObject.IsInstanceValid(weapon)) yield return weapon;
        foreach (var grid in gm.gridManager?.grids ?? new List<Grids>()) if (grid?.city != null && GodotObject.IsInstanceValid(grid.city)) yield return grid.city;
    }

    private static int Manhattan(Grids a, Grids b) => Math.Abs(a.GridIndex.X - b.GridIndex.X) + Math.Abs(a.GridIndex.Y - b.GridIndex.Y);

    private static void ConsumeAction(Node source)
    {
        if (source is Infantry unit)
        {
            unit.isAttacked = true; unit.hasActed = true; unit.state = UnitState.Acted; unit.SetWaitVisual(true);
        }
        else if (source is Weapon weapon) weapon.hasActed = true;
    }

    private static void RefreshVisual(Node target)
    {
        if (target is Infantry unit) unit.UpdateHpLabel();
        else if (target is Weapon weapon) weapon.UpdateHpLabel();
        else if (target is Facility facility)
            facility.GetType().GetMethods().FirstOrDefault(m => m.Name == "UpdateStatusBar" && m.GetParameters().Length == 0)?.Invoke(facility, null);
    }

    private static T Get<T>(object target, string name, T fallback)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
        if (field == null) return fallback;
        object value = field.GetValue(target);
        return value is T typed ? typed : fallback;
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.SetValue(target, value);
    }

    private static void ClearRuntime()
    {
        foreach (var pair in Targets)
            if (pair.Key != null && GodotObject.IsInstanceValid(pair.Key)) ClearTarget(pair.Key, pair.Value);
        Targets.Clear(); Cells.Clear(); SourceSequences.Clear(); DispelledSourceCells.Clear();
    }
}
