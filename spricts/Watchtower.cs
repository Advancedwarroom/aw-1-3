using Godot;
using System.Collections.Generic;

// ✅ 瞭望塔设施（开创人手绘图，蓝母版全阵营色相迁移烘焙）
// 视野：塔基 n-m 环带（Export 开关，默认关），射程上限吃所属队 CO 视野加成
public partial class Watchtower : Facility
{
    [ExportGroup("瞭望塔视野")]
    [Export] public bool useRingVision = false; // n-m 环带视野总开关（默认关）
    [Export] public int visionRangeMin = 1;     // 环带内圈（1 = 全圆盘）
    [Export] public int visionRangeMax = 3;     // 环带外圈

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
    {
        // P0/P1/P2 → 红槽；其余（P-1、P3+）→ 紫槽
        bool known = facilityTeam == TeamHelper.Player0 || facilityTeam == TeamHelper.Player1 || facilityTeam == TeamHelper.Player2;
        return ("P0Watchtower", known ? "watchtowerred" : "watchtowerpurple", "watchtowerblue");
    }

    public override HashSet<Grids> CalculateFacilityVision(GameManager gm)
    {
        var visible = base.CalculateFacilityVision(gm);
        if (!useRingVision) return visible;
        if (gm?.gridManager?.grids == null || ParentGrid == null) return visible;

        // ✅ CO 接线：与单位同一模块管线（stat 4 = visionBonus），只撑外圈
        int coBonus = Mathf.RoundToInt(CoModuleSystem.Sum(facilityTeam, 4, "visionBonus"));
        int rOut = Mathf.Max(visionRangeMax + coBonus, visionRangeMin);
        int rIn = Mathf.Max(0, visionRangeMin - 1);
        var c = ParentGrid.GridIndex;
        foreach (var g in gm.gridManager.grids)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            int dist = Mathf.Abs(g.GridIndex.X - c.X) + Mathf.Abs(g.GridIndex.Y - c.Y);
            if (dist > rIn && dist <= rOut) visible.Add(g);
        }
        return visible;
    }
}
