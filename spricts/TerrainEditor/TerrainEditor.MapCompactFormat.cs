using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · MapCompactFormat（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ========== ✅ 极简/紧凑导出格式 ==========

    private readonly Dictionary<string, string> compactUnitAbbr = new()
    {
        { "Infantry", "Inf" }, { "Mech", "Mch" }, { "Oozium", "Ooz" },
        { "LightTank", "LTk" }, { "MdTank", "MTk" }, { "Rocket", "Rck" },
        { "Artillery", "Art" }, { "APC", "APC" }, { "Recon", "Rec" },
        { "AntiAir", "AA" }, { "AntiTank", "AT" }, { "Flare", "Flr" },
        { "Bike", "Bke" }, { "FlyBomb", "FBo" }, { "Fighter", "Ftr" }, { "Bomber", "Bom" }, { "BCopter", "BH" }, { "TCopter", "TH" }, { "AntiAirMissile", "SAM" }, { "Stealth", "Stl" }, { "Dog", "Dog" },
        { "PipeRunner", "PRun" },
        { "RepairBoat", "RBt" }, { "Battleship", "BS" }, { "Cruiser", "Cru" }, { "Submarine", "Sub" }, { "Lander", "Lnd" }, { "Carrier", "Car" }, { "Gunboat", "GBt" },
        { "Neotank", "Neo" }, { "Megatank", "Mga" }, { "WarTank", "WTk" }, { "Rig", "Rig" }, { "Duster", "Dst" }, { "Seaplane", "SPl" }, { "ProducibleBoat", "PBt" }, { "KamikazeBoat", "KBt" }, { "PipeWorker", "PWk" }
    };
    private readonly Dictionary<string, string> compactWeaponAbbr = new()
    {
        { "BlackCannon", "BCn" }, { "Laser", "Lsr" }, { "Crystal", "Cry" }
    };
    private readonly Dictionary<string, string> compactFacilityAbbr = new()
    {
        { "City", "Cty" }, { "Base", "Bse" }, { "AirPort", "Apt" }, { "Port", "Prt" }, { "TempAirPort", "TAp" }, { "TempPort", "TPt" }
    };
    private readonly Dictionary<string, string> compactTeamAbbr = new()
    {
        { "Player1", "P1" }, { "Player2", "P2" }, { "Player0", "P0" }, { "Player", "PN" }
    };

    // 反向映射（缩写→完整名）
    private string AbbrToUnitType(string abbr) => compactUnitAbbr.FirstOrDefault(kv => kv.Value == abbr).Key ?? abbr;
    private string AbbrToWeaponType(string abbr) => compactWeaponAbbr.FirstOrDefault(kv => kv.Value == abbr).Key ?? abbr;
    private string AbbrToFacilityType(string abbr) => compactFacilityAbbr.FirstOrDefault(kv => kv.Value == abbr).Key ?? abbr;
    private string AbbrToTeam(string abbr) => compactTeamAbbr.FirstOrDefault(kv => kv.Value == abbr).Key ?? abbr;

    private string UnitTypeToAbbr(string typeName) => compactUnitAbbr.GetValueOrDefault(typeName, typeName);
    private string WeaponTypeToAbbr(string typeName) => compactWeaponAbbr.GetValueOrDefault(typeName, typeName);
    private string FacilityTypeToAbbr(string typeName) => compactFacilityAbbr.GetValueOrDefault(typeName, typeName);
    private string TeamToAbbr(string team) => compactTeamAbbr.GetValueOrDefault(team, team);

    private int TerrainToNumber(GridType gt) => (int)gt + 1;
    private GridType NumberToTerrain(int num)
    {
        var values = Enum.GetValues<GridType>();
        if (num >= 1 && num <= values.Length) return values[num - 1];
        return GridType.GROUND; // 默认
    }

    // ✅ 极简导出
    private void ExportCompactMap()
    {
        if (gridManager?.map == null) { ShowImportHint(TranslateGroup.T("❌ GridManager 未初始化")); return; }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var grid in gridManager.grids)
        { if (grid == null) continue; minX = Math.Min(minX, grid.GridIndex.X); minY = Math.Min(minY, grid.GridIndex.Y); maxX = Math.Max(maxX, grid.GridIndex.X); maxY = Math.Max(maxY, grid.GridIndex.Y); }
        if (minX == int.MaxValue) { minX = 0; minY = 0; maxX = 0; maxY = 0; }
        int width = maxX - minX + 1, height = maxY - minY + 1;
        bool fog = gameManager?.fogOfWarManager?.isFogOfWarEnabled ?? false;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== AW Compact ===");
        int _sc = gridManager?.GetScreenCount() ?? 1;
        string _sw = (gridManager?.screenWidths != null && gridManager.screenWidths.Length == _sc) ? string.Join(",", gridManager.screenWidths) : null;
        string _sh = (gridManager?.screenHeights != null && gridManager.screenHeights.Length == _sc) ? string.Join(",", gridManager.screenHeights) : null;
        string _sy = (gridManager?.screenYOffsets != null && gridManager.screenYOffsets.Length == _sc) ? string.Join(",", gridManager.screenYOffsets) : null;
        sb.AppendLine($"W:{width} H:{height} F:{(fog ? 1 : 0)} S:{_sc}" + (_sw != null ? $" SW:{_sw}" : "") + (_sh != null ? $" SH:{_sh}" : "") + (_sy != null ? $" SY:{_sy}" : ""));
        sb.AppendLine();

        sb.AppendLine("=== T ===");
        for (int y = minY; y <= maxY; y++)
        {
            sb.Append($"{y}:");
            for (int x = minX; x <= maxX; x++)
            {
                var grid = (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1)) ? gridManager.map[x, y] : null;
                int num = grid != null ? TerrainToNumber(grid.gridType) : 0;
                sb.Append($" {num}");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("=== U ===");
        if (gameManager?.unitManager?.AllUnits != null)
        {
            foreach (var unit in gameManager.unitManager.AllUnits)
            {
                if (unit == null || !IsInstanceValid(unit) || unit.grid == null) continue;
                int hx = unit.grid.GridIndex.X, hy = unit.grid.GridIndex.Y;
                string abbr = UnitTypeToAbbr(unit.GetType().Name);
                string team = TeamToAbbr(unit.team);
                int hp = Mathf.CeilToInt(unit.health / 10f);
                string diff = GetUnitDiffString(unit);
                sb.AppendLine($"[{hx},{hy}] {abbr} {team} {hp}{diff}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== W ===");
        if (gameManager?.weaponManager?.AllWeapons != null)
        {
            foreach (var weapon in gameManager.weaponManager.AllWeapons)
            {
                if (weapon == null || !IsInstanceValid(weapon) || weapon.grid == null) continue;
                int hx = weapon.grid.GridIndex.X, hy = weapon.grid.GridIndex.Y;
                string abbr = WeaponTypeToAbbr(weapon.GetType().Name);
                string team = TeamToAbbr(weapon.team);
                int hp = Mathf.CeilToInt(weapon.health / 10f);
                string diff = GetWeaponDiffString(weapon);
                sb.AppendLine($"[{hx},{hy}] {abbr} {team} {hp}{diff}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== F ===");
        if (gridManager?.grids != null)
        {
            foreach (var grid in gridManager.grids)
            {
                if (grid?.city == null || !IsInstanceValid(grid.city)) continue;
                int hx = grid.GridIndex.X, hy = grid.GridIndex.Y;
                string abbr = FacilityTypeToAbbr(grid.city.GetType().Name);
                string team = TeamToAbbr(grid.city.facilityTeam ?? "Player0");
                string diff = GetFacilityDiffString(grid.city);
                sb.AppendLine($"[{hx},{hy}] {abbr} {team}{diff}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("=== $ ===");
        sb.AppendLine($"P1:{gameManager?.p1Funds ?? 0}");
        sb.AppendLine($"P2:{gameManager?.p2Funds ?? 0}");
        sb.AppendLine();
        sb.AppendLine("=== E ===");

        // ✅ N屏：传送链路（每链一行：源>目标 单向/互传）
        sb.AppendLine("=== TP ===");
        if (gridManager?.teleportLinks != null)
        {
            foreach (var l in gridManager.teleportLinks)
                sb.AppendLine($"[{l.from.X},{l.from.Y}] > [{l.to.X},{l.to.Y}] {(l.oneWay ? "W" : "B")}");
        }
        sb.AppendLine();

        // ✅ 盟友机制：同盟对
        sb.AppendLine("=== AL ===");
        foreach (var (a, b) in TeamHelper.GetAlliancePairs())
            sb.AppendLine($"{a}:{b}");
        sb.AppendLine();

        string content = sb.ToString();
        string fileName = $"AW13_Compact_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        try
        {
            string dir = OS.GetSystemDir(OS.SystemDir.Downloads);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                string path = Path.Combine(dir, fileName);
                File.WriteAllText(path, content);
                DisplayServer.ClipboardSet(content);
                ShowImportHint($"✅ {TranslateGroup.T("极简导出成功")}！\n📁 {path}\n📋 已复制到剪贴板");
                return;
            }
        }
        catch (System.Exception ex) { GD.PushWarning($"[TerrainEditor] {ex.GetType().Name}: {ex.Message}"); }
        try
        {
            string path = Path.Combine(OS.GetUserDataDir(), fileName);
            File.WriteAllText(path, content);
            DisplayServer.ClipboardSet(content);
            ShowImportHint($"✅ {TranslateGroup.T("极简导出成功")}！\n📁 {path}\n📋 已复制到剪贴板");
        }
        catch (Exception ex) { ShowImportHint($"❌ {TranslateGroup.T("导出失败")}: {ex.Message}"); }
    }

        private static int[] ParseCsvInts(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;
            var list = new System.Collections.Generic.List<int>();
            foreach (var p in csv.Split(','))
                if (int.TryParse(p.Trim(), out int v)) list.Add(v);
            return list.Count > 0 ? list.ToArray() : null;
        }

    // ✅ 极简导入
    private void ParseCompactMap(string content)
    {
        try
        {
            if (gameManager != null) gameManager.blockVictoryCheck = true;
            TeamHelper.ClearAllAlliances(); // ✅ 盟友机制：导入前清空旧同盟
            ClearAllMapEntities();

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        // ✅ 先扫描一遍计算地图尺寸
            int mapWidth = 0, mapHeight = 0, mapScreens = 1;
            int[] mapSw = null, mapSh = null, mapSy = null;
            string scanSection = "";
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("=== ") && line.EndsWith(" ==="))
                {
                    scanSection = line.Substring(4, line.Length - 8).Trim();
                    continue;
                }
                // ✅ N屏：头部解析屏数 S:n
                if (scanSection == "AW Compact")
                {
                    var sm = System.Text.RegularExpressions.Regex.Match(line, @"S:(\d+)");
                    if (sm.Success) mapScreens = Math.Max(1, int.Parse(sm.Groups[1].Value));
                    var msw = System.Text.RegularExpressions.Regex.Match(line, @"SW:([\d,]+)");
                    if (msw.Success) mapSw = ParseCsvInts(msw.Groups[1].Value);
                    var msh = System.Text.RegularExpressions.Regex.Match(line, @"SH:([\d,]+)");
                    if (msh.Success) mapSh = ParseCsvInts(msh.Groups[1].Value);
                    var msy = System.Text.RegularExpressions.Regex.Match(line, @"SY:([\d,]+)");
                    if (msy.Success) mapSy = ParseCsvInts(msy.Groups[1].Value);
                }
                if (scanSection == "T")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+):\s+(.+)");
                    if (m.Success)
                    {
                        int y = int.Parse(m.Groups[1].Value);
                        var nums = m.Groups[2].Value.Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        mapWidth = Math.Max(mapWidth, nums.Length);
                        mapHeight = Math.Max(mapHeight, y + 1);
                    }
                }
                else if (scanSection == "U" || scanSection == "W" || scanSection == "F")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]");
                    if (m.Success)
                    {
                        int x = int.Parse(m.Groups[1].Value);
                        int y = int.Parse(m.Groups[2].Value);
                        mapWidth = Math.Max(mapWidth, x + 1);
                        mapHeight = Math.Max(mapHeight, y + 1);
                    }
                }
            }

            if (mapWidth > 0 && mapHeight > 0)
            {
                // ✅ N屏：先设屏数再建图（screenId 在 Init 中分配）
                if (gridManager != null) gridManager.screenCount = mapScreens;
                // ✅ 恢复各屏几何（旧档无 SW/SH/SY 则留空=均分/顶对齐，向后兼容）
                if (mapSw != null && mapSw.Length == mapScreens) gridManager.screenWidths = mapSw;
                if (mapSh != null && mapSh.Length == mapScreens) gridManager.screenHeights = mapSh;
                if (mapSy != null && mapSy.Length == mapScreens) gridManager.screenYOffsets = mapSy;
                gridManager?.ResizeMap(mapWidth, mapHeight);
                // ✅ 关键：ResizeMap 后必须重新获取 grids 引用
                gridManager?.Init();
                gridManager?.PruneOutsideScreens();   // ✅ 清真空带（ResizeMap 建的是全矩形）
            }

            string section = "";
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("=== ") && line.EndsWith(" ==="))
                {
                    section = line.Substring(4, line.Length - 8).Trim();
                    continue;
                }

                if (section == "T")
                {
                    // y: n1 n2 n3 ...
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+):\s+(.+)");
                    if (m.Success)
                    {
                        int y = int.Parse(m.Groups[1].Value);
                        var nums = m.Groups[2].Value.Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int x = 0; x < nums.Length; x++)
                        {
                            if (int.TryParse(nums[x], out int num))
                            {
                                if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
                                {
                                    var g = gridManager.map[x, y];
                                    if (g != null) g.gridType = NumberToTerrain(num);
                                }
                            }
                        }
                    }
                }
                else if (section == "U")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)\s+(\d+)(.*)");
                    if (m.Success)
                    {
                        int ux = int.Parse(m.Groups[1].Value), uy = int.Parse(m.Groups[2].Value);
                        string uAbbr = m.Groups[3].Value, tAbbr = m.Groups[4].Value;
                        int uHp = int.Parse(m.Groups[5].Value);
                        string diffPart = m.Groups[6].Value;
                        string fullType = AbbrToUnitType(uAbbr);
                        string fullTeam = AbbrToTeam(tAbbr);
                        string scenePath = GetScenePathForUnitType(fullType);
                        if (!string.IsNullOrEmpty(scenePath) && ux >= 0 && ux < gridManager.map.GetLength(0) && uy >= 0 && uy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[ux, uy];
                            if (grid != null)
                            {
                                SpawnUnitAtGrid(grid, scenePath, fullTeam);
                                var spawned = grid.infantries.LastOrDefault();
                                if (spawned != null)
                                {
                                    spawned.health = uHp * 10;
                                    spawned.UpdateHpLabel();
                                    ApplyDiffString(spawned, diffPart);
                                }
                            }
                        }
                    }
                }
                else if (section == "W")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)\s+(\d+)(.*)");
                    if (m.Success)
                    {
                        int wx = int.Parse(m.Groups[1].Value), wy = int.Parse(m.Groups[2].Value);
                        string wAbbr = m.Groups[3].Value, tAbbr = m.Groups[4].Value;
                        int wHp = int.Parse(m.Groups[5].Value);
                        string diffPart = m.Groups[6].Value;
                        string fullType = AbbrToWeaponType(wAbbr);
                        string fullTeam = AbbrToTeam(tAbbr);
                        string scenePath = GetScenePathForWeaponType(fullType);
                        if (!string.IsNullOrEmpty(scenePath) && wx >= 0 && wx < gridManager.map.GetLength(0) && wy >= 0 && wy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[wx, wy];
                            if (grid != null)
                            {
                                SpawnWeaponAtGrid(grid, scenePath, fullTeam);
                                var spawned = grid.weapon;
                                if (spawned != null)
                                {
                                    spawned.health = wHp * 10;
                                    spawned.UpdateHpLabel();
                                    ApplyDiffString(spawned, diffPart);
                                }
                            }
                        }
                    }
                }
                else if (section == "F")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)(.*)");
                    if (m.Success)
                    {
                        int fx = int.Parse(m.Groups[1].Value), fy = int.Parse(m.Groups[2].Value);
                        string fAbbr = m.Groups[3].Value, tAbbr = m.Groups[4].Value;
                        string diffPart = m.Groups[5].Value;
                        string fullType = AbbrToFacilityType(fAbbr);
                        string fullTeam = AbbrToTeam(tAbbr);
                        string scenePath = GetScenePathForFacilityType(fullType);
                        if (!string.IsNullOrEmpty(scenePath) && fx >= 0 && fx < gridManager.map.GetLength(0) && fy >= 0 && fy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[fx, fy];
                            if (grid != null)
                            {
                                SpawnFacilityAtGrid(grid, scenePath, fullTeam);
                                var spawned = grid.city;
                                if (spawned != null) ApplyDiffString(spawned, diffPart);
                            }
                        }
                    }
                }
                else if (section == "$")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"P1:(\d+)");
                    if (m.Success && gameManager != null) gameManager.p1Funds = int.Parse(m.Groups[1].Value);
                    m = System.Text.RegularExpressions.Regex.Match(line, @"P2:(\d+)");
                    if (m.Success && gameManager != null) gameManager.p2Funds = int.Parse(m.Groups[1].Value);
                }
                else if (section == "TP")
                {
                    // ✅ N屏：传送链路 [fx,fy] > [tx,ty] W|B
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s*>\s*\[(\d+),(\d+)\]\s*(\w+)");
                    if (m.Success && gridManager != null)
                    {
                        var f = new Vector2I(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
                        var t = new Vector2I(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
                        bool oneWay = m.Groups[5].Value != "B";
                        gridManager.AddTeleportLink(f, t, oneWay);
                    }
                }
                else if (section == "AL")
                {
                    // ✅ 盟友机制：同盟对 Player1:Player3
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\w+):(\w+)$");
                    if (m.Success)
                        TeamHelper.SetAlliance(m.Groups[1].Value, m.Groups[2].Value, true);
                }
            }

            // ✅ 重新收集所有单位和兵器（确保 AllUnits/AllWeapons 列表正确）
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.weaponManager?.RefreshWeaponList();

            // ✅ 重新设置所有回调和视觉状态
            if (gameManager != null)
            {
                foreach (var unit in gameManager.unitManager?.AllUnits ?? new List<Infantry>())
                {
                    if (unit != null && IsInstanceValid(unit))
                    {
                        unit.OnClickPiece = gameManager.OnSelectPiece;
                        unit.UpdateTeamVisual();
                        unit.UpdateHpLabel();
                    }
                }
                foreach (var weapon in gameManager.weaponManager?.AllWeapons ?? new List<Weapon>())
                {
                    if (weapon != null && IsInstanceValid(weapon))
                    {
                        weapon.OnClickWeapon = gameManager.OnSelectWeapon;
                        weapon.UpdateHpLabel();
                    }
                }
            }

            // ✅ 重置游戏状态为初始回合
            if (gameManager != null)
            {
                gameManager.turnPhase = 1;
                gameManager.selectedInfantry = null;
                gameManager.UpdateTurnLabel();
            }

            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            gameManager?.ResetGameState();
            ShowImportHint(TranslateGroup.T("✅ 极简导入成功！"));
        }
        catch (Exception ex)
        {
            if (gameManager != null) gameManager.blockVictoryCheck = false;
            ShowImportHint($"❌ {TranslateGroup.T("导入失败")}:\n{ex.Message}");
        }
        finally
        {
            // ✅ 关键修复：不再在这里重置 blockVictoryCheck
            // 因为 GameManager.ResetForMapImport() 已经设置了3秒保护计时器
            // 这里立即重置会导致保护失效，触发延迟判定
            // 保护计时器会在3秒后自动解除并调用 CheckVictoryCondition
            GD.Print(TranslateGroup.T("[Import] ParseAndImportMap 完成，保护计时器由 GameManager 管理"));
        }
    }

    // ✅ 极简导入对话框
    private void OpenCompactImportDialog()
    {
        var dialog = new Panel();
        dialog.Name = "CompactImportDialog";
        dialog.CustomMinimumSize = new Vector2(420, 240);
        dialog.SetAnchorsPreset(LayoutPreset.Center);
        dialog.ZIndex = 1400;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.5f, 0.7f, 0.9f);
        dialog.AddThemeStyleboxOverride("panel", panelStyle);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.OffsetLeft = 20; vbox.OffsetRight = -20;
        vbox.OffsetTop = 20; vbox.OffsetBottom = -20;
        vbox.AddThemeConstantOverride("separation", 12);

        var title = new Label();
        TranslateGroup.SetText(title, "📥 极简导入");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1f));
        vbox.AddChild(title);

        var fileBtn = new Button();
        TranslateGroup.SetText(fileBtn, "📁 选择 txt 文件");
        fileBtn.CustomMinimumSize = new Vector2(360, 45);
        fileBtn.AddThemeFontSizeOverride("font_size", 14);
        fileBtn.Pressed += () => {
            dialog.QueueFree();
            var fd = new FileDialog();
            fd.FileMode = FileDialog.FileModeEnum.OpenFile;
            fd.Access = FileDialog.AccessEnum.Filesystem;
            fd.Filters = new string[] { "*.txt ; Text Files" };
            fd.Title = TranslateGroup.T("选择极简导出文件");
            fd.Size = new Vector2I(800, 600);
            fd.FileSelected += (path) => {
                try { ParseCompactMap(File.ReadAllText(path)); } catch (Exception ex) { ShowImportHint($"❌ {TranslateGroup.T("读取失败")}: {ex.Message}"); }
                fd.QueueFree();
            };
            fd.Canceled += () => fd.QueueFree();
            AddChild(fd); fd.PopupCentered();
        };
        vbox.AddChild(fileBtn);

        var clipBtn = new Button();
        TranslateGroup.SetText(clipBtn, "📋 从剪贴板导入");
        clipBtn.CustomMinimumSize = new Vector2(360, 45);
        clipBtn.AddThemeFontSizeOverride("font_size", 14);
        clipBtn.Pressed += () => {
            dialog.QueueFree();
            string content = DisplayServer.ClipboardGet();
            if (string.IsNullOrWhiteSpace(content)) { ShowImportHint(TranslateGroup.T("❌ 剪贴板为空")); return; }
            ParseCompactMap(content);
        };
        vbox.AddChild(clipBtn);

        var hint = new Label();
        TranslateGroup.SetText(hint, "极简格式：每行一个对象，无复杂Test块\n更可靠、更紧凑");
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.8f, 0.6f, 0.3f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(hint);

        dialog.AddChild(vbox);
        AddChild(dialog);
    }
}
