using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// TerrainEditor.PropertyEditorV3c.cs - ✅ 开创人2026-11-25: 检查器V3c（权重10/17/18）
// ⑩复制/粘贴配置(剪贴板) ⑰预设模板(命名批量,默认25,0~300可调) ⑱导入导出(文件,与地图同逻辑)
public partial class TerrainEditor
{
    private readonly Dictionary<string, Type> _rowMemberTypes = new();

    // ===== 序列化：当前面板已注册字段 → Godot字典（仅可JSON化值）=====
    private Godot.Collections.Dictionary CollectV3Config()
    {
        var dict = new Godot.Collections.Dictionary();
        foreach (var kv in _rowGetters)
        {
            try
            {
                var v = kv.Value();
                if (v == null) continue;
                if (v is GodotObject) continue;   // 节点/资源引用不可剪贴板
                dict[kv.Key] = Variant.From(v);
            }
            catch { }
        }
        return dict;
    }

    // ===== 类型回魂：剪贴板Variant → 字段真实类型 =====
    private object ConvertV3Value(Variant raw, Type t)
    {
        if (t == typeof(string)) return raw.AsString();
        if (t == typeof(bool)) return raw.AsBool();
        if (t == typeof(int)) return (int)raw.AsInt64();
        if (t == typeof(float)) return (float)raw.AsDouble();
        if (t == typeof(double)) return raw.AsDouble();
        if (t.IsEnum) return Enum.Parse(t, raw.AsString());
        if (typeof(IDictionary).IsAssignableFrom(t) && raw.VariantType == Variant.Type.Dictionary)
        {
            var gd = raw.AsGodotDictionary();
            var sys = (IDictionary)Activator.CreateInstance(t);
            foreach (var kv in gd) sys[kv.Key.ToString()] = Convert.ToInt32(kv.Value.AsInt64());
            return sys;
        }
        return null;   // 其余复杂类型跳过
    }

    // ===== 应用配置：逐字段走原管线(历史/高亮/联动全生效) =====
    private int ApplyV3Config(Godot.Collections.Dictionary cfg)
    {
        int applied = 0;
        foreach (var kv in cfg)
        {
            var name = kv.Key.ToString();
            if (!_rowSetters.TryGetValue(name, out var set) || !_rowGetters.TryGetValue(name, out var get)) continue;
            var t = _rowMemberTypes.TryGetValue(name, out var tt) ? tt : typeof(string);
            object v;
            try { v = ConvertV3Value(kv.Value, t); } catch { continue; }
            if (v == null) continue;
            var oldV = get();
            if (Equals(oldV, v)) continue;
            var undoValue = oldV; var newValue = v;
            var (zn3, _) = ExportLabelDb.Get(name);
            PushJournal(new V3EditRecord
            {
                field = name, zh = string.IsNullOrEmpty(zn3) ? name : zn3,
                oldS = $"{oldV}", newS = $"{newValue}", time = DateTime.Now.ToString("HH:mm:ss"),
                undo = () => set(undoValue), redo = () => set(newValue),
            });
            set(v);
            _lastEditTime[name] = DateTime.Now;
            UpdateV3Highlight(name);
            applied++;
        }
        _editFuture.Clear();
        RefreshUndoButtons();
        RefreshV3Deps();
        return applied;
    }

    // ===== ⑩ 剪贴板复制/粘贴 =====
    public void CopyV3ConfigToClipboard()
    {
        var cfg = CollectV3Config();
        var json = Json.Stringify(cfg);
        DisplayServer.ClipboardSet(json);
        TranslateGroup.SetText(spawnHintLabel, $"📋 已复制 {cfg.Count} 项配置到剪贴板");
    }

