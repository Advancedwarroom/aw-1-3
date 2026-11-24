using Godot;
using System;
using System.Collections.Generic;

// TerrainEditor.PropertyEditorV3.cs - ✅ 开创人2026-11-25: 检查器V3a（权重1/2/3/9）
// ①联动显隐(requires) ②取值暗示(min/max/unit/step) ③类型对口控件(开关/滑条+输入/下拉) ⑨默认值恢复+改动高亮
public partial class TerrainEditor
{
    // ---- 字段元数据（来自 export_labels.zh.json v2）----
    private class V3Meta { public string py, requires, unit; public float? min, max, step; public bool has; }
    private static Dictionary<string, V3Meta> _v3meta;
    private static bool _v3loaded;

    private static void EnsureV3Meta()
    {
        if (_v3loaded) return; _v3loaded = true;
        _v3meta = new Dictionary<string, V3Meta>();
        try
        {
            const string path = "res://asscets/EditorData/export_labels.zh.json";
            if (!FileAccess.FileExists(path)) return;
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            var parsed = Json.ParseString(f.GetAsText()).AsGodotDictionary();
            foreach (var kv in parsed)
            {
                var d = kv.Value.AsGodotDictionary();
                var m = new V3Meta { has = true };
                if (d.ContainsKey("py")) m.py = d["py"].ToString();
                if (d.ContainsKey("requires")) m.requires = d["requires"].ToString();
                if (d.ContainsKey("unit")) m.unit = d["unit"].ToString();
                if (d.ContainsKey("min")) m.min = (float)d["min"].AsDouble();
                if (d.ContainsKey("max")) m.max = (float)d["max"].AsDouble();
                if (d.ContainsKey("step")) m.step = (float)d["step"].AsDouble();
                _v3meta[kv.Key.ToString()] = m;
            }
        }
        catch { }
    }

    private static V3Meta V3(string field) { EnsureV3Meta(); return _v3meta.TryGetValue(field, out var m) ? m : null; }

    // ---- 行级状态：初值/取件器/标签/行容器（供高亮/重置/联动/收藏）----
    private readonly Dictionary<string, object> _rowInitial = new();
    private readonly Dictionary<string, Func<object>> _rowGetters = new();
    private readonly Dictionary<string, Action<object>> _rowSetters = new();
    private readonly Dictionary<string, Control> _rowBoxes = new();
    private readonly Dictionary<string, Label> _rowLabels = new();

    // ===== ③⑨ 类型对口控件 + 取值暗示（替换 V2 的 CreateConfirmedScalarEditor 使用点）=====
    private Control CreateV3ScalarEditor(string field, Type t, object value, Func<object> getter, Action<object> commit)
    {
        var meta = V3(field);
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 4);

        // bool → 开关
        if (t == typeof(bool))
        {
            bool cur = value is bool bv && bv;
            var sw = new CheckButton { ButtonPressed = cur, Text = "" };
            sw.Toggled += on =>
            {
                // ✅ 防误触：开关也要二次确认? 不——开关态即时可见，直接提交但入历史
                commit(on);
                RefreshV3Deps();
                UpdateV3Highlight(field);
            };
            box.AddChild(sw);
            return box;
        }

        // enum → 下拉
        if (t.IsEnum)
        {
            var ob = new OptionButton();
            var names = Enum.GetNames(t);
            var curName = value?.ToString();
            int sel = 0;
            for (int i = 0; i < names.Length; i++) { ob.AddItem(names[i], i); if (names[i] == curName) sel = i; }
            ob.Selected = sel;
            var ok = new Button { Text = "✓", TooltipText = TranslateGroup.T("应用修改") };
            ok.Pressed += () => { if (ob.Selected >= 0) { commit(Enum.Parse(t, names[ob.Selected])); UpdateV3Highlight(field); } };
            box.AddChild(ob); box.AddChild(ok);
            return box;
        }

