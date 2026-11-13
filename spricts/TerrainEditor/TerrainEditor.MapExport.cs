using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · MapExport（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ✅ 导出地图配置为 txt（增强版：全图、价值统计、真实资金、Test差异、迷雾状态）
    private void ExportMapToTxt()
    {
        if (gridManager?.map == null)
        {
            return;
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var grid in gridManager.grids)
        {
            if (grid == null) continue;
            minX = System.Math.Min(minX, grid.GridIndex.X);
            minY = System.Math.Min(minY, grid.GridIndex.Y);
            maxX = System.Math.Max(maxX, grid.GridIndex.X);
            maxY = System.Math.Max(maxY, grid.GridIndex.Y);
        }
        if (minX == int.MaxValue) { minX = 0; minY = 0; maxX = 0; maxY = 0; }
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== AW-1-3 Map Export ===");
        sb.AppendLine($"Width: {width}");
        sb.AppendLine($"Height: {height}");
        int _esc = gridManager?.GetScreenCount() ?? 1;
        sb.AppendLine($"Screens: {_esc}");
        if (gridManager?.screenWidths != null && gridManager.screenWidths.Length == _esc)
            sb.AppendLine($"ScreenW: {string.Join(",", gridManager.screenWidths)}");
        if (gridManager?.screenHeights != null && gridManager.screenHeights.Length == _esc)
            sb.AppendLine($"ScreenH: {string.Join(",", gridManager.screenHeights)}");
        if (gridManager?.screenYOffsets != null && gridManager.screenYOffsets.Length == _esc)
            sb.AppendLine($"ScreenY: {string.Join(",", gridManager.screenYOffsets)}");
        sb.AppendLine($"ExportTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        bool fogEnabled = gameManager?.fogOfWarManager?.isFogOfWarEnabled ?? false;
        sb.AppendLine($"FogOfWar: {(fogEnabled ? "Enabled" : "Disabled")}");
        // ✅ 天气系统（模块15）：当前天气+剩余天数、出现权重、随机开关、持续天数随图导出
        sb.AppendLine($"Weather: {WeatherSystem.Current},{WeatherSystem.DaysRemaining}");
        sb.AppendLine($"WeatherChances: {string.Join(",", WeatherSystem.Chances)}");
        sb.AppendLine($"WeatherRandom: {(WeatherSystem.RandomWeather ? "On" : "Off")}");
        sb.AppendLine($"WeatherDuration: {WeatherSystem.RandomDurationMin}-{WeatherSystem.RandomDurationMax}");
        // ✅ issue#1 区域天气：总开关+基底+格子栈随图导出（WeatherStacks 行为 x,y,typeId,turns,replaceSunny;... ）
        var wmExport = (Engine.GetMainLoop() as SceneTree)?.GetFirstNodeInGroup("weather_manager") as WeatherManager;
        sb.AppendLine($"LocalWeather: {((wmExport != null && wmExport.localWeatherEnabled) ? "On" : "Off")}");
        sb.AppendLine($"WeatherStacks: {wmExport?.SerializeStacks() ?? ""}");
        // ✅ issue#1 随机屏映射（开创人2026-11-08：随图导出，多屏图导回不再丢链接设置）
        sb.AppendLine($"RandomLinks: {(gridManager.randomScreenLinks ? "On" : "Off")}");
        sb.AppendLine($"RandomLinkSeed: {gridManager.randomLinkSeed}");
        sb.AppendLine($"RandomLinksPerPair: {gridManager.randomLinksPerPair}");
        sb.AppendLine($"RandomLinkInnerOnly: {(gridManager.randomLinkInnerOnly ? "On" : "Off")}");
        // ✅ 开创人2026-11-14：屏间隔 + 截停地形清单随行（此前不导，恒默认值；哪天开放编辑即成雷，先写死）
        sb.AppendLine($"ScreenGap: {gridManager?.screenGap ?? 3}");
        sb.AppendLine($"CannotStop: {(gridManager != null && gridManager.cannotStopTerrainTypes != null ? string.Join(",", gridManager.cannotStopTerrainTypes.Select(t => t.ToString())) : "")}");
        // ✅ 自定义移动/地形类型注册表随图导出（开创人2026-11-08：剪贴板分享的图跨设备可玩）
        var esExport = (Engine.GetMainLoop() as SceneTree)?.GetFirstNodeInGroup("extra_save") as ExtraSave;
        if (esExport != null)
        {
            foreach (var mt in esExport.CustomMoveTypes.Values)
            {
                var mc = string.Join(",", mt.moveCosts.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                sb.AppendLine($"CustomMoveType: {mt.id}|{mt.displayName}|{mc}|{mt.customTexturePath}|{(mt.useCustomTexture ? 1 : 0)}");
            }
            foreach (var tt in esExport.CustomTerrainTypes.Values)
            {
                var tc = string.Join(",", tt.moveCosts.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                sb.AppendLine($"CustomTerrainType: {tt.id}|{tt.displayName}|{tc}|{tt.defenseBonus.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{tt.terrainColorHex}|{tt.customTexturePath}|{(tt.useCustomTexture ? 1 : 0)}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== Terrain Matrix [x,y]=Type ===");
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Grids grid = null;
                if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
                    grid = gridManager.map[x, y];
                if (grid != null)
                    sb.Append($"[{x},{y}]={grid.gridType}  ");
                else
                    sb.Append($"[{x},{y}]=NULL  ");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("=== Units [x,y] Type Team HP ===");
        if (gameManager?.unitManager?.AllUnits != null)
        {
            foreach (var unit in gameManager.unitManager.AllUnits)
            {
                if (unit != null && IsInstanceValid(unit) && unit.grid != null)
                {
                    int hx = unit.grid.GridIndex.X;
                    int hy = unit.grid.GridIndex.Y;
                    string typeName = unit.GetType().Name;
                    int hp = Mathf.CeilToInt(unit.health / 10f);
                    string diff = GetUnitDiffString(unit);
                    // ✅ 开创人2026-11-14：HP 非整十时追加 RAW 精确血量（Ceil 向上取整会吞 0.1 粒度）
                    string rawTag = (unit.health % 10 != 0) ? $" RAW{unit.health}" : "";
                    sb.AppendLine($"[{hx},{hy}] {typeName} {unit.team} HP{hp}{rawTag}{diff}");
                    // ✅ 开创人2026-11-14：载员随行（此前 transportedUnits 里的兵 grid=null 且不在 AllUnits，导出即蒸发）
                    if (unit.transportedUnits != null && unit.transportedUnits.Count > 0)
                    {
                        foreach (var cargo in unit.transportedUnits)
                        {
                            if (cargo == null || !IsInstanceValid(cargo)) continue;
                            string cDiff = GetUnitDiffString(cargo);
                            int chp = Mathf.CeilToInt(cargo.health / 10f);
                            string cRaw = (cargo.health % 10 != 0) ? $" RAW{cargo.health}" : "";
                            sb.AppendLine($"> [{hx},{hy}] {cargo.GetType().Name} {cargo.team} HP{chp}{cRaw}{cDiff}");
                        }
                    }
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== Weapons [x,y] Type Team HP ===");
        if (gameManager?.weaponManager?.AllWeapons != null)
        {
            foreach (var weapon in gameManager.weaponManager.AllWeapons)
            {
                if (weapon != null && IsInstanceValid(weapon) && weapon.grid != null)
                {
                    int hx = weapon.grid.GridIndex.X;
                    int hy = weapon.grid.GridIndex.Y;
                    string typeName = weapon.GetType().Name;
                    int hp = Mathf.CeilToInt(weapon.health / 10f);
                    string diff = GetWeaponDiffString(weapon);
                    sb.AppendLine($"[{hx},{hy}] {typeName} {weapon.team} HP{hp}{diff}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== Facilities [x,y] Type Team ===");
        if (gridManager?.grids != null)
        {
            foreach (var grid in gridManager.grids)
            {
                if (grid?.city != null && IsInstanceValid(grid.city))
                {
                    int hx = grid.GridIndex.X;
                    int hy = grid.GridIndex.Y;
                    string typeName = grid.city.GetType().Name;
                    string team = grid.city.facilityTeam ?? "None";
                    string fDiff = GetFacilityDiffString(grid.city);   // ✅ 开创人2026-11-08：设施字段 diff 补导出（方向/生产/四态等，此前从未被调用=静默丢）
                    sb.AppendLine($"[{hx},{hy}] {typeName} {team}{fDiff}");
                }
            }
        }
        sb.AppendLine();

        // ✅ 开创人2026-11-14：传送链路随完整格式导出（此前只有极简格式有 TP 段，完整导出丢链）
        sb.AppendLine("=== Teleport Links ===");
        if (gridManager?.teleportLinks != null)
        {
            foreach (var l in gridManager.teleportLinks)
                sb.AppendLine($"[{l.from.X},{l.from.Y}] > [{l.to.X},{l.to.Y}] {(l.oneWay ? "W" : "B")}");
        }
        sb.AppendLine();

        // ✅ 开创人2026-11-14：同盟对随完整格式导出（此前只有极简格式有 AL 段）
        sb.AppendLine("=== Alliances ===");
        foreach (var (a, b) in TeamHelper.GetAlliancePairs())
            sb.AppendLine($"{a}:{b}");
        sb.AppendLine();

        // ✅ 开创人2026-11-08：格子自定义段（伤害/弹药/燃料/迷雾规则/视野塔/方向烘焙全套 [Export] 字段，
        //    与 txt 导入及存档槽走同一 {Test:...} diff 格式；无自定义的格子不占行）
        sb.AppendLine("=== Grid Customs ===");
        if (gridManager?.grids != null)
        {
            foreach (var grid in gridManager.grids)
            {
                if (grid == null) continue;
                string gDiff = GetGridCustomsDiff(grid);
                if (!string.IsNullOrEmpty(gDiff))
                    sb.AppendLine($"[{grid.GridIndex.X},{grid.GridIndex.Y}] {gDiff}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== Unit Value Statistics ===");
        long pn1Value = 0, p0Value = 0, p1Value = 0, p2Value = 0;
        if (gameManager?.unitManager?.AllUnits != null)
        {
            foreach (var unit in gameManager.unitManager.AllUnits)
            {
                if (unit != null && IsInstanceValid(unit))
                {
                    switch (unit.team)
                    {
                        case "Player": pn1Value += unit.cost; break;
                        case "Player0": p0Value += unit.cost; break;
                        case "Player1": p1Value += unit.cost; break;
                        case "Player2": p2Value += unit.cost; break;
                    }
                }
            }
        }
        if (gameManager?.weaponManager?.AllWeapons != null)
        {
            foreach (var weapon in gameManager.weaponManager.AllWeapons)
            {
                if (weapon != null && IsInstanceValid(weapon))
                {
                    switch (weapon.team)
                    {
                        case "Player": pn1Value += weapon.cost; break;
                        case "Player0": p0Value += weapon.cost; break;
                        case "Player1": p1Value += weapon.cost; break;
                        case "Player2": p2Value += weapon.cost; break;
                    }
                }
            }
        }
        long totalValue = pn1Value + p0Value + p1Value + p2Value;
        sb.AppendLine($"P-1 (Player):  {pn1Value} G");
        sb.AppendLine($"P0  (Neutral): {p0Value} G");
        sb.AppendLine($"P1  (Red):     {p1Value} G");
        sb.AppendLine($"P2  (Blue):    {p2Value} G");
        sb.AppendLine($"Total:         {totalValue} G");
        sb.AppendLine();

        sb.AppendLine("=== Funds Statistics ===");
        // ✅ 开创人2026-11-08：Funds 机器行=全体活跃势力（P-1/P0/P3~P8/自定义），导入以它为准；下列人类行仅展示
        if (gameManager != null)
            sb.AppendLine($"Funds: {string.Join(",", TeamHelper.GetActiveFactionIds().Select(id => $"{id}={gameManager.GetTeamFunds(id)}"))}");
        int realP1Funds = gameManager?.p1Funds ?? 0;
        int realP2Funds = gameManager?.p2Funds ?? 0;
        sb.AppendLine($"P1  (Red):     {realP1Funds} G");
        sb.AppendLine($"P2  (Blue):    {realP2Funds} G");
        sb.AppendLine();

        sb.AppendLine("=== End of Export ===");

        string content = sb.ToString();
        string fileName = $"AW13_MapExport_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        string savedPath = "";
        bool savedToDownloads = false;

        try
        {
            string downloadDir = OS.GetSystemDir(OS.SystemDir.Downloads);
            if (!string.IsNullOrEmpty(downloadDir) && Directory.Exists(downloadDir))
            {
                string downloadPath = Path.Combine(downloadDir, fileName);
                File.WriteAllText(downloadPath, content);
                savedPath = downloadPath;
                savedToDownloads = true;
            }
        }
        catch (Exception)
        {
        }

        if (!savedToDownloads)
        {
            try
            {
                string userDir = OS.GetUserDataDir();
                string userPath = Path.Combine(userDir, fileName);
                File.WriteAllText(userPath, content);
                savedPath = userPath;
            }
            catch (Exception)
            {
            }
        }

        try
        {
            DisplayServer.ClipboardSet(content);
        }
        catch (Exception)
        {
        }

        string hintText;
        if (savedToDownloads)
            hintText = $"✅ {TranslateGroup.T("已导出到下载目录")}:\n{savedPath}\n📋 {TranslateGroup.T("内容已复制到剪贴板")}";
        else if (!string.IsNullOrEmpty(savedPath))
            hintText = $"⚠️ {TranslateGroup.T("已保存到应用目录")}:\n{savedPath}\n📋 {TranslateGroup.T("内容已复制到剪贴板")}\n({TranslateGroup.T("手机可在剪贴板粘贴分享")})";
        else
            hintText = TranslateGroup.T($"❌ 文件保存失败\n📋 但{TranslateGroup.T("内容已复制到剪贴板")}\n请手动粘贴保存");

        var hintLabel = new Label();
        hintLabel.Text = hintText;
        hintLabel.AddThemeFontSizeOverride("font_size", 13);
        hintLabel.AddThemeColorOverride("font_color", savedToDownloads ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.8f, 0.2f));
        hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hintLabel.Position = new Vector2(0, globalConfigPanel.Size.Y - 80);
        hintLabel.Size = new Vector2(globalConfigPanel.Size.X, 70);
        globalConfigPanel.AddChild(hintLabel);

        var timer = new Timer();
        timer.WaitTime = 3.0f;
        timer.OneShot = true;
        timer.Timeout += () => { hintLabel.QueueFree(); timer.QueueFree(); };
        globalConfigPanel.AddChild(timer);
        timer.Start();
    }

    // ========== ✅ 模板差异比较 ==========
    private Dictionary<string, object> _templateInstanceCache = new();

    private object GetTemplateInstance(Type type, string scenePath)
    {
        string key = $"{type.Name}:{scenePath}";
        if (_templateInstanceCache.ContainsKey(key)) return _templateInstanceCache[key];

        var scene = GD.Load<PackedScene>(scenePath);
        if (scene == null) return null;

        var instance = scene.Instantiate();
        if (instance == null || !type.IsInstanceOfType(instance))
        {
            instance?.QueueFree();
            return null;
        }
        _templateInstanceCache[key] = instance;
        return instance;
    }

    // ✅ 格子自定义 diff（开创人2026-11-08：txt 导出/导入与存档槽共用；new Grids() 模板 + IsSupportedType 过滤，
    //    grid.tscn 只覆写 terrainIconTextures 一个 Dictionary 字段，过滤后无噪音）
    public string GetGridCustomsDiff(Grids grid)
    {
        if (grid == null) return "";
        var diffs = GetDiffs(grid, new Grids());
        if (diffs.Count == 0) return "";
        return "{Test:true;" + string.Join(";", diffs) + "}";
    }

    private string GetUnitDiffString(Infantry unit)
    {
        string scenePath = GetScenePathForUnitType(unit.GetType().Name);
        if (string.IsNullOrEmpty(scenePath)) return "";

        var template = GetTemplateInstance(unit.GetType(), scenePath);
        if (template == null) return "";

        var diffs = GetDiffs(unit, template);
        if (diffs.Count == 0) return "";
        return "{Test:true;" + string.Join(";", diffs) + "}";
    }

    private string GetWeaponDiffString(Weapon weapon)
    {
        string scenePath = GetScenePathForWeaponType(weapon.GetType().Name);
        if (string.IsNullOrEmpty(scenePath)) return "";

        var template = GetTemplateInstance(weapon.GetType(), scenePath);
        if (template == null) return "";

        var diffs = GetDiffs(weapon, template);
        if (diffs.Count == 0) return "";
        return "{Test:true;" + string.Join(";", diffs) + "}";
    }

    private string GetFacilityDiffString(Facility facility)
    {
        string scenePath = GetScenePathForFacilityType(facility.GetType().Name);
        if (string.IsNullOrEmpty(scenePath)) return "";

        var template = GetTemplateInstance(facility.GetType(), scenePath);
        if (template == null) return "";

        var diffs = GetDiffs(facility, template);
        if (diffs.Count == 0) return "";
        return "{Test:true;" + string.Join(";", diffs) + "}";
    }

    private List<string> GetDiffs(object instance, object template)
    {
        var diffs = new List<string>();
        var type = instance.GetType();

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<ExportAttribute>() != null && IsSupportedType(f.FieldType));
        foreach (var field in fields)
        {
            var v1 = field.GetValue(instance);
            var v2 = field.GetValue(template);
            if (!AreValuesEqual(v1, v2))
                diffs.Add($"{field.Name}:{FormatValue(v1)}");
        }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ExportAttribute>() != null && p.CanRead && p.CanWrite && IsSupportedType(p.PropertyType));
        foreach (var prop in props)
        {
            var v1 = prop.GetValue(instance);
            var v2 = prop.GetValue(template);
            if (!AreValuesEqual(v1, v2))
                diffs.Add($"{prop.Name}:{FormatValue(v1)}");
        }

        return diffs;
    }

    private bool AreValuesEqual(object a, object b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.GetType() != b.GetType()) return false;
        if (ExportPersistenceCodec.TrySerialize(a, out string encodedA) && ExportPersistenceCodec.TrySerialize(b, out string encodedB))
            return encodedA == encodedB;
        if (a is Godot.Collections.Dictionary dictA && b is Godot.Collections.Dictionary dictB)
            return dictA.Count == dictB.Count;
        if (a is Godot.Collections.Array arrA && b is Godot.Collections.Array arrB)
            return arrA.Count == arrB.Count;
        return a.Equals(b);
    }

    private string FormatValue(object val)
    {
        if (val == null) return "null";
        if (val is bool b) return b ? "true" : "false";
        if (val is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (val is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ExportPersistenceCodec.TrySerialize(val, out string encoded)) return encoded;
        return val.ToString();
    }

    private string GetScenePathForUnitType(string typeName)
    {
        var tmpl = unitTemplates.FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(t.ScenePath).Replace("_", "").Replace("(1)", "").Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (tmpl != null) return tmpl.ScenePath;
        tmpl = unitTemplates.FirstOrDefault(t => t.ScenePath.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0);
        return tmpl?.ScenePath ?? "";
    }

    private string GetScenePathForWeaponType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "";

        // ✅ 显式 AWME 类型名映射（避免 IndexOf 模糊匹配）
        var awmeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["black_cannon"] = "res://Prefabs/black_cannon.tscn",
            ["blackcannon"] = "res://Prefabs/black_cannon.tscn",
            ["BlackCannon"] = "res://Prefabs/black_cannon.tscn",
            ["large_cannon"] = "res://Prefabs/large_cannon.tscn",
            ["largecannon"] = "res://Prefabs/large_cannon.tscn",
            ["LargeCannon"] = "res://Prefabs/large_cannon.tscn",
            ["Laser"] = "res://Prefabs/Laser.tscn",
            ["death_ray"] = "res://Prefabs/death_ray.tscn",
            ["DeathRay"] = "res://Prefabs/death_ray.tscn",
            ["Crystal"] = "res://Prefabs/crystal.tscn",
            ["crystal"] = "res://Prefabs/crystal.tscn",
            ["black_obelisk"] = "res://Prefabs/black_obelisk.tscn",
            ["blackobelisk"] = "res://Prefabs/black_obelisk.tscn",
            ["BlackObelisk"] = "res://Prefabs/black_obelisk.tscn",
            ["missile_silo"] = "res://Prefabs/missile_silo.tscn",
            ["missilesilo"] = "res://Prefabs/missile_silo.tscn",
            ["MissileSilo"] = "res://Prefabs/missile_silo.tscn",
            ["force_satellite"] = "res://Prefabs/ForceSatellite.tscn",
            ["ForceSatellite"] = "res://Prefabs/ForceSatellite.tscn",
            ["attack_satellite"] = "res://Prefabs/AttackSatellite.tscn",
            ["AttackSatellite"] = "res://Prefabs/AttackSatellite.tscn",
            ["PipeSeam"] = "res://Prefabs/PipeSeam.tscn",
            ["pipeseam"] = "res://Prefabs/PipeSeam.tscn",
            ["SuperFactory"] = "res://Prefabs/SuperFactory.tscn",
            ["superfactory"] = "res://Prefabs/SuperFactory.tscn",
        };
        if (awmeNameMap.TryGetValue(typeName, out string explicitPath))
        {
            return explicitPath;
        }

        var tmpl = weaponTemplates.FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(t.ScenePath).Replace("_", "").Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (tmpl != null) return tmpl.ScenePath;
        tmpl = weaponTemplates.FirstOrDefault(t => t.ScenePath.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0);
        return tmpl?.ScenePath ?? "";
    }

    private string GetScenePathForFacilityType(string typeName)
    {
        var tmpl = facilityTemplates.FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(t.ScenePath).Replace("_", "").Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (tmpl != null) return tmpl.ScenePath;
        tmpl = facilityTemplates.FirstOrDefault(t => t.ScenePath.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0);
        return tmpl?.ScenePath ?? "";
    }
}
