using Godot;
using System.Collections.Generic;

// ✅ 开创人2026-11-24: Export字段中文字典——属性面板渲染中文名+悬浮说明
// 数据文件: res://asscets/EditorData/export_labels.zh.json —— 随时手动增补，格式 {"字段名": {"name":"中文名","desc":"一句话说明"}}
public static class ExportLabelDb
{
    private static Dictionary<string, (string zh, string desc)> _map;
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
                using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
                var parsed = Json.ParseString(f.GetAsText()).AsGodotDictionary();
                foreach (var kv in parsed)
                {
                    var d = kv.Value.AsGodotDictionary();
                    _map[kv.Key.ToString()] = (
                        d.ContainsKey("name") ? d["name"].ToString() : "",
                        d.ContainsKey("desc") ? d["desc"].ToString() : "");
                }
            }
        }
        catch
        {
            // 字典缺失或损坏时静默回退英文原名
        }
    }

    public static (string zh, string desc) Get(string fieldName)
    {
        Ensure();
        return _map.TryGetValue(fieldName, out var v) ? v : (null, null);
    }
}
