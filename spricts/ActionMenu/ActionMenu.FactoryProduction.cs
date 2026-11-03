// ActionMenu.FactoryProduction.cs - 超级兵工厂：三个生产口各一个按钮（DOOR1/2/3，开创人规矩）
// 流程：生产口N按钮 → 单位选择列表（全列表/白名单）→ 直接在该口出生
// 原作模式：免费+即刻行动；双菜单模式：即时1.2x/待机0.8x；红利期：免费3个即刻单位
using Godot;
using System.Linq;
using System.Collections.Generic;

public partial class ActionMenu
{
    private readonly Button[] factoryPortButtons = new Button[3];
    private VBoxContainer factoryChoiceContainer;
    private SuperFactory factoryTarget;
    private string factoryChosenUnit;
    private bool factoryInstantMode;
    private Grids factoryChosenExit;

    private void EnsureFactoryButtons()
    {
        if (factoryPortButtons[0] != null) return;
        var parent = (waitButton != null && waitButton.GetParent() != null) ? waitButton.GetParent() : GetParent();
        if (parent == null) return;
        int idx = (waitButton != null && waitButton.GetParent() != null) ? parent.GetChildren().IndexOf(waitButton) : parent.GetChildCount() - 1;
        for (int i = 0; i < 3; i++)
        {
            var btn = new Button();
            int port = i;
            TranslateGroup.SetText(btn, $"生产口{i + 1}");
            btn.Name = $"FactoryPort{i + 1}Button";
            btn.Pressed += () => OnFactoryPortPressed(port);
            parent.AddChild(btn);
            if (idx >= 0) parent.MoveChild(btn, idx + 1 + i);
            factoryPortButtons[i] = btn;
        }
    }

    // 由 ShowWeaponMenu 调用
    public void UpdateFactoryButtons()
    {
        EnsureFactoryButtons();
        foreach (var b in factoryPortButtons) if (b != null) b.Visible = false;
        if (currentWeapon == null || !IsInstanceValid(currentWeapon)) return;
        if (currentWeapon is SuperFactory f && f.CanProduceNow() && !Grids.IsForceActionMode)
        {
            factoryTarget = f;
            var exits = f.GetProductionExitGrids();
            for (int i = 0; i < 3 && i < exits.Count; i++)
            {
                factoryPortButtons[i].Visible = true;
                bool occupied = exits[i].infantries.Count > 0 || exits[i].weapons.Count > 0;
                bool used = i < f.portUsedThisTurn.Length && f.portUsedThisTurn[i];
                factoryPortButtons[i].Disabled = occupied || used;   // 被占用或本回合已用→置灰
                TranslateGroup.SetText(factoryPortButtons[i], used ? $"生产口{i + 1} ✓" : $"生产口{i + 1}");
            }
        }
    }

    private void OnFactoryPortPressed(int port)
    {
        if (factoryTarget == null || !IsInstanceValid(factoryTarget)) return;
        var exits = factoryTarget.GetProductionExitGrids();
        if (port >= exits.Count) return;
        factoryChosenExit = exits[port];
        Hide();
        ShowFactoryUnitChoices();
    }

