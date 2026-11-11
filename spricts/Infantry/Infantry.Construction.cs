// Infantry.cs - 建造模块：单位可在舱内/外部建造建筑
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Infantry · Construction（建造模块）
// 由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注
// ✅ 与生产模块对称，差异：
//    建造的是建筑（白名单填建筑类型名：City/Base/AirPort/Port…，见 FacilityDatabase）。
//    内部建造与搭载模块无绑定：建好的建筑存在舱内数据里，之后用「部署」放到场上。
//    外部建造：范围 [constructionMinRange, constructionMaxRange]，自动随机或手选目标格。
//    进度制：总进度 N / 每回合进度 Z。「内部建造时可否移动」= 移动会不会打断进度：
//    关=移动即中断；外部建造移动即中断。
//    自动化：开启后点一次建造，之后每回合自动推进（只显示中断按钮），不消耗单位行动。
//    冷却：constructionCooldownTurns 回合。
//    建造弹药：currentConstructionAmmo>0 才显示建造按钮；开工消耗 1 发（统一弹药系统）。
public partial class Infantry
{
    [ExportGroup("建造模块")]
    [Export] public bool canBuildFacilities = false;                                  // ① 是否可建造
    [Export(PropertyHint.Enum, "关闭,内部,外部,可选内外")] public int constructionLocationMode = 0;  // ② 建造位置
    [Export] public int constructionMinRange = 1;                                     // ③ 外部建造最小范围
    [Export] public int constructionMaxRange = 2;                                     // ③ 外部建造最大范围
    [Export] public bool constructionCanCrossScreens = false;                        // 外部建造目标范围是否允许跨屏
    [Export] public bool constructionAutoPlace = false;                                // ④ 外部自动随机放置（false=手选目标格）
    [Export] public Godot.Collections.Array<string> constructionWhitelist = new();   // ⑤ 建造白名单（建筑类型名）
    [Export] public int maxConstructionAmmo = 0;                                      // ⑥ 建造弹药上限
    [Export] public int currentConstructionAmmo = 0;                                  // ⑥ 当前建造弹药
    [Export] public int constructionTotalProgress = 20;                                // ⑦ 建造总进度 N
    [Export] public int constructionProgressPerTurn = 10;                              // ⑧ 每回合进度 Z
    [Export] public bool canMoveWhileBuilding = false;                                // ⑨ 内部建造时可否移动
    [Export] public bool constructionAutomation = false;                               // ⑩ 自动化
    [Export] public int constructionCooldownTurns = 0;                                  // ⑪ 冷却回合数
    // ⑫ 可建造地形白名单：写地形名（如 "BEACH"）=对所有建筑生效；写 "建筑:地形"（如 "TempPort:BEACH"）=只对该建筑生效。
    //    留空=未配置，按 FacilityDatabase 默认规则（临时机场=平原，临时港口=海滩，其余任意）。
    [Export] public Godot.Collections.Array<string> constructionAllowedTerrains = new();
    // ⑬ 建造产物归属势力：0=仅友方(建造者势力)；1=仅敌方(就近敌方势力，找不到回退中立)；2=中立/全部(无队伍，类似管道接缝)。
    //    同时作用于设施与兵器；内部建造(舱内携带)在部署那一刻才解析势力。
    [Export(PropertyHint.Enum, "仅友方,仅敌方,中立(全部)")] public int constructionTargetTeamPolicy = 0;

    // ===== 运行时状态（不导出） =====
    public bool isConstructing = false;                 // 建造中
    public int constructionProgress = 0;                // 当前累计进度
    public string constructionTargetFacilityType = ""; // 本次建造的建筑
    public bool constructionInternal = true;            // 本次建造：内部/外部
    public int constructionCooldownRemaining = 0;       // 剩余冷却
    public Grids constructionTargetGrid = null;         // 外部建造目标格

    /// <summary>舱内待部署建筑（内部建造的产出，类型名列表）</summary>
    public List<string> carriedFacilityTypes = new List<string>();

    public bool ConstructionAllowsInternal => constructionLocationMode == 1 || constructionLocationMode == 3;
    public bool ConstructionAllowsExternal => constructionLocationMode == 2 || constructionLocationMode == 3;

