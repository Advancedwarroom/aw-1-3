// GrandBoltModule.Systems.cs - 拆分摆放的 GrandBolt 模块完整系统移植（开创人令 2026-11-15：选B，和GrandBolt一样，包括蠕动）
// 语义与 GrandBolt 单体版一一对应，适配单格个体：生产落点=本格+四邻，蠕动=整件平移，电磁爆=本格周围 x-y 范围。
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class GrandBoltModule
{
    // ==================== 生产模块 ====================
    [ExportGroup("模块生产")]
    /// <summary>1. 是否开启 Oozium 生产</summary>
    [Export] public bool moduleOoziumProductionEnabled = true;
    /// <summary>2. 回合开始是否自动生产</summary>
    [Export] public bool moduleAutoProduceOnTurnStart = false;
    /// <summary>3. 是否允许手动生产（按钮）</summary>
    [Export] public bool moduleManualProduceEnabled = true;
    /// <summary>4. 生产的单位名</summary>
    [Export] public string moduleOoziumUnitName = "Oozium";
    /// <summary>5. 每回合生产数量</summary>
    [Export] public int moduleOoziumProductionPerTurn = 1;
    /// <summary>6. 生产间隔（0=每回合，>0=间隔回合）</summary>
    [Export] public int moduleOoziumProductionCooldown = 0;
    /// <summary>7. 生产格被单位/兵器占用时是否禁止（否则选其它落点）</summary>
    [Export] public bool moduleOoziumBlockedByOccupants = false;
    /// <summary>8. 生产格被占用时是否碾碎现有单位（血量清0）</summary>
    [Export] public bool moduleOoziumOverwriteUnits = true;

    // ==================== 蠕动模块 ====================
    [ExportGroup("模块蠕动")]
    /// <summary>1. 是否开启蠕动（每回合整体平移）</summary>
    [Export] public bool moduleWriggleEnabled = false;
    /// <summary>2. 每回合蠕动距离（格）</summary>
    [Export] public int moduleWriggleDistance = 1;
    /// <summary>3. 蠕动是否允许跨屏</summary>
    [Export] public bool moduleWriggleCanCrossScreens = false;
    /// <summary>4. 蠕动落格被敌方单位占用时是否禁止（否则碾碎通过）</summary>
    [Export] public bool moduleWriggleBlockedByOccupants = true;

    // ==================== 电磁爆模块 ====================
    [ExportGroup("模块电磁爆")]
    /// <summary>1. 电磁爆范围 x（以本格为核心）</summary>
    [Export] public int moduleEmpRangeX = 1;
    /// <summary>2. 电磁爆范围 y</summary>
    [Export] public int moduleEmpRangeY = 1;
    /// <summary>3. 对兵器伤害</summary>
    [Export] public int moduleEmpWeaponDamage = 1;
    /// <summary>4. 对步兵伤害（原版语义：降至1血下限）</summary>
    [Export] public int moduleEmpInfantryDamage = 0;

    // ==================== 运行时状态 ====================
    public int moduleOoziumProducedThisTurn = 0;
    public int moduleOoziumCooldownCounter = 0;
    public bool hasModuleWriggledThisTurn = false;

    // ==================== 生产实现 ====================

    /// <summary>本回合生产配额是否还有剩余</summary>
    public bool HasModuleProduceQuotaLeft() => moduleOoziumProducedThisTurn < moduleOoziumProductionPerTurn;

    /// <summary>生产落点：本格优先，其次四邻（上右下左）。被占且禁占/禁覆则顺延。</summary>
    public Grids ModuleProduceCell(GameManager gm)
    {
        if (gm?.gridManager?.map == null || grid == null) return null;
        var map = gm.gridManager.map;
        var candidates = new List<Grids> { grid };
        int x = grid.GridIndex.X, y = grid.GridIndex.Y;
        foreach (var d in new Vector2I[] { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) })
        {
            int nx = x + d.X, ny = y + d.Y;
            if (nx < 0 || ny < 0 || nx >= map.GetLength(0) || ny >= map.GetLength(1)) continue;
            if (map[nx, ny] != null) candidates.Add(map[nx, ny]);
        }
        foreach (var c in candidates)
        {
            if (c == null) continue;
            bool blocked = c.infantries.Any(u => u != null && IsInstanceValid(u));
            if (blocked)
            {
                if (moduleOoziumBlockedByOccupants) continue;
                if (!moduleOoziumOverwriteUnits) continue;
            }
            return c;
        }
        return null;
    }

    /// <summary>✅ 生产一次 Oozium 到落点（配额+冷却校验，占用则碾碎）</summary>
    public bool ModuleProduceOozium(Grids targetGrid, GameManager gm)
    {
        if (!moduleOoziumProductionEnabled || targetGrid == null || gm?.unitManager == null) return false;
        if (!HasModuleProduceQuotaLeft()) return false;
        if (moduleOoziumCooldownCounter > 0) return false;
        if (targetGrid.infantries.Any(u => u != null && IsInstanceValid(u)))
        {
            if (moduleOoziumBlockedByOccupants) return false;
            if (!moduleOoziumOverwriteUnits) return false;
            foreach (var u in targetGrid.infantries.ToList())
            {
                if (u == null || !IsInstanceValid(u)) continue;
                if (u.health <= 0) continue;
                u.health = 0;
                u.TryFlee();
                if (u.health <= 0) gm.RemovePiece(u);
            }
        }
        var unit = gm.UnitManager_CreateUnitAt(moduleOoziumUnitName, targetGrid.GridIndex, team);
        if (unit == null) return false;
        unit.hasActed = true;
        moduleOoziumProducedThisTurn++;
        if (moduleOoziumProductionCooldown > 0) moduleOoziumCooldownCounter = moduleOoziumProductionCooldown + 1;
        return true;
    }

    /// <summary>手动生产前置校验（按钮显隐/点击）</summary>
    public bool CanManualModuleProduce()
    {
        return moduleOoziumProductionEnabled && moduleManualProduceEnabled
            && health > 0 && !isDestroyed && HasModuleProduceQuotaLeft()
            && moduleOoziumCooldownCounter <= 0;
    }

    /// <summary>✅ 手动生产（菜单按钮调用）：向本格/邻格产出</summary>
    public bool ManualModuleProduce(GameManager gm)
    {
        if (!CanManualModuleProduce()) return false;
        return ModuleProduceOozium(ModuleProduceCell(gm), gm);
    }

    // ==================== 蠕动实现 ====================

    /// <summary>本回合是否还能蠕动</summary>
    public bool CanModuleWriggle() => moduleWriggleEnabled && !hasModuleWriggledThisTurn && health > 0 && !isDestroyed;

    /// <summary>✅ 蠕动：整件朝方向平移 moduleWriggleDistance 格（落格校验+碾碎敌方单位）</summary>
    public void ModuleWriggle(Vector2I direction, GameManager gm)
    {
        if (!CanModuleWriggle() || gm?.gridManager?.map == null || grid == null) return;
        int steps = Mathf.Max(1, moduleWriggleDistance);
        bool moved = false;
        for (int i = 0; i < steps; i++)
        {
            Vector2I next = grid.GridIndex + direction;
            if (next.X < 0 || next.Y < 0 || next.X >= gm.gridManager.searchRange.X || next.Y >= gm.gridManager.searchRange.Y) break;
            var nextGrid = gm.gridManager.map[next.X, next.Y];
            if (nextGrid == null) break; // 跨屏/空洞
            if (!gm.gridManager.IsRangeAllowed(grid, nextGrid, moduleWriggleCanCrossScreens)) break;

            var units = nextGrid.infantries.Where(u => u != null && IsInstanceValid(u)).ToList();
            if (units.Any(u => u.team != team) && moduleWriggleBlockedByOccupants) break;

            // 碾碎落格所有单位（蠕动碾过）
            foreach (var u in units)
            {
                if (u == null || !IsInstanceValid(u)) continue;
                if (u.health <= 0) continue;
                u.health = 0;
                u.TryFlee();
                if (u.health <= 0) gm.RemovePiece(u);
            }

            // 迁移兵器绑定
            grid?.weapons?.Remove(this);
            nextGrid.weapons.Add(this);
            grid = nextGrid;
            moved = true;
        }
        if (!moved) return;
        GlobalPosition = grid.GlobalPosition;
        Position = new Vector2(grid.GridIndex.X * gm.gridManager.CellWidth, grid.GridIndex.Y * gm.gridManager.CellHeight);
        hasModuleWriggledThisTurn = true;
    }

    // ==================== 电磁爆实现 ====================

    /// <summary>✅ 电磁爆：本格周围 x-y 范围内敌方兵器受伤、敌方步兵降至下限</summary>
    public void ModuleEmpBurst(GameManager gm)
    {
        if (gm?.gridManager?.map == null || grid == null) return;
        int cx = grid.GridIndex.X, cy = grid.GridIndex.Y;
        var map = gm.gridManager.map;
        for (int dx = -moduleEmpRangeX; dx <= moduleEmpRangeX; dx++)
            for (int dy = -moduleEmpRangeY; dy <= moduleEmpRangeY; dy++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= map.GetLength(0) || y >= map.GetLength(1)) continue;
                var g = map[x, y];
                if (g == null) continue;
                foreach (var enemyWeapon in g.weapons.Where(w => w != null && IsInstanceValid(w) && w.team != team).ToList())
                {
                    if (moduleEmpWeaponDamage > 0) enemyWeapon.TakeDamage(moduleEmpWeaponDamage);
                    else enemyWeapon.TakeDamage(1); // 与GrandBolt一致：0也保底1
                }
                foreach (var u in g.infantries.Where(u => u != null && IsInstanceValid(u) && u.team != team).ToList())
                {
                    u.health = Mathf.Max(1, u.health - moduleEmpInfantryDamage);
                }
            }
    }

    // ==================== 回合开始（与 GrandBolt.OnTurnStart 同构） ====================

    /// <summary>✅ 回合开始：复位计数 + 自动生产 + 电磁爆（由 GameManager 武器循环统一调用）</summary>
    public override void OnTurnStart(GameManager gm)
    {
        moduleOoziumProducedThisTurn = 0;
        if (moduleOoziumCooldownCounter > 0) moduleOoziumCooldownCounter--;
        hasModuleWriggledThisTurn = false;

        if (moduleAutoProduceOnTurnStart && moduleOoziumProductionEnabled)
        {
            while (HasModuleProduceQuotaLeft() && moduleOoziumCooldownCounter <= 0)
            {
                var cell = ModuleProduceCell(gm);
                if (cell == null) break;
                if (!ModuleProduceOozium(cell, gm)) break;
            }
        }

        ModuleEmpBurst(gm);
    }
}
