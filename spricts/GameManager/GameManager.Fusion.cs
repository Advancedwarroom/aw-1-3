using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// GameManager · Fusion —— 单位融合模块（开创人 2026-11-20 钦定）
// 资格：操作单位与目标单位【至少一方】开启 fusionEnabled（单位级独立开关，无总开关）
// 融合方式（开创人2026-11-20 钦定）：只能【移动到目标位置】融合——移动落点后扫描 fusionRange 带内可融合目标
//   真人弹「融合/取消」确认框（确认=融合；取消=正常弹菜单），AI 直融
// 配置归属：双方都开 → 取操作单位；仅一方开 → 取开启方
// 配方制：fusionRecipes 条目 "父母A+父母B=孩子"（UnitTypeId，支持 B+A 无序查找）
// 自由混沌制：任意组合 → fusionFreeResult 指定的孩子（空=禁止）
// 献祭式(fusionSelfDestruct=true)：孩子为模板纯净体，不带父母数值/资源/技能
// 合并式(false)：血量/上限/防御/移动力按 fusionStatMode，燃料八弹种按 fusionResourceMode，技能按 fusionSkillMode
// 舱内乘客三策略（开创人2026-11-20 钦定，看齐合流）：0=先卸后融 1=拒融(默认) 2=保留乘客
// 冷却：孩子出生自带 fusionCooldown 回合冷却（大回合结算，与召唤冷却同节拍）
public partial class Infantry
{
    [ExportGroup("融合模块（Fusion · 开创人2026-11-20）")]
    [Export] public bool fusionEnabled = false;                    // 单位级独立开关：至少一方开启即可融合
    [Export] public int fusionRangeMin = 0;                        // 融合范围带：曼哈顿距离下限（0=自身格）
    [Export] public int fusionRangeMax = 1;                        // 融合范围带上限（默认0-1：落点格+四邻）
    [Export(PropertyHint.Enum, "配方制,自由混沌制")]
    public int fusionMode = 0;                                     // 0=查配方表 1=任意组合→fusionFreeResult
    [Export] public string[] fusionRecipes = new string[0];      // "A+B=C"（UnitTypeId，支持无序）
    [Export] public string fusionFreeResult = "";                  // 自由制结果单位名（空=禁止自由融合）
    [Export(PropertyHint.Enum, "模板纯净,技能并集,白名单勾选")]
    public int fusionSkillMode = 1;                                // 0=只留模板 1=A∪B 布尔技能并集 2=fusionSkillWhitelist
    [Export] public string[] fusionSkillWhitelist = new string[0]; // 模式2：仅继承这些布尔技能（Export 字段名）
    [Export(PropertyHint.Enum, "平均,取大,取小,合计封顶")]
    public int fusionStatMode = 0;                                 // 血量/血上限/防御/移动力合成方式
    [Export(PropertyHint.Enum, "合计封顶,取最大值")]
    public int fusionResourceMode = 0;                             // 燃料+八弹种（与合流同语义）
    [Export(PropertyHint.Enum, "先卸后融,拒融,保留乘客")]
    public int fusionLoadedPolicy = 1;                             // 舱内乘客三策略（看齐合流 mergeLoadedPolicy）
    [Export] public bool fusionNeedsAction = true;                 // 融合后孩子视为已行动（false=白赚全新行动）
    [Export] public int fusionCooldown = 0;                        // 孩子出生后冷却回合（0=无）
    [Export] public bool fusionSelfDestruct = false;               // true=献祭式(纯净体) false=合并式(资源带入)
    [Export] public string[] fusionWhitelist = new string[0];    // 可参与融合的单位白名单（空=不限；至少一方命中即过）
    [Export] public string[] fusionBlacklist = new string[0];    // 禁入融合的单位黑名单（UnitTypeId/TemplateId/类名）
    public int fusionCooldownRemaining = 0;                        // 运行时冷却（大回合递减，与 summonCooldownRemaining 同节拍）
}

public partial class GameManager
{
    // ========== 配置来源裁决（开创人2026-11-20：双方都开取操作单位） ==========
    private static Infantry FusionConfigSource(Infantry op, Infantry target)
    {
        if (op.fusionEnabled) return op;
        if (target.fusionEnabled) return target;
        return null;
    }

    private static bool FusionListHit(string[] list, Infantry u)
    {
        if (list == null || list.Length == 0 || u == null) return false;
        return list.Contains(u.UnitTypeId) || list.Contains(u.TemplateId) || list.Contains(u.GetType().Name);
    }