    // ---------- 地形建造条目（管道构造者建管道接缝）----------
    /// <summary>"Terrain:PIPESEAM" → GridType.PIPESEAM；不是地形条目返回 null</summary>
    public static GridType? ParseTerrainBuildEntry(string entry)
    {
        if (string.IsNullOrEmpty(entry)) return null;
        if (!entry.StartsWith("Terrain:", System.StringComparison.OrdinalIgnoreCase)) return null;
        string terrainName = entry[7..].Trim();
        return System.Enum.TryParse<GridType>(terrainName, true, out var g) ? g : null;
    }

    public static bool IsTerrainBuildEntry(string entry) => ParseTerrainBuildEntry(entry) != null;

    // ---------- 兵器建造条目（"Weapon:模板键"，键全集见 WeaponBuildDatabase）----------
    /// <summary>"Weapon:black_cannon" → "black_cannon"；兼容 "Weapon:键:TERRAIN" 地形尾缀（尾缀在 CanBuildFacilityOnTerrain 单独剥离）</summary>
    public static string ParseWeaponBuildEntry(string entry)
    {
        if (string.IsNullOrEmpty(entry)) return null;
        if (!entry.StartsWith("Weapon:", System.StringComparison.OrdinalIgnoreCase)) return null;
        string rest = entry["Weapon:".Length..].Trim();
        int sep = rest.LastIndexOf(':');
        if (sep > 0) rest = rest[..sep].Trim();
        return string.IsNullOrEmpty(rest) ? null : rest;
    }

    public static bool IsWeaponBuildEntry(string entry) => ParseWeaponBuildEntry(entry) != null;

    /// <summary>能否开始一次建造（按钮显示条件）</summary>
    public bool CanStartConstruction(bool internalMode)
    {
        if (!canBuildFacilities) return false;
        if (isConstructing) return false;
        if (constructionCooldownRemaining > 0) return false;
        if (constructionWhitelist == null || constructionWhitelist.Count == 0) return false;
        if (currentConstructionAmmo <= 0) return false;                    // ⑥ 建造弹药决定能否建造
        if (internalMode && !ConstructionAllowsInternal) return false;
        if (!internalMode && !ConstructionAllowsExternal) return false;
        return true;
    }

    /// <summary>开始建造</summary>
    public bool StartConstruction(string facilityType, bool internalMode, Grids externalTargetGrid = null)
    {
        if (!CanStartConstruction(internalMode)) return false;
        if (!internalMode && (externalTargetGrid == null || !GetValidExternalConstructionGrids(facilityType).Contains(externalTargetGrid))) return false;
        // 支持三类条目：① FacilityDatabase 建筑；② "Terrain:地形名"；③ "Weapon:模板键"（兵器建造）
        if (FacilityDatabase.GetScenePath(facilityType) == null && !IsTerrainBuildEntry(facilityType) && !IsWeaponBuildEntry(facilityType)) return false;

        if (!TryConsumeAmmo(AmmoKind.Construction)) return false;  // 开工消耗 1 发建造弹药

        constructionTargetFacilityType = facilityType;
        constructionInternal = internalMode;
        constructionTargetGrid = externalTargetGrid;
        constructionProgress = 0;
        isConstructing = true;

        // ⑩ 自动化=建造不消耗行动；非自动化=本回合被建造占用（待机）
        if (!constructionAutomation)
        {
            isMoved = true;
            isAttacked = true;
            hasActed = true;
            state = UnitState.Acted;
            SetWaitVisual(true);
        }
        // ✅ 总进度 N ≤ 0 = 即时建造（DoR Rig 搭临时机场/港口：当回合完成）
        if (constructionTotalProgress <= 0) CompleteConstruction();
        return true;
    }

    /// <summary>每回合推进（由 GameManager.Turn 调用）</summary>
    public void ProcessTurnConstruction()
    {
        if (constructionCooldownRemaining > 0) constructionCooldownRemaining--;
        if (!isConstructing) return;
        constructionProgress += Math.Max(0, constructionProgressPerTurn);
        if (constructionProgress >= constructionTotalProgress) CompleteConstruction();
    }

