// BlackHoleFactory.cs —— 黑洞工厂（多格兵器，3宽×4高，可四向旋转）
// 旋转：基类 RotateDirection 钩子（canRotate 控制按钮显隐，TowerRotate 菜单现成）
//       90°步进：宽高调换+掩码转置+贴图旋转；旋转前检查越界/单位/兵器/设施占位，被占→失败提醒不动
// 供能联动：接缝在→归属随接缝队伍；接缝毁/无→中立废铁（Player0）；Rig 建中转口（新接缝）→归属随新接缝
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class BlackHoleFactory : Weapon
{
    [ExportGroup("黑洞工厂")]
    [Export] public Vector2I factorySize = new Vector2I(3, 4);      // 宽×高（旋转后 4宽×3高）
    [Export] public string[] footprintMask = { "111", "111", "111", "111" };  // 占位掩码（行=自上而下）
    [Export] public Vector2I supplySeamOffset = new Vector2I(0, 1); // 供能接缝相对锚点偏移（默认下方）
    [Export] public bool supplyLinkEnabled = true;                  // 供能联动开关
    [ExportGroup("生产口模块")]
    [Export] public int productionPortUsesPerTurn = 3;       // 三口=每回合3次（CoW DOOR1/2/3）
    [Export] public bool productionPortInstantMove = true;   // 生产后立即移动（原作模式）
    [Export] public bool productionPortFullList = true;      // 全生产列表（跳过白名单）
    private int portUsesLeft = 0;

    public override void _Ready()
    {
        isMultiTile = true;   // 多格兵器
        base._Ready();
        var dots = GetNodeOrNull<AnimatedSprite2D>("YellowDots");
        if (dots != null && dots.SpriteFrames != null && dots.SpriteFrames.HasAnimation("Blink")) dots.Play("Blink"); // 黄灯闪烁（独立节点不吃调色板）
    }

    // 贴图规矩：P1=橙兵工厂双帧 / P2=蓝月兵工厂双帧 / 其余=中立双帧走LUT换色（黄灯层不参与）
    public override string GetIdleAnimName()
    {
        if (team == TeamHelper.Player1) return "OrangeStar";
        if (team == TeamHelper.Player2) return "BlueMoon";
        return "Neutral";
    }

    public override List<Vector2I> GetOccupiedIndices(Vector2I anchorIndex)
    {
        var list = new List<Vector2I>();
        int w = factorySize.X, h = factorySize.Y;
        for (int y = 0; y < h && y < footprintMask.Length; y++)
        {
            var row = footprintMask[y];
            for (int x = 0; x < w && x < row.Length; x++)
                if (row[x] == '1') list.Add(new Vector2I(anchorIndex.X + x, anchorIndex.Y + y));
        }
        return list;
    }

    // —— 三生产口：走 GameManager.ShowProductionMenu 的设施生产管线（真正的复用生产模块）
    //       规格=原作原汁原味：免费/立即移动/全列表；每回合3口，产一次扣一口；出生格=朝向正面边外3格
    [Export] public int productionMaxRange = 1;   // 出生格搜索距离（曼哈顿）

    private double bhfMenuCd = 0;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (bhfMenuCd > 0) bhfMenuCd -= delta;
    }

    // 每回合开始充能三口（挂进新回合循环，见 GameManager.Turn.cs）
    public void ResetPortUses() { portUsesLeft = productionPortUsesPerTurn; }

    public bool CanUsePortNow()
    {
        return portUsesLeft > 0 && !isDestroyed && team != TeamHelper.Player0 && grid != null && GetFreeDoorGrid() != null;
    }

    private Grids GetFreeDoorGrid()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null || grid == null) return null;
        foreach (var idx in GetDoorIndices())
        {
            if (idx.X < 0 || idx.Y < 0 || idx.X >= gm.gridManager.searchRange.X || idx.Y >= gm.gridManager.searchRange.Y) continue;
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            if (g.infantries.Any(u => u != null && IsInstanceValid(u))) continue;
            if (g.weapon != null && g.weapon != this) continue;
            if (g.city != null) continue;
            return g;
        }
        return null;
    }

    // 影子设施：设施生产线的复用载体（prefab 挂 ShadowFactory 子节点后即通；未挂时给出明确提示）
    private Facility shadowFactory = null;
    private Facility GetShadowFactory()
    {
        if (shadowFactory != null && IsInstanceValid(shadowFactory)) return shadowFactory;
        shadowFactory = GetNodeOrNull<Facility>("ShadowFactory");
        return shadowFactory;
    }

    public void OpenProductionPorts()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || bhfMenuCd > 0) return;
        bhfMenuCd = 0.5;
        var shadow = GetShadowFactory();
        if (shadow == null)
        {
            GD.PrintErr("[黑洞工厂] 未找到 ShadowFactory 子节点——三生产口待接线（影子设施方案，见提交记录）");
            return;
        }
        var door = GetFreeDoorGrid();
        if (door == null) return;
        gm.ShowProductionMenu(shadow, door);
    }

    private List<Vector2I> GetDoorIndices()
    {
        var doors = new List<Vector2I>();
        var anchor = grid.GridIndex;
        int rot = ((int)(RotationDegrees / 90f)) % 4; if (rot < 0) rot += 4;
        int w = factorySize.X, h = factorySize.Y;
        for (int i = 0; i < 3; i++)
            switch (rot)
            {
                case 0: doors.Add(new Vector2I(anchor.X + i, anchor.Y + h)); break;   // 正面=下
                case 1: doors.Add(new Vector2I(anchor.X - 1, anchor.Y + i)); break;   // 正面=左
                case 2: doors.Add(new Vector2I(anchor.X + i, anchor.Y - 1)); break;   // 正面=上
                default: doors.Add(new Vector2I(anchor.X + w, anchor.Y + i)); break;  // 正面=右
            }
        return doors;
    }

    public override void RotateDirection()
    {
        if (!canRotate) return;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || grid == null) return;
        var anchorIndex = grid.GridIndex;
        var oldIndices = GetOccupiedIndices(anchorIndex);
        // 90°：锚点不动，占据格绕锚点旋转（dx,dy）→(-dy,dx)
        var rotated = oldIndices
            .Select(p => new Vector2I(anchorIndex.X - (p.Y - anchorIndex.Y), anchorIndex.Y + (p.X - anchorIndex.X)))
            .ToList();
        // ① 越界检查
        foreach (var idx in rotated)
            if (idx.X < 0 || idx.Y < 0 || idx.X >= gm.gridManager.searchRange.X || idx.Y >= gm.gridManager.searchRange.Y)
            { GD.PrintErr("[黑洞工厂] 旋转失败：目标位置越界"); return; }
        // ② 占位检查（不压碎：单位/他方兵器/设施任一被占→失败提醒，原地不动）
        foreach (var idx in rotated)
        {
            if (oldIndices.Contains(idx)) continue;
            var g = gm.gridManager.map[idx.X, idx.Y];
            if (g == null) continue;
            if ((g.infantries?.Count ?? 0) > 0) { GD.PrintErr("[黑洞工厂] 旋转失败：目标位置有单位"); return; }
            if (g.weapon != null && g.weapon != this) { GD.PrintErr("[黑洞工厂] 旋转失败：目标位置有兵器"); return; }
            if (g.city != null) { GD.PrintErr("[黑洞工厂] 旋转失败：目标位置有设施"); return; }
        }
        // ③ 应用：宽高调换 + 掩码转置 + 贴图旋转 + 重绑占据
        footprintMask = RotateMask(footprintMask);
        factorySize = new Vector2I(factorySize.Y, factorySize.X);
        RotationDegrees += 90f;
        gm.weaponManager?.BindWeaponToGrid(this, true);
        GD.Print("[黑洞工厂] 旋转 90°");
    }

    private string[] RotateMask(string[] mask)
    {
        int h = mask.Length, w = mask[0].Length;
        var nb = new string[w];
        for (int x = 0; x < w; x++)
        {
            var sb = new System.Text.StringBuilder();
            for (int y = h - 1; y >= 0; y--) sb.Append(mask[y][x]);
            nb[x] = sb.ToString();
        }
        return nb;
    }

    // ========== 供能联动（每秒一查）==========
    private float bhfLinkTimer = 0f;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!supplyLinkEnabled) return;
        bhfLinkTimer += (float)delta;
        if (bhfLinkTimer < 1.0f) return;
        bhfLinkTimer = 0f;
        CheckSupplyLink();
    }

    private void CheckSupplyLink()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || grid == null || isDestroyed) return;
        var seamIndex = grid.GridIndex + supplySeamOffset;
        if (seamIndex.X < 0 || seamIndex.Y < 0 || seamIndex.X >= gm.gridManager.searchRange.X || seamIndex.Y >= gm.gridManager.searchRange.Y)
            return;
        var seam = gm.gridManager.map[seamIndex.X, seamIndex.Y].weapon as PipeSeam;
        string newTeam = (seam != null && !seam.isDestroyed) ? seam.team : TeamHelper.Player0;  // 无接缝=中立废铁
        if (newTeam == team) return;
        team = newTeam;
        RefreshTeamVisual();
        GD.Print($"[黑洞工厂] 供能联动：归属→{(newTeam == TeamHelper.Player0 ? "中立废铁" : newTeam)}");
    }
}
