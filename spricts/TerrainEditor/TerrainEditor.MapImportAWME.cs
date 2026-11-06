using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · MapImportAWME（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ========== ✅ AWS Map Editor 多格式导入 (.aws/.aw2/.awm/.awd) ==========
    private void OpenAWMEImportDialog()
    {
        var dialog = new Panel();
        dialog.Name = "AWMEImportDialog";
        dialog.CustomMinimumSize = new Vector2(420, 280);
        dialog.SetAnchorsPreset(LayoutPreset.Center);
        dialog.ZIndex = 1400;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.12f, 0.18f, 0.97f);
        bg.SetCornerRadiusAll(16);
        bg.SetBorderWidthAll(3);
        bg.BorderColor = new Color(0.6f, 0.9f, 0.4f);
        dialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("margin_left", 20);
        vbox.AddThemeConstantOverride("margin_right", 20);
        vbox.AddThemeConstantOverride("margin_top", 16);
        vbox.AddThemeConstantOverride("margin_bottom", 16);
        vbox.AddThemeConstantOverride("separation", 12);

        var title = new Label();
        TranslateGroup.SetText(title, "🗺️ 导入 AW 地图");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.5f, 0.95f, 0.4f));
        vbox.AddChild(title);

        var fileBtn = new Button();
        TranslateGroup.SetText(fileBtn, "📁 选择 AW 地图文件");
        fileBtn.CustomMinimumSize = new Vector2(360, 45);
        fileBtn.AddThemeFontSizeOverride("font_size", 14);
        fileBtn.Pressed += () => {
            // ✅ Android：无存储权限则先发起请求并提示授权后重试
            if (!GameManager.EnsureStoragePermission())
            {
                ShowImportHint(TranslateGroup.T("⚠️ 需要存储权限才能导入文件\n请在系统设置中允许「所有文件访问」后重试"));
                return;
            }
            dialog.QueueFree();
            var fd = new FileDialog();
            fd.FileMode = FileDialog.FileModeEnum.OpenFile;
            fd.Access = FileDialog.AccessEnum.Filesystem;
            fd.Filters = new string[] { "*.aws ; AW Advance Wars", "*.aw2 ; AW2 Black Hole Rising", "*.awm ; AWM Advance Wars Map", "*.awd ; AWDS Dual Strike" };
            fd.Title = TranslateGroup.T("选择 AW 地图文件");
            fd.Size = new Vector2I(800, 600);
            // ✅ Android：默认打开下载目录，方便找到手机下载的文件
            if (OS.GetName() == "Android")
                fd.CurrentDir = OS.GetSystemDir(OS.SystemDir.Downloads);
            fd.FileSelected += (path) => {
                try {
                    byte[] fileData = File.ReadAllBytes(path);
                    ParseAWMEMap(fileData);
                }
                catch (Exception ex) { ShowImportHint($"❌ {TranslateGroup.T("读取失败")}: {ex.Message}"); }
                fd.QueueFree();
            };
            fd.Canceled += () => fd.QueueFree();
            AddChild(fd);
            fd.PopupCentered();
        };
        vbox.AddChild(fileBtn);

        var hint = new Label();
        TranslateGroup.SetText(hint, $"支持 .aws / .aw2 / .awm / .awd 格式\n{TranslateGroup.T("未知地形")}将保留为 GROUND\n不支持的单位/设施/兵器将自动跳过");
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.8f, 0.6f, 0.3f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        var closeBtn = new Button();
        TranslateGroup.SetText(closeBtn, "❌ 关闭");
        closeBtn.CustomMinimumSize = new Vector2(360, 40);
        closeBtn.AddThemeFontSizeOverride("font_size", 13);
        closeBtn.Pressed += () => dialog.QueueFree();
        vbox.AddChild(closeBtn);

        dialog.AddChild(vbox);
        AddChild(dialog);
    }

    private void ParseAWMEMap(byte[] fileData)
    {
        try
        {
            if (fileData == null || fileData.Length < 10)
            {
                ShowImportHint(TranslateGroup.T("❌ 文件过短，无法解析"));
                return;
            }

            if (gameManager != null) gameManager.blockVictoryCheck = true;
            ClearAllMapEntities();

            string header = System.Text.Encoding.ASCII.GetString(fileData, 0, 9);
            int offset = 10;
            int width = 0, height = 0;

            if (header == "AWSMap001")
            {
                if (fileData.Length < 13) { ShowImportHint(TranslateGroup.T("❌ AWS 文件过短")); return; }
                width = fileData[10];
                height = fileData[11]; 
                offset = 13;
            }
            else if (header == "AW2Map001" || header == "AWMap 001")
            {
                width = 30;
                height = 20;
                offset = 10;
            }
            else if (header == "AWDMap001")
            {
                if (fileData.Length < 11) { ShowImportHint(TranslateGroup.T("❌ AWD 文件过短")); return; }
                width = 30;
                height = 20;
                offset = 11;
            }
            else
            {
                ShowImportHint(TranslateGroup.T("❌ 不支持的文件格式"));
                return;
            }

            // ✅ 外部单屏格式：重置屏状态，避免沿用旧图的多屏设置被切成幽灵屏
            if (gridManager != null)
            {
                gridManager.screenCount = 1;
                gridManager.screenWidths = new int[0];
                gridManager.screenHeights = new int[0];
                gridManager.screenYOffsets = new int[0];
            }
            // ✅ 重新调整地图大小，创建/删除格子以匹配导入尺寸
            gridManager?.ResizeMap(width, height);

            int gridCount = width * height;
            int terrainDataSize = gridCount * 2;
            int unitDataSize = gridCount * 2;

            if (fileData.Length < offset + terrainDataSize + unitDataSize)
            {
                ShowImportHint(TranslateGroup.T("❌ 文件数据不完整"));
                return;
            }

            int importedTiles = 0;
            int skippedTiles = 0;
            int facilityCount = 0;
            int unitCount = 0;
            int weaponCount = 0;
            var unknownTerrainIds = new HashSet<int>();
            var unknownFacilityIds = new HashSet<int>();
            var unknownUnitIds = new HashSet<int>();
            var unknownWeaponIds = new HashSet<int>();

            // ✅ 修复：读取地形数据（纯地形层，不包含设施/单位/兵器）
            // AWS 格式是列优先（Column-major）：x * height + y
            // 每个格子 2 字节：Byte0=地形ID(0-255), Byte1=地形变体/方向
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int idx = offset + (x * height + y) * 2;
                    if (idx + 1 >= fileData.Length) continue;

                    // ✅ 修复：地形层使用单字节地形ID（0-255）
                    byte terrainId = fileData[idx];
                    byte terrainVariant = fileData[idx + 1];  // 变体/方向，暂时保留

                    if (x >= gridManager.map.GetLength(0) || y >= gridManager.map.GetLength(1))
                        continue;

                    var grid = gridManager.map[x, y];
                    if (grid == null) continue;

                    // 纯地形解析（0-255）
                    var terrain = GetAWMETerrainMapping((int)terrainId);
                    if (terrain == GridType.GROUND && terrainId != 0)
                    {
                        unknownTerrainIds.Add((int)terrainId);
                        skippedTiles++;
                    }
                    else
                    {
                        importedTiles++;
                    }
                    grid.gridType = terrain;
                }
            }

            // ✅ 修复：读取单位/设施/兵器数据（单位层）
            // 单位层同样是列优先，每个格子 2 字节
            // 0xFFFF 表示空
            int unitOffset = offset + width * height * 2;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int idx = unitOffset + (x * height + y) * 2;
                    if (idx + 1 >= fileData.Length) continue;

                    ushort rawVal = (ushort)(fileData[idx] | (fileData[idx + 1] << 8));

                    // 空格子检测
                    if (rawVal == 0xFFFF) continue;

                    if (x >= gridManager.map.GetLength(0) || y >= gridManager.map.GetLength(1))
                        continue;

                    var grid = gridManager.map[x, y];
                    if (grid == null) continue;

                    // 设施 (300-499)
                    if (rawVal >= 300 && rawVal <= 499)
                    {
                        var (facType, facTeam) = GetAWMEFacilityMapping((int)rawVal);
                        if (!string.IsNullOrEmpty(facType))
                        {
                            string scenePath = GetScenePathForFacilityType(facType);
                            if (!string.IsNullOrEmpty(scenePath))
                            {
                                SpawnFacilityAtGrid(grid, scenePath, facTeam ?? "Player0");
                                facilityCount++;
                            }
                            else
                            {
                                unknownFacilityIds.Add((int)rawVal);
                            }
                        }
                        else
                        {
                            unknownFacilityIds.Add((int)rawVal);
                        }
                    }
                    // 单位 (500-899)
                    else if (rawVal >= 500 && rawVal <= 899)
                    {
                        var (unitType, unitTeam) = GetAWMEUnitMapping((int)rawVal);
                        if (!string.IsNullOrEmpty(unitType))
                        {
                            string scenePath = GetScenePathForUnitType(unitType);
                            if (!string.IsNullOrEmpty(scenePath))
                            {
                                SpawnUnitAtGrid(grid, scenePath, unitTeam ?? "Player1");
                                unitCount++;
                            }
                            else
                            {
                                unknownUnitIds.Add((int)rawVal);
                            }
                        }
                        else
                        {
                            unknownUnitIds.Add((int)rawVal);
                        }
                    }
                    // 兵器 (900-1299)
                    else if (rawVal >= 900 && rawVal <= 1299)
                    {
                        var (wepType, wepTeam, variant) = GetAWMEWeaponMapping((int)rawVal);
                        if (!string.IsNullOrEmpty(wepType))
                        {
                            string scenePath = GetScenePathForWeaponType(wepType);
                            if (!string.IsNullOrEmpty(scenePath))
                            {
                                SpawnWeaponAtGrid(grid, scenePath, wepTeam ?? "Player0");
                                var spawned = grid.weapon;
                                if (spawned is BlackCannon cannon)
                                {
                                    cannon.direction = variant switch
                                    {
                                        0 => BlackCannon.CannonDirection.Down,
                                        1 => BlackCannon.CannonDirection.Up,
                                        2 => BlackCannon.CannonDirection.Right,
                                        3 => BlackCannon.CannonDirection.Left,
                                        _ => BlackCannon.CannonDirection.Down
                                    };
                                    cannon.UpdateDirectionVisual();
                                }
                                else if (spawned is Laser laser)
                                {
                                    laser.SetFireAngles(new List<float> { 0f, 90f, 180f, 270f });
                                    laser.UpdateLaserVisual();
                                }
                                weaponCount++;
                            }
                            else
                            {
                                unknownWeaponIds.Add((int)rawVal);
                            }
                        }
                        else
                        {
                            unknownWeaponIds.Add((int)rawVal);
                        }
                    }
                    else
                    {
                        unknownUnitIds.Add((int)rawVal);
                    }
                }
            }

            foreach (var grid in gridManager.grids)
            {
                if (grid != null && IsInstanceValid(grid))
                {
                    UpdateGridVisual(grid);
                }
            }

            // ✅ 关键：导入完成后刷新所有列表和状态
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.weaponManager?.RefreshWeaponList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            // ✅ 不再调用 ResetGameState()，因为 ResetForMapImport 已经设置了保护计时器
            // 保护计时器会在3秒后自动解除并检查胜利条件

            string summary = $"✅ {TranslateGroup.T("AW 地图导入成功")}！\n尺寸: {width}×{height} | 导入: {importedTiles} 格";
            if (facilityCount > 0) summary += $" | {TranslateGroup.T("设施")}: {facilityCount} 个";
            if (unitCount > 0) summary += $" | {TranslateGroup.T("单位")}: {unitCount} 个";
            if (weaponCount > 0) summary += $" | {TranslateGroup.T("兵器")}: {weaponCount} 个";
            if (skippedTiles > 0) summary += $" | {TranslateGroup.T("跳过")}: {skippedTiles} {TranslateGroup.T("格")}";

            string unknownInfo = "";
            if (unknownTerrainIds.Count > 0)
                unknownInfo += $"\n{TranslateGroup.T("未知地形")}: {string.Join(", ", unknownTerrainIds.OrderBy(i => i).Take(20))}";
            if (unknownFacilityIds.Count > 0)
                unknownInfo += $"\n未知{TranslateGroup.T("设施")}: {string.Join(", ", unknownFacilityIds.OrderBy(i => i).Take(20))}";
            if (unknownUnitIds.Count > 0)
                unknownInfo += $"\n未知{TranslateGroup.T("单位")}: {string.Join(", ", unknownUnitIds.OrderBy(i => i).Take(20))}";
            if (unknownWeaponIds.Count > 0)
                unknownInfo += $"\n未知{TranslateGroup.T("兵器")}: {string.Join(", ", unknownWeaponIds.OrderBy(i => i).Take(20))}";

            ShowImportHint(summary + unknownInfo);
        }
        catch (Exception ex)
        {
            if (gameManager != null) gameManager.blockVictoryCheck = false;
            ShowImportHint($"❌ AW 地图{TranslateGroup.T("导入失败")}:\n{ex.Message}");
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

    private GridType GetAWMETerrainMapping(int value)
    {
        if (value < 0 || value > 299) return GridType.GROUND;

        switch (value)
        {
            case 0: return GridType.GROUND;
            case 1: return GridType.ROAD;
            case 2: return GridType.BRIDGE;
            case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14:
                return GridType.RIVER;
            case 15: case 17: case 18: case 19: case 20: case 21: case 22: case 23: case 24: case 25: case 26: case 27:
                return GridType.ROAD;
            case 16: return GridType.PIPE;
            case 28: return GridType.SEA;
            case 29: case 31: case 32: case 39:
                return GridType.BEACH;
            case 30: return GridType.REEF;
            case 33: return GridType.REEF;
            case 60: return GridType.SEA;
            case 90: return GridType.FOREST;
            case 150: return GridType.HILL;
            case 167: return GridType.GROUND;
            case 226: return GridType.PIPESEAM;
            default:
                int x = value % 30;
                int y = value / 30;
                if (y == 0 && x == 16) return GridType.PIPE;
                if (y == 7 && x == 16) return GridType.PIPESEAM;
                return GridType.GROUND;
        }
    }

    private (string typeName, string team) GetAWMEFacilityMapping(int value)
    {
        if (value < 300 || value > 499) return (null, null);

        int baseVal = value - 300;
        int x = baseVal % 10;
        int y = baseVal / 10;

        string team = y == 0 ? "Player1" : (y == 1 ? "Player2" : "Player0");

        string typeName = x switch
        {
            0 => "HQ",
            1 => "City",
            2 => "Base",
            3 => "AirPort",
            4 => "Port",
            5 => "Tower",
            6 => "Lab",
            _ => null
        };

        if (string.IsNullOrEmpty(typeName)) return (null, null);

        if (typeName == "HQ" || typeName == "Port" || typeName == "Tower" || typeName == "Lab")
            return (null, null);

        return (typeName, team);
    }

    private (string typeName, string team) GetAWMEUnitMapping(int value)
    {
        if (value < 500 || value > 899) return (null, null);

        int baseVal = value - 500;
        int x = baseVal % 20;
        int y = baseVal / 20;

        string team = (y / 2) switch
        {
            0 => "Player1",
            1 => "Player2",
            _ => "Player0"
        };

        string typeName = x switch
        {
            0 => "infantry",
            20 => "mech",
            1 => "Md_Tank",
            21 => "light_tank",
            2 => "Recon",
            22 => "apc",
            3 => "Artillery",
            23 => "Rocket",
            4 => "AntiAir",
            24 => "Anti_Tank",
            12 => "oozium",
            32 => "FlyBomb",
            _ => null
        };

        return (typeName, team);
    }

    private (string typeName, string team, int variant) GetAWMEWeaponMapping(int value)
    {
        if (value < 900 || value > 1299) return (null, null, -1);

        int baseVal = value - 900;
        int x = baseVal % 20;
        int y = baseVal / 20;

        string team = (y / 2) switch
        {
            0 => "Player1",
            1 => "Player2",
            _ => "Player0"
        };

        // ✅ 修复：扩大映射范围，支持 AW2/AWD 中 DeathRay 和 BlackCannon 的不同编码
        // 同时支持 y=0,1,2,3 等多种变体，避免映射到错误类型
        string typeName = x switch
        {
            // MiniCannon (单格小炮) - 4方向
            0 => "black_cannon",
            1 => "black_cannon",

            // L-Cannon / Laser - 支持多 y 值
            2 => "Laser",

            // BlackCrystal / Crystal - 支持多 y 值  
            3 => "Crystal",

            // ✅ 关键修复：BlackCannon (大型黑炮) → LargeCannon
            // 支持所有 y 值，避免被错误映射
            4 => "large_cannon",

            // ✅ 关键修复：DeathRay → DeathRay
            // 支持所有 y 值（y=0,1,2,3），避免被错误映射为 Laser 或其他
            5 => "death_ray",

            // BlackObelisk / Crystal
            6 => "Crystal",

            // 扩展：处理更多可能的编码位置
            7 => "black_cannon",   // 额外的 MiniCannon 变体
            8 => "Laser",         // 额外的 Laser 变体
            9 => "Crystal",       // 额外的 Crystal 变体
            10 => "large_cannon", // 额外的 LargeCannon 变体
            11 => "death_ray",    // 额外的 DeathRay 变体
            12 => "Crystal",      // 额外的 Obelisk 变体

            _ => null
        };

        // Variant: 0=Down, 1=Up, 2=Right, 3=Left
        // ✅ 修复：根据 y 值计算正确的方向，而不是固定值
        int variant = x switch
        {
            0 => y % 4 switch { 0 => 1, 1 => 2, 2 => 0, 3 => 3, _ => 0 },
            1 => y % 4 switch { 0 => 3, 1 => 0, 2 => 1, 3 => 2, _ => 0 },
            2 => y % 4,           // Laser: 根据 y 值循环方向
            3 => 0,               // Crystal: 无方向
            4 => y % 4,           // ✅ LargeCannon: 根据 y 值正确设置方向
            5 => y % 4,           // ✅ DeathRay: 根据 y 值正确设置方向
            6 => 0,               // Obelisk: 无方向
            7 => y % 4,
            8 => y % 4,
            9 => 0,
            10 => y % 4,
            11 => y % 4,
            12 => 0,
            _ => 0
        };

        return (typeName, team, variant);
    }
}
