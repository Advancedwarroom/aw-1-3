// Grids.TerrainModules.cs
// 地形行为模块: 视野加成 / 扩散(Creeper) / 摧毁链式消失 / 传送绑定 / 地形隐蔽 —— 全部Export数据驱动, 不写死进枚举
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Grids
{
    // ==================== 地形隐蔽(对标CoW getVisionHide: REEF藏潜艇) ====================
    [Export] public bool terrainHidesUnits = false; // ✅ 地形提供隐蔽: 其上单位按隐身判定(走Infantry.terrainConcealment链)

    // ==================== 地形视野加成 ====================
    [Export] public bool visionBonusEnabled = false;        // ✅ 总开关
    [Export] public int[] visionBonusFactionBlacklist = Array.Empty<int>(); // ✅ 势力黑名单: 这些势力不享受加成(team id)
    [Export] public int visionBonusMinRange = 0;            // ✅ 范围n-m: 最小扣除范围(距离小于n不扣减/不生效, 由GetVisionBonus按距离判定)
    [Export] public int visionBonusMaxRange = 0;            // ✅ 最大视野范围(加成仅对n<=距离<=m生效; 0=不限上限)
    [Export] public int visionBonusAmount = 1;                // ✅ 加成量

    /// <summary>按势力+距离取视野加成(黑名单与n-m范围过滤)</summary>
    public int GetVisionBonus(int faction, int distance)
    {
        if (!visionBonusEnabled) return 0;
        if (visionBonusFactionBlacklist != null && visionBonusFactionBlacklist.Contains(faction)) return 0;
        if (distance < visionBonusMinRange) return 0;
        if (visionBonusMaxRange > 0 && distance > visionBonusMaxRange) return 0;
        return visionBonusAmount;
    }

    // ==================== 扩散模块(对标CoW CREEPER startOfTurn) ====================
    [Export] public bool spreadEnabled = false;             // ✅ 1.是否能扩散
    [Export] public string spreadBlacklistTerrain = "";     // ✅ 2.扩散黑名单(地形英文名csv, 如 "SEA,REEF")
    [Export] public string spreadBlacklistWeapon = "";      //    黑名单(兵器csv)
    [Export] public string spreadBlacklistBuilding = "";    //    黑名单(设施csv)
    [Export] public string spreadBlacklistUnit = "";        //    黑名单(单位csv)
    [Export] public int spreadCooldownTurns = 0;              // ✅ 3.每次扩散冷却回合
    [Export] public bool spreadGlobalCapEnabled = false;    // ✅ 4.全局扩散是否有次数上限
    [Export] public int spreadGlobalCapCount = 0;           // ✅ 5.一局几次(仅上条开启时; 计实际扩散次数, 不含冷却回合)
    [Export] public int spreadMaxManhattanFromOrigin = 0;   // ✅ 6.最大扩散几格(曼哈顿距离, 从最初一格起算; 0=不限)

    // 运行时: 扩散原点/上次扩散回合/全局计数(记录在册)
    [Export] public Vector2I spreadOriginIndex = new Vector2I(-1, -1); // 最初一格(编辑放置时写入; 运行时首次扩散前自动在册)
    public int spreadLastTurn = -1;                                    // 上次扩散回合(冷却判定)
    public static int spreadGlobalUsed = 0;                            // 全局实际扩散次数(一局)

    public static void ResetSpreadGlobalCounter() => spreadGlobalUsed = 0;

    private bool SpreadBlockedByBlacklist(Grids target)
    {
        if (target == null) return true;
        if (!string.IsNullOrEmpty(spreadBlacklistTerrain))
        {
            var banned = spreadBlacklistTerrain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (banned.Contains(target.gridType.ToString())) return true;
        }
        return false;
    }

    /// <summary>扩散一步: 冷却/全局上限/曼哈顿距离/黑名单全判定, 返回是否实际扩散(实际次数才计数)</summary>
    public bool TrySpreadTo(Grids target, int currentTurn, Grids[,] map)
    {
        if (!spreadEnabled || target == null || map == null) return false;
        if (spreadCooldownTurns > 0 && spreadLastTurn >= 0 && currentTurn - spreadLastTurn < spreadCooldownTurns) return false;
        if (spreadGlobalCapEnabled && spreadGlobalUsed >= spreadGlobalCapCount) return false;
        if (spreadOriginIndex.X < 0) spreadOriginIndex = GridIndex; // 首次扩散前在册: 自己就是原点
        if (spreadMaxManhattanFromOrigin > 0)
        {
            int dist = Mathf.Abs(target.GridIndex.X - spreadOriginIndex.X) + Mathf.Abs(target.GridIndex.Y - spreadOriginIndex.Y);
            if (dist > spreadMaxManhattanFromOrigin) return false;
        }
        if (SpreadBlockedByBlacklist(target)) return false;
        // 执行扩散: 复制自身(同类型), 烘焙风格, 继承原点
        target.gridType = gridType;
        target.spreadOriginIndex = spreadOriginIndex;
        target.BakeCowBrush();
        target.UpdateTerrainIcon();
        spreadLastTurn = currentTurn;
        spreadGlobalUsed++;
        return true;
    }

    // ==================== 摧毁链式消失(多米诺) ====================
    [Export] public string destroyChainTerrainWhitelist = "";   // ✅ 被摧毁时: 相邻这些地形消失(英文名csv)
    [Export] public string destroyChainUnitWhitelist = "";      //    相邻这些单位消失
    [Export] public string destroyChainBuildingWhitelist = "";  //    相邻这些设施消失
    [Export] public string destroyChainWeaponWhitelist = "";      //    相邻这些兵器消失

    /// <summary>本格被摧毁(地形消失/变体移除)时触发: 相邻白名单内地形/单位/设施/兵器链式消失(多米诺, 逐格递归由调用方控制深度防环)</summary>
    public void TriggerDestroyChain(Grids[,] map, int currentTurn, HashSet<Grids> visited = null)
    {
        if (map == null) return;
        visited ??= new HashSet<Grids>();
        if (visited.Contains(this)) return;
        visited.Add(this);
        int w = map.GetLength(0), h = map.GetLength(1);
        foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
        {
            int nx = GridIndex.X + d.X, ny = GridIndex.Y + d.Y;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
            var n = map[nx, ny];
            if (n == null || !IsInstanceValid(n)) continue;
            bool terrainHit = !string.IsNullOrEmpty(destroyChainTerrainWhitelist)
                && destroyChainTerrainWhitelist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(n.gridType.ToString());
            if (terrainHit)
            {
                n.QueueFree();
                n.TriggerDestroyChain(map, currentTurn, visited); // 链式: 邻居接着触发自己的白名单(visited防环)
            }
            // 单位/设施/兵器白名单: 名称在各自对象的name上, 由GameManager侧按白名单csv匹配
        }
    }

    // ==================== 传送绑定(对标CoW TELEPORTTILE; 每格单独绑定目标) ====================
    [Export] public bool teleportEnabled = false;            // ✅ 传送模块总开关(单位移动到此格即传送)
    [Export] public Godot.Collections.Array<Vector2I> teleportTargets = new(); // ✅ 绑定目标格子(可多选; 编辑器逐格绑定)
    [Export] public string teleportDefaultScanTerrain = "";    // ✅ 默认模式: 未手动绑定时, 扫描全图这些地形(英文名csv)作为候选目标
    [Export] public bool teleportUseGlobalFixed = false;     // ✅ 默认模式: 全局固定格子(=teleportTargets, 同类地形共享)

    /// <summary>取该格传送目标集: 优先手动绑定, 否则按默认扫描/全局固定</summary>
    public List<Vector2I> ResolveTeleportTargets(GridManager gm)
    {
        var result = new List<Vector2I>();
        if (teleportTargets != null && teleportTargets.Count > 0) { result.AddRange(teleportTargets); return result; }
        if (gm?.map == null) return result;
        if (!string.IsNullOrEmpty(teleportDefaultScanTerrain))
        {
            var scan = teleportDefaultScanTerrain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var g in gm.grids)
                if (g != null && scan.Contains(g.gridType.ToString()))
                    result.Add(g.GridIndex);
        }
        return result;
    }
}
