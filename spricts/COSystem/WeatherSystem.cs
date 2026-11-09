// WeatherSystem.cs - 全球天气与区域天气统一读取 WeatherManager 注册表。
using Godot;
using System;
using System.Collections.Generic;

public enum WeatherType { Sun = 0, Rain = 1, Snow = 2, Sandstorm = 3, Mist = 4 }

public static class WeatherSystem
{
    private static bool IsImmune(Infantry unit) => CoModuleSystem.GetWeatherImmune(unit);
    private static WeatherType current = WeatherType.Sun;
    private static int daysRemaining = 0;
    private static bool randomWeather = true;
    private static bool fogForced = false;
    private static bool fogStateBeforeForce = true;

    public static int[] Chances = { 70, 10, 10, 10, 0 };
    public static int RandomDurationMin = 1;
    public static int RandomDurationMax = 2;
    public static WeatherType Current => current;
    public static int DaysRemaining => daysRemaining;
    public static bool RandomWeather => randomWeather;
    public static event Action OnWeatherChanged;

    private static FogOfWarManager Fog => (Engine.GetMainLoop() as SceneTree)?
        .GetFirstNodeInGroup("fog_of_war_manager") as FogOfWarManager;

    private static RegionalWeatherType GlobalDefinition
        => WeatherManager.Instance?.GetRegionalWeatherType((int)current);

    public static string WeatherName(WeatherType w)
        => WeatherManager.Instance?.GetRegionalWeatherType((int)w)?.name ?? w.ToString();

    public static void AdvanceDay()
    {
        if (daysRemaining > 0 && --daysRemaining <= 0)
            SetWeather(WeatherType.Sun, 0, forceRestoreFog: true);
        if (daysRemaining == 0 && randomWeather)
        {
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

    public static void SetWeather(WeatherType w, int days = 1, bool forceRestoreFog = false)
    {
        bool changed = current != w;
        current = w;
        daysRemaining = w == WeatherType.Sun ? 0 : Math.Max(days, 1);
        var fog = Fog;
        if (fog != null)
        {
            bool forceFog = WeatherManager.Instance?.GetRegionalWeatherType((int)w)?.forceFog ?? false;
            if (forceFog && !fog.isFogOfWarEnabled)
            {
                if (!fogForced)
                {
                    fogStateBeforeForce = fog.isFogOfWarEnabled;
                    fogForced = true;
                }
                fog.isFogOfWarEnabled = true;
                fog.OnUnitMoved();
            }
            else if (fogForced && (!forceFog || forceRestoreFog))
            {
                fog.isFogOfWarEnabled = fogStateBeforeForce;
                fogForced = false;
                fog.OnUnitMoved();
            }
        }
        if (changed) OnWeatherChanged?.Invoke();
    }

    public static void SetRandomWeather(bool enabled) => randomWeather = enabled;

    // 保留给现有 UI/查询调用；实际战斗效果不再通过枚举 switch 计算。
    public static WeatherType EffectiveAt(Vector2I cell)
        => WeatherManager.GetRegionalWeatherAt(cell) ?? current;

    private static bool TryGetRegional(Vector2I cell, out WeatherManager wm)
    {
        wm = WeatherManager.Instance;
        if (wm == null || !wm.localWeatherEnabled || wm.GetLayersAt(cell).Count == 0)
        {
            wm = null;
            return false;
        }
        return true; // 有任意区域层即屏蔽全球天气，且区域层不吃 CO 天气免疫。
    }

    public static int GetVisionModifier(Infantry unit)
    {
        if (unit?.grid != null && TryGetRegional(unit.grid.GridIndex, out var wm))
            return wm.GetVisionDelta(unit.grid.GridIndex);
        if (IsImmune(unit)) return 0;
        return GlobalDefinition?.visionDelta ?? 0;
    }

    private static readonly HashSet<GridType> SnowExempt = new()
    {
        GridType.ROAD, GridType.DESERT_ROAD, GridType.SNOW_ROAD, GridType.WASTE_ROAD,
        GridType.BRIDGE, GridType.LAVABRIDGE, GridType.TRACK, GridType.STATION,
        GridType.BROKENTRACK, GridType.TP, GridType.PASSABLEPIPE, GridType.OVERPASS,
        GridType.SHIPGATE,
    };

    private static int MoveCostFromDefinition(RegionalWeatherType type, Infantry unit, Grids grid)
    {
        if (type == null) return 0;
        if (type.id != (int)WeatherType.Snow) return type.moveCostDelta;
        if (unit is { IsAirUnit: true } || grid == null || grid.city != null || SnowExempt.Contains(grid.gridType)) return 0;
        return type.moveCostDelta;
    }

    public static int GetMovementCostModifier(Infantry unit, Grids grid)
    {
        if (grid != null && TryGetRegional(grid.GridIndex, out var wm))
        {
            int delta = 0;
            foreach (var layer in wm.GetLayersAt(grid.GridIndex))
                delta += MoveCostFromDefinition(wm.GetRegionalWeatherType(layer.typeId), unit, grid);
            return delta;
        }
        if (IsImmune(unit)) return 0;
        return MoveCostFromDefinition(GlobalDefinition, unit, grid);
    }

    public static float GetOffenseModifierPercent(Infantry unit)
    {
        if (unit?.grid != null && TryGetRegional(unit.grid.GridIndex, out var wm))
            return (wm.GetAttackPercent(unit.grid.GridIndex) - 1000) / 10f;
        if (IsImmune(unit)) return 0f;
        return ((GlobalDefinition?.attackPercent ?? 1000) - 1000) / 10f;
    }

    public static int GetFireRangeModifier(Infantry unit)
    {
        if (unit?.grid != null && TryGetRegional(unit.grid.GridIndex, out var wm))
            return wm.GetFireRangeDelta(unit.grid.GridIndex);
        if (IsImmune(unit)) return 0;
        return GlobalDefinition?.fireRangeDelta ?? 0;
    }
}
