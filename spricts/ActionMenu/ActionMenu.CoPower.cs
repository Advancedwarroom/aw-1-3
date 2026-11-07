// ActionMenu.CoPower.cs - ✅ CO 三十模块体系：COP/SCOP 按钮
// 有 CO 的势力：能量条攒满 COP★ 显示"技能"按钮（COP），攒满 SCOP★ 显示"大招"按钮（SCOP）
// 点击 → 发动（能量清零，效果持续 durationTurns 个己方回合，回合结束自动过期）
// 模块8（healHp）示例：发动时全军回血
using Godot;
using System.Linq;

public partial class ActionMenu
{
    private Button copButton;
    private Button scopButton;
    private Button tagButton;
    private Button switchButton;

    public void EnsureCoPowerButtons()
    {
        if (copButton == null || !IsInstanceValid(copButton))
        {
            if (moveButton == null) return;
            copButton = new Button();
            copButton.Name = "CopButton";
            TranslateGroup.SetText(copButton, "技能");
            copButton.Pressed += OnCopPressed;
            var parent = moveButton.GetParent();
            parent.AddChild(copButton);
            parent.MoveChild(copButton, moveButton.GetIndex() + 1);
        }
        if (scopButton == null || !IsInstanceValid(scopButton))
        {
            if (moveButton == null) return;
            scopButton = new Button();
            scopButton.Name = "ScopButton";
            TranslateGroup.SetText(scopButton, "大招");
            scopButton.Pressed += OnScopPressed;
            var parent = moveButton.GetParent();
            parent.AddChild(scopButton);
            parent.MoveChild(scopButton, moveButton.GetIndex() + 2);
        }
        if (tagButton == null || !IsInstanceValid(tagButton))
        {
            if (moveButton == null) return;
            tagButton = new Button();
            tagButton.Name = "TagButton";
            TranslateGroup.SetText(tagButton, "合体技");
            tagButton.Pressed += OnTagPressed;
            var parent = moveButton.GetParent();
            parent.AddChild(tagButton);
            parent.MoveChild(tagButton, moveButton.GetIndex() + 3);
        if (switchButton == null || !IsInstanceValid(switchButton))
        {
            if (moveButton == null) return;
            switchButton = new Button();
            switchButton.Name = "SwitchCoButton";
            TranslateGroup.SetText(switchButton, "切换CO");
            switchButton.Pressed += OnSwitchPressed;
            parent.AddChild(switchButton);
            parent.MoveChild(switchButton, moveButton.GetIndex() + 4);
        }
        }
    }

    /// <summary>菜单刷新时的 COP/SCOP/Tag 按钮可见性（由 ShowMenu 调用）</summary>
    public void UpdateCoPowerButtons(Infantry unit)
    {
        EnsureCoPowerButtons();
        if (copButton == null || !IsInstanceValid(copButton)) return;
        if (scopButton == null || !IsInstanceValid(scopButton)) return;
        if (tagButton == null || !IsInstanceValid(tagButton)) return;

        string team = unit?.team;
        bool canCop = unit != null && CoModuleSystem.CanUsePower(team);
        bool canScop = unit != null && CoModuleSystem.CanUseSuperPower(team);
        bool canTag = unit != null && CoModuleSystem.CanUseTagPower(team);
        copButton.Visible = canCop;
        scopButton.Visible = canScop;
        tagButton.Visible = canTag;
        bool canSwitch = unit != null && CoModuleSystem.HasSwitchablePartner(team);
        if (switchButton != null && IsInstanceValid(switchButton)) switchButton.Visible = canSwitch;
        if (canCop)
            TranslateGroup.SetText(copButton, $"技能 {CoModuleSystem.GetCharge(team):F1}/{CoModuleSystem.GetProfile(team).powerStars}★");
        if (canScop)
            TranslateGroup.SetText(scopButton, $"大招 {CoModuleSystem.GetCharge(team):F1}/{CoModuleSystem.GetProfile(team).superStars}★");
        if (canTag)
            TranslateGroup.SetText(tagButton, $"合体技 {CoModuleSystem.GetCharge(team):F1}/{CoModuleSystem.GetTagPowerNeeded(team):F0}★");
    }

    private void OnCopPressed() => ActivateCoPower(false);
    private void OnScopPressed() => ActivateCoPower(true);

    private void OnSwitchPressed()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;
        string team = TeamHelper.GetPhaseTeamName(gm.turnPhase);
        var old = gm.GetNodeOrNull("CoSwitchPopup");
        if (old != null) { old.QueueFree(); return; }   // 再点=关闭
        var popup = new VBoxContainer { Name = "CoSwitchPopup" };
        popup.Position = new Vector2(60, 200);
        gm.AddChild(popup);
        var partners = CoModuleSystem.GetState(team)?.partners;
        if (partners != null)
            for (int i = 0; i < partners.Count; i++)
            {
                int idx = i;
                var b = new Button();
                bool used = CoModuleSystem.IsPartnerSwitchedThisTurn(team, idx);
                TranslateGroup.SetText(b, $"{partners[i].name}{(used ? $"（{TranslateGroup.T("已切换")}）" : "")}");
                b.Disabled = used;
                b.Pressed += () =>
                {
                    popup.QueueFree();
                    if (!CoModuleSystem.SwitchToPartner(team, idx, out var err)) GD.PrintErr(err);
                    Hide();
                };
                popup.AddChild(b);
            }
        var cancel = new Button();
        TranslateGroup.SetText(cancel, "取消");
        cancel.Pressed += () => popup.QueueFree();
        popup.AddChild(cancel);
    }
    private void OnTagPressed()
    {
        var unit = currentUnit;
        if (unit == null || !IsInstanceValid(unit)) return;
        string team = unit.team;
        if (!CoModuleSystem.ActivateTagPower(team)) return;
        // ✅ 回血/天气等发动时效果已统一在 CoModuleSystem.ExecuteActivationEffects 结算

        Hide();
        ShowMenu(unit, false);
    }

    private void ActivateCoPower(bool scop)
    {
        var unit = currentUnit;
        if (unit == null || !IsInstanceValid(unit)) return;
        string team = unit.team;

        bool ok = scop ? CoModuleSystem.ActivateSuperPower(team) : CoModuleSystem.ActivatePower(team);
        if (!ok) return;

        // ✅ 回血/天气等发动时效果已统一在 CoModuleSystem.ExecuteActivationEffects 结算
        Hide();
        // 发动后重新弹菜单（大招一般还允许本回合继续行动该单位）
        ShowMenu(unit, false);
    }
}
