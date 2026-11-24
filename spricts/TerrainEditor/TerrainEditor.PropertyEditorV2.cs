using Godot;
using System;
using System.Collections.Generic;

// TerrainEditor.PropertyEditorV2.cs - ✅ 开创人2026-11-24: 检查器V2
// 搜索筛选 + 折叠分区 + 字段源词索引(ExportLabelDb→TranslateGroup.T多语言)
// 撤销/重做：50步历史栈 (开创人2026-11-25加深)
public partial class TerrainEditor
{
    private readonly List<V3EditRecord> _editFuture = new();
    private Button _undoBtn, _redoBtn;

    private void RefreshUndoButtons()
    {
        if (_undoBtn != null)
            _undoBtn.Text = EditJournal.Count > 0 ? $"↩ {TranslateGroup.T("撤销")}({EditJournal.Count})" : $"↩ {TranslateGroup.T("撤销")}";
        if (_redoBtn != null)
            _redoBtn.Text = _editFuture.Count > 0 ? $"↪ {TranslateGroup.T("重做")}({_editFuture.Count})" : $"↪ {TranslateGroup.T("重做")}";
    }

    // ===== 工具栏：搜索 + 撤销 + 重做 =====
    private void BuildInspectorToolbar(object target)
    {
        _editFuture.Clear();

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);

        var search = new LineEdit();
        search.PlaceholderText = $"🔍 {TranslateGroup.T("搜索字段")}";
        search.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        search.TextChanged += (t) => FilterInspectorRows(t);
        bar.AddChild(search);

        _undoBtn = new Button();
        _undoBtn.TooltipText = TranslateGroup.T("撤销");
        _undoBtn.Pressed += () =>
        {
            if (EditJournal.Count == 0) return;
            var top = EditJournal[^1];
            EditJournal.RemoveAt(EditJournal.Count - 1);
            _editFuture.Add(top);
            top.undo();
            TranslateGroup.SetText(spawnHintLabel, TranslateGroup.T("已撤销上一步修改"));
            RefreshUndoButtons();
        };
        bar.AddChild(_undoBtn);

        _redoBtn = new Button();
        _redoBtn.TooltipText = TranslateGroup.T("重做");
        _redoBtn.Pressed += () =>
        {
            if (_editFuture.Count == 0) return;
            var top = _editFuture[^1];
            _editFuture.RemoveAt(_editFuture.Count - 1);
            PushJournal(top);
            top.redo();
            TranslateGroup.SetText(spawnHintLabel, TranslateGroup.T("重做"));
            RefreshUndoButtons();
        };
        bar.AddChild(_redoBtn);

        // ✅ V3b: 排序/卡片/历史
        var sortOb = new OptionButton();
        sortOb.AddItem("按模块", 0); sortOb.AddItem("按类型", 1); sortOb.AddItem("按修改时间", 2);
        sortOb.Selected = 0;
        sortOb.ItemSelected += i => ApplyV3Sort((V3Sort)(int)i);
        bar.AddChild(sortOb);

        var cardChk = new CheckButton { Text = "▦卡片", TooltipText = "两级导航：模块卡片→浮窗" };
        cardChk.Toggled += on => SetCardMode(on);
        bar.AddChild(cardChk);

        var histBtn = new Button { Text = "🕘", TooltipText = "历史记录(点击回退)" };
        histBtn.Pressed += () => { EnsureHistCap(); OpenHistoryPopup(); };
        bar.AddChild(histBtn);

        propertyContainer.AddChild(bar);
        EnsureHistCap();
        RefreshUndoButtons();
    }

    // ===== 搜索筛选 =====
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
            var ok = new Button { Text = "✓", TooltipText = TranslateGroup.T("应用修改") };
            var no = new Button { Text = "✗", TooltipText = TranslateGroup.T("取消修改") };
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
            var ok2 = new Button { Text = "✓", TooltipText = TranslateGroup.T("应用修改") };
            var no2 = new Button { Text = "✗", TooltipText = TranslateGroup.T("取消修改") };
            ok2.Pressed += () => { if (ob.Selected >= 0) commit(Enum.Parse(t, names[ob.Selected])); };
            no2.Pressed += () => ob.Selected = sel;
            box.AddChild(ob); box.AddChild(ok2); box.AddChild(no2);
            return box;
        }

        var edit = new LineEdit { Text = value?.ToString() ?? "", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var ok3 = new Button { Text = "✓", TooltipText = $"{TranslateGroup.T("应用修改")}(Enter)" };
        var no3 = new Button { Text = "✗", TooltipText = TranslateGroup.T("取消修改") };
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
                edit.Text = orig;
            }
        }
        edit.TextSubmitted += _ => TryApply();
        ok3.Pressed += TryApply;
        no3.Pressed += () => edit.Text = orig;
        box.AddChild(edit); box.AddChild(ok3); box.AddChild(no3);
        return box;
    }
    // ✅ 开创人2026-11-25: 可折叠分区头（格子属性面板与主检查器共用）
    private Button MakeSectionHeader(string title)
    {
        var btn = new Button();
        btn.Text = $"▼ {title}";
        btn.SetMeta("sectionHeader", true);
        btn.AddThemeFontSizeOverride("font_size", 14);
        btn.CustomMinimumSize = new Vector2(0, 30);
        btn.Pressed += () =>
        {
            var kids = propertyContainer.GetChildren();
            int idx = kids.IndexOf(btn);
            bool collapsing = btn.Text.StartsWith("▼");
            btn.Text = collapsing ? $"▶ {title}" : $"▼ {title}";
            for (int k = idx + 1; k < kids.Count; k++)
            {
                var nx = kids[k];
                if (nx is Button b && b.HasMeta("sectionHeader")) break;
                if (nx is CanvasItem ci) ci.Visible = !collapsing;
            }
        };
        return btn;
    }
}