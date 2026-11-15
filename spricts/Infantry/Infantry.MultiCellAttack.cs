// Infantry.MultiCellAttack.cs — ✅ 开创人2026-11-16: 通用多格攻击模式（目标零点模式）
// 模式锚定"攻击目标点为零点"，随攻击方向四向旋转（前向坐标系），逐格独立：倍率/三军位掩码/弱点格/现形。
// 激光坦克="0,0;1,0;2,0"  霰弹坦克="0,0;1,1;1,-1"（T型）——同一张表的两行。
using Godot;
using System;
using System.Collections.Generic;

public partial class Infantry
{
    [ExportGroup("多格攻击模式（目标零点模式）")]
    [Export] public bool multiCellAttackEnabled = false;                       // 总开关
    [Export] public string[] attackPatternCells = new string[] { "0,0" };      // 相对目标零点的偏移"dx,dy"（前向坐标系；可空格间隙、任意拓展）
    [Export] public float[] attackCellFactors = new float[] { };              // 逐格伤害倍率（与索引对齐；留空=全1.0统一值）
    [Export] public int[] attackCellPolicy = new int[] { };                    // 逐格三军位掩码：1敌军|2友军|4盟军（留空=默认统一值）
    [Export] public int attackCellDefaultPolicy = 1;                          // 默认统一值=仅敌军
    [Export] public bool patternRotateWithAttack = true;                       // 随攻击方向旋转（坦克朝向即前向）

    static readonly Vector2I[] RotFwd = { new Vector2I(1, 0), new Vector2I(0, 1), new Vector2I(-1, 0), new Vector2I(0, -1) }; // E S W N

    /// <summary>模式解析：目标零点+攻击方向旋转+越界剔除。index0=目标本格。</summary>
    public List<Grids> ResolvePatternCells(Grids zero)
    {
        var result = new List<Grids>();
        if (zero == null) return result;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return result;
        int dir = 0;
        if (patternRotateWithAttack && grid != null)
        {
            var d = zero.GridIndex - grid.GridIndex;
            if (Mathf.Abs(d.X) >= Mathf.Abs(d.Y)) dir = d.X >= 0 ? 0 : 2;
            else dir = d.Y >= 0 ? 1 : 3;
        }
        foreach (var raw in attackPatternCells ?? Array.Empty<string>())
        {
            var parts = raw.Split(',');
            if (parts.Length < 2 || !int.TryParse(parts[0].Trim(), out int dx) || !int.TryParse(parts[1].Trim(), out int dy)) continue;
            var f = RotFwd[dir];
            var r = new Vector2I(-f.Y, f.X);
            var g = gm.gridManager.GetGridAt(zero.GridIndex + new Vector2I(f.X * dx + r.X * dy, f.Y * dx + r.Y * dy));
            if (g != null) result.Add(g);
        }
        return result;
    }

    public float FactorAt(int i) => (attackCellFactors != null && i < attackCellFactors.Length && attackCellFactors[i] > 0f) ? attackCellFactors[i] : 1f;

    /// <summary>逐格三军判定：友军=同队,盟军=结盟,敌军=其余。位掩码1敌|2友|4盟。</summary>
    public bool PolicyAllowsAt(int i, string victimTeam)
    {
        int mask = attackCellDefaultPolicy;
        if (attackCellPolicy != null && i < attackCellPolicy.Length) mask = attackCellPolicy[i];
        if (victimTeam == team) return (mask & 2) != 0;
        if (TeamHelper.AreAllied(team, victimTeam)) return (mask & 4) != 0;
        return (mask & 1) != 0;
    }
}
