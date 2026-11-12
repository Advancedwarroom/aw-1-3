// ActionMenu.PushChoice.cs - 推动选择：转移方向 m 选 1 / 反弹距离 n-m 的推动前预选择菜单
// 推挤决议是同步虚拟推演，无法中途插菜单；改为推动前选定方向/距离，之后一次推完。
using Godot;
using System.Collections.Generic;

public partial class ActionMenu
{
    private Button pushChoiceButton;
    private bool isPushChoiceSelecting = false;
    private readonly List<Button> pushChoiceOptionButtons = new();

    public void EnsurePushChoiceButton()
    {
        if (pushChoiceButton != null && IsInstanceValid(pushChoiceButton)) return;
        if (moveButton == null) return;
        pushChoiceButton = new Button();
        pushChoiceButton.Name = "PushChoiceButton";
        TranslateGroup.SetText(pushChoiceButton, "推动选择");
        pushChoiceButton.Pressed += OnPushChoicePressed;
        var parent = moveButton.GetParent();
        parent.AddChild(pushChoiceButton);
        parent.MoveChild(pushChoiceButton, moveButton.GetIndex() + 1);
    }

    /// <summary>菜单刷新时的推动选择按钮可见性（由 ShowMenu 调用）</summary>
    public void UpdatePushChoiceButtonVisibility(Infantry unit)
    {
        EnsurePushChoiceButton();
        if (pushChoiceButton == null || !IsInstanceValid(pushChoiceButton)) return;
        bool hasChoice = unit != null && unit.pushEnabled
            && ((unit.transferDirections != null && unit.transferDirections.Count > 1 && !unit.transferAutoDirection)
                || (!unit.reboundAuto && unit.pushReboundEnabled && unit.reboundRangeMax > 0)
                || (unit.pushMitigationEnabled && unit.pushMitigationCeremony
                    && unit.mitigationSpreadDirections != null && unit.mitigationSpreadDirections.Count > 1 && !unit.mitigationAuto)
                || (unit.pushDistanceSelectionMode == 1 && unit.pushDistance > 1));
        pushChoiceButton.Visible = hasChoice;
        if (hasChoice)
            TranslateGroup.SetText(pushChoiceButton, isPushChoiceSelecting ? "取消选择" : "推动选择");
    }

    private void OnPushChoicePressed()
    {
        isPushChoiceSelecting = !isPushChoiceSelecting;
        ClearPushChoiceOptions();
        if (!isPushChoiceSelecting) return;
        var unit = currentUnit as Infantry;
        if (unit == null) { isPushChoiceSelecting = false; return; }
        var parent = pushChoiceButton.GetParent();
        int idx = pushChoiceButton.GetIndex() + 1;
        // 原文"0~360°自选几向"：Export 里的方向向量原样使用，几个向量=几个选项，不量化
        if (unit.transferDirections != null && !unit.transferAutoDirection)
        {
            foreach (var dir in unit.transferDirections)
            {
                if (dir == Vector2I.Zero) continue;
                var b = new Button();
                TranslateGroup.SetText(b, $"转移方向 {dir.X},{dir.Y}");
                b.Pressed += () => { unit.pushChosenTransferDirection = dir; unit.pushChosenReboundRange = null; ExitPushChoiceMode(); };
                parent.AddChild(b);
                parent.MoveChild(b, idx++);
                pushChoiceOptionButtons.Add(b);
            }
        }
        if (!unit.reboundAuto && unit.pushReboundEnabled && unit.reboundRangeMax > 0)
        {
            int lo = Mathf.Max(1, unit.reboundRangeMin);
            for (int n = unit.reboundRangeMax; n >= lo; n--)
            {
                int nn = n;
                var b = new Button();
                TranslateGroup.SetText(b, $"反弹距离 {nn}");
                b.Pressed += () => { unit.pushChosenReboundRange = nn; unit.pushChosenTransferDirection = null; ExitPushChoiceMode(); };
                parent.AddChild(b);
                parent.MoveChild(b, idx++);
                pushChoiceOptionButtons.Add(b);
            }
        }
        // 推动距离手动选择（pushDistanceSelectionMode=1 时）：1~pushDistance 单选，选完即出
        if (unit.pushDistanceSelectionMode == 1 && unit.pushDistance > 1)
        {
            for (int n = 1; n <= unit.pushDistance; n++)
            {
                int nn = n;
                var b = new Button();
                TranslateGroup.SetText(b, $"推动距离 {nn}");
                b.Pressed += () => { unit.pushChosenDistance = nn; ExitPushChoiceMode(); };
                parent.AddChild(b);
                parent.MoveChild(b, idx++);
                pushChoiceOptionButtons.Add(b);
            }
        }

        // 卸力均分方向 n 选 m：多选开关，绿色=选中
        if (unit.pushMitigationEnabled && unit.pushMitigationCeremony && !unit.mitigationAuto
            && unit.mitigationSpreadDirections != null && unit.mitigationSpreadDirections.Count > 1)
        {
            foreach (var d in unit.mitigationSpreadDirections)
            {
                if (d == Vector2I.Zero) continue;
                var dir = d;
                bool on = unit.pushChosenSpreadDirections.Contains(dir);
                var b = new Button();
                TranslateGroup.SetText(b, $"均分方向 {dir.X},{dir.Y}{(on ? " ✓" : "")}");
                if (on) b.Modulate = new Color(0.6f, 1f, 0.6f);
                b.Pressed += () =>
                {
                    if (unit.pushChosenSpreadDirections.Contains(dir))
                        unit.pushChosenSpreadDirections.Remove(dir);
                    else
                        unit.pushChosenSpreadDirections.Add(dir);
                    OnPushChoicePressed(); // 重绘选中态
                };
                parent.AddChild(b);
                parent.MoveChild(b, idx++);
                pushChoiceOptionButtons.Add(b);
            }
        }
        if (pushChoiceOptionButtons.Count == 0) isPushChoiceSelecting = false;
    }

    private void ExitPushChoiceMode()
    {
        isPushChoiceSelecting = false;
        ClearPushChoiceOptions();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm != null && currentUnit is Infantry u) ShowMenu(u);
    }

    private void ClearPushChoiceOptions()
    {
        foreach (var b in pushChoiceOptionButtons)
            if (IsInstanceValid(b)) b.QueueFree();
        pushChoiceOptionButtons.Clear();
    }
}