    /// <summary>中断建造（进度清零，不返还弹药，冷却照常开始）</summary>
    public void CancelConstruction()
    {
        if (!isConstructing) return;
        isConstructing = false;
        constructionProgress = 0;
        constructionTargetFacilityType = "";
        constructionTargetGrid = null;
        constructionCooldownRemaining = Math.Max(0, constructionCooldownTurns);
    }

    private void CompleteConstruction()
    {
        isConstructing = false;
        constructionCooldownRemaining = Math.Max(0, constructionCooldownTurns);
        string facilityType = constructionTargetFacilityType;
        bool internalMode = constructionInternal;
        Grids target = constructionTargetGrid;
        constructionProgress = 0;
        constructionTargetFacilityType = "";
        constructionTargetGrid = null;

        if (string.IsNullOrEmpty(facilityType)) return;

        // ✅ 地形建造条目（管道构造者：Terrain:PIPESEAM）
        var terrainBuild = ParseTerrainBuildEntry(facilityType);
        if (terrainBuild.HasValue)
        {
            if (target == null || !IsInstanceValid(target))
                target = FindValidExternalConstructionGrid(facilityType);
            if (target != null) BuildTerrainOnGrid(terrainBuild.Value, target);
            return;
        }

        // ✅ 兵器建造条目（Weapon:模板键）：内部=存舱内携带，外部=直接落子（多格兵器足印被挡则返还弹药）
        if (IsWeaponBuildEntry(facilityType))
        {
            if (internalMode)
            {
                carriedFacilityTypes.Add(facilityType);
                return;
            }
            var wTarget = target;
            if (wTarget == null || !IsInstanceValid(wTarget))
                wTarget = FindValidExternalConstructionGrid(facilityType);
            if (wTarget == null) return;
            if (!SpawnWeaponOnGrid(facilityType, wTarget))
            {
                currentConstructionAmmo = Math.Min(maxConstructionAmmo, currentConstructionAmmo + 1);
                GD.PushWarning($"[建造] 兵器 {facilityType} 落子失败：足印格被占用，建造弹药已返还");
            }
            return;
        }

        if (internalMode)
        {
            // 内部建造与搭载模块无绑定：建好的建筑存进舱内数据，之后「部署」到场上
            carriedFacilityTypes.Add(facilityType);
            return;
        }

        // 外部：直接放到目标格（自动放置时由调用方选好格）
        if (target == null || !IsInstanceValid(target))
            target = FindValidExternalConstructionGrid(facilityType);
        if (target == null) return;
        SpawnFacilityOnGrid(facilityType, target);
    }

    /// <summary>在目标格铺设地形（管道构造者修复/续接管道接缝）</summary>
    private void BuildTerrainOnGrid(GridType terrain, Grids target)
    {
        target.gridType = terrain;   // Grids._Process 检测变化后自动刷新地形图标/贴图
        // ✅ 多方向地形：地形变化后刷新方向（本格+邻格）
        var gmDir = GetTree()?.GetFirstNodeInGroup("grid_manager") as GridManager;
        gmDir?.RefreshAllTerrainDirections();

        // 管道接缝：生成可被攻击的接缝兵器（99HP，防御≈中型坦克，摧毁后地形自动变 BROKENPIPE）
        if (terrain == GridType.PIPESEAM && target.weapon == null)
        {
            var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            var scene = GD.Load<PackedScene>("res://Prefabs/PipeSeam.tscn");
            if (gm?.weaponManager != null && scene != null)
            {
                // 无队伍、不变色（原色调）——中转口才有队伍全变色
                var seam = gm.weaponManager.SpawnWeapon(scene, target.GridIndex, TeamHelper.Player0);
                seam.Name = $"{TeamHelper.Player0}_PipeSeam_{target.GridIndex.X}_{target.GridIndex.Y}";
            }
        }
    }

    /// <summary>在指定格子上生成建筑（部署/外部建造共用）</summary>
    public bool SpawnFacilityOnGrid(string facilityType, Grids target)
    {
        if (target == null || !IsInstanceValid(target)) return false;
        if (target.city != null && IsInstanceValid(target.city)) return false;   // 已有建筑
        string scenePath = FacilityDatabase.GetScenePath(facilityType);
        if (scenePath == null) return false;

        var scene = GD.Load<PackedScene>(scenePath);
        if (scene == null) return false;
        var facility = scene.Instantiate() as Facility;
        if (facility == null) return false;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null) { facility.QueueFree(); return false; }

