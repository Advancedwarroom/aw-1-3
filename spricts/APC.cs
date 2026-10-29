// APC.cs
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class APC : Infantry
{
    private Tween loadedIconTween;

    public override void _Ready()
    {
        base._Ready();
        SetupLoadedIcon();
        UpdateLoadedIcon();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.RegisterUnit(this, UnitCategory.APC);
    }

    public override string GetIdleAnimName() => team == "Player2" ? "apc2" : "apc1";

    private void SetupLoadedIcon()
    {
        if (loadedIcon == null) return;
        var spriteFrames = GD.Load<SpriteFrames>("res://asscets/AnimatedSprite/loaded.tres");
        if (spriteFrames == null) return;
        loadedIcon.SpriteFrames = spriteFrames;
        loadedIcon.Animation = "loaded";
        loadedIcon.Hide();
        loadedIcon.ZIndex = 200;
    }

    private void StartLoadedIconBlink()
    {
        if (loadedIcon == null) return;
        StopLoadedIconBlink();
        loadedIcon.Show();
        loadedIcon.Play();
        var timer = new Timer();
        timer.WaitTime = 1.0f;
        timer.OneShot = false;
        timer.Autostart = true;
        timer.Timeout += () => { if (loadedIcon != null && IsInstanceValid(loadedIcon)) loadedIcon.Visible = !loadedIcon.Visible; };
        AddChild(timer);
        SetMeta("blink_timer", timer);
    }

    private void StopLoadedIconBlink()
    {
        if (loadedIconTween != null && loadedIconTween.IsValid()) { loadedIconTween.Kill(); loadedIconTween = null; }
        if (HasMeta("blink_timer"))
        {
            var timer = GetMeta("blink_timer").As<Timer>();
            if (timer != null && IsInstanceValid(timer)) { timer.Stop(); timer.QueueFree(); }
            RemoveMeta("blink_timer");
        }
        if (loadedIcon != null) { loadedIcon.Hide(); loadedIcon.Modulate = Colors.White; loadedIcon.Visible = false; }
    }

    public override void OnDestroyed()
    {
        StopLoadedIconBlink();
        foreach (var unit in transportedUnits.ToList())
        {
            if (unit != null && IsInstanceValid(unit))
            {
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                gm?.RemovePiece(unit);
                unit.QueueFree();
            }
        }
        transportedUnits.Clear();
        base.OnDestroyed();
    }

    public override void OnWaitSelected()
    {
        isMoved = true; isAttacked = true; hasActed = true; state = UnitState.Acted; originalGrid = null;
        ShowIdleState();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

    public override void InputClick(Node viewport, InputEvent inputs, int shape_index)
    {
        if (inputs is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm == null) return;

            if (grid != null)
            {
                var mountableUnit = grid.infantries
                    .FirstOrDefault(u => u != this && u.team == this.team && !u.isAttacked && u.state != UnitState.Acted && this.CanTransportUnit(u));
                if (mountableUnit != null)
                {
                    gm.gridManager?.CloseRange();
                    gm.gridManager?.HideAttackRange();
                    gm.gridManager?.ClearWeaponRange();
                    if (gm.selectedInfantry != null && gm.selectedInfantry != mountableUnit) gm.ClearSelectedInfantry();
                    gm.selectedWeapon = null;
                    gm.isSelectingAttackTarget = false;
                    var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    actionMenu?.Hide();
                    actionMenu?.ShowTransportLoadMenu(this, mountableUnit);
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            bool isMyTurn = gm.IsTurnPhaseValid(team);
            if (!isMyTurn) return;

            if (Input.IsKeyPressed(Key.Ctrl))
            {
                bool IsMyTurn = gm.IsTurnPhaseValid(team);
                bool isEnemy = !IsMyTurn;
                if ((IsMyTurn && isMoved) || isEnemy)
                {
                    gm.gridManager.CloseRange();
                    gm.gridManager.HideAttackRange();
                    var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    actionMenu?.Hide();
                    ShowUnitInfo();
                    return;
                }
            }

            if (this.transportedUnits.Count > 0)
            {
                var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                actionMenu?.ShowTransportUnloadMenu(this);
                return;
            }

            base.InputClick(viewport, inputs, shape_index);
        }
    }

    public string GetAPCInfo()
    {
        string info = $"[APC] 装甲运兵车\n团队: {team}\n生命值: {health}/{maxHealth}\n燃料: {fuel}/{maxFuel}\n移动力: {movePoints}\n装载容量: {maxTransportCapacity}\n当前装载: {transportedUnits.Count}\n补给范围: {minSupplyRange}-{maxSupplyRange}\n";
        if (transportedUnits.Count > 0)
        {
            info += TranslateGroup.T("\n[装载单位]\n");
            foreach (var unit in transportedUnits)
                if (unit != null && IsInstanceValid(unit))
                    info += $"- {unit.Name} (HP:{unit.health})\n";
        }
        return info;
    }
}
