using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// TerrainEditor.PropertyEditorV3b.cs - ✅ 开创人2026-11-25: 检查器V3b（权重6/7/8）
// ⑥筛选排序(模块/类型/修改时间) ⑦两级导航(模块卡片→浮窗) ⑧历史栏目(10~1000次可配,点击回退)
public partial class TerrainEditor
{
    // ===== ⑥ 筛选/排序 =====
    private enum V3Sort { Module = 0, Type = 1, Time = 2 }
    private V3Sort _v3Sort = V3Sort.Module;
    private readonly Dictionary<string, int> _rowOrder = new();    // 字段→构建序号(恢复模块序用)
    private readonly Dictionary<string, int> _rowTypes = new();    // 字段→类型键(排序用)
    private readonly Dictionary<string, DateTime> _lastEditTime = new();

    private void ApplyV3Sort(V3Sort mode)
    {
        _v3Sort = mode;
        var kids = propertyContainer.GetChildren();
        if (mode == V3Sort.Module)
        {
            // 恢复原构建序 + 显示分区头
            var rows = new List<Control>();
            foreach (var c in kids) if (c is HBoxContainer r && r.HasMeta("field")) rows.Add(r);
            rows = rows.OrderBy(r => _rowOrder.TryGetValue(r.GetMeta("field").AsString(), out var o) ? o : 9999).ToList();
            foreach (var c in kids) if (c is Button && c is CanvasItem c0) c0.Visible = true;
            int pos = 0;
            foreach (var r in rows) { propertyContainer.MoveChild(r, pos++); }
            foreach (var c in kids) if (c is HBoxContainer rr && rr.HasMeta("field")) { }
            return;
        }
        // 类型序 / 时间序：隐藏分区头，行全局重排
        foreach (var c in kids)
            if (c is Button && c is CanvasItem c1) c1.Visible = false;

        IEnumerable<Control> sorted = kids
            .OfType<HBoxContainer>()
            .Where(r => r.HasMeta("field"))
            .OrderBy(r =>
            {
                var f = r.GetMeta("field").AsString();
                if (mode == V3Sort.Type)
                    return (_rowTypes.TryGetValue(f, out var t) ? t : 9,
                            _rowOrder.TryGetValue(f, out var o) ? o : 9999);
                // Time：改过的按时间倒序，没改过的按原序垫后
                return (_lastEditTime.TryGetValue(f, out var dt) ? 0 : 1,
                        _lastEditTime.TryGetValue(f, out var dt2) ? DateTime.MaxValue.Ticks - dt2.Ticks : (_rowOrder.TryGetValue(f, out var o2) ? o2 : 9999));
            });
        int p2 = 0;
        foreach (var r in sorted) propertyContainer.MoveChild(r, p2++);
    }

    // ===== ⑦ 两级导航：模块卡片 → 浮窗 =====
    private bool _cardMode;
    private V3Window _cardPopup;
    private VBoxContainer _cardBox;
    private string _cardPopupTitle = "";
    private readonly List<(Control row, int origIdx)> _cardStash = new();

    private void SetCardMode(bool on)
    {
        _cardMode = on;
        var kids = propertyContainer.GetChildren();
        if (on)
        {
            // 收起所有分区：只留卡片头
            foreach (var c in kids)
            {
                if (c is Button && c is CanvasItem c2) { c2.Visible = true; }
                else if (c is CanvasItem ci) ci.Visible = false;
            }
        }
        else
        {
            CloseCardPopup();
            foreach (var c in kids) if (c is CanvasItem ci) ci.Visible = true;
            ApplyV3Sort(_v3Sort);
        }
    }

    // 分区头点击分支（由两处 sectionHeader lambda 调用）
    private void OnSectionHeaderPressed(Button head, string title)
    {
        if (_cardMode) OpenCardPopup(head, title);
        else ToggleSectionInline(head, title);
    }

    private void ToggleSectionInline(Button head, string title)
    {
        var kids = propertyContainer.GetChildren();
        int idx = kids.IndexOf(head);
        bool collapsing = head.Text.StartsWith("▼");
        head.Text = collapsing ? $"▶ {title}" : $"▼ {title}";
        for (int k = idx + 1; k < kids.Count; k++)
        {
            var nx = kids[k];
            if (nx is Button b && b.HasMeta("sectionHeader")) break;
            if (nx is CanvasItem ci) ci.Visible = !collapsing;
        }
    }