    public void PasteV3ConfigFromClipboard()
    {
        string text;
        try { text = DisplayServer.ClipboardGet(); } catch { text = ""; }
        if (string.IsNullOrWhiteSpace(text))
        {
            TranslateGroup.SetText(spawnHintLabel, "剪贴板没有配置内容");
            return;
        }
        Variant parsed;
        try { parsed = Json.ParseString(text); } catch { parsed = default; }
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            TranslateGroup.SetText(spawnHintLabel, "剪贴板内容不是有效配置");
            return;
        }
        int n = ApplyV3Config(parsed.AsGodotDictionary());
        TranslateGroup.SetText(spawnHintLabel, $"📥 已套用 {n} 项配置");
    }

    // ===== ⑰ 预设模板（命名批量，默认25，0~300可调）=====
    private Godot.Collections.Dictionary _v3Presets;   // name → config dict
    private int _presetCap = 25;

    private void EnsureV3Presets()
    {
        if (_v3Presets != null) return;
        _v3Presets = new Godot.Collections.Dictionary();
        try
        {
            if (FileAccess.FileExists("user://inspector_presets.json"))
                _v3Presets = Json.ParseString(FileAccess.GetFileAsString("user://inspector_presets.json")).AsGodotDictionary();
            if (FileAccess.FileExists("user://inspector_presetcap.json"))
                _presetCap = Mathf.Clamp((int)Json.ParseString(FileAccess.GetFileAsString("user://inspector_presetcap.json")).AsDouble(), 0, 300);
        }
        catch { }
    }
    private void SaveV3Presets()
    {
        try { FileAccess.Open("user://inspector_presets.json", FileAccess.ModeFlags.Write).StoreString(Json.Stringify(_v3Presets)); } catch { }
    }

    private PopupPanel _presetPopup;
    private ItemList _presetList;
    private LineEdit _presetNameEdit;

    private void OpenPresetPopup()
    {
        EnsureV3Presets();
        if (_presetPopup == null)
        {
            _presetPopup = new PopupPanel();
            var vb = new VBoxContainer();
            var bar = new HBoxContainer();
            var capSpin = new SpinBox { MinValue = 0, MaxValue = 300, Step = 5, Value = _presetCap, TooltipText = "最大存档数(0=关闭)" };
            capSpin.ValueChanged += v => { _presetCap = (int)v; try { FileAccess.Open("user://inspector_presetcap.json", FileAccess.ModeFlags.Write).StoreString(Json.Stringify(_presetCap)); } catch { } };
            bar.AddChild(new Label { Text = "上限" }); bar.AddChild(capSpin);
            vb.AddChild(bar);

            _presetList = new ItemList { CustomMinimumSize = new Vector2(420, 260) };
            _presetList.ItemActivated += i => ApplyPresetAt(i);
            vb.AddChild(_presetList);

            var bar2 = new HBoxContainer();
            _presetNameEdit = new LineEdit { PlaceholderText = "模板名…", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var saveBtn = new Button { Text = "💾存为模板" };
            saveBtn.Pressed += SaveCurrentAsPreset;
            var applyBtn = new Button { Text = "套用" };
            applyBtn.Pressed += () => { if (_presetList.GetSelectedItems().Length > 0) ApplyPresetAt(_presetList.GetSelectedItems()[0]); };
            var delBtn = new Button { Text = "删除" };
            delBtn.Pressed += DeleteSelectedPreset;
            bar2.AddChild(_presetNameEdit); bar2.AddChild(saveBtn); bar2.AddChild(applyBtn); bar2.AddChild(delBtn);
            vb.AddChild(bar2);

            var bar3 = new HBoxContainer();
            var expBtn = new Button { Text = "📤导出全部" };
            expBtn.Pressed += ExportPresetsToFile;
            var impBtn = new Button { Text = "📥导入" };
            impBtn.Pressed += ImportPresetsFromFile;
            bar3.AddChild(expBtn); bar3.AddChild(impBtn);
            vb.AddChild(bar3);

            _presetPopup.AddChild(vb);
            AddChild(_presetPopup);
        }
        RefreshPresetList();
        _presetPopup.Title = $"预设模板 ({_v3Presets.Count}/{_presetCap})";
        _presetPopup.PopupCentered();
    }

    private void RefreshPresetList()
    {
        _presetList.Clear();
        foreach (var kv in _v3Presets)
            _presetList.AddItem($"{kv.Key}  ({kv.Value.AsGodotDictionary().Count}项)");
    }

    private void SaveCurrentAsPreset()
    {
        var name = _presetNameEdit.Text.Trim();
        if (string.IsNullOrEmpty(name)) { TranslateGroup.SetText(spawnHintLabel, "先给模板起个名"); return; }
        EnsureV3Presets();
        if (!_v3Presets.ContainsKey(name) && _v3Presets.Count >= _presetCap)
        {
            TranslateGroup.SetText(spawnHintLabel, $"模板已满({_presetCap})——调高上限或删旧的");
            return;
        }
        _v3Presets[name] = CollectV3Config();
        SaveV3Presets();
        RefreshPresetList();
        _presetPopup.Title = $"预设模板 ({_v3Presets.Count}/{_presetCap})";
        TranslateGroup.SetText(spawnHintLabel, $"💾 已存模板「{name}」");
    }

    private void ApplyPresetAt(long idx)
    {
        var name = _v3Presets.Keys.ElementAt((int)idx).ToString();
        int n = ApplyV3Config(_v3Presets[name].AsGodotDictionary());
        TranslateGroup.SetText(spawnHintLabel, $"已套用模板「{name}」({n}项)");
    }

    private void DeleteSelectedPreset()
    {
        var sel = _presetList.GetSelectedItems();
        if (sel.Length == 0) return;
        var name = _v3Presets.Keys.ElementAt(sel[0]).ToString();
        _v3Presets.Remove(name);
        SaveV3Presets();
        RefreshPresetList();
    }

    // ===== ⑱ 导入/导出（文件，与地图同逻辑：FileDialog走文件系统）=====
    private FileDialog _presetFileDlg;
    private bool _presetDlgImport;

    private void ExportPresetsToFile()
    {
        EnsureV3Presets();
        EnsurePresetDlg();
        _presetDlgImport = false;
        _presetFileDlg.FileMode = FileDialog.FileModeEnum.SaveFile;
        _presetFileDlg.CurrentFile = "unit_presets.json";
        _presetFileDlg.PopupCentered();
    }

    private void ImportPresetsFromFile()
    {
        EnsureV3Presets();
        EnsurePresetDlg();
        _presetDlgImport = true;
        _presetFileDlg.FileMode = FileDialog.FileModeEnum.OpenFile;
        _presetFileDlg.PopupCentered();
    }

    private void EnsurePresetDlg()
    {
        if (_presetFileDlg != null) return;
        _presetFileDlg = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Filters = new string[] { "*.json ; 单位配置包" },
            Size = new Vector2I(760, 540),
        };
        _presetFileDlg.FileSelected += OnPresetFileSelected;
        AddChild(_presetFileDlg);
    }

    private void OnPresetFileSelected(string path)
    {
        if (_presetDlgImport)
        {
            try
            {
                var parsed = Json.ParseString(FileAccess.GetFileAsString(path));
                if (parsed.VariantType == Variant.Type.Dictionary)
                {
                    int added = 0;
                    foreach (var kv in parsed.AsGodotDictionary())
                    {
                        if (!_v3Presets.ContainsKey(kv.Key.ToString()) && _v3Presets.Count >= _presetCap) break;
                        _v3Presets[kv.Key.ToString()] = kv.Value;
                        added++;
                    }
                    SaveV3Presets();
                    if (_presetList != null) RefreshPresetList();
                    TranslateGroup.SetText(spawnHintLabel, $"📥 已导入 {added} 个模板");
                }
            }
            catch { TranslateGroup.SetText(spawnHintLabel, "导入失败：文件无效"); }
        }
        else
        {
            try
            {
                FileAccess.Open(path, FileAccess.ModeFlags.Write).StoreString(Json.Stringify(_v3Presets));
                TranslateGroup.SetText(spawnHintLabel, $"📤 已导出 {_v3Presets.Count} 个模板");
            }
            catch { TranslateGroup.SetText(spawnHintLabel, "导出失败"); }
        }
    }
}
