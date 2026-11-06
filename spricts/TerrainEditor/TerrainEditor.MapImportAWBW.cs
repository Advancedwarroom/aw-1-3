using Godot;
using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;

// TerrainEditor · MapImportAWBW（由 TerrainEditor.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class TerrainEditor
{

    // ========== ✅ AWBW 地图导入 ==========
    private void OpenAWBWImportDialog()
    {
        var dialog = new Panel();
        dialog.Name = "AWBWImportDialog";
        dialog.CustomMinimumSize = new Vector2(420, 280);
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
        TranslateGroup.SetText(title, "🌐 导入 AWBW 地图");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1f));
        vbox.AddChild(title);

        var fileBtn = new Button();
        TranslateGroup.SetText(fileBtn, "📁 选择 AWBW 导出 txt 文件");
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
            fd.Filters = new string[] { "*.txt ; Text Files" };
            fd.Title = TranslateGroup.T("选择 AWBW 地图导出文件");
            fd.Size = new Vector2I(800, 600);
            // ✅ Android：默认打开下载目录，方便找到手机下载的文件
            if (OS.GetName() == "Android")
                fd.CurrentDir = OS.GetSystemDir(OS.SystemDir.Downloads);
            fd.FileSelected += (path) => {
                try { ParseAWBWMap(File.ReadAllText(path)); }
                catch (Exception ex) { ShowImportHint($"❌ {TranslateGroup.T("读取失败")}: {ex.Message}"); }
                fd.QueueFree();
            };
            fd.Canceled += () => fd.QueueFree();
            AddChild(fd); fd.PopupCentered();
        };
        vbox.AddChild(fileBtn);

        var clipBtn = new Button();
        TranslateGroup.SetText(clipBtn, "📋 从剪贴板导入 AWBW 地图");
        clipBtn.CustomMinimumSize = new Vector2(360, 45);
        clipBtn.AddThemeFontSizeOverride("font_size", 14);
        clipBtn.Pressed += () => {
            dialog.QueueFree();
            string content = DisplayServer.ClipboardGet();
            if (string.IsNullOrWhiteSpace(content)) { ShowImportHint(TranslateGroup.T("❌ 剪贴板为空")); return; }
            ParseAWBWMap(content);
        };
        vbox.AddChild(clipBtn);

        var hint = new Label();
        TranslateGroup.SetText(hint, "AWBW 格式：每行逗号分隔的地形ID\n不支持的地形将智能平替为最接近类型\n完全不支持的将保留为 GROUND");
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.8f, 0.6f, 0.3f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        var closeBtn = new Button();
        TranslateGroup.SetText(closeBtn, "❌ 关闭");
        closeBtn.CustomMinimumSize = new Vector2(360, 40);
        closeBtn.AddThemeFontSizeOverride("font_size", 14);
        closeBtn.AddThemeColorOverride("font_color", new Color(1, 0.5f, 0.5f));
        closeBtn.Pressed += () => dialog.QueueFree();
        vbox.AddChild(closeBtn);

        dialog.AddChild(vbox);
        AddChild(dialog);
    }

    /// <summary>
    /// AWBW 地形 ID → (GridType 地形, string 设施类型, string 设施归属)
    /// 设施类型为空表示不生成设施
    /// </summary>
    private (GridType terrain, string facilityType, string facilityTeam) GetAWBWTerrainMapping(int id)
    {
        switch (id)
        {
            // 基础地形
            case 1: return (GridType.GROUND, null, null);          // Plain
            case 2: return (GridType.HILL, null, null);             // Mountain
            case 3: return (GridType.FOREST, null, null);          // Wood
            case 28: return (GridType.SEA, null, null);            // Sea
            case 33: return (GridType.REEF, null, null);           // Reef
            case 195: return (GridType.TP, null, null);             // Teleporter

            // 河流 (4-14) → 平替为 RIVER
            case 4: case 5: case 6: case 7: case 8:
            case 9: case 10: case 11: case 12: case 13: case 14:
                return (GridType.RIVER, null, null);

            // 道路 (15-27) → 平替为 ROAD
            case 15: case 16: case 17: case 18: case 19:
            case 20: case 21: case 22: case 23: case 24:
            case 25: case 26: case 27:
                return (GridType.ROAD, null, null);

            // 浅滩 (29-32) → 平替为 BEACH
            case 29: case 30: case 31: case 32:
                return (GridType.BEACH, null, null);

            // 管道 (101-110) → 平替为 PIPE
            case 101: case 102: case 103: case 104: case 105:
            case 106: case 107: case 108: case 109: case 110:
                return (GridType.PIPE, null, null);

            // 管道接缝 (113-114) → PIPESEAM
            case 113: case 114:
                return (GridType.PIPESEAM, null, null);

            // 管道废墟 (115-116) → BROKENPIPE
            case 115: case 116:
                return (GridType.BROKENPIPE, null, null);

            // 中性设施 (34-36) → Player0
            case 34: return (GridType.GROUND, "City", "Player0");      // Neutral City
            case 35: return (GridType.GROUND, "Base", "Player0");      // Neutral Base
            case 36: return (GridType.GROUND, "AirPort", "Player0");   // Neutral Airport

            // Orange Star 设施 (38-41) → Player1
            case 38: return (GridType.GROUND, "City", "Player1");      // Orange Star City
            case 39: return (GridType.GROUND, "Base", "Player1");      // Orange Star Base
            case 40: return (GridType.GROUND, "AirPort", "Player1");  // Orange Star Airport

            // Blue Moon 设施 (43-46) → Player2
            case 43: return (GridType.GROUND, "City", "Player2");      // Blue Moon City
            case 44: return (GridType.GROUND, "Base", "Player2");      // Blue Moon Base
            case 45: return (GridType.GROUND, "AirPort", "Player2");  // Blue Moon Airport

            // Green Earth 设施 (48-50) → Player0
            case 48: return (GridType.GROUND, "City", "Player0");      // Green Earth City
            case 49: return (GridType.GROUND, "Base", "Player0");      // Green Earth Base
            case 50: return (GridType.GROUND, "AirPort", "Player0");  // Green Earth Airport

            // Yellow Comet 设施 (53-55) → Player0
            case 53: return (GridType.GROUND, "City", "Player0");      // Yellow Comet City
            case 54: return (GridType.GROUND, "Base", "Player0");      // Yellow Comet Base
            case 55: return (GridType.GROUND, "AirPort", "Player0");  // Yellow Comet Airport

            // Red Fire 设施 (81-83) → Player0
            case 81: return (GridType.GROUND, "City", "Player0");      // Red Fire City
            case 82: return (GridType.GROUND, "Base", "Player0");      // Red Fire Base
            case 83: return (GridType.GROUND, "AirPort", "Player0");  // Red Fire Airport

            // Grey Sky 设施 (86-88) → Player0
            case 86: return (GridType.GROUND, "City", "Player0");      // Grey Sky City
            case 87: return (GridType.GROUND, "Base", "Player0");      // Grey Sky Base
            case 88: return (GridType.GROUND, "AirPort", "Player0");  // Grey Sky Airport

            // Black Hole 设施 (91-93) → Player0
            case 91: return (GridType.GROUND, "City", "Player0");      // Black Hole City
            case 92: return (GridType.GROUND, "Base", "Player0");      // Black Hole Base
            case 93: return (GridType.GROUND, "AirPort", "Player0");  // Black Hole Airport

            // Brown Desert 设施 (96-98) → Player0
            case 96: return (GridType.GROUND, "City", "Player0");      // Brown Desert City
            case 97: return (GridType.GROUND, "Base", "Player0");      // Brown Desert Base
            case 98: return (GridType.GROUND, "AirPort", "Player0");  // Brown Desert Airport

            // Amber Blossom 设施 (117-119) → Player0
            case 117: return (GridType.GROUND, "AirPort", "Player0");  // Amber Blossom Airport
            case 118: return (GridType.GROUND, "Base", "Player0");      // Amber Blossom Base
            case 119: return (GridType.GROUND, "City", "Player0");      // Amber Blossom City

            // Jade Sun 设施 (122-124) → Player0
            case 122: return (GridType.GROUND, "AirPort", "Player0");  // Jade Sun Airport
            case 123: return (GridType.GROUND, "Base", "Player0");      // Jade Sun Base
            case 124: return (GridType.GROUND, "City", "Player0");      // Jade Sun City

            // Cobalt Ice 设施 (149-151) → Player0
            case 149: return (GridType.GROUND, "AirPort", "Player0");  // Cobalt Ice Airport
            case 150: return (GridType.GROUND, "Base", "Player0");      // Cobalt Ice Base
            case 151: return (GridType.GROUND, "City", "Player0");      // Cobalt Ice City

            // Pink Cosmos 设施 (156-158) → Player0
            case 156: return (GridType.GROUND, "AirPort", "Player0");  // Pink Cosmos Airport
            case 157: return (GridType.GROUND, "Base", "Player0");      // Pink Cosmos Base
            case 158: return (GridType.GROUND, "City", "Player0");      // Pink Cosmos City

            // Teal Galaxy 设施 (163-165) → Player0
            case 163: return (GridType.GROUND, "AirPort", "Player0");  // Teal Galaxy Airport
            case 164: return (GridType.GROUND, "Base", "Player0");      // Teal Galaxy Base
            case 165: return (GridType.GROUND, "City", "Player0");      // Teal Galaxy City

            // Purple Lightning 设施 (170-172) → Player0
            case 170: return (GridType.GROUND, "AirPort", "Player0");  // Purple Lightning Airport
            case 171: return (GridType.GROUND, "Base", "Player0");      // Purple Lightning Base
            case 172: return (GridType.GROUND, "City", "Player0");      // Purple Lightning City

            // Acid Rain 设施 (181-183) → Player0
            case 181: return (GridType.GROUND, "AirPort", "Player0");  // Acid Rain Airport
            case 182: return (GridType.GROUND, "Base", "Player0");      // Acid Rain Base
            case 183: return (GridType.GROUND, "City", "Player0");      // Acid Rain City

            // White Nova 设施 (188-190) → Player0
            case 188: return (GridType.GROUND, "AirPort", "Player0");  // White Nova Airport
            case 189: return (GridType.GROUND, "Base", "Player0");      // White Nova Base
            case 190: return (GridType.GROUND, "City", "Player0");      // White Nova City

            // Azure Asteroid 设施 (196-198) → Player0
            case 196: return (GridType.GROUND, "AirPort", "Player0");  // Azure Asteroid Airport
            case 197: return (GridType.GROUND, "Base", "Player0");      // Azure Asteroid Base
            case 198: return (GridType.GROUND, "City", "Player0");      // Azure Asteroid City

            // Noir Eclipse 设施 (203-205) → Player0
            case 203: return (GridType.GROUND, "AirPort", "Player0");  // Noir Eclipse Airport
            case 204: return (GridType.GROUND, "Base", "Player0");      // Noir Eclipse Base
            case 205: return (GridType.GROUND, "City", "Player0");      // Noir Eclipse City

            // Silver Claw 设施 (210-212) → Player0
            case 210: return (GridType.GROUND, "AirPort", "Player0");  // Silver Claw Airport
            case 211: return (GridType.GROUND, "Base", "Player0");      // Silver Claw Base
            case 212: return (GridType.GROUND, "City", "Player0");      // Silver Claw City

            // Umber Wilds 设施 (217-219) → Player0
            case 217: return (GridType.GROUND, "AirPort", "Player0");  // Umber Wilds Airport
            case 218: return (GridType.GROUND, "Base", "Player0");      // Umber Wilds Base
            case 219: return (GridType.GROUND, "City", "Player0");      // Umber Wilds City

            // 所有其他不支持的 ID → 跳过，返回 GROUND
            default:
                return (GridType.GROUND, null, null);
        }
    }

    private void ParseAWBWMap(string content)
    {
        try
        {
            if (gameManager != null) gameManager.blockVictoryCheck = true;
            ClearAllMapEntities();

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // ✅ 计算 AWBW 地图尺寸
            int mapWidth = 0;
            int mapHeight = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var ids = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                mapWidth = Math.Max(mapWidth, ids.Length);
                mapHeight++;
            }

            if (mapWidth > 0 && mapHeight > 0)
            {
                // ✅ 外部单屏格式：重置屏状态，避免沿用旧图的多屏设置被切成幽灵屏
                if (gridManager != null)
                {
                    gridManager.screenCount = 1;
                    gridManager.screenWidths = new int[0];
                    gridManager.screenHeights = new int[0];
                    gridManager.screenYOffsets = new int[0];
                }
                gridManager?.ResizeMap(mapWidth, mapHeight);
                // ✅ 关键：ResizeMap 后必须重新获取 grids 引用
                gridManager?.Init();
            }

            int importedTiles = 0;
            int skippedTiles = 0;
            int facilityCount = 0;
            var unknownIds = new HashSet<int>();

            for (int y = 0; y < lines.Length; y++)
            {
                string line = lines[y].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var ids = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int x = 0; x < ids.Length; x++)
                {
                    if (!int.TryParse(ids[x].Trim(), out int tileId))
                        continue;

                    var (terrain, facilityType, facilityTeam) = GetAWBWTerrainMapping(tileId);

                    // 记录未知ID：不是 Plain(1) 且返回 GROUND+无设施 的，视为未映射
                    bool isKnown = tileId == 1 || terrain != GridType.GROUND || facilityType != null;
                    if (!isKnown)
                    {
                        unknownIds.Add(tileId);
                        skippedTiles++;
                    }
                    else
                    {
                        importedTiles++;
                    }

                    // 应用地形到对应格子
                    if (x >= 0 && x < gridManager.map.GetLength(0) && y >= 0 && y < gridManager.map.GetLength(1))
                    {
                        var grid = gridManager.map[x, y];
                        if (grid != null)
                        {
                            grid.gridType = terrain;

                            // 生成设施
                            if (!string.IsNullOrEmpty(facilityType))
                            {
                                string scenePath = GetScenePathForFacilityType(facilityType);
                                if (!string.IsNullOrEmpty(scenePath))
                                {
                                    SpawnFacilityAtGrid(grid, scenePath, facilityTeam ?? "Player0");
                                    facilityCount++;
                                }
                            }
                        }
                    }
                }
            }

            // 刷新所有格子视觉
            foreach (var grid in gridManager.grids)
            {
                if (grid != null && IsInstanceValid(grid))
                {
                    UpdateGridVisual(grid);
                }
            }

            // 刷新游戏状态
            gameManager?.unitManager?.RefreshUnitList();
            gameManager?.UpdateUnitLists();
            gameManager?.RefreshSpecializedUnitLists();
            gameManager?.fogOfWarManager?.RefreshFog();
            gameManager?.ResetGameState();

            string unknownInfo = unknownIds.Count > 0
                ? $"\n{TranslateGroup.T("未映射ID")}: {string.Join(", ", unknownIds.OrderBy(i => i))}"
                : "";
            ShowImportHint($"✅ {TranslateGroup.T("AWBW 导入成功")}！\n导入: {importedTiles} 格 | {TranslateGroup.T("设施")}: {facilityCount} 个 | 跳过: {skippedTiles} 格{unknownInfo}");
        }
        catch (Exception ex)
        {
            if (gameManager != null) gameManager.blockVictoryCheck = false;
            ShowImportHint($"❌ AWBW {TranslateGroup.T("导入失败")}:\n{ex.Message}");
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
}
