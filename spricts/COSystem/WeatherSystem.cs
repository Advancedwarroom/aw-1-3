// WeatherSystem.cs - ✅ 天气系统（CO 模块体系·第 15 号）
// 数值对照 CoW resources/scripts/gamerules/weather/*：
//   晴 Sun      70% 默认出现率，无效果
//   雨 Rain     视野-1，强制开启战争迷雾（雨停恢复原设置），10%
//   雪 Snow     移动消耗+1（道路/桥/轨道/传送门/建筑格与空军除外），10%
//   沙暴 Sandstorm 攻击-15%、射程-1，10%
//   雾 Mist     视野-2，强制开启战争迷雾（雾散恢复原设置），默认 0%（仅规则/CO 指定）
// 每"天"（全员回合轮回一次）按出现率随机轮换；也可由 CO 技能指定（模块15）。
// 模块15（天气/战争迷雾）：weatherImmune=1 的 CO 势力单位不受天气惩罚。
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum WeatherType { Sun = 0, Rain = 1, Snow = 2, Sandstorm = 3, Mist = 4 }

public static class WeatherSystem
{
    /// <summary>天气免疫: 模块15(CO技能)weatherImmune=1 的势力单位不受天气惩罚(文件头设计注释)</summary>
    private static bool IsImmune(Infantry unit) => CoModuleSystem.GetWeatherImmune(unit);

    static WeatherType current = WeatherType.Sun;
    static int daysRemaining = 0;            // 当前天气剩余天数（0=未启用天气轮换）
    static bool randomWeather = true;        // 是否每日随机轮换
    static bool fogForced = false;           // 天气正在强制开雾
    static bool fogStateBeforeForce = true;  // 强制前玩家的雾战设置

    // 出现率（晴/雨/雪/沙暴/雾）——对照 CoW 默认 70/10/10/10/0
    public static int[] Chances = { 70, 10, 10, 10, 0 };
    public static int RandomDurationMin = 1;  // 随机天气最短持续天数
    public static int RandomDurationMax = 2;

    public static WeatherType Current => current;
    public static int DaysRemaining => daysRemaining;
    public static bool RandomWeather => randomWeather;
    /// <summary>天气变化时触发（UI 刷新订阅）</summary>
    public static event Action OnWeatherChanged;

    static FogOfWarManager Fog => (Engine.GetMainLoop() as SceneTree)?
        .GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;

    public static string WeatherName(WeatherType w) => w switch
    {
        WeatherType.Rain => "雨",
        WeatherType.Snow => "雪",
        WeatherType.Sandstorm => "沙暴",
        WeatherType.Mist => "雾",
        _ => "晴",
    };

    // ---------- 轮换 ----------
    /// <summary>新的一天（全员回合轮回完一次时由 GameManager 调用）</summary>
    public static void AdvanceDay()
    {
        if (daysRemaining > 0 && --daysRemaining <= 0)
            SetWeather(WeatherType.Sun, 0, forceRestoreFog: true);   // 天气结束恢复晴天
        if (daysRemaining == 0 && randomWeather)
        {
            // 按出现率摇新天气
            int roll = new Random().Next(100);
            int acc = 0;
            var w = WeatherType.Sun;
            for (int i = 0; i < Chances.Length; i++)
            {
                acc += Chances[i];
                if (roll < acc) { w = (WeatherType)i; break; }
            }
            if (w != WeatherType.Sun)
                SetWeather(w, new Random().Next(RandomDurationMin, RandomDurationMax + 1));
        }
    }

    /// <summary>设置天气（CO 技能/规则/调试用）。turns<=0 且非 Sun 表示持续到再次改变</summary>
    public static void SetWeather(WeatherType w, int days = 1, bool forceRestoreFog = false)
    {
        bool changed = current != w;
        current = w;
        daysRemaining = w == WeatherType.Sun ? 0 : Math.Max(days, 1);

        var fog = Fog;
        if (fog != null)
        {
            if (w is WeatherType.Rain or WeatherType.Mist && !fog.isFogOfWarEnabled)
            {
                // ✅ 雨/雾强制开雾（对照 CoW weather_rain.activate），记录原设置
                if (!fogForced)
                {
                    fogStateBeforeForce = fog.isFogOfWarEnabled;
                    fogForced = true;
                }
                fog.isFogOfWarEnabled = true;
                fog.OnUnitMoved();
            }
            else if (fogForced && (w is not (WeatherType.Rain or WeatherType.Mist) || forceRestoreFog))
            {
                // 天气过去：恢复原雾战设置
                fog.isFogOfWarEnabled = fogStateBeforeForce;
                fogForced = false;
                fog.OnUnitMoved();
            }
        }

        if (changed) OnWeatherChanged?.Invoke();
    }

    public static void SetRandomWeather(bool enabled) => randomWeather = enabled;

    // ---------- 惩罚查询（模块15 免疫在此统一过滤）----------
    // ✅ issue#1 区域天气融合：查询一律走 EffectiveAt(cell)——区域天气层优先，无层回退全球 Current。
    // 免疫（weatherImmune CO）对全球+区域天气一视同仁。

    /// <summary>某格的有效天气：区域天气层优先（WeatherManager 格子栈），无层回退全球 Current</summary>
    public static WeatherType EffectiveAt(Vector2I cell)
        => WeatherManager.GetRegionalWeatherAt(cell) ?? current;

