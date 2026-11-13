// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Victory（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{
    [ExportGroup("胜利结算")]
    [Export] public Control victoryPanel; 
    [Export] public RichTextLabel victoryTitleLabel; 
    [Export] public RichTextLabel  victorySubtitleLabel; 
    [Export] public Button restartButton;

    [ExportGroup("全灭胜利判定开关")]
    [Export] public bool p1AnnihilationVictoryEnabled = true;
    [Export] public bool p2AnnihilationVictoryEnabled = true;

    [ExportGroup("兵器摧毁胜利判定")]
    [Export] public bool weaponVictoryEnabled = true;   // ✅ 默认开：有旗兵器被全歼即分胜负（无旗地图不受影响）

    // ✅ 曾登场势力记录：从未有单位/兵器的势力不判负（修复地图刚生成时自动判负）
    private readonly HashSet<string> teamsWithPresence = new();

    // 动态移除单位
       public void CheckVictoryCondition()
{
        if (gameEnded) return;

        // ✅ 关键修复：编辑模式下完全跳过胜负判定
        var terrainEditor = GetTree()?.GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
        if (terrainEditor != null && terrainEditor.IsEditMode)
        {
            return;
        }

        // ✅ 导入保护期
        if (blockVictoryCheck) return;

        // ✅ 额外保护：如果单位列表正在刷新中，跳过判定
        if (unitManager?.AllUnits == null || weaponManager?.AllWeapons == null) return;

    // 获取所有存活单位（排除P-1和P0的中立单位）
    var allUnits = unitManager?.AllUnits.Where(u => IsInstanceValid(u) && u.health > 0).ToList();
    if (allUnits == null) return;

    // ✅ 记录曾登场势力：当前有单位或兵器的势力入册（含其盟友）；全灭判定只对入册势力生效
    foreach (var team in new[] { TeamHelper.Player1, TeamHelper.Player2 })
    {
        bool hasNow = allUnits.Any(u => u.team == team)
                      || (weaponManager?.AllWeapons.Any(w => IsInstanceValid(w) && !w.isDestroyed && w.team == team) ?? false);
        if (hasNow)
        {
            teamsWithPresence.Add(team);
            foreach (var ally in TeamHelper.GetAlliedTeams(team)) teamsWithPresence.Add(ally);
        }
    }

    // ✅ 胜利判定只统计 P1 和 P2 的单位，P-1/P0 不参与胜负判定
    var p1Units = allUnits.Where(u => u.team == TeamHelper.Player1).ToList();
    var p2Units = allUnits.Where(u => u.team == TeamHelper.Player2).ToList();
    var p0Units = allUnits.Where(u => u.team == TeamHelper.Player0).ToList();
    var pN1Units = allUnits.Where(u => u.team == TeamHelper.Player).ToList();

    // 兵器摧毁胜利判定（同样排除P-1和P0兵器）
    if (weaponVictoryEnabled && weaponManager != null)
    {
        CheckWeaponVictoryCondition(p1Units.Count > 0, p2Units.Count > 0);
        // 如果兵器胜利判定已触发，直接返回
        if (gameEnded) return;
    }

    // 势力失败判定（新增）：只有失败的势力失败，其他势力继续游戏
    // ✅ 排除P-1和P0，它们不会失败
    CheckFactionDefeatCondition();
    if (gameEnded) return;

    // 判定全灭胜利条件：某一方单位全灭且没有兵器（如果该方全灭判定开启）
    bool p1Alive = p1Units.Count > 0;
    bool p2Alive = p2Units.Count > 0;

    // ✅ 兵器也只统计P1/P2的
    bool p1HasWeapons = weaponManager?.AllWeapons.Any(w => IsInstanceValid(w) && !w.isDestroyed && w.team == TeamHelper.Player1) ?? false;
    bool p2HasWeapons = weaponManager?.AllWeapons.Any(w => IsInstanceValid(w) && !w.isDestroyed && w.team == TeamHelper.Player2) ?? false;

    // ✅ 盟友机制：势力组存活 = 本势力或任意盟友还有单位/兵器
    bool p1GroupAlive = p1Alive || p1HasWeapons || TeamGroupAlive(TeamHelper.Player1, allUnits);
    bool p2GroupAlive = p2Alive || p2HasWeapons || TeamGroupAlive(TeamHelper.Player2, allUnits);

    if (!p1GroupAlive && p2GroupAlive && p1AnnihilationVictoryEnabled && teamsWithPresence.Contains(TeamHelper.Player1))
    {
        ShowVictory("Player2", "Player1");
    }
    else if (!p2GroupAlive && p1GroupAlive && p2AnnihilationVictoryEnabled && teamsWithPresence.Contains(TeamHelper.Player2))
    {
        ShowVictory("Player1", "Player2");
    }
    else if (!p1GroupAlive && !p2GroupAlive && teamsWithPresence.Contains(TeamHelper.Player1) && teamsWithPresence.Contains(TeamHelper.Player2))
    {
        ShowVictory("Draw", "Draw");
    }
}

    // ✅ 盟友机制：势力组存活判定（本势力或任意盟友还有单位/兵器）
    private bool TeamGroupAlive(string team, List<Infantry> allUnits)
    {
        if (allUnits.Any(u => u.team == team)) return true;
        if (weaponManager?.AllWeapons.Any(w => IsInstanceValid(w) && !w.isDestroyed && w.team == team) ?? false) return true;
        foreach (var ally in TeamHelper.GetAlliedTeams(team))
        {
            if (allUnits.Any(u => u.team == ally)) return true;
            if (weaponManager?.AllWeapons.Any(w => IsInstanceValid(w) && !w.isDestroyed && w.team == ally) ?? false) return true;
        }
        return false;
    }

    // ✅ 兵器摧毁胜利判定：检查是否满足介入胜利的兵器条件
    // 修复：基于兵器原始归属团队判断，排除中立兵器，避免单一兵器被摧毁导致双方"获胜"
    private void CheckWeaponVictoryCondition(bool p1Alive, bool p2Alive)
    {
        if (weaponManager == null) return;

        // 获取所有标记为 contributesToVictory 的兵器（只统计P1/P2的，排除P-1/P0）
        var allVictoryWeapons = weaponManager.AllWeapons
            .Where(w => IsInstanceValid(w) && w.contributesToVictory 
                && (w.team == TeamHelper.Player1 || w.team == TeamHelper.Player2))
            .ToList();

        if (allVictoryWeapons.Count == 0) return; // 没有配置兵器胜利条件

        // ✅ 关键修正：只统计P1/P2的兵器
        var p1Targets = allVictoryWeapons.Where(w => w.team == TeamHelper.Player2).ToList();
        var p2Targets = allVictoryWeapons.Where(w => w.team == TeamHelper.Player1).ToList();

        // P1是否完成了摧毁P2目标兵器的任务
        bool p1TargetsAllDestroyed = p1Targets.Count == 0 || p1Targets.All(w => w.isDestroyed);
        // P2是否完成了摧毁P1目标兵器的任务  
        bool p2TargetsAllDestroyed = p2Targets.Count == 0 || p2Targets.All(w => w.isDestroyed);

        // 只有"有目标且全部摧毁"才算获胜，"无目标"不算获胜（避免平局bug）
        bool p1WinsByWeapon = p1Targets.Count > 0 && p1TargetsAllDestroyed && p1Alive;
        bool p2WinsByWeapon = p2Targets.Count > 0 && p2TargetsAllDestroyed && p2Alive;

        if (p1WinsByWeapon && !p2WinsByWeapon)
        {
            ShowVictory("Player1", "Player2");
        }
        else if (p2WinsByWeapon && !p1WinsByWeapon)
        {
            ShowVictory("Player2", "Player1");
        }
        else if (p1WinsByWeapon && p2WinsByWeapon)
        {
            ShowVictory("Draw", "Draw");
        }
        // 如果双方都无目标，不触发兵器胜利（由全灭判定或其他逻辑处理）
    }

    // 势力失败判定：检查是否有势力因全灭（无单位且无兵器）而失败
    // ✅ 只检查 P1 和 P2，P-1和P0 永远不会被判定为失败
    private void CheckFactionDefeatCondition()
    {
        if (gameEnded || blockVictoryCheck) return; // ✅ 导入期间跳过判定

        // ✅ 额外保护：确保列表已初始化
        if (unitManager?.AllUnits == null || weaponManager?.AllWeapons == null) return;

        var allUnits = unitManager?.AllUnits.Where(u => IsInstanceValid(u) && u.health > 0).ToList();
        var allWeapons = weaponManager?.AllWeapons.Where(w => IsInstanceValid(w) && !w.isDestroyed).ToList();

        bool anyNewDefeat = false;

        // ✅ 只检查 P1 和 P2，P-1/P0 永远不会被判定为失败
        var playableTeams = new[] { TeamHelper.Player1, TeamHelper.Player2 };
        foreach (var team in playableTeams)
        {
            if (defeatedTeams.Contains(team)) continue;

            // ✅ 从未登场（无单位无兵器）的势力不判负
            if (!teamsWithPresence.Contains(team)) continue;

            // ✅ 盟友机制：本势力或任意盟友还有单位/兵器 = 不判负（组存活）
            if (TeamGroupAlive(team, allUnits)) continue;

            bool hasUnits = allUnits?.Any(u => u.team == team) ?? false;
            bool hasWeapons = allWeapons?.Any(w => w.team == team) ?? false;

            if (!hasUnits && !hasWeapons)
            {
                defeatedTeams.Add(team);
                anyNewDefeat = true;
                ShowFactionDefeat(team);
            }
        }

        if (anyNewDefeat)
        {
            // 检查是否只剩一个势力
            var remaining = playableTeams.Where(t => !defeatedTeams.Contains(t)).ToList();
            if (remaining.Count == 0)
            {
                ShowVictory("Draw", "All factions defeated");
            }
            else if (remaining.Count == 1)
            {
                ShowVictory(remaining[0], "Other factions defeated");
            }
        }
    }

    private void ShowFactionDefeat(string team)
    {
        GD.Print($"[FactionDefeat] {team} has been defeated!");
        if (turnEndLabel != null)
        {
            turnEndLabel.Text = $"{team} 势力已被歼灭！";
            turnEndLabel.Show();
            var timer = GetTree().CreateTimer(2.0f);
            timer.Timeout += () => { if (turnEndLabel != null) turnEndLabel.Hide(); };
        }
    }

    /// <summary>✅ 触发某队伍判负（如HQ被占领）</summary>
    public void TriggerTeamLose(string losingTeam, string reason)
    {
        if (gameEnded) return;
        // 确定胜方：默认另一方
        string winner = losingTeam == "Player1" ? "Player2" : "Player1";
        ShowVictory(winner, losingTeam);
        // 可扩展：显示判负原因 reason
        System.Console.WriteLine($"[Victory] {losingTeam} 判负: {reason}");
    }

