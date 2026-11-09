// GameManager.WeaponSurvival.cs - 兵器存活胜负倒计时（开创人2026-11-10）
// 通用机制：任意兵器的 [Export] surviveVictoryTurns / surviveDefeatTurns（默认0=关闭），
// 大回合开始时全体在场兵器 survivedTurns+1，达标即触发所属势力的战局级胜/负。
// 数据随存档槽与 txt 导出/导入自动往返（三个字段都是 [Export] 基础类型，走既有 diff 序列化）。
using Godot;
using System.Linq;

public partial class GameManager
{
    // ✅ 大回合开始（GameManager.Turn.cs isNewTurn 唯一调用点）：
    // 在场兵器存活计数 +1；存活胜利达标→所属势力直接胜利；存活失败超标→势力出局（其余势力续战，联动整体战局）
    private void ProcessWeaponSurvivalCountdown()
    {
        if (gameEnded || blockVictoryCheck) return;

        // ✅ 编辑模式与导入保护期不计数（与 CheckVictoryCondition 同一套门）
        var terrainEditor = GetTree()?.GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
        if (terrainEditor != null && terrainEditor.IsEditMode) return;
        if (weaponManager?.AllWeapons == null) return;

        foreach (var w in weaponManager.AllWeapons)
        {
            if (!IsInstanceValid(w) || w.isDestroyed) continue;                       // 已毁兵器不计数
            if (w.surviveVictoryTurns <= 0 && w.surviveDefeatTurns <= 0) continue;   // 默认关闭=零开销
            if (w.team == TeamHelper.Player || w.team == TeamHelper.Player0) continue; // ✅ P-1/P0 中立不介入胜负（沿用全灭/兵器判定口径）

            w.survivedTurns++;

            // 存活胜利优先判定：一场战局只有一个胜者，触发即终局
            if (w.surviveVictoryTurns > 0 && w.survivedTurns >= w.surviveVictoryTurns)
            {
                GD.Print($"[WeaponSurvival] {w.team} 兵器 {w.Name} 存活 {w.survivedTurns} 大回合达标 → {w.team} 直接胜利");
                ShowVictory(w.team, "兵器存活达标");
                return;
            }

            // 存活失败：势力出局（复用 defeatedTeams 体系）；只剩一家/全灭时由剩余判定接管终局
            if (w.surviveDefeatTurns > 0 && w.survivedTurns >= w.surviveDefeatTurns
                && !defeatedTeams.Contains(w.team))
            {
                GD.Print($"[WeaponSurvival] {w.team} 兵器 {w.Name} 存活 {w.survivedTurns} 大回合超标 → {w.team} 势力失败");
                defeatedTeams.Add(w.team);
                ShowFactionDefeat(w.team);

                var playable = new[] { TeamHelper.Player1, TeamHelper.Player2 };
                var remaining = playable.Where(t => !defeatedTeams.Contains(t)).ToList();
                if (remaining.Count == 1)
                    ShowVictory(remaining[0], "Other factions defeated");
                else if (remaining.Count == 0)
                    ShowVictory("Draw", "All factions defeated");
                if (gameEnded) return;
            }
        }
    }
}
