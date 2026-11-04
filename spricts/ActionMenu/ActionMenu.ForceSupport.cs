// ActionMenu.ForceSupport.cs —— 力场支援/解除力场 按钮（动态创建，不依赖.tscn连线）
using Godot;

public partial class ActionMenu
{
    private Button forceSupportButton;
    private Button dispelForceButton;
    public Button ForceSupportButton => forceSupportButton;
    public Button DispelForceButton => dispelForceButton;

    public void EnsureForceSupportButton()
    {
        if (forceSupportButton == null)
        {
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
        if (dispelForceButton == null)
        {
            dispelForceButton = new Button();
            dispelForceButton.Name = "DispelForceButton";
            TranslateGroup.SetText(dispelForceButton, "解除力场", "Dispel Field");
            dispelForceButton.Pressed += () =>
            {
                if (currentUnit is Infantry u2 && u2.forceSupportCanDispel) u2.BeginDispelForceFieldSelection(currentUnit);
                Hide();
            };
            AddChild(dispelForceButton);
            (dispelForceButton.GetParent() as Container)?.MoveChild(dispelForceButton, forceSupportButton.GetIndex() + 1);
        }
    }

    public void UpdateForceSupportButtons()
    {
        EnsureForceSupportButton();   // ✅ 懒创建（原实现漏此调用，力场支援按钮从未生成）
        bool ok = currentUnit is Infantry u && u.forceSupportEnabled && u.forceSupportManualMode;
        forceSupportButton.Visible = ok;
        if (ok) TranslateGroup.SetText(forceSupportButton, "力场支援", "Force Support");
        bool okDispel = currentUnit is Infantry u2 && u2.forceSupportCanDispel && u2.forceSupportManualMode;
        dispelForceButton.Visible = okDispel;
        if (okDispel) TranslateGroup.SetText(dispelForceButton, "解除力场", "Dispel Field");
    }
}
