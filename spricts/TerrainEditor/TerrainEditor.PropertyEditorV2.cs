using Godot;
using System;
using System.Linq;

// TerrainEditor.PropertyEditorV2.cs - ✅ 开创人2026-11-24: 检查器V2
// 搜索筛选 + 撤销 + 折叠分区 + 中文标签(ExportLabelDb) + 标量显式确认(防误触)
public partial class TerrainEditor
{
    private Action _lastEditUndo;   // 撤销上一步属性修改

    // ===== 工具栏：搜索框 + 撤销按钮 =====
    private void BuildInspectorToolbar(object target)
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);

        var search = new LineEdit();
        search.PlaceholderText = "🔍 搜索字段（中文/英文）…";
        search.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        search.TextChanged += (t) => FilterInspectorRows(t);
        bar.AddChild(search);

        var undoBtn = new Button();
        undoBtn.Text = "↩ 撤销";
        undoBtn.TooltipText = "撤销上一次属性修改";
        undoBtn.Pressed += () =>
        {
            var u = _lastEditUndo;
            _lastEditUndo = null;
            u?.Invoke();
            TranslateGroup.SetText(spawnHintLabel, "↩ 已撤销上一步修改");
        };
        bar.AddChild(undoBtn);

        propertyContainer.AddChild(bar);
    }

    // ===== 搜索筛选：匹配行显示；分区头在有匹配行时显示；清空搜索全部展开 =====
    private void FilterInspectorRows(string text)
    {
        var q = (text ?? "").Trim().ToLower();
        var kids = propertyContainer.GetChildren();

        foreach (var c in kids)
        {
            if (c is not HBoxContainer row) continue;
            if (!row.HasMeta("search"))
            {
                row.Visible = string.IsNullOrEmpty(q);
                continue;
            }
            var s = row.GetMeta("search").AsString().ToLower();
            row.Visible = string.IsNullOrEmpty(q) || s.Contains(q);
        }

        for (int idx = 0; idx < kids.Count; idx++)
        {
            if (kids[idx] is not Button head || !head.HasMeta("sectionHeader")) continue;
            if (string.IsNullOrEmpty(q)) { head.Visible = true; continue; }
            bool any = false;
            for (int k = idx + 1; k < kids.Count; k++)
            {
                var nx = kids[k];
                if (nx is Button b && b.HasMeta("sectionHeader")) break;
                if (nx is HBoxContainer r2 && r2.Visible) { any = true; break; }
            }
            head.Visible = any;
        }
    }

    // ===== 标量判定 =====
    private static bool IsScalarType(Type t) =>
        t == typeof(string) || t == typeof(bool) ||
        t == typeof(int) || t == typeof(float) || t == typeof(double) ||
        t.IsEnum;

    // ===== 显式确认标量编辑器：✓应用(回车同效) / ✗取消 —— 滑屏误触不落地 =====
    private Control CreateConfirmedScalarEditor(Type t, object value, Action<object> commit)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 4);

        if (t == typeof(bool))
        {
            bool cur = value is bool bv && bv;
            var on = new Button { Text = cur ? "☑ 是" : "☐ 否", ToggleMode = true, ButtonPressed = cur };
            var ok = new Button { Text = "✓", TooltipText = "应用修改" };
            var no = new Button { Text = "✗", TooltipText = "取消" };
            on.Toggled += _ => on.Text = on.ButtonPressed ? "☑ 是" : "☐ 否";
            ok.Pressed += () => commit(on.ButtonPressed);
            no.Pressed += () => { on.ButtonPressed = cur; on.Text = cur ? "☑ 是" : "☐ 否"; };
            box.AddChild(on); box.AddChild(ok); box.AddChild(no);
            return box;
        }

        if (t.IsEnum)
        {
            var ob = new OptionButton();
            var names = Enum.GetNames(t);
            var curName = value?.ToString();
            int sel = 0;
            for (int i = 0; i < names.Length; i++)
            {
                ob.AddItem(names[i], i);
                if (names[i] == curName) sel = i;
            }
            ob.Selected = sel;
            var ok2 = new Button { Text = "✓", TooltipText = "应用修改" };
            var no2 = new Button { Text = "✗", TooltipText = "取消" };
            ok2.Pressed += () => { if (ob.Selected >= 0) commit(Enum.Parse(t, names[ob.Selected])); };
            no2.Pressed += () => ob.Selected = sel;
            box.AddChild(ob); box.AddChild(ok2); box.AddChild(no2);
            return box;
        }

        var edit = new LineEdit { Text = value?.ToString() ?? "", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var ok3 = new Button { Text = "✓", TooltipText = "应用修改(回车同效)" };
        var no3 = new Button { Text = "✗", TooltipText = "取消" };
        string orig = edit.Text;
        void TryApply()
        {
            try
            {
                object v = t == typeof(string) ? (object)edit.Text
                         : t == typeof(int) ? (string.IsNullOrWhiteSpace(edit.Text) ? 0 : int.Parse(edit.Text))
                         : t == typeof(float) ? float.Parse(edit.Text)
                         : double.Parse(edit.Text);
                commit(v);
            }
            catch
            {
                edit.Text = orig;   // 解析失败回退原值
            }
        }
        edit.TextSubmitted += _ => TryApply();
        ok3.Pressed += TryApply;
        no3.Pressed += () => edit.Text = orig;
        box.AddChild(edit); box.AddChild(ok3); box.AddChild(no3);
        return box;
    }
}