private void ShowVictory(string winner, string loser)
{
    gameEnded = true;
    SurvivalSession.Instance?.OnMapVictorious(winner);   // ✅ 开创人2026-11-14 生存模式：玩家胜→推进下一关/全通关结算
    CampaignSettlement.Instance?.OnVictoryDeclared(winner);   // ✅ 开创人2026-11-14 战役结算：胜利时刻统一记账

    if (victoryPanel == null) return;

    victoryPanel.Show();
    victoryPanel.ZIndex = 100;
    victoryPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect); // 全屏

    // 背景
    var bg = victoryPanel.GetNodeOrNull<ColorRect>("Background");
    if (bg != null)
    {
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.Color = new Color(0, 0, 0, 0.9f);
    }

    // 获取 VBoxContainer
    var vbox = victoryPanel.GetNodeOrNull<VBoxContainer>("VBoxContainer");
    if (vbox != null)
    {
        // ✅ 关键修复：让容器在屏幕正中间
        vbox.SetAnchorsPreset(Control.LayoutPreset.Center); // 锚点设为屏幕中心

        // 设置容器大小（固定大小以便居中计算）
        vbox.CustomMinimumSize = new Godot.Vector2(600, 300);
        vbox.Size = new Godot.Vector2(600, 300);

        // 设置容器内的对齐方式（对于VBox，Alignment控制水平轴）
        vbox.Alignment = BoxContainer.AlignmentMode.Center; // 水平居中

        // 子元素间距
        vbox.AddThemeConstantOverride("separation", 30);

        // ✅ 关键：设置 grow 模式确保居中生效
        vbox.GrowHorizontal = Control.GrowDirection.Both;
        vbox.GrowVertical = Control.GrowDirection.Both;
    }

    // 获取节点
    var titleLabel = victoryTitleLabel ?? vbox?.GetNodeOrNull<RichTextLabel>("VictoryTitleLabel");
    var subLabel = victorySubtitleLabel ?? vbox?.GetNodeOrNull<RichTextLabel>("VictorySubtitleLabel");
    var btn = restartButton ?? vbox?.GetNodeOrNull<Button>("RestartButton");

    // ========== 标题 ==========
    if (titleLabel != null)
    {
        titleLabel.BbcodeEnabled = false;
        if (winner == "Player1")
            TranslateGroup.SetText(titleLabel, "🔴 P1 获得胜利！");
        else if (winner == "Player2")
            TranslateGroup.SetText(titleLabel, "🔵 P2 获得胜利！");
        else if (winner == "Draw")
            TranslateGroup.SetText(titleLabel, "🤝 平局！");
        else
            titleLabel.Text = $"🏆 {winner} 获得胜利！";

        Color titleColor;
        if (winner == "Player1")
            titleColor = new Color(1, 0.2f, 0.2f);
        else if (winner == "Player2")
            titleColor = new Color(0.2f, 0.5f, 1);
        else if (winner == "Draw")
            titleColor = new Color(0.8f, 0.8f, 0.8f);
        else
            titleColor = new Color(1, 0.8f, 0.2f);
        titleLabel.AddThemeColorOverride("default_color", titleColor);
        titleLabel.AddThemeFontSizeOverride("normal_font_size", 56);

        // ✅ 关键修复：在容器中水平填充并居中
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; // 填充容器宽度
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.VerticalAlignment = VerticalAlignment.Center;
        titleLabel.CustomMinimumSize = new Godot.Vector2(0, 80); // 只设高度，宽度由容器决定

        titleLabel.Show();
    }

    // ========== 副标题 ==========
    if (subLabel != null)
    {
        subLabel.BbcodeEnabled = false;
        if (winner == "Player1")
            TranslateGroup.SetText(subLabel, "🔵 P2 战败");
        else if (winner == "Player2")
            TranslateGroup.SetText(subLabel, "🔴 P1 战败");
        else if (winner == "Draw")
            TranslateGroup.SetText(subLabel, "双方均未能取胜");
        else
            TranslateGroup.SetText(subLabel, "其他势力战败");
        subLabel.AddThemeColorOverride("default_color", new Color(0.8f, 0.8f, 0.8f));
        subLabel.AddThemeFontSizeOverride("normal_font_size", 32);

        // ✅ 同样设置填充
        subLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        subLabel.HorizontalAlignment = HorizontalAlignment.Center;
        subLabel.CustomMinimumSize = new Godot.Vector2(0, 50);

        subLabel.Show();
    }

    // ========== 按钮 ==========
    if (btn != null)
    {
        TranslateGroup.SetText(btn, "🔄 再来一局");
        btn.AddThemeFontSizeOverride("font_size", 24);
        btn.CustomMinimumSize = new Godot.Vector2(200, 60);

        // 按钮不需要 ExpandFill，因为它有固定宽度，VBox 会自动居中它

        var style = new StyleBoxFlat();
        if (winner == "Player1")
            style.BgColor = new Color(1, 0.3f, 0.3f);
        else if (winner == "Player2")
            style.BgColor = new Color(0.3f, 0.5f, 1);
        else if (winner == "Draw")
            style.BgColor = new Color(0.5f, 0.5f, 0.5f);
        else
            style.BgColor = new Color(1, 0.8f, 0.2f);
        style.SetCornerRadiusAll(12);
        btn.AddThemeStyleboxOverride("normal", style);

        btn.Show();
    }

}


