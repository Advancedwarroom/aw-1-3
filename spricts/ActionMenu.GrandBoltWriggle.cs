// ActionMenu.GrandBoltWriggle.cs —— GrandBolt 手动蠕动按钮（选武器时显示）
using Godot;

public partial class ActionMenu
{
    private Button grandBoltWriggleButton;
    public Button GrandBoltWriggleButton => grandBoltWriggleButton;

    public void EnsureGrandBoltWriggleButton()
    {
        if (grandBoltWriggleButton != null) return;
        grandBoltWriggleButton = new Button();
        grandBoltWriggleButton.Name = "GrandBoltWriggleButton";
        TranslateGroup.SetText(grandBoltWriggleButton, "蠕动", "Wriggle");
        grandBoltWriggleButton.Pressed += () =>
        {
            if (currentWeapon is GrandBolt gb && gb.CanWriggle())
            {
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                gb.BeginWriggleSelection(gm);
            }
            Hide();
        };
        AddChild(grandBoltWriggleButton);
        (grandBoltWriggleButton.GetParent() as Container)?.MoveChild(grandBoltWriggleButton, ForceSupportButton != null ? ForceSupportButton.GetIndex() + 1 : GetChildCount() - 1);
    }

    public void UpdateGrandBoltWriggleButtons()
    {
        EnsureGrandBoltWriggleButton();
        bool ok = currentWeapon is GrandBolt gb && gb.wriggleEnabled && gb.CanWriggle();
        grandBoltWriggleButton.Visible = ok;
        if (ok) TranslateGroup.SetText(grandBoltWriggleButton, "蠕动", "Wriggle");
    }
}
