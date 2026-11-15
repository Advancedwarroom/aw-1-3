// ActionEventsEditor.cs — ✅ 开创人2026-11-16: 触发事件编辑器（统一基底事件引擎的图形面板）
// 每事件一行表单：触发操作/条件/目标Export/模式/值/持续/叠加上限/口径/清理；改动即写回字典数组。
using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;

public partial class ActionEventsEditor : VBoxContainer
{
    public event Action<Godot.Collections.Array<Godot.Collections.Dictionary>> ValueChanged;
    private Godot.Collections.Array<Godot.Collections.Dictionary> _events = new();
    private VBoxContainer _list;
    private bool _suppress;

    public Godot.Collections.Array<Godot.Collections.Dictionary> Events
    {
        get => _events;
        set { _events = value ?? new Godot.Collections.Array<Godot.Collections.Dictionary>(); Rebuild(); }
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        var head = new Label { Text = "触发事件（做某操作→改某属性；temp=0永久,N回合后还原）" };
        head.AddThemeFontSizeOverride("font_size", 11);
        AddChild(head);
        var sc = new ScrollContainer { CustomMinimumSize = new Vector2(0, 300) };
        _list = new VBoxContainer();
        sc.AddChild(_list);
        AddChild(sc);
        var add = new Button { Text = "＋ 添加事件" };
        add.Pressed += () => { _events.Add(NewEvent()); Commit(); Rebuild(); };
        AddChild(add);
        Rebuild();
    }

    private static Godot.Collections.Dictionary NewEvent() => new()
    {
        ["action"] = "attack", ["when"] = "always", ["field"] = "attackPower",
        ["mode"] = "add", ["value"] = 10f, ["temp"] = 0, ["stackMax"] = 0, ["stackScope"] = 0, ["cleanup"] = "",
    };

    private string[] ActionNames()
    {
        var l = new List<string>();
        foreach (var f in typeof(ActionIds).GetFields(BindingFlags.Public | BindingFlags.Static))
            l.Add(f.GetValue(null)?.ToString());
        return l.ToArray();
    }

