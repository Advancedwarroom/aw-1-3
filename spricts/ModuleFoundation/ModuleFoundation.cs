using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;

// ModuleFoundation.cs - 模块通用化基座：所有可通用模块（准备/冷却/强化/天气/CO…）共享的地基。
// ① Mods.FindModule<T>：模块节点查找（挂谁家服务谁；找不到=全场放行=零入侵）
// ② FieldMods：反射修改宿主任意 public Export 字段（加/减/固定值、字符串/枚举、临时效果到期自动还原栈）
//    准备模块的"?"事件第一个用它；以后任何模块想改任意 Export 字段直接调 FieldMods，不各造反射。
public static class Mods
{
    public static T FindModule<T>(Node host) where T : Node
    {
        if (host == null || !GodotObject.IsInstanceValid(host)) return null;
        var m = host.GetNodeOrNull<T>("PrepModule");            // 约定名优先
        if (m == null) m = host.FindChild("*PrepModule*", true, false) as T;
        return m;
    }
}

/// <summary>一条已生效的临时字段修改（到期还原）</summary>
public class TempFieldEffect
{
    public string fieldName;
    public object oldValue;
    public int turnsLeft;
    public PrepEventConfig src;   // 来源事件（清理判定按它的勾选走）
}

public static class FieldMods
{
    /// <summary>反射改字段；permanent=false 时压入 tempStack 由调用方逐回合 tick 还原。返回是否真改。</summary>
    public static bool Apply(Node entity, string fieldName, PrepModifyMode mode, float value, string stringValue,
                             bool permanent, int durationTurns, PrepEventConfig src, List<TempFieldEffect> tempStack)
    {
        if (entity == null || string.IsNullOrEmpty(fieldName)) return false;
        var f = entity.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (f == null) return false;                          // 字段不存在=默认不调用
        object old;
        try { old = f.GetValue(entity); } catch { return false; }
        object newVal = old;
        try
        {
            if (!string.IsNullOrEmpty(stringValue) && (f.FieldType == typeof(string) || f.FieldType.IsEnum))
                newVal = f.FieldType.IsEnum ? Enum.Parse(f.FieldType, stringValue) : (object)stringValue;
            else if (value != 0 || mode == PrepModifyMode.SetFixed)
            {
                double cur = 0;
                try { cur = Convert.ToDouble(old); } catch { }
                double v = mode == PrepModifyMode.Add ? cur + value
                         : mode == PrepModifyMode.Subtract ? cur - value
                         : value;
                newVal = Convert.ChangeType(v, f.FieldType);
            }
            f.SetValue(entity, newVal);
        }
        catch { return false; }                               // 类型转换失败=静默不调用
        if (!permanent && tempStack != null)
            tempStack.Add(new TempFieldEffect { fieldName = fieldName, oldValue = old, turnsLeft = Mathf.Max(1, durationTurns), src = src });
        return true;
    }

    /// <summary>回合推进：临时效果到期还原。返回被还原的条数。</summary>
    public static int TickAndRestore(Node entity, List<TempFieldEffect> tempStack)
    {
        int restored = 0;
        if (tempStack == null) return 0;
        for (int i = tempStack.Count - 1; i >= 0; i--)
        {
            var e = tempStack[i];
            if (--e.turnsLeft <= 0)
            {
                RestoreOne(entity, e);
                tempStack.RemoveAt(i);
                restored++;
            }
        }
        return restored;
    }

    public static void RestoreOne(Node entity, TempFieldEffect e)
    {
        if (e?.oldValue == null) return;
        var f = entity?.GetType().GetField(e.fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (f != null && (f.FieldType.IsInstanceOfType(e.oldValue) || f.FieldType == typeof(object)))
        {
            try { f.SetValue(entity, e.oldValue); } catch { }
        }
    }
}
