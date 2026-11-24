using Godot;
using System.Collections.Generic;

// ✅ 开创人2026-11-24: Export字段→中文源词"索引"——多语言翻译正主是TranslateGroup.T(源词)
// 本文件只做字段名→源词映射；七语词条在TranslateGroup.Dictionary.cs(手写优先槽)，随语言切换自动翻译
// 数据文件: res://asscets/EditorData/export_labels.zh.json —— {"字段名": {"term":"中文源词","desc":"一句话说明(也是源词)"}}
public static class ExportLabelDb
{
    private static Dictionary<string, (string term, string desc)> _map;
    private static bool _loaded;

    private static void Ensure()
    {
        if (_loaded) return;
        _loaded = true;
        _map = new Dictionary<string, (string, string)>();
        try
        {
            const string path = "res://asscets/EditorData/export_labels.zh.json";
            if (FileAccess.FileExists(path))
            {
                using var f = FileAccess.Open(path, "res://asscets/EditorData/export_labels.zh.json".Length > 0 ? FileAccess.ModeFlags.Read : FileAccess.ModeFlags.Read);
                var parsed = Json.ParseString(f.GetAsText()).AsGodotDictionary();
                foreach (var kv in parsed)
                {
                    var d = kv.Value.AsGodotDictionary();
                    _map[kv.Key.ToString()] = (
                        d.ContainsKey("term") ? d["term"].ToString() : "",
                        d.ContainsKey("desc") ? d["desc"].ToString() : "");
                }
            }
        }
        catch
        {
            // 映射缺失时静默回退英文原名
        }
    }

    public static (string term, string desc) Get(string fieldName)
    {
        Ensure();
        return _map.TryGetValue(fieldName, out var v) ? v : (null, null);
    }
}
