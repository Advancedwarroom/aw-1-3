// ActionMenu.FactoryProduction.cs - 超级兵工厂生产按钮 + 生产选择UI（动态创建，参照导弹井发射按钮模式）
// 流程：兵器菜单"生产"按钮 → 单位选择列表（全列表/白名单）→ 出口选择（3个生产口）→ 生成
// 原作模式：免费+即刻行动；双菜单模式：即时1.2x/待机0.8x；红利期：免费3个即刻单位
using Godot;
using System.Linq;
using System.Collections.Generic;

public partial class ActionMenu
{
    private Button factoryProduceButton;
    private VBoxContainer factoryChoiceContainer;
    private SuperFactory factoryTarget;
    private string factoryChosenUnit;
    private bool factoryInstantMode;

    private void EnsureFactoryButtons()
    {
        if (factoryProduceButton == null)
        {
            factoryProduceButton = new Button();
            TranslateGroup.SetText(factoryProduceButton, "生产");
            factoryProduceButton.Name = "FactoryProduceButton";
            if (waitButton != null && waitButton.GetParent() != null)
            {
                var idx = waitButton.GetParent().GetChildren().IndexOf(waitButton);
                waitButton.GetParent().AddChild(factoryProduceButton);
                if (idx >= 0) factoryProduceButton.GetParent().MoveChild(factoryProduceButton, idx + 1);
            }
            else GetParent()?.AddChild(factoryProduceButton);
            factoryProduceButton.Pressed += OnFactoryProducePressed;
        }
    }

    // 由 ShowWeaponMenu 调用
    public void UpdateFactoryButtons()
    {
        EnsureFactoryButtons();
        if (factoryProduceButton != null) factoryProduceButton.Visible = false;
        if (currentWeapon == null || !IsInstanceValid(currentWeapon)) return;
        if (currentWeapon is SuperFactory f && f.CanProduceNow() && !Grids.IsForceActionMode)
        {
            factoryProduceButton.Visible = true;
            factoryTarget = f;
        }
    }

    private void OnFactoryProducePressed()
    {
        if (factoryTarget == null || !IsInstanceValid(factoryTarget)) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) return;

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
            gm.AddChild(factoryChoiceContainer);
        }

        foreach (Node child in factoryChoiceContainer.GetChildren())
            child.QueueFree();
        factoryChoiceContainer.Visible = true;

        // 标题
        var title = new Label();
        TranslateGroup.SetText(title, "选择生产单位");
        factoryChoiceContainer.AddChild(title);

        // 单位列表：白名单 or 全列表
        var units = factoryTarget.producibleUnitNames.Count > 0
            ? factoryTarget.producibleUnitNames.Where(u => UnitProductionDatabase.HasUnit(u)).ToList()
            : UnitProductionDatabase.GetAllUnitNames().ToList();

        // 红利期：默认即时模式
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
                ShowFactoryExitChoices();
            };
            factoryChoiceContainer.AddChild(btn);
        }

        // 取消
        var cancel = new Button();
        TranslateGroup.SetText(cancel, "取消");
        cancel.Pressed += () => factoryChoiceContainer.Visible = false;
        factoryChoiceContainer.AddChild(cancel);
    }

    /// <summary>显示出口选择（三个生产口，按当前朝向）</summary>
    private void ShowFactoryExitChoices()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || factoryTarget == null) return;
        string team = TeamHelper.GetPhaseTeamName(gm.turnPhase);

        var exits = factoryTarget.GetProductionExitGrids();

        // 只有一个可用出口 → 直接用
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

        // 多个出口 → 选择
        if (factoryChoiceContainer == null) ShowFactoryUnitChoices();  // 初始化容器
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

        // 红利期强制即时
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
        label.ZIndex = 100;
        gm.AddChild(label);
        var tween = label.CreateTween();
        tween.TweenProperty(label, "position", label.Position + new Vector2(0, -30), 1.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 1.2f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }
}
