// TerrainEditor.TerrainPicker.cs
// CoW 编辑器移植三件套：
//   1. 地形视觉目录（贴图缓存 + 同名前缀变体扫描）—— 对标 CoW biome_palettes 的区域换肤
//   2. 点击格子弹出"快速切换地形"浮窗（带预览图，对标设施/单位模板按钮）
//   3. 批量地形笔刷（1x1/3x3/5x5 + 洪水填充），整批一条撤销记录
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class TerrainEditor
{
    // ==================== 地形视觉目录 ====================
    private static class TerrainVisuals
    {
        public const string TerrainArtDir = "res://art/Grids/地形";

        private static readonly Dictionary<GridType, string> DefaultTextureFile = new()
        {
            { GridType.FOREST, "Forest.png" },
            { GridType.ROAD, "ROAD.png" },
            { GridType.SEA, "Sea.png" },
            { GridType.RIVER, "River.png" },
            { GridType.PIPE, "Pipe.png" },
            { GridType.PIPESEAM, "PipeSeam.png" },
            { GridType.BROKENPIPE, "BrokenPipeSeam.png" },
            { GridType.PASSABLEPIPE, "Pipe.png" },
            { GridType.LAVA, "Lava.png" },
            { GridType.LAVASIDE, "LAVASide.png" },
            { GridType.BEACH, "Beach.png" },
            { GridType.REEF, "Reef.png" },
            { GridType.BRIDGE, "BRIDGE.png" },
            { GridType.TP, "TP.png" },
        };

        private static readonly Dictionary<string, Texture2D> cache = new();
        private static readonly Dictionary<GridType, List<string>> variantCache = new();

        public static Texture2D GetTexture(GridType type)
        {
            if (DefaultTextureFile.TryGetValue(type, out var file))
                return Load($"{TerrainArtDir}/{file}");
            return null;
        }

        /// <summary>取该地形的全部贴图变体路径（默认贴图 + 同前缀的全部皮肤，如 Forest.png / Forest2.png）</summary>
        public static IReadOnlyList<string> GetVariantPaths(GridType type)
        {
            if (variantCache.TryGetValue(type, out var cached)) return cached;
            var list = new List<string>();
            if (DefaultTextureFile.TryGetValue(type, out var file))
            {
                var baseName = file.Replace(".png", "");
                using var dir = DirAccess.Open(TerrainArtDir);
                if (dir != null)
                {
                    foreach (var f in dir.GetFiles())
                    {
                        if (!f.EndsWith(".png")) continue;
                        if (f == file || f.StartsWith(baseName))
                            list.Add($"{TerrainArtDir}/{f}");
                    }
                }
                if (list.Count == 0)
                    list.Add($"{TerrainArtDir}/{file}");
            }
            variantCache[type] = list;
            return list;
        }

        public static int GetVariantCount(GridType type) => GetVariantPaths(type).Count;

        public static Texture2D Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (cache.TryGetValue(path, out var t) && GodotObject.IsInstanceValid(t)) return t;
            var tex = GD.Load<Texture2D>(path);
            cache[path] = tex;
            return tex;
        }
    }

    // ==================== 会话级贴图变体 ====================
    // gridType → 贴图路径。同型格子全局换肤，新放置立即生效；与 ExtraSave 的 per-type 自定义贴图同构。
    private static readonly Dictionary<GridType, string> sessionTerrainTexture = new();

    public static Texture2D GetSessionTerrainTexture(GridType type)
        => sessionTerrainTexture.TryGetValue(type, out var p) ? TerrainVisuals.Load(p) : null;

    public static void ClearSessionTerrainTexture() => sessionTerrainTexture.Clear();

    private void ApplyTerrainTextureVariant(GridType type, string texturePath)
    {
        sessionTerrainTexture[type] = texturePath;
        var tex = TerrainVisuals.Load(texturePath);
        if (tex == null) return;
        var gm = PickerResolveGridManager();
        if (gm?.grids == null) return;
        foreach (var g in gm.grids)
        {
            if (g == null || !IsInstanceValid(g) || g.gridType != type) continue;
            var sprite = g.GetNodeOrNull<Sprite2D>("Sprite2D");
            if (sprite != null) sprite.Texture = tex;
        }
    }

    // ==================== 预览图挂件（对标设施/单位模板按钮） ====================
    private void AttachTerrainPreview(Button btn, GridType type)
    {
        const float previewSize = 30f;
        var x = btn.CustomMinimumSize.X - previewSize - 8f;
        var y = (btn.CustomMinimumSize.Y - previewSize) / 2f;
        var tex = TerrainVisuals.GetTexture(type);
        Control widget;
        if (tex != null)
        {
            widget = new TextureRect
            {
                Texture = tex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(x, y),
                Size = new Vector2(previewSize, previewSize),
                MouseFilter = MouseFilterEnum.Ignore,
            };
        }
        else
        {
            widget = new ColorRect
            {
                Color = terrainColors.GetValueOrDefault(type, Colors.Gray),
                Position = new Vector2(x, y),
                Size = new Vector2(previewSize, previewSize),
                MouseFilter = MouseFilterEnum.Ignore,
            };
        }
        btn.AddChild(widget);
    }

    // ==================== 点击快速切换地形浮窗 ====================
    private Panel quickSwitchPanel;
    private GridContainer quickSwitchGrid;
    private Label quickSwitchTitle;
    private Grids quickSwitchTarget;

    private void BuildQuickSwitchPanel()
    {
        quickSwitchPanel = new Panel();
        quickSwitchPanel.Name = "TerrainQuickSwitchPanel";
        quickSwitchPanel.CustomMinimumSize = new Vector2(372, 420);
        quickSwitchPanel.Visible = false;
        quickSwitchPanel.ZIndex = 1100;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.97f);
        style.SetCornerRadiusAll(14);
        style.SetBorderWidthAll(2);
        style.BorderColor = new Color(0.5f, 0.8f, 0.6f);
        quickSwitchPanel.AddThemeStyleboxOverride("panel", style);

        quickSwitchTitle = new Label();
        quickSwitchTitle.Position = new Vector2(10, 8);
        quickSwitchTitle.Size = new Vector2(352, 24);
        quickSwitchTitle.AddThemeFontSizeOverride("font_size", 13);
        quickSwitchTitle.AddThemeColorOverride("font_color", new Color(0.6f, 0.9f, 0.7f));
        quickSwitchPanel.AddChild(quickSwitchTitle);

        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(10, 36);
        scroll.Size = new Vector2(352, 300);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        quickSwitchPanel.AddChild(scroll);

        quickSwitchGrid = new GridContainer();
        quickSwitchGrid.Columns = 5;
        quickSwitchGrid.AddThemeConstantOverride("h_separation", 4);
        quickSwitchGrid.AddThemeConstantOverride("v_separation", 4);
        quickSwitchGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(quickSwitchGrid);

        var footer = new HBoxContainer();
        footer.Position = new Vector2(10, 346);
        footer.Size = new Vector2(352, 34);
        footer.AddThemeConstantOverride("separation", 6);

        var variantBtn = new Button();
        TranslateGroup.SetText(variantBtn, "换贴图");
        variantBtn.CustomMinimumSize = new Vector2(110, 32);
        variantBtn.AddThemeFontSizeOverride("font_size", 12);
        variantBtn.Pressed += CycleTextureVariantForCurrent;
        footer.AddChild(variantBtn);

        var propsBtn = new Button();
        TranslateGroup.SetText(propsBtn, "完整属性");
        propsBtn.CustomMinimumSize = new Vector2(110, 32);
        propsBtn.AddThemeFontSizeOverride("font_size", 12);
        propsBtn.Pressed += () =>
        {
            var g = quickSwitchTarget;
            CloseQuickSwitch();
            if (g != null) OpenTerrainMenu(g);
        };
        footer.AddChild(propsBtn);

        var closeBtn = new Button();
        TranslateGroup.SetText(closeBtn, "关闭");
        closeBtn.CustomMinimumSize = new Vector2(110, 32);
        closeBtn.AddThemeFontSizeOverride("font_size", 12);
        closeBtn.Pressed += CloseQuickSwitch;
        footer.AddChild(closeBtn);

        var tip = new Label();
        tip.Position = new Vector2(10, 386);
        tip.Size = new Vector2(352, 26);
        TranslateGroup.SetText(tip, "点地形图标直接切换 | 换贴图循环该地形全部皮肤");
        tip.AddThemeFontSizeOverride("font_size", 10);
        tip.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.6f));
        quickSwitchPanel.AddChild(tip);

        AddChild(quickSwitchPanel);
    }

    private void RebuildQuickSwitchItems()
    {
        foreach (var child in quickSwitchGrid.GetChildren())
            child.QueueFree();

        var current = quickSwitchTarget?.gridType ?? GridType.GROUND;
        quickSwitchTitle.Text = $"{TranslateGroup.T("快速切换地形")} ({TranslateGroup.T("当前")}: {GetTerrainDisplayName(current)})";

        foreach (var type in availableTerrainTypes)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(66, 66);
            var displayName = GetTerrainDisplayName(type);
            btn.Text = displayName.Length > 4 ? displayName[..4] : displayName;
            btn.AddThemeFontSizeOverride("font_size", 9);
            btn.ClipText = true;

            var color = terrainColors.GetValueOrDefault(type, Colors.Gray);
            var normal = new StyleBoxFlat();
            normal.BgColor = new Color(color.R * 0.3f, color.G * 0.3f, color.B * 0.3f, 0.9f);
            normal.SetCornerRadiusAll(8);
            if (type == current)
            {
                normal.SetBorderWidthAll(2);
                normal.BorderColor = Colors.Yellow;
            }
            btn.AddThemeStyleboxOverride("normal", normal);
            var hover = new StyleBoxFlat();
            hover.BgColor = new Color(color.R * 0.55f, color.G * 0.55f, color.B * 0.55f, 0.95f);
            hover.SetCornerRadiusAll(8);
            btn.AddThemeStyleboxOverride("hover", hover);

            var tex = TerrainVisuals.GetTexture(type);
            if (tex != null)
            {
                btn.Icon = tex;
                btn.ExpandIcon = true;
                btn.IconMaxWidth = 34;
            }
            btn.TextOverrunBehavior = TextServer.OverrunBehavior.TrimChar;

            GridType captured = type;
            btn.Pressed += () => ApplyTerrainQuick(captured);
            quickSwitchGrid.AddChild(btn);
        }
    }

    private void OpenTerrainQuickSwitch(Grids grid)
    {
        CloseSelectionMenu();
        selectedGrid = grid;
        quickSwitchTarget = grid;
        if (quickSwitchPanel == null) BuildQuickSwitchPanel();
        RebuildQuickSwitchItems();

        var vp = GetViewport().GetVisibleRect().Size;
        var mp = GetGlobalMousePosition();
        quickSwitchPanel.Position = new Vector2(
            Mathf.Clamp(mp.X, 0, Mathf.Max(0, vp.X - quickSwitchPanel.CustomMinimumSize.X)),
            Mathf.Clamp(mp.Y, 0, Mathf.Max(0, vp.Y - quickSwitchPanel.CustomMinimumSize.Y)));
        quickSwitchPanel.Visible = true;
        isMenuOpen = true;
    }

    private void CloseQuickSwitch()
    {
        if (quickSwitchPanel != null) quickSwitchPanel.Visible = false;
        quickSwitchTarget = null;
    }

    private void ApplyTerrainQuick(GridType newType)
    {
        var g = quickSwitchTarget ?? selectedGrid;
        if (g == null || !IsInstanceValid(g)) { CloseQuickSwitch(); return; }
        GridType old = g.gridType;
        if (old == newType) { CloseQuickSwitch(); return; }

        SaveCustomDamageValues(g);
        SaveAmmoFuelValues(g);
        g.gridType = newType;
        UpdateGridVisual(g);
        PushEditorAction(EditorActionType.PlaceTerrain, g, old);
        CloseQuickSwitch();
    }

    private void CycleTextureVariantForCurrent()
    {
        var g = quickSwitchTarget;
        if (g == null) return;
        var paths = TerrainVisuals.GetVariantPaths(g.gridType);
        if (paths.Count == 0) return;
        var cur = sessionTerrainTexture.TryGetValue(g.gridType, out var p) ? p : paths[0];
        int idx = (paths.IndexOf(cur) + 1) % paths.Count;
        ApplyTerrainTextureVariant(g.gridType, paths[idx]);
        if (quickSwitchTitle != null)
            quickSwitchTitle.Text = $"{TranslateGroup.T("快速切换地形")} - {GetTerrainDisplayName(g.gridType)} | {TranslateGroup.T("贴图")} {idx + 1}/{paths.Count}";
    }

    // ==================== 批量地形笔刷 ====================
    // 1x1 / 3x3 / 5x5 方形笔刷 + 洪水填充，整批合并为一条撤销记录（对标 CoW EditorPlacementSize + Fill）
    public int BatchBrushSize { get; private set; } = 1;
    public bool BatchFloodFill { get; private set; } = false;

    private void CreateBatchBrushBar(Panel panel)
    {
        var bar = new HBoxContainer();
        bar.Position = new Vector2(10, 486);
        bar.Size = new Vector2(320, 34);
        bar.AddThemeConstantOverride("separation", 5);

        foreach (var opt in new (string label, int size, bool flood)[]
        {
            ("1x1", 1, false), ("3x3", 3, false), ("5x5", 5, false), ("填充", 0, true)
        })
        {
            var b = new Button();
            TranslateGroup.SetText(b, opt.label);
            b.CustomMinimumSize = new Vector2(76, 32);
            b.ToggleMode = true;
            b.AddThemeFontSizeOverride("font_size", 12);
            b.ButtonPressed = (opt.flood && BatchFloodFill) || (!opt.flood && !BatchFloodFill && BatchBrushSize == opt.size);
            var captured = opt;
            b.Pressed += () =>
            {
                BatchFloodFill = captured.flood;
                BatchBrushSize = captured.size;
                foreach (var sib in bar.GetChildren().OfType<Button>())
                    sib.ButtonPressed = sib == b;
            };
            bar.AddChild(b);
        }
        panel.AddChild(bar);
    }

    private GridManager PickerResolveGridManager() => GetTree()?.GetFirstNodeInGroup("grid_manager") as GridManager;
    private MapEditorHelper PickerResolveHelper() => GetTree()?.GetFirstNodeInGroup("map_editor_helper") as MapEditorHelper;

    private List<Grids> GetBrushArea(Grids center, int size)
    {
        var result = new List<Grids>();
        var gm = PickerResolveGridManager();
        if (gm?.map == null || center == null) { result.Add(center); return result; }
        int radius = size / 2;
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = center.GridIndex.X + dx, y = center.GridIndex.Y + dy;
                if (x < 0 || x >= gm.map.GetLength(0) || y < 0 || y >= gm.map.GetLength(1)) continue;
                var g = gm.map[x, y];
                if (g != null && IsInstanceValid(g)) result.Add(g);
            }
        return result;
    }

    private void ApplyBatchBrushAt(Grids grid)
    {
        if (BatchFloodFill) { FloodFillTerrain(grid, batchTerrainType); return; }

        var targets = GetBrushArea(grid, BatchBrushSize);
        var parent = new EditorActionRecord
        {
            actionType = EditorActionType.FillArea,
            position = grid.GridIndex,
            newTerrain = batchTerrainType,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        int changed = 0;
        foreach (var g in targets)
        {
            if (g == null || !IsInstanceValid(g) || g.gridType == batchTerrainType) continue;
            var old = g.gridType;
            g.gridType = batchTerrainType;
            g.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
            UpdateGridVisual(g);
            parent.subActions.Add(new EditorActionRecord
            {
                actionType = EditorActionType.PlaceTerrain,
                position = g.GridIndex,
                oldTerrain = old,
                newTerrain = batchTerrainType
            });
            changed++;
        }
        if (changed > 0)
        {
            // ✅ 邻域方向刷新(自动优化开启的格子): 多格铺设改变邻接关系, 同 ApplyBatchDirectionConfig 的5x5范围
            var gm = PickerResolveGridManager();
            if (gm?.map != null)
            {
                int w = gm.map.GetLength(0), h = gm.map.GetLength(1);
                foreach (var g in targets)
                {
                    if (g == null || !IsInstanceValid(g)) continue;
                    var gi = g.GridIndex;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = gi.X + dx, ny = gi.Y + dy;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                                gm.map[nx, ny]?.RefreshDirectionFromNeighbors(gm.map);
                        }
                }
            }
            PickerResolveHelper()?.PushAction(parent);
            TranslateGroup.SetText(spawnHintLabel, $"已铺设 {changed} 格");
        }
    }

    private void FloodFillTerrain(Grids start, GridType newType)
    {
        var gm = PickerResolveGridManager();
        if (gm?.map == null || start == null) return;
        var target = start.gridType;
        if (target == newType) return;

        int w = gm.map.GetLength(0), h = gm.map.GetLength(1);
        var visited = new HashSet<Vector2I> { start.GridIndex };
        var queue = new Queue<Vector2I>();
        queue.Enqueue(start.GridIndex);

        var parent = new EditorActionRecord
        {
            actionType = EditorActionType.FillArea,
            position = start.GridIndex,
            oldTerrain = target,
            newTerrain = newType,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        int changed = 0;
        const int maxFill = 5000;

        while (queue.Count > 0 && changed < maxFill)
        {
            var p = queue.Dequeue();
            if (p.X < 0 || p.X >= w || p.Y < 0 || p.Y >= h) continue;
            var g = gm.map[p.X, p.Y];
            if (g == null || !IsInstanceValid(g) || g.gridType != target) continue;

            g.gridType = newType;
            g.BakeCowBrush(); // ✅ 放置/修改地形时把当前画笔风格烘焙进格子
            UpdateGridVisual(g);
            parent.subActions.Add(new EditorActionRecord
            {
                actionType = EditorActionType.PlaceTerrain,
                position = p,
                oldTerrain = target,
                newTerrain = newType
            });
            changed++;

            foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
            {
                var n = p + d;
                if (n.X >= 0 && n.X < w && n.Y >= 0 && n.Y < h && !visited.Contains(n))
                {
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        if (changed > 0)
        {
            // ✅ 邻域方向刷新: 按填充记录逐格5x5刷新
            foreach (var sub in parent.subActions)
            {
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = sub.position.X + dx, ny = sub.position.Y + dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                            gm.map[nx, ny]?.RefreshDirectionFromNeighbors(gm.map);
                    }
            }
            PickerResolveHelper()?.PushAction(parent);
            TranslateGroup.SetText(spawnHintLabel, $"已填充 {changed} 格");
        }
    }
}
