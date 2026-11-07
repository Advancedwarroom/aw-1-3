// TerrainEditor.GrandBoltConfig.cs
// 巨雷配置行落地: 改动实时应用到场上所有GrandBolt(本体+PartVisual图层+EMP数值)
using Godot;
using System;

public partial class TerrainEditor
{
    private void ApplyGrandBoltConfig(bool rotEnabled, int dir, double fps, double empW, double empB)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("grid_manager") as GridManager;
        if (gm?.grids == null) return;
        foreach (var g in gm.grids)
        {
            if (g?.weapons == null) continue;
            foreach (var w in g.weapons)
            {
                if (w is not GrandBolt gb || !IsInstanceValid(gb)) continue;
                gb.empBurstWeaponDamage = (int)empW;
                gb.empBurstInfantryDamage = (int)empB;
                var vis = gb.GetNodeOrNull<GrandBoltVisual>("PartVisual");
                if (vis == null) continue;
                vis.SetEnableRotation(rotEnabled);
                vis.SetDirection(dir);
                vis.SetAnimFps(fps);
            }
        }
    }
}