    // ✅ issue#1 区域天气·多层聚合接管：≥2层 或 含自定义类型(>4) 时，
    // 内建单层已由 GetRegionalWeatherAt 桥接枚举覆盖；多层/自定义改走 WeatherManager 聚合（纯区域各层乘区/增量）
    static bool RegionalAggregateTakesOver(Vector2I cell, out WeatherManager wm)
    {
        wm = WeatherManager.Instance;
        if (wm == null || !wm.localWeatherEnabled) { wm = null; return false; }
        var layers = wm.GetLayersAt(cell);
        if (layers == null || layers.Count == 0) { wm = null; return false; }
        if (layers.Count == 1 && layers[0].typeId >= 0 && layers[0].typeId <= 4) { wm = null; return false; }
        return true;
    }

    /// <summary>视野修正：雨-1 / 雾-2（按观察单位所在格）。区域天气层不受 CO 技能/免疫影响（2026-11-07 拍板）；全球天气听 CO</summary>
    public static int GetVisionModifier(Infantry unit)
    {
        if (unit?.grid != null)
        {
            if (RegionalAggregateTakesOver(unit.grid.GridIndex, out var wm)) return wm.GetVisionDelta(unit.grid.GridIndex);   // 多层/自定义区域层：免疫不适用
            var rw = WeatherManager.GetRegionalWeatherAt(unit.grid.GridIndex);
            if (rw != null) return rw switch   // 单层内建区域层（含显式转晴）：免疫不适用，且屏蔽全球天气
            {
                WeatherType.Rain => -1,
                WeatherType.Mist => -2,
                _ => 0,
            };
        }
        if (IsImmune(unit)) return 0;
        return current switch
        {
            WeatherType.Rain => -1,
            WeatherType.Mist => -2,
            _ => 0,
        };
    }

    /// <summary>移动消耗修正：雪+1（按目标格；道路/桥/轨道/传送门/建筑格与空军除外）。区域天气层不受 CO 技能/免疫影响；全球天气听 CO</summary>
    public static int GetMovementCostModifier(Infantry unit, Grids g)
    {
        if (g != null)
        {
            if (RegionalAggregateTakesOver(g.GridIndex, out var wm)) return wm.GetMoveCostDelta(g.GridIndex);   // 多层/自定义区域层（调用点 Mathf.Max(1,…) 保底）
            var rw = WeatherManager.GetRegionalWeatherAt(g.GridIndex);
            if (rw.HasValue)   // 单层内建区域层（含显式转晴）：免疫不适用，豁免表照跑，且屏蔽全球天气
            {
                if (rw.Value != WeatherType.Snow) return 0;
                if (unit is { IsAirUnit: true }) return 0;
                if (g.city != null) return 0;   // 建筑格
                return ExemptSnowTerrain(g.gridType) ? 0 : 1;
            }
        }
        if (IsImmune(unit)) return 0;
        if (current != WeatherType.Snow) return 0;
        if (unit is { IsAirUnit: true }) return 0;
        if (g == null) return 0;
        if (g.city != null) return 0;   // 建筑格
        return ExemptSnowTerrain(g.gridType) ? 0 : 1;
    }

    static readonly HashSet<GridType> SnowExempt = new()
    {
        GridType.ROAD, GridType.DESERT_ROAD, GridType.SNOW_ROAD, GridType.WASTE_ROAD,
        GridType.BRIDGE, GridType.LAVABRIDGE,
        GridType.TRACK, GridType.STATION, GridType.BROKENTRACK,
        GridType.TP, GridType.PASSABLEPIPE, GridType.OVERPASS, GridType.SHIPGATE,
    };
    static bool ExemptSnowTerrain(GridType t) => SnowExempt.Contains(t);

    /// <summary>攻击百分比修正：沙暴-15%（按攻击单位所在格）。区域天气层不受 CO 技能/免疫影响；全球天气听 CO</summary>
    public static float GetOffenseModifierPercent(Infantry unit)
    {
        if (unit?.grid != null)
        {
            if (RegionalAggregateTakesOver(unit.grid.GridIndex, out var wm)) return (wm.GetAttackPercent(unit.grid.GridIndex) - 1000) / 10f;   // 多层/自定义区域层（千分制→百分制）
            var rw = WeatherManager.GetRegionalWeatherAt(unit.grid.GridIndex);
            if (rw.HasValue) return rw.Value == WeatherType.Sandstorm ? -15f : 0f;   // 单层内建区域层（含显式转晴）：免疫不适用，且屏蔽全球天气
        }
        if (IsImmune(unit)) return 0f;
        return current != WeatherType.Sandstorm ? 0f : -15f;
    }

    /// <summary>射程修正：沙暴-1（按攻击单位所在格）。区域天气层不受 CO 技能/免疫影响；全球天气听 CO</summary>
    public static int GetFireRangeModifier(Infantry unit)
    {
        if (unit?.grid != null)
        {
            if (RegionalAggregateTakesOver(unit.grid.GridIndex, out var wm)) return wm.GetFireRangeDelta(unit.grid.GridIndex);   // 多层/自定义区域层
            var rw = WeatherManager.GetRegionalWeatherAt(unit.grid.GridIndex);
            if (rw.HasValue) return rw.Value == WeatherType.Sandstorm ? -1 : 0;   // 单层内建区域层（含显式转晴）：免疫不适用，且屏蔽全球天气
        }
        if (IsImmune(unit)) return 0;
        return current != WeatherType.Sandstorm ? 0 : -1;
    }
}