private void CreateFallbackLabel(Control parent, string name, string text, int fontSize)
{
    var label = new Label(); // 使用普通Label而不是RichTextLabel，更简单可靠
    label.Name = name + "Label";
    label.Text = text;
    label.AddThemeFontSizeOverride("font_size", fontSize);
    label.AddThemeColorOverride("font_color", Colors.White);
    label.HorizontalAlignment = HorizontalAlignment.Center;
    label.CustomMinimumSize = new Godot.Vector2(400, fontSize + 20);

    parent.AddChild(label);
    label.Owner = parent;

}
private void SetupVictoryLayout()
{
    if (victoryPanel == null) return;

    var viewportSize = GetViewport().GetVisibleRect().Size;
    var centerX = viewportSize.X / 2;
    var centerY = viewportSize.Y / 2;

    // 标题位置（中上）
    if (victoryTitleLabel != null)
    {
        victoryTitleLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
        victoryTitleLabel.Position = new Godot.Vector2(centerX - 200, centerY - 120);
        victoryTitleLabel.Size = new Godot.Vector2(400, 80);
    }

    // 副标题位置（中）
    if (victorySubtitleLabel != null)
    {
        victorySubtitleLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
        victorySubtitleLabel.Position = new Godot.Vector2(centerX - 150, centerY - 20);
        victorySubtitleLabel.Size = new Godot.Vector2(300, 50);
    }

    // 按钮位置（中下）
    if (restartButton != null)
    {
        restartButton.SetAnchorsPreset(Control.LayoutPreset.Center);
        restartButton.Position = new Godot.Vector2(centerX - 100, centerY + 60);
    }
}