    /// <summary>显示可生产单位列表（白名单优先；空=全列表）</summary>
    private void ShowFactoryUnitChoices()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || factoryTarget == null) return;
        string team = TeamHelper.GetPhaseTeamName(gm.turnPhase);

        if (factoryChoiceContainer == null)
        {
            factoryChoiceContainer = new VBoxContainer();
            factoryChoiceContainer.Name = "FactoryChoiceContainer";
            factoryChoiceContainer.Position = new Vector2(40, 60);
            factoryChoiceContainer.Size = new Vector2(180, 300);
            factoryChoiceContainer.TopLevel = true;   // 置顶：不被地图盖住
            gm.AddChild(factoryChoiceContainer);
        }

        foreach (Node child in factoryChoiceContainer.GetChildren())
            child.QueueFree();
        factoryChoiceContainer.Visible = true;

        var title = new Label();
        TranslateGroup.SetText(title, "选择生产单位");
        factoryChoiceContainer.AddChild(title);

        var units = factoryTarget.producibleUnitNames.Count > 0
            ? factoryTarget.producibleUnitNames.Where(u => UnitProductionDatabase.HasUnit(u)).ToList()
            : UnitProductionDatabase.GetAllUnitNames().ToList();

        factoryInstantMode = factoryTarget.instantMoveAfterProduction || factoryTarget.IsInDividend(team);

        foreach (var unitName in units)
        {
            var info = UnitProductionDatabase.GetInfo(unitName);
            int cost = factoryTarget.GetProductionCost(info.Cost, factoryInstantMode);
            string costText = cost <= 0 ? "免费" : $"{cost}G";
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(160, 30);
            TranslateGroup.SetText(btn, $"{info.DisplayName} [{costText}]");
            string captured = unitName;
            btn.Pressed += () =>
            {
                factoryChosenUnit = captured;
                factoryChoiceContainer.Visible = false;
                // 已选定生产口 → 直接出生；否则退回出口选择
                if (factoryChosenExit != null && IsInstanceValid(factoryChosenExit)
                    && factoryChosenExit.infantries.Count == 0 && factoryChosenExit.weapons.Count == 0)
                    ProduceFactoryUnit(factoryChosenExit);
                else
                    ShowFactoryExitChoices();
            };
            factoryChoiceContainer.AddChild(btn);
        }

        var cancel = new Button();
        TranslateGroup.SetText(cancel, "取消");
        cancel.Pressed += () => factoryChoiceContainer.Visible = false;
        factoryChoiceContainer.AddChild(cancel);
    }

    /// <summary>出口选择（兜底：未预选生产口时使用）</summary>
    private void ShowFactoryExitChoices()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || factoryTarget == null) return;

        var exits = factoryTarget.GetProductionExitGrids();
        var free = exits.Where(g => g.infantries.Count == 0 && g.weapons.Count == 0).ToList();
        if (free.Count == 0)
        {
            ShowFactoryToast(TranslateGroup.T("所有生产口被占用"));
            return;
        }
        if (free.Count == 1)
        {
            ProduceFactoryUnit(free[0]);
            return;
        }

        foreach (Node child in factoryChoiceContainer.GetChildren())
            child.QueueFree();
        factoryChoiceContainer.Visible = true;

        var title = new Label();
        TranslateGroup.SetText(title, "选择生产口");
        factoryChoiceContainer.AddChild(title);

        foreach (var exit in free)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(160, 30);
            TranslateGroup.SetText(btn, $"{TranslateGroup.T("出口")} ({exit.GridIndex.X},{exit.GridIndex.Y})");
            Grids captured = exit;
            btn.Pressed += () =>
            {
                factoryChoiceContainer.Visible = false;
                ProduceFactoryUnit(captured);
            };
            factoryChoiceContainer.AddChild(btn);
        }

        var cancel = new Button();
        TranslateGroup.SetText(cancel, "取消");
        cancel.Pressed += () => factoryChoiceContainer.Visible = false;
        factoryChoiceContainer.AddChild(cancel);
    }

    private void ProduceFactoryUnit(Grids exit)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || factoryTarget == null) return;
        string team = TeamHelper.GetPhaseTeamName(gm.turnPhase);

        bool instant = factoryInstantMode || factoryTarget.IsInDividend(team) || factoryTarget.instantMoveAfterProduction;
        string error = factoryTarget.ProduceUnitAt(factoryChosenUnit, exit, instant, team);
        if (!string.IsNullOrEmpty(error))
            ShowFactoryToast(error);
        else
            ShowFactoryToast($"{factoryChosenUnit} {TranslateGroup.T("已生产")}");
    }

    private void ShowFactoryToast(string message)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;
        var label = new Label();
        label.Text = message;
        label.Position = new Vector2(gm.GetViewport().GetVisibleRect().Size.X / 2 - 60, 40);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.3f));
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeFontSizeOverride("font_size", 14);
        label.TopLevel = true;
        gm.AddChild(label);
        var tween = label.CreateTween();
        tween.TweenProperty(label, "position", label.Position + new Vector2(0, -30), 1.2f).SetTrans(Tween.TransitionType.Quad);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 1.2f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }
}