    // 配方查找：无序（A+B 与 B+A 等价）
    private static string FindFusionRecipe(Infantry op, Infantry target)
    {
        var cfg = FusionConfigSource(op, target);
        if (cfg?.fusionRecipes == null) return "";
        string a = string.IsNullOrWhiteSpace(op.UnitTypeId) ? op.TemplateId : op.UnitTypeId;
        string b = string.IsNullOrWhiteSpace(target.UnitTypeId) ? target.TemplateId : target.UnitTypeId;
        foreach (var entry in cfg.fusionRecipes)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var kv = entry.Split('=');
            if (kv.Length != 2) continue;
            var pair = kv[0].Split('+');
            if (pair.Length != 2) continue;
            string p0 = pair[0].Trim(), p1 = pair[1].Trim();
            if ((p0 == a && p1 == b) || (p0 == b && p1 == a)) return kv[1].Trim();
        }
        return "";
    }

    private static string ResolveFusionChild(Infantry op, Infantry target)
    {
        var cfg = FusionConfigSource(op, target);
        if (cfg == null) return "";
        return cfg.fusionMode == 0 ? FindFusionRecipe(op, target) : cfg.fusionFreeResult;
    }

    // ========== 融合合法性（全门集中） ==========
    public bool CanFuse(Infantry op, Infantry target)
    {
        if (op == null || target == null) return false;
        if (!IsInstanceValid(op) || !IsInstanceValid(target)) return false;
        if (op == target) return false;
        if (op.team != target.team) return false;
        if (!op.fusionEnabled && !target.fusionEnabled) return false;   // 钦定：至少一方开启
        var cfg = FusionConfigSource(op, target);
        if (cfg == null) return false;
        if (cfg.fusionCooldownRemaining > 0) return false;
        if (cfg.fusionWhitelist != null && cfg.fusionWhitelist.Length > 0
            && !FusionListHit(cfg.fusionWhitelist, op) && !FusionListHit(cfg.fusionWhitelist, target)) return false;
        if (FusionListHit(cfg.fusionBlacklist, op) || FusionListHit(cfg.fusionBlacklist, target)) return false;
        // 舱内乘客三策略（开创人2026-11-20：看齐合流）：
        //   0=先卸后融 1=拒融(默认) 2=保留乘客（ExecuteFusion 吸收）
        if (AnyLoadedTransport(op, target) && cfg.fusionLoadedPolicy == 1) return false;
        string childName = ResolveFusionChild(op, target);
        if (string.IsNullOrWhiteSpace(childName)) return false;
        if (!UnitProductionDatabase.HasUnit(childName)) return false;
        return true;
    }

    // ========== 范围带扫描（开创人2026-11-20：移动到目标位置融合；带 = 操作方 fusionRange，未开方取默认0-1） ==========
    public List<Infantry> GetFusionTargetsInBand(Infantry op, Vector2I centerIndex)
    {
        var result = new List<Infantry>();
        if (op == null || gridManager == null) return result;
        int rMin = op.fusionEnabled ? Mathf.Max(0, op.fusionRangeMin) : 0;
        int rMax = op.fusionEnabled ? Mathf.Max(rMin, op.fusionRangeMax) : 1;
        for (int dx = -rMax; dx <= rMax; dx++)
        for (int dy = -rMax; dy <= rMax; dy++)
        {
            int man = Mathf.Abs(dx) + Mathf.Abs(dy);
            if (man < rMin || man > rMax) continue;
            var pos = centerIndex + new Vector2I(dx, dy);
            if (!unitManager.IsValidGrid(pos)) continue;
            var g = gridManager.map[pos.X, pos.Y];
            if (g?.infantry == null) continue;
            if (CanFuse(op, g.infantry)) result.Add(g.infantry);
        }
        return result;
    }

    // ========== 真人「融合/取消」确认框（开创人2026-11-20：对齐合流确认框） ==========
    // 移动已完成后调用：确认→onConfirm（ExecuteFusion）；取消→onCancel（正常弹菜单，单位保持落点）。
    public void PromptFusionConfirmation(Infantry op, Infantry target, Node dialogParent, Action onConfirm, Action onCancel = null)
    {
        var dlg = new ConfirmationDialog
        {
            DialogText = $"是否与 {target.unitDisplayName}（{target.health}/{target.maxHealth}）融合？\n（确认=融合；取消=保持落点）",
            OkButtonText = "融合",
            CancelButtonText = "取消"
        };
        dialogParent.AddChild(dlg);
        dlg.Confirmed += () =>
        {
            onConfirm?.Invoke();
            dlg.QueueFree();
        };
        dlg.Canceled += () =>
        {
            onCancel?.Invoke();
            dlg.QueueFree();
        };
        dlg.PopupCentered();
    }

    // ========== 数值合成：0=平均 1=取大 2=取小 3=合计封顶 ==========
    private static int FuseStat(int mode, int a, int b) => mode switch
    {
        1 => Mathf.Max(a, b),
        2 => Mathf.Min(a, b),
        3 => a + b,
        _ => Mathf.RoundToInt((a + b) / 2f),
    };

    private static int FuseResource(int mode, int a, int b, int max)
        => mode == 1 || max <= 0 ? Mathf.Max(a, b) : Mathf.Min(Mathf.Max(1, max), a + b);

    private void ApplyFusionStats(Infantry cfg, Infantry a, Infantry b, Infantry child)
    {
        int sm = cfg.fusionStatMode;
        child.maxHealth = Mathf.Max(1, FuseStat(sm, a.maxHealth, b.maxHealth));
        child.health = FuseStat(sm, a.health, b.health);
        if (sm == 3) child.health = Mathf.Min(child.health, child.maxHealth);   // 合计封顶
        child.health = Mathf.Clamp(child.health, 1, child.maxHealth);
        child.baseDefense = FuseStat(sm, a.baseDefense, b.baseDefense);
        child.movePoints = FuseStat(sm, a.movePoints, b.movePoints);
        child.defaultMovePoints = child.movePoints;
    }

    private void ApplyFusionResources(Infantry cfg, Infantry a, Infantry b, Infantry child)
    {
        int rm = cfg.fusionResourceMode;
        child.fuel = FuseResource(rm, a.fuel, b.fuel, child.maxFuel);
        child.currentPrimaryAmmo = FuseResource(rm, a.currentPrimaryAmmo, b.currentPrimaryAmmo, child.maxPrimaryAmmo);
        child.currentExplodeAmmo = FuseResource(rm, a.currentExplodeAmmo, b.currentExplodeAmmo, child.maxExplodeAmmo);
        child.currentFlareAmmo = FuseResource(rm, a.currentFlareAmmo, b.currentFlareAmmo, child.maxFlareAmmo);
        child.currentStealthBullets = FuseResource(rm, a.currentStealthBullets, b.currentStealthBullets, child.maxStealthBullets);
        child.currentRevealBullets = FuseResource(rm, a.currentRevealBullets, b.currentRevealBullets, child.maxRevealBullets);
        child.currentCamoBullets = FuseResource(rm, a.currentCamoBullets, b.currentCamoBullets, child.maxCamoBullets);
        child.currentProductionAmmo = FuseResource(rm, a.currentProductionAmmo, b.currentProductionAmmo, child.maxProductionAmmo);
        child.currentConstructionAmmo = FuseResource(rm, a.currentConstructionAmmo, b.currentConstructionAmmo, child.maxConstructionAmmo);
    }

    // 技能继承黑名单：身份/状态/融合本身等绝不继承
    private static readonly string[] FusionSkillBlacklist =
    {
        "team", "UnitTypeId", "TemplateId", "useDefaultConfig", "isCoUnit", "isAIControlled",
        "fusionEnabled", "isMoved", "isAttacked", "hasActed", "isTransported", "isStealthed",
        "healthLocked", "canMerge", "isPowerActivated", "state",
    };

    // 技能继承：仅布尔型 Export；1=并集（A∪B 有 true 即 true） 2=白名单字段名
    private void ApplyFusionSkills(Infantry cfg, Infantry a, Infantry b, Infantry child)
    {
        if (cfg.fusionSkillMode == 0) return;
        var fields = typeof(Infantry).GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var f in fields)
        {
            if (f.FieldType != typeof(bool)) continue;
            if (f.GetCustomAttribute<ExportAttribute>() == null) continue;
            if (Array.IndexOf(FusionSkillBlacklist, f.Name) >= 0) continue;
            if (cfg.fusionSkillMode == 2
                && (cfg.fusionSkillWhitelist == null || !cfg.fusionSkillWhitelist.Contains(f.Name))) continue;
            bool va = f.GetValue(a) is bool ba && ba;
            bool vb = f.GetValue(b) is bool bb && bb;
            if (va || vb) f.SetValue(child, true);
        }
    }

    // ========== 先卸后融：融合完成后把孩子舱内乘客卸到四邻空格（没位子就留在舱内——仁慈兜底） ==========
    private void UnloadFusionPassengers(Infantry child)
    {
        if (child?.transportedUnits == null || child.transportedUnits.Count == 0) return;
        if (child.grid == null) return;
        var frees = new List<Vector2I>();
        Vector2I[] offsets = { new Vector2I(0, 1), new Vector2I(-1, 0), new Vector2I(0, -1), new Vector2I(1, 0) };
        foreach (var off in offsets)
        {
            var pos = child.grid.GridIndex + off;
            if (!unitManager.IsValidGrid(pos)) continue;
            var g = gridManager.map[pos.X, pos.Y];
            if (g == null || g.infantry != null || g.weapon != null) continue;
            if (g.PreventsUnitStop(gridManager)) continue;
            frees.Add(pos);
        }
        int fi = 0;
        foreach (var p in child.transportedUnits.ToList())
        {
            if (fi >= frees.Count) break;
            var pos = frees[fi++];
            var g = gridManager.map[pos.X, pos.Y];
            child.transportedUnits.Remove(p);
            p.isTransported = false;
            g.infantries.Add(p);
            if (g.infantry == null) g.infantry = p;
            p.grid = g;
            p.Position = g.Position;
        }
        child.UpdateLoadedIcon();
    }

    // ========== ✅ 融合执行：两单位 → 指定孩子（孩子落在目标格，目标原地"变身"） ==========
    public Infantry ExecuteFusion(Infantry op, Infantry target)
    {
        if (!CanFuse(op, target)) return null;
        var cfg = FusionConfigSource(op, target);
        string childName = ResolveFusionChild(op, target);
        var info = UnitProductionDatabase.GetInfo(childName);
        if (info == null) return null;
        var scene = GD.Load<PackedScene>(info.ScenePath);
        if (scene == null) return null;

        var targetGridIndex = target.grid.GridIndex;

        // ① 父母特殊状态清理（防幽灵 capturingUsers/tearingUsers，镜像合流④）
        foreach (var p in new[] { op, target })
        {
            if (p.capturingGrid?.city != null) p.capturingGrid.city.OnCaptureInterrupted(p);
            if (p.tearingGrid?.city != null) p.tearingGrid.city.OnCaptureInterrupted(p);
            p.currentCaptureProgress = 0;
            p.currentTearProgress = 0;
            p.capturingGrid = null;
            p.tearingGrid = null;
        }

        // ② 生成孩子（真实体：注册/占位/迷雾全走现行管线）
        var childObj = unitManager.SpawnUnit(scene, targetGridIndex, op.team);
        var child = childObj as Infantry;
        if (child == null)
        {
            if (childObj != null) childObj.QueueFree();
            GD.Print("[Fusion] 孩子生成失败，父母保持原位");
            return null;
        }
        RegisterUnit(child, InferCategory(child));

        // ③ 献祭式=纯净体；合并式=数值/资源/技能合成
        if (!cfg.fusionSelfDestruct)
        {
            ApplyFusionStats(cfg, op, target, child);
            ApplyFusionResources(cfg, op, target, child);
            ApplyFusionSkills(cfg, op, target, child);
        }

        // ④ 舱内乘客三策略（开创人2026-11-20：看齐合流）
        //   2=保留乘客：target 吸收全部乘客（可超载/违白名单），锁再装载（"我的仁慈"只给一次）
        //   0=先卸后融：融合完成后把孩子舱内乘客卸到四邻空格（没位子留在舱内兜底）
        //   1=拒融：CanFuse 已拦截，到不了这里
        child.transportedUnits ??= new List<Infantry>();
        foreach (var p in new[] { op, target })
        {
            if (p.transportedUnits == null) continue;
            foreach (var pass in p.transportedUnits)
            {
                if (pass == null || !IsInstanceValid(pass)) continue;
                child.transportedUnits.Add(pass);
                pass.isTransported = true;
            }
            p.transportedUnits.Clear();
        }
        if (cfg.fusionLoadedPolicy == 2)
        {
            child.transportMergeOverflowLock = true;
            child.UpdateLoadedIcon();
        }
        else
        {
            UnloadFusionPassengers(child);
        }

        // ⑤ 父母离场
        RemoveUnit(op);
        RemoveUnit(target);

        // ⑥ 冷却 + 行动状态
        child.fusionCooldownRemaining = Mathf.Max(0, cfg.fusionCooldown);
        if (cfg.fusionNeedsAction)
        {
            child.isMoved = true;
            child.isAttacked = true;
            child.hasActed = true;
            child.state = UnitState.Acted;
            child.SetWaitVisual(true);
        }
        else
        {
            child.OnTurnEnd();   // 白赚全新行动
        }

        child.UpdateHpLabel();
        UpdateUnitLists();
        RefreshSpecializedUnitLists();
        fogOfWarManager?.OnUnitMoved();
        EmpowermentRuntime.RefreshAll(this);
        CheckVictoryCondition();
        return child;
    }
}
