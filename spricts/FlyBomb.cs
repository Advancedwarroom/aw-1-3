// FlyBomb.cs —— 空军自爆单位（AWDS Black Bomb 黑炸弹/黑弹 复刻·同位，2026-11-01 由开创人亲口登记入册）
using Godot;
using System;
using System.Linq;

public partial class FlyBomb : Infantry
{
    public override string GetIdleAnimName() => team == "Player2" ? "FlyBomb2" : "FlyBomb1";

    public override void _Ready()
    {
        base._Ready();
        // FlyBomb 的特殊初始化已通过 UnitRegistry 完成
    }

    public override void OnWaitSelected()
    {
        isMoved = true;
        isAttacked = true;
        state = UnitState.Acted;
        originalGrid = null;
        ShowIdleState();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }

}