        // 数值：有min+max → 滑条+输入框；否则 输入框。都有 ✓/✗
        bool ranged = meta?.min != null && meta?.max != null;
        var edit = new LineEdit { Text = value?.ToString() ?? "", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        // ②取值暗示：占位符显示范围与单位
        if (ranged)
            edit.PlaceholderText = $"{meta.min} ~ {meta.max}{(string.IsNullOrEmpty(meta.unit) ? "" : " " + meta.unit)}";
        string orig = edit.Text;

        HSlider slider = null;
        if (ranged && (t == typeof(int) || t == typeof(float) || t == typeof(double)))
        {
            slider = new HSlider { MinValue = meta.min.Value, MaxValue = meta.max.Value, Step = meta.step ?? 1, Value = Convert.ToDouble(value ?? 0), CustomMinimumSize = new Vector2(90, 0) };
            slider.ValueChanged += v => edit.Text = t == typeof(int) ? ((int)v).ToString() : v.ToString("0.##");
            box.AddChild(slider);
        }

        var ok2 = new Button { Text = "✓", TooltipText = $"{TranslateGroup.T("应用修改")}(Enter)" };
        var no2 = new Button { Text = "✗", TooltipText = TranslateGroup.T("取消修改") };
        void TryApply()
        {
            try
            {
                object v = t == typeof(int) ? (object)(string.IsNullOrWhiteSpace(edit.Text) ? 0 : int.Parse(edit.Text))
                         : t == typeof(float) ? float.Parse(edit.Text) : double.Parse(edit.Text);
                commit(v);
                UpdateV3Highlight(field);
                RefreshV3Deps();
            }
            catch { edit.Text = orig; }
        }
        edit.TextSubmitted += _ => TryApply();
        ok2.Pressed += TryApply;
        no2.Pressed += () => edit.Text = orig;
        box.AddChild(edit); box.AddChild(ok2); box.AddChild(no2);
        return box;
    }

    // ===== ⑨ 改动高亮 + 默认值恢复 =====
    private void UpdateV3Highlight(string field)
    {
        if (!_rowLabels.TryGetValue(field, out var lab) || !_rowGetters.TryGetValue(field, out var get)) return;
        bool changed = !Equals(get(), _rowInitial.TryGetValue(field, out var ini) ? ini : null);
        lab.AddThemeColorOverride("font_color", changed ? new Color(1f, 0.85f, 0.2f) : new Color(0.8f, 0.8f, 0.9f));
    }

    private void ResetV3Field(string field)
    {
        if (!_rowInitial.TryGetValue(field, out var ini)) return;
        if (_rowSetters.TryGetValue(field, out var set)) set(ini);   // 走原管线：历史栈+刷新全生效
        UpdateV3Highlight(field);
        RefreshV3Deps();
    }

    // ===== ① 联动显隐：requires 字段关闭 → 依赖行灰掉 =====
    private void RefreshV3Deps()
    {
        foreach (var kv in _rowBoxes)
        {
            var meta = V3(kv.Key);
            if (meta == null || string.IsNullOrEmpty(meta.requires)) continue;
            bool depOn = true;
            if (_rowGetters.TryGetValue(meta.requires, out var get))
            {
                var v = get();
                depOn = v is bool b ? b : v != null && !(v is int i && i == 0) && !(v is float f && f == 0);
            }
            kv.Value.Modulate = depOn ? Colors.White : new Color(1, 1, 1, 0.35f);
            SetChildrenDisabled(kv.Value, !depOn);
        }
    }
    private static void SetChildrenDisabled(Control c, bool disabled)
    {
        foreach (var ch in c.GetChildren())
        {
            if (ch is Control cc) { cc.FocusMode = disabled ? Control.FocusModeEnum.None : Control.FocusModeEnum.All; SetChildrenDisabled(cc, disabled); }
        }
    }
    // ---- ④收藏/置顶：user:// 持久化， starred 行提到所属分区顶部 ----
    private HashSet<string> _v3Fav;
    private void EnsureV3Fav()
    {
        if (_v3Fav != null) return;
        _v3Fav = new HashSet<string>();
        try
        {
            const string p = "user://inspector_fav.json";
            if (FileAccess.FileExists(p))
                foreach (var s in Json.ParseString(FileAccess.GetFileAsString(p)).AsStringArray())
                    _v3Fav.Add(s);
        }
        catch { }
    }
    private bool IsV3Fav(string f) { EnsureV3Fav(); return _v3Fav.Contains(f); }
    private void ToggleV3Fav(string f)
    {
        EnsureV3Fav();
        if (!_v3Fav.Remove(f)) _v3Fav.Add(f);
        try { FileAccess.Open("user://inspector_fav.json", FileAccess.ModeFlags.Write).StoreString(Json.Stringify(new Godot.Collections.Array<string>(_v3Fav))); } catch { }
    }
    private void ApplyV3FavOrder()
    {
        var kids = propertyContainer.GetChildren();
        int insertAt = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            if (kids[i] is Button b && b.HasMeta("sectionHeader")) { insertAt = i + 1; continue; }
            if (kids[i] is HBoxContainer row && row.HasMeta("field") && IsV3Fav(row.GetMeta("field").AsString()))
                propertyContainer.MoveChild(row, insertAt++);
        }
    }
}