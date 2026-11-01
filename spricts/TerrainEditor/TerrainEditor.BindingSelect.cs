// TerrainEditor.BindingSelect.cs - 图形化联动绑定选择模式
// 用户规格：
//   编辑模式选择要链接的目标/范围时，点击格子不弹出任何东西，而是「新增/删除」这个绑定；
//   点击正下方确认/取消键才能恢复正常。（为以后火山攻击范围打好铺垫）
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

public partial class TerrainEditor
{
    // ---------- 绑定选择模式状态 ----------
    private bool isBindingSelectMode = false;
    private List<Grids> bindingSelection = new();
    private Action<List<Grids>> bindingOnConfirm = null;
    private HBoxContainer bindingConfirmBar = null;
    private Label bindingHintLabel = null;

    /// <summary>
    /// 进入绑定选择模式。
    /// title：提示文字；initial：初始已绑定格；onConfirm：确认回调（null=取消）
    /// </summary>
    public void BeginBindingSelect(string title, List<Grids> initial, Action<List<Grids>> onConfirm)
    {
        // 先退出可能存在的旧模式
        if (isBindingSelectMode) CancelBindingSelect();

        isBindingSelectMode = true;
        bindingSelection = initial?.Where(g => g != null && IsInstanceValid(g)).ToList() ?? new List<Grids>();
        bindingOnConfirm = onConfirm;

        // 高亮已绑定格
        foreach (var g in bindingSelection) HighlightBindingGrid(g, true);

        // 顶部提示
        if (bindingHintLabel == null)
        {
            bindingHintLabel = new Label();
            bindingHintLabel.Name = "BindingSelectHint";
            bindingHintLabel.ZIndex = 100;
            AddChild(bindingHintLabel);
        }
        bindingHintLabel.Visible = true;
        TranslateGroup.SetText(bindingHintLabel, $"{title}（点击格子=增删绑定）");
        bindingHintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.3f));
        bindingHintLabel.AddThemeConstantOverride("outline_size", 4);
        bindingHintLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        bindingHintLabel.Position = new Vector2(20, 8);

        // 底部确认/取消条（正下方）
        if (bindingConfirmBar == null)
        {
            bindingConfirmBar = new HBoxContainer();
            bindingConfirmBar.Name = "BindingConfirmBar";
            bindingConfirmBar.ZIndex = 100;

            var okBtn = new Button();
            TranslateGroup.SetText(okBtn, "✅ 确认绑定");
            okBtn.CustomMinimumSize = new Vector2(110, 36);
            okBtn.Pressed += ConfirmBindingSelect;

            var cancelBtn = new Button();
            TranslateGroup.SetText(cancelBtn, "❌ 取消");
            cancelBtn.CustomMinimumSize = new Vector2(110, 36);
            cancelBtn.Pressed += CancelBindingSelect;

            bindingConfirmBar.AddChild(okBtn);
            bindingConfirmBar.AddChild(cancelBtn);
            AddChild(bindingConfirmBar);
        }
        bindingConfirmBar.Visible = true;
        // 放到屏幕底部中央
        var vp = GetViewport().GetVisibleRect().Size;
        bindingConfirmBar.Position = new Vector2(vp.X / 2 - 120, vp.Y - 60);

        // 关闭可能干扰的面板
        spawnMenuPanel?.Hide();
        terrainMenuPanel?.Hide();
    }

    /// <summary>绑定模式下的格子点击：切换（新增/删除）绑定，不弹任何菜单</summary>
    private void HandleBindingGridClick(Grids grid)
    {
        if (!isBindingSelectMode || grid == null) return;
        if (bindingSelection.Contains(grid))
        {
            bindingSelection.Remove(grid);
            HighlightBindingGrid(grid, false);
        }
        else
        {
            bindingSelection.Add(grid);
            HighlightBindingGrid(grid, true);
        }
    }

    /// <summary>确认：应用绑定并退出</summary>
    private void ConfirmBindingSelect()
    {
        var confirmed = bindingSelection.ToList();
        var cb = bindingOnConfirm;
        ExitBindingSelectMode();
        cb?.Invoke(confirmed);
    }

    /// <summary>取消：丢弃更改并退出</summary>
    private void CancelBindingSelect()
    {
        // 清除高亮
        foreach (var g in bindingSelection.ToList()) HighlightBindingGrid(g, false);
        ExitBindingSelectMode();
    }

    private void ExitBindingSelectMode()
    {
        isBindingSelectMode = false;
        bindingSelection.Clear();
        bindingOnConfirm = null;
        if (bindingHintLabel != null) bindingHintLabel.Visible = false;
        if (bindingConfirmBar != null) bindingConfirmBar.Visible = false;
    }

    /// <summary>绑定格高亮/取消高亮（用 Modulate 标记）</summary>
    private void HighlightBindingGrid(Grids grid, bool on)
    {
        if (grid == null || !IsInstanceValid(grid)) return;
        var sprite = grid.GetNodeOrNull<Sprite2D>("Sprite2D");
        if (sprite == null) return;
        // 记录原色只做一次（用 Meta）
        if (on)
        {
            if (!grid.HasMeta("binding_orig_modulate"))
                grid.SetMeta("binding_orig_modulate", sprite.Modulate);
            sprite.Modulate = new Color(1f, 0.4f, 0.4f, 0.95f);   // 红色高亮=已绑定
        }
        else
        {
            if (grid.HasMeta("binding_orig_modulate"))
            {
                sprite.Modulate = (Color)grid.GetMeta("binding_orig_modulate");
                grid.RemoveMeta("binding_orig_modulate");
            }
        }
    }

    /// <summary>是否处于绑定选择模式（供 GridClick 拦截判断）</summary>
    public bool IsBindingSelectMode => isBindingSelectMode;
}
