using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

// ✅ 方案甲（2026-11-15）：分帧极简解析 BootParseCompactMap——与 MapCompactFormat.ParseCompactMap
// 同构，但逐行/逐实体 await 让帧并上报 BootMapLoader 千分制进度。
// ⚠️ 同步版仍是权威：改解析/生成逻辑须两边同步改（BootMapLoader.BOOT_ENABLED=true 后本函数即开盘主路径）。
public partial class TerrainEditor
{
    public async Task BootParseCompactMap(string content, BootMapLoader boot)
    {
        try
        {
            if (gameManager != null) gameManager.blockVictoryCheck = true;
            TeamHelper.ClearAllAlliances();
            ClearAllMapEntities();

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // ===== 扫描（同步，便宜）：尺寸 + 屏几何 + 进度分母 =====
            int mapWidth = 0, mapHeight = 0, mapScreens = 1;
            int[] mapSw = null, mapSh = null, mapSy = null;
            int terrainRows = 0, unitCount = 0, weaponCount = 0, facilityCount = 0;
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
                        terrainRows++;
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
                        if (scanSection == "U") unitCount++;
                        else if (scanSection == "W") weaponCount++;
                        else facilityCount++;
                    }
                }
            }

            if (mapWidth > 0 && mapHeight > 0)
            {
                if (gridManager != null) gridManager.screenCount = mapScreens;
                if (mapSw != null && mapSw.Length == mapScreens) gridManager.screenWidths = mapSw;
                if (mapSh != null && mapSh.Length == mapScreens) gridManager.screenHeights = mapSh;
                if (mapSy != null && mapSy.Length == mapScreens) gridManager.screenYOffsets = mapSy;

                boot?.SetPhase("部署格子");
                boot?.Begin(mapHeight + terrainRows + unitCount + weaponCount + facilityCount);

                await gridManager.ResizeMapChunked(mapWidth, mapHeight, boot);
                gridManager?.Init();
                gridManager?.PruneOutsideScreens();
            }

            // ===== 主循环（分段让帧 + tick）=====
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
                        boot?.Tick(1);
                        boot?.SetPhase($"铺设地形 {y + 1}/{mapHeight}");
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
                                int joinId = 0;
                                var joinMatch = System.Text.RegularExpressions.Regex.Match(diffPart, @"JG(\d+)");
                                if (joinMatch.Success) joinId = int.Parse(joinMatch.Groups[1].Value);
                                diffPart = System.Text.RegularExpressions.Regex.Replace(diffPart, @"\s*JG\d+", "");
                                SpawnUnitAtGrid(grid, scenePath, fullTeam, preserveJoinedUnits: joinId > 0, joinGroupId: joinId);
                                var spawned = grid.infantries.LastOrDefault();
                                if (spawned != null)
                                {
                                    spawned.health = uHp * 10;
                                    spawned.UpdateHpLabel();
                                    ApplyDiffString(spawned, diffPart);
                                }
                            }
                        }
                        boot?.Tick(1);
                        boot?.SetPhase($"部署单位 {fullType}");
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
                        boot?.Tick(1);
                        boot?.SetPhase($"部署兵器 {fullType}");
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
                        if (!string.IsNullOrEmpty(scenePath) && fx >= 0 && fx < gridManager.map.GetLength(1) * 0 + gridManager.map.GetLength(0) && fy >= 0 && fy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[fx, fy];
                            if (grid != null)
                            {
                                SpawnFacilityAtGrid(grid, scenePath, fullTeam);
                                var spawned = grid.city;
                                if (spawned != null) ApplyDiffString(spawned, diffPart);
                            }
                        }
                        boot?.Tick(1);
                        boot?.SetPhase($"部署设施 {fullType}");
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\w+):(\w+)$");
                    if (m.Success)
                        TeamHelper.SetAlliance(m.Groups[1].Value, m.Groups[2].Value, true);
                }
            }

            // ===== 收尾（与同步版 ParseCompactMap 尾段逐项对齐）=====
            boot?.SetPhase("收尾");
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.weaponManager?.RefreshWeaponList();

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

            // ✅ 重置游戏状态为初始回合（同同步版）
            if (gameManager != null)
            {
                gameManager.turnPhase = 1;
                gameManager.selectedInfantry = null;
                gameManager.UpdateTurnLabel();
            }

            
        // ✅ 开创人2026-11-19: 方向性地形(管道/山地/海崖)CoW贴图在格子创建时按默认方向烘焙——批量铺格/载入后必须全员重刷
        if (gridManager?.map != null)
        {
            foreach (var g in gridManager.map)
                if (g != null && GodotObject.IsInstanceValid(g))
                    g.UpdateCowTerrainIcon();
        }
gameManager?.unitManager?.RefreshUnitList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            gameManager?.ResetGameState();
            GD.Print("[BootParse] ✅ 地图分帧生成完毕");
        }
        catch (Exception ex)
        {
            GD.PushError($"[BootParse] ❌ {ex}");
            throw; // 交给 BootMapLoader.BootAsync 标 IsFailed
        }
    }
}