        facility.facilityTeam = ResolveConstructionTargetTeam();   // ⑬ 产物势力策略
        target.AddChild(facility);   // Facility._Ready 会以父节点 Grids 自动绑定 ParentGrid
        return true;
    }

    /// <summary>部署舱内建筑：从 carriedFacilityTypes 取一个放到目标格</summary>
    public bool DeployCarriedFacility(Grids target)
    {
        if (carriedFacilityTypes.Count == 0) return false;
        if (target == null || !CanBuildFacilityOnTerrain(carriedFacilityTypes[0], target.gridType)) return false;   // ⑫ 部署也受地形限制
        if (IsWeaponBuildEntry(carriedFacilityTypes[0]))
        {
            if (SpawnWeaponOnGrid(carriedFacilityTypes[0], target)) { carriedFacilityTypes.RemoveAt(0); return true; }
            GD.PushWarning($"[建造] 兵器 {carriedFacilityTypes[0]} 部署失败：足印格被占用");
            return false;
        }
        if (!SpawnFacilityOnGrid(carriedFacilityTypes[0], target)) return false;
        carriedFacilityTypes.RemoveAt(0);
        return true;
    }

    /// <summary>⑫ 该建筑能否建在此地形：检查器白名单优先，未配置才用 FacilityDatabase 默认规则</summary>
    public bool CanBuildFacilityOnTerrain(string facilityType, GridType terrain)
    {
        // ✅ 地形建造条目：管道接缝默认可建在 平原/管道废墟(修复)/管道(延伸) 上
        var terrainBuild = ParseTerrainBuildEntry(facilityType);
        if (terrainBuild.HasValue)
        {
            if (terrainBuild.Value == GridType.PIPESEAM)
                return terrain is GridType.GROUND or GridType.BROKENPIPE or GridType.PIPE;
            return terrain == GridType.GROUND;   // 其他地形条目：默认平原
        }

        // ✅ 兵器建造条目：默认不限地形；写 "Weapon:键:TERRAIN"（如 "Weapon:black_cannon:SEA"）限定。键含冒号，单独剥离尾缀。
        if (IsWeaponBuildEntry(facilityType) && constructionAllowedTerrains != null && constructionAllowedTerrains.Count > 0)
        {
            bool weaponRule = false;
            foreach (var raw in constructionAllowedTerrains)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string entry = raw.Trim();
                if (!entry.StartsWith(facilityType + ":", StringComparison.OrdinalIgnoreCase)) continue;
                weaponRule = true;
                string ter = entry[facilityType.Length..].TrimStart(':').Trim();
                if (Enum.TryParse<GridType>(ter, true, out var gt) && gt == terrain) return true;
            }
            return !weaponRule;   // 白名单没提该兵器 = 不限地形（与建筑规则一致）
        }

        if (constructionAllowedTerrains == null || constructionAllowedTerrains.Count == 0)
            return FacilityDatabase.IsTerrainAllowed(facilityType, terrain);

        bool hasRuleForThis = false;
        foreach (var raw in constructionAllowedTerrains)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string entry = raw.Trim();
            string fac = null, ter = entry;
            int sep = entry.IndexOf(':');
            if (sep >= 0) { fac = entry[..sep].Trim(); ter = entry[(sep + 1)..].Trim(); }
            if (fac != null && !string.Equals(fac, facilityType, StringComparison.OrdinalIgnoreCase)) continue;
            hasRuleForThis = true;
            if (Enum.TryParse<GridType>(ter, true, out var g) && g == terrain) return true;
        }
        // 白名单里完全没有提到这个建筑（也没有通用条目）=该建筑不限地形
        return !hasRuleForThis;
    }

    /// <summary>外部建造自动放置：在范围内随机挑一个可放格子</summary>
    public Grids FindValidExternalConstructionGrid(string facilityType)
    {
        var candidates = GetValidExternalConstructionGrids(facilityType);
        if (candidates.Count == 0) return null;
        var rng = new Random();
        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>外部建造有效目标格：范围内 + 空位 + 无既有建筑</summary>
    public List<Grids> GetValidExternalConstructionGrids(string facilityType = null)
    {
        var result = new List<Grids>();
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null || gm.gridManager.map == null) return result;

        int x = grid != null ? grid.GridIndex.X : -1;
        int y = grid != null ? grid.GridIndex.Y : -1;
        if (x < 0) return result;

        int minX = Math.Max(0, x - constructionMaxRange);
        int maxX = Math.Min(gm.gridManager.map.GetLength(0) - 1, x + constructionMaxRange);
        int minY = Math.Max(0, y - constructionMaxRange);
        int maxY = Math.Min(gm.gridManager.map.GetLength(1) - 1, y + constructionMaxRange);

        for (int gx = minX; gx <= maxX; gx++)
        {
            for (int gy = minY; gy <= maxY; gy++)
            {
                int dist = Math.Abs(gx - x) + Math.Abs(gy - y);
                if (dist < constructionMinRange || dist > constructionMaxRange) continue;
                var g = gm.gridManager.map[gx, gy];
                if (g == null || !gm.gridManager.IsRangeAllowed(grid, g, constructionCanCrossScreens)) continue;
                if (g.city != null && IsInstanceValid(g.city)) continue;   // 已有建筑
                if (g.weapon != null) continue;                            // 兵器占位
                if (!CanBuildFacilityOnTerrain(facilityType, g.gridType)) continue;   // ⑫ 可建造地形白名单（如临时港口只能建在海滩）
                result.Add(g);
            }
        }
        return result;
    }

    // ==================== 兵器建造 / ⑬ 势力策略 ====================

    /// ⑬ 解析建造产物归属势力：0=仅友方(建造者) 1=仅敌方(就近敌方势力，找不到回退中立) 2=中立/全部(Player0)
    public string ResolveConstructionTargetTeam()
    {
        switch (constructionTargetTeamPolicy)
        {
            case 1: return FindNearestEnemyTeam() ?? TeamHelper.Player0;
            case 2: return TeamHelper.Player0;
            default: return team;
        }
    }

    /// 就近搜索与建造者敌对的势力（曼哈顿距离最近的敌方单位/设施所属队伍；无敌方则 null）
    private string FindNearestEnemyTeam()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var grids = gm?.gridManager?.grids;
        if (grids == null || grid == null) return null;
        string best = null;
        int bestDist = int.MaxValue;
        Vector2I origin = grid.GridIndex;
        foreach (var g in grids)
        {
            string t = null;
            if (g.infantry != null && IsInstanceValid(g.infantry) && TeamHelper.IsEnemyTeam(team, g.infantry.team))
                t = g.infantry.team;
            else if (g.infantries != null)
            {
                foreach (var other in g.infantries)
                    if (other != null && IsInstanceValid(other) && TeamHelper.IsEnemyTeam(team, other.team)) { t = other.team; break; }
            }
            if (t == null && g.city != null && IsInstanceValid(g.city) && TeamHelper.IsEnemyTeam(team, g.city.facilityTeam))
                t = g.city.facilityTeam;
            if (t == null) continue;
            int d = Math.Abs(g.GridIndex.X - origin.X) + Math.Abs(g.GridIndex.Y - origin.Y);
            if (d < bestDist) { bestDist = d; best = t; }
        }
        return best;
    }

    /// 生成建造产物兵器（与 SpawnFacilityOnGrid 对称的兵器版；多格兵器足印被挡时 SpawnWeapon 返回 null）
    public bool SpawnWeaponOnGrid(string weaponEntry, Grids target)
    {
        if (target == null || !IsInstanceValid(target)) return false;
        string key = ParseWeaponBuildEntry(weaponEntry);
        string scenePath = key == null ? null : WeaponBuildDatabase.GetScenePath(key);
        if (scenePath == null) return false;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.weaponManager == null) return false;
        var scene = GD.Load<PackedScene>(scenePath);
        if (scene == null) return false;
        return gm.weaponManager.SpawnWeapon(scene, target.GridIndex, ResolveConstructionTargetTeam()) != null;
    }

}
