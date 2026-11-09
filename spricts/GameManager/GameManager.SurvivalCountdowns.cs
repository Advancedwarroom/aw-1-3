// GameManager.SurvivalCountdowns.cs - 存活胜负倒计时（开创人2026-11-10，单位/兵器/设施三族通用）
// 通用机制：任意实体的 [Export] surviveVictoryTurns / surviveDefeatTurns（默认0=关闭），
// 大回合开始时全体在场实体 survivedTurns+1，达标即触发所属势力的战局级胜/负。
// 字段定义在三个基类上同名字段：Infantry（单位）/ Weapon（兵器）/ Facility（设施）。
// 数据随存档槽与 txt 导出/导入自动往返（[Export] 基础类型，走既有 diff 序列化）。
using Godot;
using System.Linq;

public partial class GameManager
{
    // ✅ 大回合开始（GameManager.Turn.cs isNewTurn 唯一调用点）：
    // 在场实体存活计数 +1；存活胜利达标→所属势力直接胜利；存活失败超标→势力出局（其余势力续战，联动整体战局）
    private void ProcessSurvivalCountdowns()
    {
        if (gameEnded || blockVictoryCheck) return;

        // ✅ 编辑模式与导入保护期不计数（与 CheckVictoryCondition 同一套门）
        var terrainEditor = GetTree()?.GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
        if (terrainEditor != null && terrainEditor.IsEditMode) return;

        // ── 兵器 ──
        if (weaponManager?.AllWeapons != null)
            foreach (var w in weaponManager.AllWeapons)
            {
                if (!IsInstanceValid(w) || w.isDestroyed) continue;
                TickSurvival(w.team, w.surviveVictoryTurns, w.surviveDefeatTurns, ref w.survivedTurns, w.Name);
                if (gameEnded) return;
            }

        // ── 单位 ──
        if (unitManager?.AllUnits != null)
            foreach (var u in unitManager.AllUnits)
            {
                if (!IsInstanceValid(u) || u.health <= 0) continue;
                TickSurvival(u.team, u.surviveVictoryTurns, u.surviveDefeatTurns, ref u.survivedTurns, u.Name);
                if (gameEnded) return;
            }

        // ── 设施（逐格取 grid.city，一设施一主格）──
        if (gridManager?.grids != null)
            foreach (var grid in gridManager.grids)
            {
                if (grid?.city == null) continue;
                var f = grid.city;
                if (!IsInstanceValid(f)) continue;
                string fteam = f.facilityTeam;
                if (string.IsNullOrEmpty(fteam) || fteam == "None") continue;
                TickSurvival(fteam, f.surviveVictoryTurns, f.surviveDefeatTurns, ref f.survivedTurns, f.Name);
                if (gameEnded) return;
            }
    }

    // ✅ 单实体计时与阈值判定（三类实体共用；存活胜利优先——一场战局只有一个胜者）
    private void TickSurvival(string team, int surviveVictoryTurns, int surviveDefeatTurns, ref int survivedTurns, string entityName)
    {
        if (surviveVictoryTurns <= 0 && surviveDefeatTurns <= 0) return;          // 默认关闭=零开销
        if (team == TeamHelper.Player || team == TeamHelper.Player0) return;     // ✅ P-1/P0 中立不介入胜负（沿用全灭/兵器判定口径）

        survivedTurns++;

        if (surviveVictoryTurns > 0 && survivedTurns >= surviveVictoryTurns)
        {
            GD.Print($"[Survival] {team} 实体 {entityName} 存活 {survivedTurns} 大回合达标 → {team} 直接胜利");
            ShowVictory(team, "存活达标");
            return;
        }

        if (surviveDefeatTurns > 0 && survivedTurns >= surviveDefeatTurns && !defeatedTeams.Contains(team))
            TriggerTeamSurvivalDefeat(team, entityName, survivedTurns);
    }

    // ✅ 存活超标→势力失败出局（复用 defeatedTeams 体系，其余势力续战；只剩一家自然接管胜利）
    private void TriggerTeamSurvivalDefeat(string team, string entityName, int survivedTurns)
    {
        GD.Print($"[Survival] {team} 实体 {entityName} 存活 {survivedTurns} 大回合超标 → {team} 势力失败");
        defeatedTeams.Add(team);
        ShowFactionDefeat(team);

        var playable = new[] { TeamHelper.Player1, TeamHelper.Player2 };
        var remaining = playable.Where(t => !defeatedTeams.Contains(t)).ToList();
        if (remaining.Count == 1)
            ShowVictory(remaining[0], "Other factions defeated");
        else if (remaining.Count == 0)
            ShowVictory("Draw", "All factions defeated");
    }
}
