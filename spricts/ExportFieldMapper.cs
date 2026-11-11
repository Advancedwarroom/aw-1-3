// ExportFieldMapper.cs - 通用 Export 映射：只处理显式列出的字段，未映射字段保持原值。
using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

public static class ExportFieldMapper
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy;

    public static FieldInfo FindExportedField(object target, string fieldName)
    {
        if (target == null || string.IsNullOrWhiteSpace(fieldName)) return null;
        var field = target.GetType().GetField(fieldName.Trim(), Flags);
        if (field == null || field.IsStatic) return null;
        bool exported = field.GetCustomAttributes(true).Any(a => a.GetType().Name == "ExportAttribute");
        return exported ? field : null;
    }

    public static object ReadValue(object target, string fieldName)
    {
        return FindExportedField(target, fieldName)?.GetValue(target);
    }

    public static bool WriteValue(object target, string fieldName, string value)
    {
        var field = FindExportedField(target, fieldName);
        if (field == null) return false;
        try
        {
            object converted = ConvertString(value ?? "", field.FieldType);
            converted = ClampSpecialExportValue(target, field, converted);
            field.SetValue(target, converted);
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Export映射] {target.GetType().Name}.{fieldName} 无法写入“{value}”：{e.Message}");
            return false;
        }
    }

    public static bool AddNumber(object target, string fieldName, float delta)
    {
        var field = FindExportedField(target, fieldName);
        if (field == null) return false;
        try
        {
            object old = field.GetValue(target);
            if (!IsNumeric(field.FieldType)) return false;
            double result = Convert.ToDouble(old, CultureInfo.InvariantCulture) + delta;
            result = ClampSpecialExportNumber(target, field, result);
            field.SetValue(target, ConvertNumber(result, field.FieldType));
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Export映射] {target.GetType().Name}.{fieldName} 无法加减 {delta}：{e.Message}");
            return false;
        }
    }

    // 读取“若再施加 delta 后的值”，但不改实例；供存档反推出模块介入前的底值。
    public static object GetNumberAfterDelta(object target, string fieldName, float delta)
    {
        var field = FindExportedField(target, fieldName);
        if (field == null || !IsNumeric(field.FieldType)) return null;
        object old = field.GetValue(target);
        double result = Convert.ToDouble(old, CultureInfo.InvariantCulture) + delta;
        result = ClampSpecialExportNumber(target, field, result);
        return ConvertNumber(result, field.FieldType);
    }

    public static void ApplyOverrides(object target, Godot.Collections.Dictionary<string, string> overrides)
    {
        if (target == null || overrides == null) return;
        foreach (var pair in overrides) WriteValue(target, pair.Key, pair.Value);
    }

    public static void ApplyNumericDeltas(object target, Godot.Collections.Dictionary<string, float> deltas)
    {
        if (target == null || deltas == null) return;
        foreach (var pair in deltas) AddNumber(target, pair.Key, pair.Value);
    }

    public static Dictionary<string, object> Capture(object target, IEnumerable<string> fieldNames)
    {
        var result = new Dictionary<string, object>();
        if (target == null || fieldNames == null) return result;
        foreach (string name in fieldNames.Distinct())
        {
            var field = FindExportedField(target, name);
            if (field != null && !result.ContainsKey(name)) result[name] = field.GetValue(target);
        }
        return result;
    }

    public static void RestoreCaptured(object target, Dictionary<string, object> captured)
    {
        if (target == null || captured == null) return;
        foreach (var pair in captured)
        {
            var field = FindExportedField(target, pair.Key);
            if (field != null)
                field.SetValue(target, ClampSpecialExportValue(target, field, pair.Value));
        }
    }

    // Weight is a shared push-system stat. Clamp it at the common Export
    // mapping boundary so internal services, empowerment, save restoration, and
    // other reflection-based Export edits cannot leave an out-of-range base value.
    private static object ClampSpecialExportValue(object target, FieldInfo field, object value)
    {
        if (value == null || field == null || field.Name != "weight"
            || !(target is Infantry || target is Weapon || target is Facility))
            return value;
        double numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return (float)ClampSpecialExportNumber(target, field, numeric);
    }

    private static double ClampSpecialExportNumber(object target, FieldInfo field, double value)
    {
        if (field != null && field.Name == "weight"
            && (target is Infantry || target is Weapon || target is Facility))
            return Math.Clamp(value, 0.0, 9999.0);
        return value;
    }

    private static bool IsNumeric(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong)
            || t == typeof(float) || t == typeof(double) || t == typeof(decimal);
    }

    private static object ConvertNumber(double value, Type type)
    {
        if (type == typeof(int)) return Mathf.RoundToInt((float)value);
        if (type == typeof(uint)) return (uint)Math.Max(0, Math.Round(value));
        if (type == typeof(short)) return (short)Math.Round(value);
        if (type == typeof(ushort)) return (ushort)Math.Max(0, Math.Round(value));
        if (type == typeof(byte)) return (byte)Math.Clamp(Math.Round(value), 0, 255);
        if (type == typeof(sbyte)) return (sbyte)Math.Clamp(Math.Round(value), -128, 127);
        if (type == typeof(long)) return (long)Math.Round(value);
        if (type == typeof(ulong)) return (ulong)Math.Max(0, Math.Round(value));
        if (type == typeof(float)) return (float)value;
        if (type == typeof(double)) return value;
        if (type == typeof(decimal)) return (decimal)value;
        return value;
    }

    private static object ConvertString(string text, Type type)
    {
        text = text?.Trim() ?? "";
        if (ExportPersistenceCodec.TryParse(text, type, out object encodedValue)) return encodedValue;
        if (type == typeof(string)) return text;
        if (type == typeof(bool)) return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("是", StringComparison.OrdinalIgnoreCase);
        if (type.IsEnum) return Enum.Parse(type, text, true);
        if (IsNumeric(type)) return ConvertNumber(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture), type);
        if (type == typeof(Vector2I))
        {
            var p = text.Split(','); return new Vector2I(int.Parse(p[0]), int.Parse(p[1]));
        }
        if (type == typeof(Vector2))
        {
            var p = text.Split(','); return new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture));
        }
        if (type == typeof(string[])) return text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        if (type == typeof(Godot.Collections.Array<string>))
        {
            var array = new Godot.Collections.Array<string>();
            foreach (string s in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) array.Add(s.Trim());
            return array;
        }
        if (type == typeof(Godot.Collections.Dictionary<string, int>))
        {
            var dictionary = new Godot.Collections.Dictionary<string, int>();
            foreach (string entry in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = entry.Split('='); if (p.Length == 2) dictionary[p[0].Trim()] = int.Parse(p[1]);
            }
            return dictionary;
        }
        throw new NotSupportedException($"暂不支持 {type.Name}，请使用对应类型的专用映射栏");
    }
}
