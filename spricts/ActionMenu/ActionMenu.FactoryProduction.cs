// ActionMenu.FactoryProduction.cs - 超级兵工厂：三个生产口各一个按钮（DOOR1/2/3，开创人规矩）
// 流程：生产口N按钮 → 单位选择列表（全列表/白名单）→ 直接在该口出生
// 原作模式：免费+即刻行动；双菜单模式：即时1.2x/待机0.8x；红利期：免费3个即刻单位
using Godot;
using System.Linq;
using System.Collections.Generic;

public partial class ActionMenu
{
    private readonly Button[] factoryPortButtons = new Button[3];
    private Button factoryRotateButton;
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
        if (factoryRotateButton == null)
        {
            factoryRotateButton = new Button();
            TranslateGroup.SetText(factoryRotateButton, "旋转");
            factoryRotateButton.Name = "FactoryRotateButton";
            factoryRotateButton.Pressed += () =>
            {
                if (factoryTarget != null && IsInstanceValid(factoryTarget)) factoryTarget.RotateFactoryClockwise();
            };
            var parent = (waitButton != null && waitButton.GetParent() != null) ? waitButton.GetParent() : GetParent();
            if (parent != null)
            {
                parent.AddChild(factoryRotateButton);
                int idx2 = (waitButton != null) ? parent.GetChildren().IndexOf(waitButton) : parent.GetChildCount() - 1;
                if (idx2 >= 0) parent.MoveChild(factoryRotateButton, idx2 + 4);
            }
        }
        if (factoryRotateButton != null) factoryRotateButton.Visible = false;
        foreach (var b in factoryPortButtons) if (b != null) b.Visible = false;
        if (currentWeapon == null || !IsInstanceValid(currentWeapon)) return;
        if (currentWeapon is SuperFactory f && f.CanProduceNow() && !Grids.IsForceActionMode)
        {
            factoryTarget = f;
            if (factoryRotateButton != null) factoryRotateButton.Visible = f.factoryCanRotate;
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
            var am = GetTree().GetFirstNodeInGroup("action_menu");
            Node host = (am != null && am.GetParent() != null) ? am.GetParent() : gm;
            var topLayer = new CanvasLayer { Layer = 1300, Name = "FactoryChoiceTopLayer" };   // 顶层：不被地图盖住
            host.AddChild(topLayer);
            var scroll = new ScrollContainer();   // 可滚动：再长的兵种表也拖得到底
            scroll.CustomMinimumSize = new Vector2(220, 420);
            scroll.Position = new Vector2(40, 60);
            topLayer.AddChild(scroll);
            factoryChoiceContainer = new VBoxContainer();
            factoryChoiceContainer.Name = "FactoryChoiceContainer";
            factoryChoiceContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            scroll.AddChild(factoryChoiceContainer);
        }

        foreach (Node child in factoryChoiceContainer.GetChildren())
            child.QueueFree();
        factoryChoiceContainer.Visible = true;

        var title = new Label();
        TranslateGroup.SetText(title, "选择生产单位");
        factoryChoiceContainer.AddChild(title);

        bool updatable = factoryTarget.isUpdatable;   // 与ProduceUnitAt的白名单闸门同一字段
        var units = (factoryTarget.producibleUnitNames.Count > 0
            ? factoryTarget.producibleUnitNames.Where(u => UnitProductionDatabase.HasUnit(u))
            : UnitProductionDatabase.GetAllUnitNames())
            .Where(u => !updatable || UnitProductionWhitelist.CanProduce(u))   // 不能产的就不显示（开创人规矩）
            .ToList();
        if (units.Count == 0)
        {
            var emptyTip = new Label();
            TranslateGroup.SetText(emptyTip, "（无可生产单位——需占领实验室解锁）");
            factoryChoiceContainer.AddChild(emptyTip);
        }

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
                    ProduceFactoryUnit(factoryChosenExit, true);
                else
                    ShowFactoryExitChoices(true);
            };
            factoryChoiceContainer.AddChild(btn);
            if (factoryTarget.dualProductionMode)
            {
                // 双菜单模式：⚡立即(1.2×) / 😴待机(0.8×) 两按钮（开创人规矩：想产出来就玩多花钱，等一回合便宜）
                int instantCost = factoryTarget.GetProductionCost(Mathf.RoundToInt(info.Cost * factoryTarget.instantCostMultiplier), true);
                int standbyCost = factoryTarget.GetProductionCost(Mathf.RoundToInt(info.Cost * factoryTarget.standbyCostMultiplier), false);
                var btnI = new Button();
                btnI.CustomMinimumSize = new Vector2(160, 30);
                TranslateGroup.SetText(btnI, $"⚡{info.DisplayName} 立即 [{instantCost}G]");
                var btnS = new Button();
                btnS.CustomMinimumSize = new Vector2(160, 30);
                TranslateGroup.SetText(btnS, $"😴{info.DisplayName} 待机 [{standbyCost}G]");
                string cap = unitName;
                btnI.Pressed += () => { factoryChosenUnit = cap; factoryChoiceContainer.Visible = false; ProduceFactoryUnit(factoryChosenExit, true); };
                btnS.Pressed += () => { factoryChosenUnit = cap; factoryChoiceContainer.Visible = false; ProduceFactoryUnit(factoryChosenExit, false); };
                factoryChoiceContainer.AddChild(btnI);
                factoryChoiceContainer.AddChild(btnS);
                btn.Visible = false;
            }
        }

        var cancel = new Button();
        TranslateGroup.SetText(cancel, "取消");
        cancel.Pressed += () => factoryChoiceContainer.Visible = false;
        factoryChoiceContainer.AddChild(cancel);
    }

    /// <summary>出口选择（兜底：未预选生产口时使用）</summary>
    private void ShowFactoryExitChoices(bool dualInstant = true)
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
            ProduceFactoryUnit(free[0], dualInstant);
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
                ProduceFactoryUnit(captured, dualInstant);
            };
            factoryChoiceContainer.AddChild(btn);
        }

        var cancel = new Button();
        TranslateGroup.SetText(cancel, "取消");
        cancel.Pressed += () => factoryChoiceContainer.Visible = false;
        factoryChoiceContainer.AddChild(cancel);
    }

    private void ProduceFactoryUnit(Grids exit, bool dualInstant = true)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || factoryTarget == null) return;
        string team = TeamHelper.GetPhaseTeamName(gm.turnPhase);

        // 双菜单：严格按所点档位；非双菜单：维持旧语义
        bool instant = factoryTarget.dualProductionMode
            ? dualInstant
            : (factoryInstantMode || factoryTarget.IsInDividend(team) || factoryTarget.instantMoveAfterProduction);
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
        var am2 = GetTree().GetFirstNodeInGroup("action_menu");
        Node host2 = (am2 != null && am2.GetParent() != null) ? am2.GetParent() : gm;
        var toastLayer = host2.GetNodeOrNull<CanvasLayer>("FactoryChoiceTopLayer");
        if (toastLayer == null) { toastLayer = new CanvasLayer { Layer = 1300, Name = "FactoryChoiceTopLayer" }; host2.AddChild(toastLayer); }
        toastLayer.AddChild(label);
        var tween = label.CreateTween();
        tween.TweenProperty(label, "position", label.Position + new Vector2(0, -30), 1.2f).SetTrans(Tween.TransitionType.Quad);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 1.2f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }
}
