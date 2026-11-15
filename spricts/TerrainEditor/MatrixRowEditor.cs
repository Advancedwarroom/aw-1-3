// MatrixRowEditor.cs — ✅ 开创人2026-11-16: 伤害矩阵·图形化行编辑器
// 选攻击方→该行全防御方数值横排编辑；批量：全100/全0/反填对称/清空本行。
using Godot;
using System;
using System.Collections.Generic;

public partial class MatrixRowEditor : VBoxContainer
{
    public event Action<Dictionary<string, int>> ValueChanged;
    private Dictionary<string, int> _matrix = new();
    private List<string> _attackers = new();
    private List<string> _defenders = new();
    private OptionButton _atkSel;
    private VBoxContainer _rows;
    private bool _suppress;

    public Dictionary<string, int> Matrix
    {
        get => _matrix;
        set { _matrix = value ?? new Dictionary<string, int>(); ParseIds(); RefreshAll(); }
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        var head = new Label { Text = "伤害矩阵——选攻击方,横排编辑该行对全防御方数值" };
        head.AddThemeFontSizeOverride("font_size", 11);
        AddChild(head);

        var top = new HBoxContainer();
        _atkSel = new OptionButton();
        foreach (var a in _attackers) _atkSel.AddItem(a);
        _atkSel.ItemSelected += i => RebuildRows((int)i);
        top.AddChild(_atkSel);
        foreach (var (label, act) in new (string, Action)[] {
            ("本行全100", () => FillRow(100)), ("本行全0", () => FillRow(0)),
            ("反填对称", MirrorRow), ("清空本行外", ClearRow) })
        {
            var b = new Button { Text = label };
            b.Pressed += act;
            top.AddChild(b);
        }
        AddChild(top);
        _rows = new VBoxContainer();
        var sc = new ScrollContainer { CustomMinimumSize = new Vector2(0, 260) };
        sc.AddChild(_rows);
        AddChild(sc);
        RefreshAll();
    }

    private void ParseIds()
    {
        _attackers.Clear(); _defenders.Clear();
        var sa = new HashSet<string>(); var sd = new HashSet<string>();
        foreach (var k in _matrix.Keys)
        {
            var p = k.Split('_');
            if (p.Length < 2) continue;
            sa.Add(p[0]); sd.Add(p[1]);
        }
        _attackers = new List<string>(sa); _attackers.Sort();
        _defenders = new List<string>(sd); _defenders.Sort();
    }

    private void RefreshAll()
    {
        if (_atkSel == null) return;
        _atkSel.Clear();
        foreach (var a in _attackers) _atkSel.AddItem(a);
        if (_attackers.Count > 0) RebuildRows(Mathf.Min(_atkSel.Selected, _attackers.Count - 1));
    }

    private void RebuildRows(int atkIdx)
    {
        foreach (Node c in _rows.GetChildren()) c.QueueFree();
        if (atkIdx < 0 || atkIdx >= _attackers.Count) return;
        string atk = _attackers[atkIdx];
        foreach (var def in _defenders)
        {
            var row = new HBoxContainer();
            var lb = new Label { Text = def, CustomMinimumSize = new Vector2(110, 0) };
            row.AddChild(lb);
            var sp = new SpinBox { MinValue = 0, MaxValue = 999, Step = 5, CustomMinimumSize = new Vector2(90, 0) };
            string key = atk + "_" + def;
            sp.Value = _matrix.TryGetValue(key, out int v) ? v : 0;
            string d = def;
            sp.ValueChanged += val => OnCell(atk, d, (int)val);
            row.AddChild(sp);
            _rows.AddChild(row);
        }
    }

    private void OnCell(string atk, string def, int val)
    {
        if (_suppress) return;
        _matrix[atk + "_" + def] = val;
        ValueChanged?.Invoke(new Dictionary<string, int>(_matrix));
    }

    private void FillRow(int val)
    {
        int i = _atkSel?.Selected ?? 0;
        if (i < 0 || i >= _attackers.Count) return;
        string atk = _attackers[i];
        foreach (var def in _defenders) _matrix[atk + "_" + def] = val;
        ValueChanged?.Invoke(new Dictionary<string, int>(_matrix));
        RebuildRows(i);
    }

    private void MirrorRow()
    {
        int i = _atkSel?.Selected ?? 0;
        if (i < 0 || i >= _attackers.Count) return;
        string atk = _attackers[i];
        foreach (var def in _defenders)
        {
            string rev = def + "_" + atk;
            if (_matrix.TryGetValue(rev, out int v)) _matrix[atk + "_" + def] = v;
        }
        ValueChanged?.Invoke(new Dictionary<string, int>(_matrix));
        RebuildRows(i);
    }

    private void ClearRow()
    {
        int i = _atkSel?.Selected ?? 0;
        if (i < 0 || i >= _attackers.Count) return;
        string atk = _attackers[i];
        var del = new List<string>();
        foreach (var k in _matrix.Keys) if (k.StartsWith(atk + "_")) del.Add(k);
        foreach (var k in del) _matrix.Remove(k);
        ValueChanged?.Invoke(new Dictionary<string, int>(_matrix));
        RebuildRows(i);
    }
}
