using Godot;
using System;

// GridManager.MergeUnload.cs —— 舱内乘客·先卸后融 状态机（开创人2026-11-19 定案）
// 流程：合流判定为"先卸后融" → 强制弹出卸载菜单（source 必须卸空；target 冲突时也要卸到合体可行）
//       卸完自动合流；玩家不卸载点空地 → SetupEmptyClickForRollback 整体回退 + 合流取消（RollbackMove 里清状态）
public partial class GridManager
{
    private Infantry pendingUnloadSource;
    private Infantry pendingUnloadTarget;

    private GameManager GmRef() => GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
    private ActionMenu MenuRef() => GetTree()?.GetFirstNodeInGroup("action_menu") as ActionMenu;

    public void StartUnloadFirstMerge(Infantry source, Infantry target)
    {
        var gm = GmRef();
        if (source == null || target == null) { gm?.MergeUnits(source, target); return; }
        pendingUnloadSource = source;
        pendingUnloadTarget = target;
        SetupEmptyClickForRollback(source);
        MenuRef()?.ShowTransportUnloadButtonsOnly(source, false);
        if (gm != null && gm.MergeLoadedConflict(source, target))
            MenuRef()?.ShowTransportUnloadButtonsOnly(target, false);
    }

    public void NotifyUnloadFirstProgress(Infantry transport)
    {
        if (pendingUnloadSource == null || transport == null) return;
        if (transport != pendingUnloadSource && transport != pendingUnloadTarget) return;
        if (!IsInstanceValid(pendingUnloadSource) || !IsInstanceValid(pendingUnloadTarget))
        { CancelUnloadFirstMerge(); return; }
        if ((pendingUnloadSource.transportedUnits?.Count ?? 0) > 0) return;
        var gm = GmRef();
        if (gm != null && gm.MergeLoadedConflict(pendingUnloadSource, pendingUnloadTarget)) return;
        var s = pendingUnloadSource; var tg = pendingUnloadTarget;
        pendingUnloadSource = null; pendingUnloadTarget = null;
        gm?.MergeUnits(s, tg);
    }

    public void CancelUnloadFirstMerge()
    {
        pendingUnloadSource = null;
        pendingUnloadTarget = null;
    }
}