// 入场动画（淡入+缩放）
private void AnimateVictoryEntry()
{
    if (victoryPanel == null) return;

    victoryPanel.Modulate = new Color(1, 1, 1, 0); // 完全透明开始

    var tween = CreateTween();
    tween.SetTrans(Tween.TransitionType.Back);
    tween.SetEase(Tween.EaseType.Out);

    // 淡入
    tween.TweenProperty(victoryPanel, "modulate:a", 1.0f, 0.5f);

    // 标题弹跳效果
    if (victoryTitleLabel != null)
    {
        victoryTitleLabel.Scale = Godot.Vector2.Zero;
        var tween2 = CreateTween();
        tween2.SetTrans(Tween.TransitionType.Back);
        tween2.SetEase(Tween.EaseType.Out);
        tween2.TweenProperty(victoryTitleLabel, "scale", Godot.Vector2.One, 0.6f)
               .SetDelay(0.1f);
    }
}

// 如果Inspector没绑定节点，自动创建UI（备用方案）
private void CreateVictoryUI()
{
    // 创建主面板
    victoryPanel = new Control();
    victoryPanel.Name = "VictoryPanel";
    AddChild(victoryPanel);

    // 创建背景
    var bg = new ColorRect();
    bg.Name = "Background";
    bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    bg.Color = new Color(0, 0, 0, 0.9f);
    victoryPanel.AddChild(bg);

    // 创建标题标签
    victoryTitleLabel = new RichTextLabel();
    victoryTitleLabel.Name = "VictoryTitle";
    victoryTitleLabel.BbcodeEnabled = false; // 不用BBCode，用Modulate控制颜色
    victoryPanel.AddChild(victoryTitleLabel);

    // 创建副标题
    victorySubtitleLabel = new RichTextLabel();
    victorySubtitleLabel.Name = "VictorySubtitle";
    victorySubtitleLabel.BbcodeEnabled = false;
    victoryPanel.AddChild(victorySubtitleLabel);

    // 创建按钮
    restartButton = new Button();
    restartButton.Name = "RestartButton";
    restartButton.Pressed += OnRestartPressed;
    victoryPanel.AddChild(restartButton);

    victoryPanel.Hide(); // 初始隐藏
}
}
