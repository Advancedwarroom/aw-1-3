// ActionMenu.ArcStrike.cs - 黑色弧光兵器"天基打击"按钮（动态创建，十字准星瞄准）
using Godot;

public partial class ActionMenu
{
    private Button arcStrikeButton;

    private void EnsureArcStrikeButton()
    {
        if (arcStrikeButton != null) return;
        arcStrikeButton = new Button();
        TranslateGroup.SetText(arcStrikeButton, "天基打击");
        arcStrikeButton.Name = "ArcStrikeButton";
        if (attackButton != null && attackButton.GetParent() != null)
        {
            var idx = attackButton.GetParent().GetChildren().IndexOf(attackButton);
            attackButton.GetParent().AddChild(arcStrikeButton);
            if (idx >= 0) arcStrikeButton.GetParent().MoveChild(arcStrikeButton, idx + 1);
        }
        else GetParent()?.AddChild(arcStrikeButton);
        arcStrikeButton.Pressed += OnArcStrikePressed;
    }

    // 由 ShowWeaponMenu 调用
    public void UpdateArcStrikeButton()
    {
        EnsureArcStrikeButton();
        if (arcStrikeButton != null) arcStrikeButton.Visible = false;
        if (currentWeapon == null || !IsInstanceValid(currentWeapon)) return;
        if (currentWeapon is BlackArc arc && arc.ArcStrikeReady && !currentWeapon.hasActed)
        {
            if (arcStrikeButton != null) arcStrikeButton.Visible = true;
        }
    }

    private void OnArcStrikePressed()
    {
        if (currentWeapon == null || !IsInstanceValid(currentWeapon)) return;
        if (currentWeapon is not BlackArc arc) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        Hide();
        arc.BeginArcTargeting(gm); // ✅ 进入十字准星瞄准模式
    }
}
