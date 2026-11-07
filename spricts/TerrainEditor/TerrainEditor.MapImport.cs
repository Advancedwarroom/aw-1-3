using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · MapImport（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{
        private static int[] ParseCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;
            var list = new System.Collections.Generic.List<int>();
            foreach (var p in csv.Split(','))
                if (int.TryParse(p.Trim(), out int v)) list.Add(v);
            return list.Count > 0 ? list.ToArray() : null;
        }


    // ========== ✅ 地图导入 ==========
    private void OpenImportDialog()
    {
        var dialog = new Panel();
        dialog.Name = "ImportDialog";
        dialog.CustomMinimumSize = new Vector2(420, 320);
        dialog.SetAnchorsPreset(LayoutPreset.Center);
        dialog.ZIndex = 1400;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.5f, 0.7f, 0.9f);
        dialog.AddThemeStyleboxOverride("panel", panelStyle);

        var title = new Label();
        TranslateGroup.SetText(title, "📥 导入地图配置");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1f));
        title.CustomMinimumSize = new Vector2(0, 40);
        title.Position = new Vector2(0, 15);
        title.Size = new Vector2(420, 40);
        dialog.AddChild(title);

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.Position = new Vector2(380, 12);
        closeBtn.Pressed += () => dialog.QueueFree();
        dialog.AddChild(closeBtn);

        var vbox = new VBoxContainer();
        vbox.Position = new Vector2(30, 70);
        vbox.Size = new Vector2(360, 220);
        vbox.AddThemeConstantOverride("separation", 12);

        var fileBtn = new Button();
        TranslateGroup.SetText(fileBtn, "📁 选择 txt 文件导入");
        fileBtn.CustomMinimumSize = new Vector2(360, 45);
        fileBtn.AddThemeFontSizeOverride("font_size", 14);
        fileBtn.Pressed += () => {
            dialog.QueueFree();
            OpenFileImportDialog();
        };
        vbox.AddChild(fileBtn);

        var clipBtn = new Button();
        TranslateGroup.SetText(clipBtn, "📋 从剪贴板导入");
        clipBtn.CustomMinimumSize = new Vector2(360, 45);
        clipBtn.AddThemeFontSizeOverride("font_size", 14);
        clipBtn.Pressed += () => {
            dialog.QueueFree();
            ImportMapFromClipboard();
        };
        vbox.AddChild(clipBtn);

        var hint = new Label();
        TranslateGroup.SetText(hint, "提示：导入会清空当前所有单位、兵器和设施，\n并重建地图。请确保已导出备份！");
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.8f, 0.6f, 0.3f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        dialog.AddChild(vbox);
        AddChild(dialog);
    }

    private void OpenFileImportDialog()
    {
        var fd = new FileDialog();
        fd.FileMode = FileDialog.FileModeEnum.OpenFile;
        fd.Access = FileDialog.AccessEnum.Filesystem;
        fd.Filters = new string[] { "*.txt ; Text Files" };
        fd.Title = TranslateGroup.T("选择地图导出文件");
        fd.Size = new Vector2I(800, 600);
        fd.FileSelected += (path) => {
            try
            {
                string content = File.ReadAllText(path);
                ShowImportConfirmDialog(content);
            }
            catch (Exception ex)
            {
                ShowImportHint($"❌ {TranslateGroup.T("读取文件失败")}:\n{ex.Message}");
            }
            fd.QueueFree();
        };
        fd.Canceled += () => fd.QueueFree();
        AddChild(fd);
        fd.PopupCentered();
    }

    private void ImportMapFromClipboard()
    {
        string content = DisplayServer.ClipboardGet();
        if (string.IsNullOrWhiteSpace(content))
        {
            ShowImportHint(TranslateGroup.T("❌ 剪贴板为空或无法读取"));
            return;
        }
        ShowImportConfirmDialog(content);
    }

    private void ShowImportConfirmDialog(string content)
    {
        var confirm = new ConfirmationDialog();
        confirm.Title = TranslateGroup.T("⚠️ 确认导入");
        confirm.DialogText = TranslateGroup.T("导入将清空当前所有单位、兵器和设施，\n并用文件内容重建地图。\n\n是否继续？");
        confirm.Size = new Vector2I(400, 180);
        confirm.Confirmed += () => {
            ParseAndImportMap(content);
            confirm.QueueFree();
        };
        confirm.Canceled += () => confirm.QueueFree();
        AddChild(confirm);
        confirm.PopupCentered();
    }

    private void ShowImportHint(string text)
    {
        var hint = new Label();
        hint.Text = text;
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.2f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.Position = new Vector2(0, globalConfigPanel.Size.Y - 80);
        hint.Size = new Vector2(globalConfigPanel.Size.X, 70);
        globalConfigPanel.AddChild(hint);
        var t = new Timer();
        t.WaitTime = 3.0f;
        t.OneShot = true;
        t.Timeout += () => { hint.QueueFree(); t.QueueFree(); };
        globalConfigPanel.AddChild(t);
        t.Start();
    }

    public void ClearAllMapEntities()
    {
        // ✅ 批量清理，避免逐个调用 gameManager.RemoveUnit 触发胜利检查
        
        // ✅ 关键修复：清理已失败势力记录，避免导入后延迟判定失败
        if (gameManager != null)
        {
            gameManager.ResetForMapImport();
        }
        
        // ✅ 导入后自动进入编辑模式
        if (!IsEditMode)
        {
            SetEditMode(true);
        }
        
        // 1. 清理单位 - 直接从 units 子节点遍历，确保包含所有对象
        if (gameManager?.unitManager?.units != null)
        {
            var allChildren = gameManager.unitManager.units.GetChildren().ToList();
            foreach (var child in allChildren)
            {
                if (child is Infantry unit)
                {
                    if (IsInstanceValid(unit))
                    {
                        gameManager.unitCategories.Remove(unit);
                        if (unit.grid != null)
                        {
                            unit.grid.infantries.Remove(unit);
                            if (unit.grid.infantry == unit)
                                unit.grid.infantry = unit.grid.infantries.Count > 0 ? unit.grid.infantries[0] : null;
                        }
                        // ✅ 使用 Free() 立即释放，避免 QueueFree 延迟导致遍历到废弃对象
                        unit.Free();
                    }
                    else
                    {
                        try { gameManager.unitManager.units.RemoveChild(unit); } catch (System.Exception ex) { GD.PushWarning($"[TerrainEditor] {ex.GetType().Name}: {ex.Message}"); }
                    }
                }
            }
            gameManager.unitManager.AllUnits.Clear();
        }

        // 2. 清理兵器
        if (gameManager?.weaponManager?.weaponsNode != null)
        {
            var allWeapons = gameManager.weaponManager.weaponsNode.GetChildren().ToList();
            foreach (var child in allWeapons)
            {
                if (child is Weapon weapon)
                {
                    if (IsInstanceValid(weapon))
                    {
                        if (weapon.grid != null)
                        {
                            weapon.grid.weapons.Remove(weapon);
                            if (weapon.grid.weapon == weapon)
                                weapon.grid.weapon = null;
                        }
                        weapon.Free();
                    }
                    else
                    {
                        try { gameManager.weaponManager.weaponsNode.RemoveChild(weapon); } catch (System.Exception ex) { GD.PushWarning($"[TerrainEditor] {ex.GetType().Name}: {ex.Message}"); }
                    }
                }
            }
            gameManager.weaponManager.AllWeapons.Clear();
        }

        // 3. 清理所有格子的引用
        if (gridManager?.grids != null)
        {
            foreach (var grid in gridManager.grids)
            {
                if (grid == null) continue;
                grid.infantries.Clear();
                grid.infantry = null;
                grid.weapons.Clear();
                grid.weapon = null;
                if (grid.city != null && IsInstanceValid(grid.city))
                {
                    grid.city.Free();
                }
                grid.city = null;
            }
        }

        // ✅ 重置 GameManager 的选中状态，防止引用已删除的单位
        if (gameManager != null)
        {
            gameManager.selectedInfantry = null;
            gameManager.selectedWeapon = null;
        }

    }

    private void ParseAndImportMap(string content)
    {
        try
        {
            if (gameManager != null) gameManager.blockVictoryCheck = true; // ✅ 屏蔽胜利检查
            ClearAllMapEntities();

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // ✅ 天气系统（模块15）：预扫描天气配置（旧版无这些行=沿用当前设置）
            foreach (var rawLine in lines)
            {
                string wl = rawLine.Trim();
                if (wl.StartsWith("WeatherChances: "))
                {
                    var parts = wl.Substring("WeatherChances: ".Length).Split(',');
                    for (int i = 0; i < parts.Length && i < WeatherSystem.Chances.Length; i++)
                        if (int.TryParse(parts[i].Trim(), out int c)) WeatherSystem.Chances[i] = Mathf.Clamp(c, 0, 100);
                }
                else if (wl.StartsWith("WeatherRandom: "))
                    WeatherSystem.SetRandomWeather(wl.EndsWith("On"));
                else if (wl.StartsWith("WeatherDuration: "))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(wl.Substring("WeatherDuration: ".Length), @"(\d+)\s*-\s*(\d+)");
                    if (m.Success)
                    {
                        int dmin = Math.Max(1, int.Parse(m.Groups[1].Value));
                        int dmax = Math.Max(dmin, int.Parse(m.Groups[2].Value));
                        WeatherSystem.RandomDurationMin = dmin;
                        WeatherSystem.RandomDurationMax = dmax;
                    }
                }
                else if (wl.StartsWith("LocalWeather: "))
                {
                    var wmx = (Engine.GetMainLoop() as SceneTree)?.GetFirstNodeInGroup("weather_manager") as WeatherManager;
                    if (wmx != null) wmx.localWeatherEnabled = wl.EndsWith("On");
                }
                else if (wl.StartsWith("WeatherStacks: "))
                {
                    // 顺序保证：LocalWeather 行在本行之前解析，DeserializeStacks 内 AddWeather 受开关门控
                    var wmx = (Engine.GetMainLoop() as SceneTree)?.GetFirstNodeInGroup("weather_manager") as WeatherManager;
                    wmx?.DeserializeStacks(wl.Substring("WeatherStacks: ".Length).Trim());
                }
                else if (wl.StartsWith("Weather: "))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(wl.Substring("Weather: ".Length), @"(\w+)\s*,\s*(\d+)");
                    if (m.Success && Enum.TryParse<WeatherType>(m.Groups[1].Value, out var wt))
                        WeatherSystem.SetWeather(wt, Math.Max(1, int.Parse(m.Groups[2].Value)));
                }
            }

            // ✅ 先扫描一遍计算地图尺寸
            int mapWidth = 0, mapHeight = 0;
            int mapScreens = 1;
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
                if (scanSection == "")
                {
                    if (line.StartsWith("Screens:") && int.TryParse(line.Substring(8).Trim(), out int sv)) mapScreens = Math.Max(1, sv);
                    else if (line.StartsWith("ScreenW:")) mapSw = ParseCsv(line.Substring(8));
                    else if (line.StartsWith("ScreenH:")) mapSh = ParseCsv(line.Substring(8));
                    else if (line.StartsWith("ScreenY:")) mapSy = ParseCsv(line.Substring(8));
                }

                if (scanSection == "Terrain Matrix [x,y]=Type")
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(part, @"\[(\d+),(\d+)\]=(.+?)");
                        if (m.Success)
                        {
                            int tx = int.Parse(m.Groups[1].Value);
                            int ty = int.Parse(m.Groups[2].Value);
                            mapWidth = Math.Max(mapWidth, tx + 1);
                            mapHeight = Math.Max(mapHeight, ty + 1);
                        }
                    }
                }
                else if (scanSection == "Units [x,y] Type Team HP" || scanSection == "Weapons [x,y] Type Team HP" || scanSection == "Facilities [x,y] Type Team")
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
                if (gridManager != null)
                {
                    gridManager.screenCount = mapScreens;
                    if (mapSw != null && mapSw.Length == mapScreens) gridManager.screenWidths = mapSw;
                    if (mapSh != null && mapSh.Length == mapScreens) gridManager.screenHeights = mapSh;
                    if (mapSy != null && mapSy.Length == mapScreens) gridManager.screenYOffsets = mapSy;
                }
                gridManager?.ResizeMap(mapWidth, mapHeight);
                gridManager?.Init();
                gridManager?.PruneOutsideScreens();
            }

            int lineIdx = 0;
            string section = "";

            while (lineIdx < lines.Length)
            {
                string line = lines[lineIdx].Trim();
                lineIdx++;

                if (line.StartsWith("=== ") && line.EndsWith(" ==="))
                {
                    section = line.Substring(4, line.Length - 8).Trim();
                    continue;
                }
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (section == "Terrain Matrix [x,y]=Type")
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(part, @"\[(\d+),(\d+)\]=(.+?)");
                        if (m.Success)
                        {
                            int tx = int.Parse(m.Groups[1].Value);
                            int ty = int.Parse(m.Groups[2].Value);
                            string tType = m.Groups[3].Value.Trim();
                            if (tx >= 0 && tx < gridManager.map.GetLength(0) && ty >= 0 && ty < gridManager.map.GetLength(1))
                            {
                                var g = gridManager.map[tx, ty];
                                if (g != null)
                                {
                                    if (Enum.TryParse<GridType>(tType, out var gt))
                                    {
                                        g.gridType = gt;
                                    }
                                    else
                                    {
                                        // ✅ 无法解析的地形类型默认 GROUND，并记录日志
                                        g.gridType = GridType.GROUND;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (section == "Units [x,y] Type Team HP")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)\s+HP(\d+)(.*)");
                    if (m.Success)
                    {
                        int ux = int.Parse(m.Groups[1].Value);
                        int uy = int.Parse(m.Groups[2].Value);
                        string uType = m.Groups[3].Value;
                        string uTeam = m.Groups[4].Value;
                        int uHp = int.Parse(m.Groups[5].Value);
                        string diffPart = m.Groups[6].Value;

                        string scenePath = GetScenePathForUnitType(uType);
                        if (!string.IsNullOrEmpty(scenePath) && ux >= 0 && ux < gridManager.map.GetLength(0) && uy >= 0 && uy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[ux, uy];
                            if (grid != null)
                            {
                                SpawnUnitAtGrid(grid, scenePath, uTeam);
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
                else if (section == "Weapons [x,y] Type Team HP")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)\s+HP(\d+)(.*)");
                    if (m.Success)
                    {
                        int wx = int.Parse(m.Groups[1].Value);
                        int wy = int.Parse(m.Groups[2].Value);
                        string wType = m.Groups[3].Value;
                        string wTeam = m.Groups[4].Value;
                        int wHp = int.Parse(m.Groups[5].Value);
                        string diffPart = m.Groups[6].Value;

                        string scenePath = GetScenePathForWeaponType(wType);
                        if (!string.IsNullOrEmpty(scenePath) && wx >= 0 && wx < gridManager.map.GetLength(0) && wy >= 0 && wy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[wx, wy];
                            if (grid != null)
                            {
                                SpawnWeaponAtGrid(grid, scenePath, wTeam);
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
                else if (section == "Facilities [x,y] Type Team")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d+),(\d+)\]\s+(\w+)\s+(\w+)");
                    if (m.Success)
                    {
                        int fx = int.Parse(m.Groups[1].Value);
                        int fy = int.Parse(m.Groups[2].Value);
                        string fType = m.Groups[3].Value;
                        string fTeam = m.Groups[4].Value;

                        string scenePath = GetScenePathForFacilityType(fType);
                        if (!string.IsNullOrEmpty(scenePath) && fx >= 0 && fx < gridManager.map.GetLength(0) && fy >= 0 && fy < gridManager.map.GetLength(1))
                        {
                            var grid = gridManager.map[fx, fy];
                            if (grid != null)
                                SpawnFacilityAtGrid(grid, scenePath, fTeam);
                        }
                    }
                }
                else if (section == "Funds Statistics")
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"P1\s+\(Red\):\s*(\d+)");
                    if (m.Success && gameManager != null) gameManager.p1Funds = int.Parse(m.Groups[1].Value);
                    m = System.Text.RegularExpressions.Regex.Match(line, @"P2\s+\(Blue\):\s*(\d+)");
                    if (m.Success && gameManager != null) gameManager.p2Funds = int.Parse(m.Groups[1].Value);
                }
            }

            if (gameManager != null) gameManager.blockVictoryCheck = false; // ✅ 恢复胜利检查
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            gameManager?.ResetGameState(); // ✅ 重置游戏状态，防止胜利面板残留
            ShowImportHint(TranslateGroup.T("✅ 地图导入成功！"));
        }
        catch (Exception ex)
        {
            if (gameManager != null) gameManager.blockVictoryCheck = false; // ✅ 异常时也要恢复
            ShowImportHint($"❌ {TranslateGroup.T("导入失败")}:\n{ex.Message}");
        }
    }

    private void ApplyDiffString(object target, string diffPart)
    {
        if (target == null || string.IsNullOrWhiteSpace(diffPart)) return;

        // 使用嵌套花括号计数器提取 Test 内容
        int startIdx = diffPart.IndexOf("{Test:");
        if (startIdx < 0) return;
        startIdx += 6; // 跳过 "{Test:"

        int braceCount = 1;
        int endIdx = startIdx;
        while (endIdx < diffPart.Length && braceCount > 0)
        {
            if (diffPart[endIdx] == '{') braceCount++;
            else if (diffPart[endIdx] == '}') braceCount--;
            endIdx++;
        }
        if (braceCount != 0) return; // 花括号不匹配，跳过

        string inner = diffPart.Substring(startIdx, endIdx - startIdx - 1);
        if (string.IsNullOrWhiteSpace(inner)) return;

        var items = inner.Split(';');
        var type = target.GetType();
        int successCount = 0;
        int skipCount = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;

            var kv = item.Split(new[] { ':' }, 2);
            if (kv.Length < 2) continue;
            string key = kv[0].Trim();
            string valStr = kv[1].Trim();
            if (string.IsNullOrEmpty(key) || key == "true") continue;

            // 跳过中文字段名
            if (ContainsChinese(key)) { skipCount++; continue; }

            // ✅ 值层面过滤：跳过节点引用/贴图/空集合/复杂值
            if (ShouldSkipValue(valStr)) { skipCount++; continue; }

            var field = type.GetField(key, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.GetCustomAttribute<ExportAttribute>() != null)
            {
                if (!IsSupportedType(field.FieldType)) { skipCount++; continue; }
                try
                {
                    object val = ParseValue(valStr, field.FieldType);
                    if (val != null) { field.SetValue(target, val); successCount++; }
                }
                catch { skipCount++; }
            }
            else
            {
                var prop = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.GetCustomAttribute<ExportAttribute>() != null && prop.CanWrite)
                {
                    if (!IsSupportedType(prop.PropertyType)) { skipCount++; continue; }
                    try
                    {
                        object val = ParseValue(valStr, prop.PropertyType);
                        if (val != null) { prop.SetValue(target, val); successCount++; }
                    }
                    catch { skipCount++; }
                }
            }
        }

    }

    // ✅ 值层面：跳过节点引用/贴图/空集合等不需要导入的值
    private bool ShouldSkipValue(string valStr)
    {
        if (string.IsNullOrEmpty(valStr)) return true;
        // 节点引用格式：<NodeName#ID>
        if (valStr.StartsWith("<") && valStr.EndsWith(">")) return true;
        // Godot 集合类型格式（如 Dictionary、Array 的 ToString）
        if (valStr == "{}" || valStr == "[]" || valStr == "{ }" || valStr == "[ ]") return true;
        // 包含空字典或空数组的格式
        if (valStr.Contains("<")) return true; // 任何包含 < 的（节点引用）
        return false;
    }

    // ✅ 检测字符串是否包含中文
    private bool ContainsChinese(string str)
    {
        if (string.IsNullOrEmpty(str)) return false;
        foreach (char c in str)
        {
            if (c >= 0x4E00 && c <= 0x9FFF) return true; // CJK 统一表意文字
            if (c >= 0x3400 && c <= 0x4DBF) return true; // CJK 扩展A
            if (c >= 0x3000 && c <= 0x303F) return true; // CJK 标点符号
        }
        return false;
    }

    // ✅ 判断类型是否为支持的基本类型
    private bool IsSupportedType(Type t)
    {
        if (t == null) return false;
        if (t == typeof(bool)) return true;
        if (t == typeof(int)) return true;
        if (t == typeof(uint)) return true;
        if (t == typeof(long)) return true;
        if (t == typeof(float)) return true;
        if (t == typeof(double)) return true;
        if (t == typeof(string)) return true;
        if (t.IsEnum) return true;
        if (t == typeof(Color)) return true;
        if (t == typeof(Vector2)) return true;
        if (t == typeof(Vector2I)) return true;
        if (t == typeof(Vector3)) return true;
        if (t == typeof(Vector3I)) return true;
        return false;
    }

    private object ParseValue(string str, Type targetType)
    {
        if (targetType == null || str == null) return null;

        // ✅ 如果是字符串类型，直接返回（包括中文内容）
        if (targetType == typeof(string)) return str;

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(str, out bool bv)) return bv;
            if (str == "1" || str.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (str == "0" || str.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
        if (targetType == typeof(int))
        {
            if (int.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out int iv)) return iv;
            return null;
        }
        if (targetType == typeof(uint))
        {
            if (uint.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out uint uv)) return uv;
            return null;
        }
        if (targetType == typeof(long))
        {
            if (long.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out long lv)) return lv;
            return null;
        }
        if (targetType == typeof(float))
        {
            if (float.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float fv)) return fv;
            return null;
        }
        if (targetType == typeof(double))
        {
            if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dv)) return dv;
            return null;
        }
        if (targetType.IsEnum)
        {
            if (int.TryParse(str, out int ev)) return ev;
            if (Enum.TryParse(targetType, str, true, out object ev2)) return ev2;
            return null;
        }
        // ✅ Godot 颜色类型解析 (R,G,B,A) 或 Color 构造函数
        if (targetType == typeof(Color))
        {
            // 尝试从 Godot 的 ToString 格式解析，如 "Color(1, 0, 0, 1)"
            var colorMatch = System.Text.RegularExpressions.Regex.Match(str, @"Color\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)(?:,\s*([-\d.]+))?\)");
            if (colorMatch.Success)
            {
                float r = float.Parse(colorMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float g = float.Parse(colorMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float b = float.Parse(colorMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                float a = colorMatch.Groups[4].Success ? float.Parse(colorMatch.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) : 1.0f;
                return new Color(r, g, b, a);
            }
            // 尝试从 HTML 颜色格式解析（如 #FF0000）
            try
            {
                if (str.StartsWith("#"))
                {
                    return new Color(str);
                }
            }
            catch (System.Exception ex) { GD.PushWarning($"[TerrainEditor] {ex.GetType().Name}: {ex.Message}"); }
            return null;
        }
        // ✅ Godot Vector2 类型解析
        if (targetType == typeof(Vector2))
        {
            var vec2Match = System.Text.RegularExpressions.Regex.Match(str, @"\(([-\d.]+),\s*([-\d.]+)\)");
            if (vec2Match.Success)
            {
                float x = float.Parse(vec2Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(vec2Match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Vector2(x, y);
            }
            return null;
        }
        // ✅ Godot Vector2I 类型解析
        if (targetType == typeof(Vector2I))
        {
            var vec2iMatch = System.Text.RegularExpressions.Regex.Match(str, @"\(([-\d.]+),\s*([-\d.]+)\)");
            if (vec2iMatch.Success)
            {
                int x = int.Parse(vec2iMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int y = int.Parse(vec2iMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Vector2I(x, y);
            }
            return null;
        }
        // ✅ Godot Vector3 类型解析
        if (targetType == typeof(Vector3))
        {
            var vec3Match = System.Text.RegularExpressions.Regex.Match(str, @"\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)\)");
            if (vec3Match.Success)
            {
                float x = float.Parse(vec3Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(vec3Match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(vec3Match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Vector3(x, y, z);
            }
            return null;
        }
        // ✅ Godot Vector3I 类型解析
        if (targetType == typeof(Vector3I))
        {
            var vec3iMatch = System.Text.RegularExpressions.Regex.Match(str, @"\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)\)");
            if (vec3iMatch.Success)
            {
                int x = int.Parse(vec3iMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int y = int.Parse(vec3iMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                int z = int.Parse(vec3iMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Vector3I(x, y, z);
            }
            return null;
        }
        return null;
    }
}
