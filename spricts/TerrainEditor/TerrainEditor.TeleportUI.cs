// TerrainEditor.TeleportUI.cs - ✅ N屏地图：传送链路编辑模式
// 流程：点"进入传送编辑模式"（自动关面板）→ 先点源格（高亮）→ 再点目标格 → 成链
//       N:M：同一个源格可以多点几次目标格，形成一对多；一个目标格也可被多个源格指向
//       互传模式开时，成链自动补反向链；再点已有链的源格=删除该源格全部出链
// ESC 或再次进入可退出模式
using Godot;
using System.Linq;

public partial class TerrainEditor
{
    private bool isTeleportLinkMode = false;      // 传送链编辑模式
    private Vector2I? pendingTeleportSource = null; // 已选源格
    private bool teleportMutualDefault = false;   // 互传模式（默认单向）

        private Control _tpModeBar;

/// <summary>进入传送链编辑模式（关闭配置面板以便点格子）</summary>
    public void EnterTeleportLinkMode()
    {
        if (gridManager == null)
        {
            ShowImportHint(TranslateGroup.T("❌ GridManager 未初始化"));
            return;
        }
        isTeleportLinkMode = true;
        BuildTpModeBar();   // ✅ 常驻控制条（步骤提示+互传+完成/取消）
        pendingTeleportSource = null;
        if (globalConfigPanel != null && globalConfigPanel.Visible) globalConfigPanel.Visible = false;
        ShowImportHint($"🔗 {TranslateGroup.T("传送编辑模式：先点源格，再点目标格成链（ESC退出）")}");
    }

    /// <summary>退出传送链编辑模式</summary>
    public void CancelTeleportLinkMode()
    {
        isTeleportLinkMode = false;
        pendingTeleportSource = null;
        // ✅ 退出彻底清理（修复：残留状态卡死生成菜单/无法再次进入）
        pendingTeleportSource = null;
        isTeleportLinkMode = false;
        FreeTpModeBar();
    }

    public bool IsTeleportLinkMode => isTeleportLinkMode;

    /// <summary>传送模式下的格子点击</summary>
    private void HandleTeleportGridClick(Grids grid)
    {
        if (!isTeleportLinkMode || grid == null) return;

        // 第一次点：选源格
        if (pendingTeleportSource == null)
        {
            pendingTeleportSource = grid.GridIndex;
            grid.attackRangeIcon?.Show();
            if (grid.attackRangeIcon != null)
                grid.attackRangeIcon.Modulate = new Color(0.4f, 0.8f, 1f, 0.9f);
            ShowImportHint($"🔗 {TranslateGroup.T("源格已选")}: [{grid.GridIndex.X},{grid.GridIndex.Y}] → {TranslateGroup.T("请点目标格")}");
            return;
        }

        // 第二次点：成链（源=目标则取消）
        var from = pendingTeleportSource.Value;
        if (from == grid.GridIndex)
        {
            pendingTeleportSource = null;
            ShowImportHint(TranslateGroup.T("已取消源格，请重新点源格"));
            return;
        }

        gridManager.AddTeleportLink(from, grid.GridIndex, !teleportMutualDefault);
        ShowImportHint($"✅ {TranslateGroup.T("传送链已建")}: [{from.X},{from.Y}] → [{grid.GridIndex.X},{grid.GridIndex.Y}] {(teleportMutualDefault ? TranslateGroup.T("（互传）") : TranslateGroup.T("（单向）"))}\n{TranslateGroup.T("可继续点下一个目标格（N:M），或点新源格")}");

        // 保持源格不变，可连续建一对多
        // 若想换源格：点当前源格本身即取消，再点新源格
    }

    /// <summary>刷新配置面板里的传送链路列表</summary>
    private void RefreshTeleportPanelList(VBoxContainer container)
    {
        if (container == null) return;
        foreach (var child in container.GetChildren()) child.QueueFree();

        if (gridManager?.teleportLinks == null || gridManager.teleportLinks.Count == 0)
        {
            var empty = new Label();
            TranslateGroup.SetText(empty, "（暂无传送链：进入编辑模式，先点源格再点目标格）");
            empty.AddThemeFontSizeOverride("font_size", 12);
            container.AddChild(empty);
            return;
        }

        foreach (var link in gridManager.teleportLinks.ToList())
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var label = new Label();
            label.Text = $"[{link.from.X},{link.from.Y}] → [{link.to.X},{link.to.Y}]";
            label.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(label);

            var delBtn = new Button();
            TranslateGroup.SetText(delBtn, "删除");
            delBtn.AddThemeFontSizeOverride("font_size", 11);
            var captured = link;
            delBtn.Pressed += () => {
                gridManager.RemoveTeleportLink(captured);
                RefreshTeleportPanelList(container);
            };
            row.AddChild(delBtn);

            container.AddChild(row);
        }

        // 清空按钮
        var clearBtn = new Button();
        TranslateGroup.SetText(clearBtn, "🗑 清空全部传送链");
        clearBtn.AddThemeFontSizeOverride("font_size", 12);
        clearBtn.Pressed += () => {
            gridManager.ClearTeleportLinks();
            RefreshTeleportPanelList(container);
        };
        container.AddChild(clearBtn);
    }

    // ✅ 传送模式常驻控制条：步骤提示 + 互传开关 + 完成/取消
    private void BuildTpModeBar()
    {
        if (_tpModeBar != null) return;
        var bar = new HBoxContainer();
        bar.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        bar.Position = new Vector2(-260, 6);
        bar.AddThemeConstantOverride("separation", 8);
        var hint = new Label { Text = TranslateGroup.T("🔗 传送编辑：①点源格 ②点目标格") };
        var mutual = new CheckBox { Text = TranslateGroup.T("⇄ 互传"), ButtonPressed = creatingMutualLink };
        mutual.Toggled += on => creatingMutualLink = on;
        var done = new Button { Text = TranslateGroup.T("✅ 完成") };
        done.Pressed += () => { CancelTeleportLinkMode(); RestoreGlobalConfigPanel(); };
        var cancel = new Button { Text = TranslateGroup.T("↩ 取消") };
        cancel.Pressed += CancelTeleportLinkMode;
        bar.AddChild(hint); bar.AddChild(mutual); bar.AddChild(done); bar.AddChild(cancel);
        AddChild(bar);
        _tpModeBar = bar;
    }

    private void FreeTpModeBar()
    {
        if (_tpModeBar != null) { _tpModeBar.QueueFree(); _tpModeBar = null; }
    }
}