    private void Rebuild()
    {
        if (_list == null) return;
        foreach (Node c in _list.GetChildren()) c.QueueFree();
        string[] actions = ActionNames();
        string[] scopes = { "0不同字段", "1全部叠加", "2除自身" };
        string[] cleanups = { "无", "move移动清", "hit受击清", "manual手动清" };
        for (int i = 0; i < _events.Count; i++)
        {
            int idx = i;
            var d = _events[i] ?? new Godot.Collections.Dictionary();
            var card = new PanelContainer();
            var box = new VBoxContainer();
            card.AddChild(box);

            var r1 = new HBoxContainer();
            r1.AddChild(MkLabel("触发"));
            var act = new OptionButton { CustomMinimumSize = new Vector2(110, 0) };
            foreach (var a in actions) act.AddItem(a);
            int ai = Array.IndexOf(actions, d.GetValueOrDefault("action", "attack").ToString());
            if (ai >= 0) act.Select(ai);
            act.ItemSelected += s => { _events[idx]["action"] = actions[(int)s]; Commit(); };
            r1.AddChild(act);
            r1.AddChild(MkLabel("条件"));
            var whenOpt = new OptionButton();
            whenOpt.AddItem("always"); whenOpt.AddItem("current>=N");
            string whenStr = d.GetValueOrDefault("when", "always").ToString();
            bool isCur = whenStr.StartsWith("current>=");
            whenOpt.Select(isCur ? 1 : 0);
            var needSpin = new SpinBox { MinValue = 1, MaxValue = 999, Visible = isCur, Value = isCur && int.TryParse(whenStr.Substring(9), out int wv) ? wv : 20 };
            whenOpt.ItemSelected += s =>
            {
                _events[idx]["when"] = (int)s == 0 ? "always" : $"current>={(int)needSpin.Value}";
                needSpin.Visible = (int)s == 1; Commit();
            };
            needSpin.ValueChanged += v => { if (whenOpt.Selected == 1) { _events[idx]["when"] = $"current>={(int)v}"; Commit(); } };
            r1.AddChild(whenOpt); r1.AddChild(needSpin);
            box.AddChild(r1);

            var r2 = new HBoxContainer();
            r2.AddChild(MkLabel("改字段"));
            var field = new LineEdit { Text = d.GetValueOrDefault("field", "").ToString(), CustomMinimumSize = new Vector2(130, 0), PlaceholderText = "任意Export名" };
            field.TextChanged += t => { _events[idx]["field"] = t; Commit(); };
            r2.AddChild(field);
            var mode = new OptionButton();
            mode.AddItem("set固定"); mode.AddItem("add加减");
            mode.Select(d.GetValueOrDefault("mode", "set").ToString() == "add" ? 1 : 0);
            mode.ItemSelected += s => { _events[idx]["mode"] = (int)s == 1 ? "add" : "set"; Commit(); };
            r2.AddChild(mode);
            r2.AddChild(MkLabel("值"));
            var val = new SpinBox { MinValue = -9999, MaxValue = 9999, Step = 1, Value = Convert.ToDouble(d.GetValueOrDefault("value", 0f)), CustomMinimumSize = new Vector2(90, 0) };
            val.ValueChanged += v => { _events[idx]["value"] = (float)v; Commit(); };
            r2.AddChild(val);
            box.AddChild(r2);

            var r3 = new HBoxContainer();
            r3.AddChild(MkLabel("持续"));
            var temp = new SpinBox { MinValue = 0, MaxValue = 99, Value = Convert.ToInt32(d.GetValueOrDefault("temp", 0)), CustomMinimumSize = new Vector2(70, 0) };
            temp.ValueChanged += v => { _events[idx]["temp"] = (int)v; Commit(); };
            r3.AddChild(temp);
            r3.AddChild(MkLabel("上限"));
            var sm = new SpinBox { MinValue = 0, MaxValue = 99, Value = Convert.ToInt32(d.GetValueOrDefault("stackMax", 0)), CustomMinimumSize = new Vector2(70, 0) };
            sm.ValueChanged += v => { _events[idx]["stackMax"] = (int)v; Commit(); };
            r3.AddChild(sm);
            var scope = new OptionButton { CustomMinimumSize = new Vector2(110, 0) };
            foreach (var s2 in scopes) scope.AddItem(s2);
            scope.Select(Convert.ToInt32(d.GetValueOrDefault("stackScope", 0)));
            scope.ItemSelected += s => { _events[idx]["stackScope"] = (int)s; Commit(); };
            r3.AddChild(scope);
            var clean = new OptionButton();
            foreach (var c2 in cleanups) clean.AddItem(c2);
            string ct = d.GetValueOrDefault("cleanup", "").ToString();
            clean.Select(Mathf.Max(0, Array.IndexOf(new[] { "", "move", "hit", "manual" }, ct)));
            clean.ItemSelected += s =>
            {
                string tag = new[] { "", "move", "hit", "manual" }[(int)s];
                _events[idx]["cleanup"] = tag;
                _events[idx]["cleanupMove"] = tag == "move" ? "True" : "False";   // 兼容双键
                Commit();
            };
            r3.AddChild(clean);
            var del = new Button { Text = "✕ 删除" };
            del.Pressed += () => { _events.RemoveAt(idx); Commit(); Rebuild(); };
            r3.AddChild(del);
            box.AddChild(r3);

            _list.AddChild(card);
        }
    }

    private static Label MkLabel(string t) { var l = new Label { Text = t }; l.AddThemeFontSizeOverride("font_size", 10); return l; }

    private void Commit()
    {
        if (!_suppress) ValueChanged?.Invoke(new Godot.Collections.Array<Godot.Collections.Dictionary>(_events));
    }
}
