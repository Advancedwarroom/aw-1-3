// ActionMenu.ForceSupport.cs —— 力场支援按钮（手动选目标）
using Godot;

public partial class ActionMenu
{
    private Button forceSupportButton;
    public Button ForceSupportButton => forceSupportButton;

    public void EnsureForceSupportButton()
    {
        if (forceSupportButton != null) return;
        forceSupportButton = new Button();
        forceSupportButton.Name = "ForceSupportButton";
        TranslateGroup.SetText(forceSupportButton, "力场支援", "Force Support");
        forceSupportButton.Pressed += () =>
        {
            if (currentUnit is Infantry u && u.forceSupportEnabled) u.BeginForceSupportSelection(currentUnit);
            Hide();
        };
        AddChild(forceSupportButton);
        (forceSupportButton.GetParent() as Container)?.MoveChild(forceSupportButton, spaceStrikeButton != null ? spaceStrikeButton.GetIndex() + 1 : GetChildCount() - 1);
    }

    public void UpdateForceSupportButtons()
    {
        if (forceSupportButton == null) return;
        bool ok = currentUnit is Infantry u && u.forceSupportEnabled && u.forceSupportManualMode;
        forceSupportButton.Visible = ok;
        if (ok) TranslateGroup.SetText(forceSupportButton, "力场支援", "Force Support");
    }
}
