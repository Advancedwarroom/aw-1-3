// ActionMenu.Teleport.cs - ✅ N屏地图：传送（TP）按钮
// 单位站在有出链的格子上时菜单显示"传送"：
//   第一次按 → 进入传送选择模式，高亮全部目标格（N:M：一个源格可有多条出链）
//   点击目标格 → 立即传送（✅ 消耗本回合行动：传送完成后该单位当场待机，不可再攻击/移动）
//   ✅ 每回合限一次：hasTeleportedThisTurn=true，本回合内不可再传送（也不能直接传回），回合结束重置
//   再按一次按钮或点空处 → 取消
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class ActionMenu
{
    private Button teleportButton;          // 传送按钮（动态创建）
    private bool isTeleportSelecting = false; // 正在选择传送目标

    // ---------- 按钮 ----------
    public void EnsureTeleportButton()
    {
        if (teleportButton != null && IsInstanceValid(teleportButton)) return;
        if (moveButton == null) return;
        teleportButton = new Button();
        teleportButton.Name = "TeleportButton";
        TranslateGroup.SetText(teleportButton, "传送");
        teleportButton.Pressed += OnTeleportPressed;
        var parent = moveButton.GetParent();
        parent.AddChild(teleportButton);
        // 放在移动按钮后面
        int idx = moveButton.GetIndex();
        parent.MoveChild(teleportButton, idx + 1);
    }

    /// <summary>菜单刷新时的传送按钮可见性（由 ShowMenu 调用）</summary>
    public void UpdateTeleportButtonVisibility(Infantry unit)
    {
        EnsureTeleportButton();
        if (teleportButton == null || !IsInstanceValid(teleportButton)) return;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        var links = gm?.gridManager?.GetOutgoingTeleportLinks(unit?.grid) ?? new List<TeleportLink>();
        // 站在有出链的格子上、本回合未行动、未被眩晕、且本回合未传送过时可用
        // ✅ hasTeleportedThisTurn：每回合限传送一次——传送后（即使未待机）也不能直接传回或再次传送
        bool canTP = links.Count > 0 && unit != null
            && !unit.hasActed && unit.state != UnitState.Acted
            && !unit.isAttacked && !unit.IsStunned && unit.CanMove()
            && !unit.hasTeleportedThisTurn;
        teleportButton.Visible = canTP;
        if (canTP)
            TranslateGroup.SetText(teleportButton, isTeleportSelecting ? "取消传送" : "传送");
    }

    // ---------- 按钮回调 ----------
    private void OnTeleportPressed()
    {
        if (currentUnit == null || !IsInstanceValid(currentUnit)) return;
        if (isTeleportSelecting)
        {
            CancelTeleportSelect();
            ShowMenu(currentUnit, false);
            return;
        }
        BeginTeleportSelect();
    }

    // ---------- 进入/退出选择模式 ----------
    private void BeginTeleportSelect()
    {
        var unit = currentUnit;
        if (unit == null || unit.grid == null) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || gm.gridManager == null) return;

        var links = gm.gridManager.GetOutgoingTeleportLinks(unit.grid);
        if (links.Count == 0) return;

        isTeleportSelecting = true;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        foreach (var link in links)
        {
            var dest = gm.gridManager.GetGridAt(link.to);
            if (dest == null) continue;
            // 目标格被敌军占据或地形不可站则不可选（提示性高亮仍显示）
            dest.attackRangeIcon?.Show();
            if (dest.attackRangeIcon != null)
                dest.attackRangeIcon.Modulate = new Color(0.4f, 0.8f, 1f, 0.85f); // 青色=传送目标
            dest.IsInWeaponRange = true;
            dest.OnClickGrid = (to) =>
            {
                if (!IsInstanceValid(this)) return;
                if (IsInstanceValid(unit))
                    PerformTeleport(unit, to);
            };
            gm.gridManager.OverrideUnitInput(dest, true);
        }

        // 范围外点击取消
        foreach (var g in gm.gridManager.grids)
        {
            if (links.Any(l => l.to == g.GridIndex)) continue;
            g.OnClickEmpty = () => { if (IsInstanceValid(this)) CancelTeleportSelect(); };
        }

        Hide();
        if (teleportButton != null && IsInstanceValid(teleportButton))
            TranslateGroup.SetText(teleportButton, "取消传送");
    }

    private void CancelTeleportSelect()
    {
        isTeleportSelecting = false;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;
        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        foreach (var g in gm.gridManager.grids)
        {
            gm.gridManager.OverrideUnitInput(g, false);
            g.OnClickGrid = null;
            g.OnClickEmpty = null;
        }
    }

    // ---------- 执行传送 ----------
    private void PerformTeleport(Infantry unit, Grids to)
    {
        if (unit == null || !IsInstanceValid(unit) || to == null) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || gm.gridManager == null) return;

        // 落点校验：不能落在敌军格上；地形必须可通行
        if (to.infantries.Any(e => e != null && e != unit && TeamHelper.IsEnemyForAttacker(unit.team, e.team)))
        {
            CancelTeleportSelect();
            ShowMenu(unit, false);
            return;
        }
        if (unit.GetMoveCostOnGrid(to) >= 999)
        {
            CancelTeleportSelect();
            ShowMenu(unit, false);
            return;
        }

        CancelTeleportSelect();

        var from = unit.grid;
        // 1. 清理旧格
        from.infantries?.Remove(unit);
        if (from.infantry == unit) from.infantry = null;

        // 2. 落入新格
        if (to.infantries == null) to.infantries = new List<Infantry>();
        to.infantries.Add(unit);
        if (to.infantry == null) to.infantry = unit;
        unit.grid = to;
        unit.Position = to.Position;

        // 3. ✅ 传送消耗本回合行动：单位当场待机（灰显），本回合不可再移动/攻击/占领
        unit.hasTeleportedThisTurn = true; // 双保险：即使后续改为"传送后可继续行动"，本回合也绝不能第二次传送（不能直接传回）
        unit.OnWaitSelected();             // 内部：isMoved/isAttacked/state=Acted + FreezeToIdleDark 待机灰显 + 清除选中
        gm.gridManager.pathLine?.Hide();
        gm.gridManager.ClearMoveArrows();   // ✅ CoW 方向箭头同步清除

        // 4. 镜头跟随 + 雾/视野刷新
        var cam = GetTree().GetFirstNodeInGroup("main_camera") as Camera2D;
        if (cam != null)
        {
            var tw = cam.CreateTween();
            tw.TweenProperty(cam, "global_position", to.Position, 0.25f).SetTrans(Tween.TransitionType.Cubic);
        }
        gm.fogOfWarManager?.OnUnitMoved();
        gm.gridManager.CloseRange();
    }
}