    private void OpenCardPopup(Button head, string title)
    {
        CloseCardPopup();
        _cardPopupTitle = title;
        if (_cardPopup == null)
        {
            _cardPopup = new V3Window(new Vector2(440, 480));
            _cardBox = new VBoxContainer();
            _cardPopup.Body.AddChild(_cardBox);
            AddChild(_cardPopup);
            _cardPopup.Closed += CloseCardPopup;
        }
        _cardBox.Name = $"card_{title}";
        // 收编该分区的行
        _cardStash.Clear();
        var kids = propertyContainer.GetChildren();
        int idx = kids.IndexOf(head);
        for (int k = idx + 1; k < kids.Count; k++)
        {
            if (kids[k] is Button b && b.HasMeta("sectionHeader")) break;
            if (kids[k] is HBoxContainer row && row.HasMeta("field"))
            {
                _cardStash.Add((row, _rowOrder.TryGetValue(row.GetMeta("field").AsString(), out var o) ? o : k));
                row.Visible = true;
                _cardBox.AddChild(row);
            }
        }
        _cardPopup.Title = title;
        _cardPopup.ShowCentered();
    }

    private void CloseCardPopup()
    {
        if (_cardPopup == null || _cardStash.Count == 0) return;
        foreach (var (row, orig) in _cardStash.OrderBy(s => s.origIdx))
        {
            propertyContainer.AddChild(row);
            propertyContainer.MoveChild(row, Mathf.Min(orig, propertyContainer.GetChildCount() - 1));
            row.Visible = !_cardMode;
        }
        _cardStash.Clear();
    }

    // ===== ⑧ 历史栏目：日志 + 点击回退 =====
    public class V3EditRecord
    {
        public string field, zh, oldS, newS, time;
        public Action undo, redo;
    }
    public readonly List<V3EditRecord> EditJournal = new();
    private int _histCap = 100;   // 10~1000 可调，默认100
    public int HistCap => _histCap;

    public void EnsureHistCap()
    {
        try
        {
            const string p = "user://inspector_histcap.json";
            if (FileAccess.FileExists(p)) _histCap = Mathf.Clamp((int)Json.ParseString(FileAccess.GetFileAsString(p)).AsDouble(), 10, 1000);
        }
        catch { }
    }
    private void SaveHistCap()
    {
        try { FileAccess.Open("user://inspector_histcap.json", FileAccess.ModeFlags.Write).StoreString(Json.Stringify(_histCap)); } catch { }
    }

    public void PushJournal(V3EditRecord rec)
    {
        EditJournal.Add(rec);
        if (EditJournal.Count > _histCap) EditJournal.RemoveAt(0);
    }

    private V3Window _histPopup;
    private ItemList _histList;
    private SpinBox _histCapSpin;

    private void OpenHistoryPopup()
    {
        if (_histPopup == null)
        {
            _histPopup = new V3Window(new Vector2(480, 420));
            var vb = new VBoxContainer();
            var bar = new HBoxContainer();
            var capLabel = new Label { Text = "上限" };
            _histCapSpin = new SpinBox { MinValue = 10, MaxValue = 1000, Step = 10, Value = _histCap };
            _histCapSpin.ValueChanged += v => { _histCap = (int)v; SaveHistCap(); };
            var hint = new Label { Text = "点击条目回退到该次修改之前" };
            bar.AddChild(capLabel); bar.AddChild(_histCapSpin); bar.AddChild(hint);
            vb.AddChild(bar);
            _histList = new ItemList { CustomMinimumSize = new Vector2(460, 400) };
            _histList.ItemActivated += RollbackToEntry;   // 双击回退
            _histList.ItemClicked += (idx, _, _) => RollbackToEntry(idx);
            vb.AddChild(_histList);
            _histPopup.Body.AddChild(vb);
            AddChild(_histPopup);
        }
        _histCapSpin.Value = _histCap;
        _histList.Clear();
        for (int i = EditJournal.Count - 1; i >= 0; i--)
        {
            var r = EditJournal[i];
            _histList.AddItem($"{r.time}  {r.zh} ({r.field}): {r.oldS} → {r.newS}");
        }
        _histPopup.Title = $"历史记录 ({EditJournal.Count}/{_histCap})";
        _histPopup.ShowCentered();
    }

    private void RollbackToEntry(long index)
    {
        // index 是倒序显示位：目标 = EditJournal[Count-1-index]
        int target = EditJournal.Count - 1 - (int)index;
        while (EditJournal.Count > target)
        {
            var top = EditJournal[^1];
            EditJournal.RemoveAt(EditJournal.Count - 1);
            top.undo();
        }
        RefreshUndoButtons();
        if (_histList != null)
        {
            _histList.Clear();
            for (int i = EditJournal.Count - 1; i >= 0; i--)
            {
                var r = EditJournal[i];
                _histList.AddItem($"{r.time}  {r.zh} ({r.field}): {r.oldS} → {r.newS}");
            }
        }
        if (_histPopup != null) _histPopup.Title = $"历史记录 ({EditJournal.Count}/{_histCap})";
        TranslateGroup.SetText(spawnHintLabel, TranslateGroup.T("已撤销上一步修改"));
    }
}
