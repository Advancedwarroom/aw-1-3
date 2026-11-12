using Godot;
using System;
using System.Linq;

// ============================================================
// PaidResupplyModules.cs —— 自费补给模块（开创人 2026-11-11 定案）
// 三族通用：任何单位/兵器/设施都可成为「供弹方」或「受补方」。
// 模式：0=自点自补（点自己出补给按钮） 1=代点他补（点供弹方，给n-m内白名单目标补给） 2=两者皆可
// 铁律：每势力相位一次（stamp防伪）＋冷却回合；操作消耗行动力（prActs=true则待机）
// 代价四方式：①资金（可负）②自身十四资源（可负）③献祭（范围内按势力政策抽第三者十四资源）
//           ④组合：启用几种，菜单亮几个按钮，玩家自选
// 特殊弹（第11资源）：useSpecialAmmo=false 或 max<=0 = 无限（支付/补充对它免单）
// ============================================================

public partial class Infantry
{
    [ExportGroup("自费补给·开关")]
    [Export] public bool paidResupplyEnabled = false;                 // 总开关
    [Export(PropertyHint.Enum, "自点自补,代点他补,两者皆可")]
    public int paidResupplyMode = 0;                                  // ①
    [Export] public bool paidResupplyActs = true;                     // ②独立操作（true=补完待机）
    [Export] public int paidResupplyCooldown = 0;                     // ③冷却回合（0=无冷却，仍每相位一次）

    [ExportGroup("自费补给·补什么（勾选受补资源，0=补满）")]
    [Export] public int paidResupplyRefillAmount = 0;               // 0=补满；>0=固定量
    [Export] public bool prTargetHp = true;
    [Export] public bool prTargetFuel = false;
    [Export] public bool prTargetWeight = false;
    [Export] public bool prTargetAmmoPrimary = true;
    [Export] public bool prTargetAmmoExplode = false;
    [Export] public bool prTargetAmmoFlare = false;
    [Export] public bool prTargetAmmoStealth = false;
    [Export] public bool prTargetAmmoReveal = false;
    [Export] public bool prTargetAmmoCamo = false;
    [Export] public bool prTargetAmmoProduction = false;
    [Export] public bool prTargetAmmoConstruction = false;
    [Export] public bool prTargetAmmoSpecial = false;               // 第11资源·特殊弹
    [Export] public bool prTargetAmmoPushRebound = false;
    [Export] public bool prTargetAmmoPushMitigation = false;
    [Export] public bool prTargetAmmoAntiGravityTransfer = false;

    [ExportGroup("自费补给·对象资格（模式1代补：谁是合法受补方）")]
    [Export] public int prRangeMin = 1;                             // n-m 距离带（0=同格/正上方）
    [Export] public int prRangeMax = 1;                             // 0-0=仅同格；1-1=临格
    [Export] public string[] prTargetWhitelist = System.Array.Empty<string>();  // 受补白名单（UnitTypeId/类名；空=不限）
    [Export] public bool prTargetUnits = true;
    [Export] public bool prTargetWeapons = true;
    [Export] public bool prTargetFacilities = true;

    [ExportGroup("自费补给·代价①资金")]
    [Export] public bool prUseFunds = true;                         // 启用资金支付
    [Export] public int prFundsCost = 0;                            // 资金价（负=反向退款）

    [ExportGroup("自费补给·代价②自身资源（扣供弹方自己的）")]
    [Export] public bool prUseSelf = false;
    [Export] public int prSelfCostAmount = 0;                       // 每勾选资源扣这么多（负=返还）
    [Export] public bool prSelfCostHp = false;
    [Export] public bool prSelfCostFuel = false;
    [Export] public bool prSelfCostWeight = false;
    [Export] public bool prSelfCostAmmoPrimary = false;
    [Export] public bool prSelfCostAmmoExplode = false;
    [Export] public bool prSelfCostAmmoFlare = false;
    [Export] public bool prSelfCostAmmoStealth = false;
    [Export] public bool prSelfCostAmmoReveal = false;
    [Export] public bool prSelfCostAmmoCamo = false;
    [Export] public bool prSelfCostAmmoProduction = false;
    [Export] public bool prSelfCostAmmoConstruction = false;
    [Export] public bool prSelfCostAmmoSpecial = false;
    [Export] public bool prSelfCostAmmoPushRebound = false;
    [Export] public bool prSelfCostAmmoPushMitigation = false;
    [Export] public bool prSelfCostAmmoAntiGravityTransfer = false;

    [ExportGroup("自费补给·代价③献祭（抽n-m内第三者的资源）")]
    [Export] public bool prUseSacrifice = false;
    [Export] public int prSacrificeAmount = 0;                      // 每个受害者被抽的量（负=反哺）
    [Export] public bool prSacrificeIncludeFriendly = true;
    [Export] public bool prSacrificeIncludeEnemy = false;
    [Export] public bool prSacrificeIncludeAllied = false;
    [Export] public bool prSacrificeHp = false;
    [Export] public bool prSacrificeFuel = false;
    [Export] public bool prSacrificeWeight = false;
    [Export] public bool prSacrificeAmmoPrimary = false;
    [Export] public bool prSacrificeAmmoExplode = false;
    [Export] public bool prSacrificeAmmoFlare = false;
    [Export] public bool prSacrificeAmmoStealth = false;
    [Export] public bool prSacrificeAmmoReveal = false;
    [Export] public bool prSacrificeAmmoCamo = false;
    [Export] public bool prSacrificeAmmoProduction = false;
    [Export] public bool prSacrificeAmmoConstruction = false;
    [Export] public bool prSacrificeAmmoSpecial = false;
    [Export] public bool prSacrificeAmmoPushRebound = false;
    [Export] public bool prSacrificeAmmoPushMitigation = false;
    [Export] public bool prSacrificeAmmoAntiGravityTransfer = false;

    [ExportGroup("特殊弹（第11资源）")]
    [Export] public bool useSpecialAmmo = false;                    // 启用特殊弹槽（false=该资源无限/免单）
    [Export] public int currentSpecialAmmo = 0;
    [Export] public int maxSpecialAmmo = 0;                         // 0=无限

    // ===== 运行时（非Export）：每相位一次 + 冷却的时间戳 =====
    public int prLastUseStamp = -1;

    private GameManager PrGM() => GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
    private int PrStamp()
    {
        var gm = PrGM();
        return ((gm?.turns ?? 0) + 1) * 100 + (gm?.turnPhase ?? 0);
    }
    public bool PrCanUseNow()
    {
        if (!paidResupplyEnabled) return false;
        int stamp = PrStamp();
        if (prLastUseStamp == stamp) return false;                    // 铁律：每势力相位一次
        if (paidResupplyCooldown > 0 && prLastUseStamp >= 0)
        {
            int phases = Mathf.Max(1, PrGM()?.totalPhases ?? 1);
            int usedT = prLastUseStamp / 100, usedP = prLastUseStamp % 100;
            int nowT = stamp / 100, nowP = stamp % 100;
            int elapsed = (nowT - usedT) * phases + (nowP - usedP);
            if (elapsed <= paidResupplyCooldown * phases) return false; // 冷却中
        }
        return true;
    }

    // ===== 十五资源读写（0血 1油 2-13弹药 14重量；重量按0.01缩放为整数）=====
    public int PrGet(int res) => res switch
    {
        0 => health, 1 => fuel, 2 => currentPrimaryAmmo, 3 => currentExplodeAmmo,
        14 => Mathf.RoundToInt(weight * 100.0f),
        4 => currentFlareAmmo, 5 => currentStealthBullets, 6 => currentRevealBullets,
        7 => currentCamoBullets, 8 => currentProductionAmmo, 9 => currentConstructionAmmo,
        10 => currentSpecialAmmo, 11 => currentPushReboundAmmo,
        12 => currentPushMitigationAmmo, 13 => currentAntiGravityTransferAmmo, _ => 0
    };
    public int PrMax(int res) => res switch
    {
        0 => maxHealth, 1 => maxFuel, 2 => maxPrimaryAmmo, 3 => maxExplodeAmmo,
        4 => maxFlareAmmo, 5 => maxStealthBullets, 6 => maxRevealBullets,
        7 => maxCamoBullets, 8 => maxProductionAmmo, 9 => maxConstructionAmmo,
        10 => maxSpecialAmmo, 11 => maxPushReboundAmmo,
        12 => maxPushMitigationAmmo, 13 => maxAntiGravityTransferAmmo, 14 => 999900, _ => 0
    };
    public void PrSet(int res, int v)
    {
        switch (res)
        {
            case 0: health = v; break; case 1: fuel = v; break;
            case 2: currentPrimaryAmmo = v; break; case 3: currentExplodeAmmo = v; break;
            case 4: currentFlareAmmo = v; break; case 5: currentStealthBullets = v; break;
            case 6: currentRevealBullets = v; break; case 7: currentCamoBullets = v; break;
            case 8: currentProductionAmmo = v; break; case 9: currentConstructionAmmo = v; break;
            case 10: currentSpecialAmmo = v; break;
            case 11: currentPushReboundAmmo = v; break;
            case 12: currentPushMitigationAmmo = v; break;
            case 13: currentAntiGravityTransferAmmo = v; break;
            case 14: weight = Mathf.Clamp(v / 100.0f, 0.0f, 9999.0f); break;
        }
    }
    public bool PrIsInfinite(int res)
        => res == 10 && (!useSpecialAmmo || maxSpecialAmmo <= 0);

    public bool PrTargetResEnabled(int res) => res switch
    {
        0 => prTargetHp, 1 => prTargetFuel, 14 => prTargetWeight, 2 => prTargetAmmoPrimary, 3 => prTargetAmmoExplode,
        4 => prTargetAmmoFlare, 5 => prTargetAmmoStealth, 6 => prTargetAmmoReveal,
        7 => prTargetAmmoCamo, 8 => prTargetAmmoProduction, 9 => prTargetAmmoConstruction,
        10 => prTargetAmmoSpecial, 11 => prTargetAmmoPushRebound,
        12 => prTargetAmmoPushMitigation, 13 => prTargetAmmoAntiGravityTransfer, _ => false
    };
    public bool PrSelfCostEnabled(int res) => res switch
    {
        0 => prSelfCostHp, 1 => prSelfCostFuel, 14 => prSelfCostWeight, 2 => prSelfCostAmmoPrimary, 3 => prSelfCostAmmoExplode,
        4 => prSelfCostAmmoFlare, 5 => prSelfCostAmmoStealth, 6 => prSelfCostAmmoReveal,
        7 => prSelfCostAmmoCamo, 8 => prSelfCostAmmoProduction, 9 => prSelfCostAmmoConstruction,
        10 => prSelfCostAmmoSpecial, 11 => prSelfCostAmmoPushRebound,
        12 => prSelfCostAmmoPushMitigation, 13 => prSelfCostAmmoAntiGravityTransfer, _ => false
    };
    public bool PrSacrificeEnabled(int res) => res switch
    {
        0 => prSacrificeHp, 1 => prSacrificeFuel, 14 => prSacrificeWeight, 2 => prSacrificeAmmoPrimary, 3 => prSacrificeAmmoExplode,
        4 => prSacrificeAmmoFlare, 5 => prSacrificeAmmoStealth, 6 => prSacrificeAmmoReveal,
        7 => prSacrificeAmmoCamo, 8 => prSacrificeAmmoProduction, 9 => prSacrificeAmmoConstruction,
        10 => prSacrificeAmmoSpecial, 11 => prSacrificeAmmoPushRebound,
        12 => prSacrificeAmmoPushMitigation, 13 => prSacrificeAmmoAntiGravityTransfer, _ => false
    };

    // ===== 主入口：由菜单调用。method: 0=资金 1=自身 2=献祭 =====
    public void ExecutePaidResupplyOn(Infantry targetUnit, Weapon targetWeapon, Facility targetFacility, int method)
    {
        if (!PrCanUseNow()) return;
        GameManager gm = PrGM();
        if (gm == null) return;

        // 支付
        if (method == 0 && prUseFunds)
            gm.SetTeamFunds(team, Math.Max(0, gm.GetTeamFunds(team) - prFundsCost));  // 负价=退款
        else if (method == 1 && prUseSelf)
        {
            for (int r = 0; r <= 14; r++)
                if (PrSelfCostEnabled(r) && !PrIsInfinite(r))
                    PrSet(r, Math.Max(0, PrGet(r) - prSelfCostAmount));
        }
        else if (method == 2 && prUseSacrifice)
        {
            var origin = targetUnit?.grid ?? targetWeapon?.grid;
            if (origin == null && targetFacility != null && gm.gridManager?.map != null)
            {
                int ow = gm.gridManager.map.GetLength(0), oh = gm.gridManager.map.GetLength(1);
                for (int ox = 0; ox < ow && origin == null; ox++)
                    for (int oy = 0; oy < oh; oy++)
                        if (gm.gridManager.map[ox, oy]?.city == targetFacility) { origin = gm.gridManager.map[ox, oy]; break; }
            }
            if (gm.gridManager?.map != null && origin != null)
            {
                int w = gm.gridManager.map.GetLength(0), h = gm.gridManager.map.GetLength(1);
                for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
                {
                    var g = gm.gridManager.map[x, y];
                    if (g == null) continue;
                    int d = Mathf.Abs(g.GridIndex.X - origin.GridIndex.X) + Mathf.Abs(g.GridIndex.Y - origin.GridIndex.Y);
                    if (d < prRangeMin || d > prRangeMax) continue;
                    if (prTargetUnits) foreach (var u in g.infantries ?? new System.Collections.Generic.List<Infantry>())
                        PrSacrificeDrainUnit(gm, u);
                    if (prTargetWeapons) foreach (var wp in g.weapons ?? new System.Collections.Generic.List<Weapon>())
                        PrSacrificeDrainWeapon(gm, wp);
                    if (prTargetFacilities && g.city != null)
                        PrSacrificeDrainFacility(gm, g.city);
                }
            }
        }
        else return;   // 方式未启用，交易取消

        // 受补
        PrRefill(targetUnit, targetWeapon, targetFacility);

        // 记账与行动
        prLastUseStamp = PrStamp();
        if (paidResupplyActs) { isMoved = true; isAttacked = true; hasActed = true; state = UnitState.Acted; }
    }

    private void PrRefill(Infantry tu, Weapon tw, Facility tf)
    {
        for (int r = 0; r <= 14; r++)
        {
            if (!PrTargetResEnabled(r)) continue;
            if (tu != null && IsInstanceValid(tu) && !tu.PrIsInfinite(r))
            {
                int cur = tu.PrGet(r), max = Mathf.Max(cur, tu.PrMax(r));
                int add = paidResupplyRefillAmount <= 0 ? max - cur : paidResupplyRefillAmount;
                tu.PrSet(r, Mathf.Clamp(cur + add, r == 0 ? 1 : 0, max));
                if (r == 0) tu.UpdateHpLabel();
            }
            else if (tw != null && IsInstanceValid(tw) && !tw.PrIsInfiniteW(r))
            {
                int cur = tw.PrGetW(r), max = Mathf.Max(cur, tw.PrMaxW(r));
                int add = paidResupplyRefillAmount <= 0 ? max - cur : paidResupplyRefillAmount;
                tw.PrSetW(r, Mathf.Clamp(cur + add, r == 0 ? 1 : 0, max));
            }
            else if (tf != null && IsInstanceValid(tf) && !tf.PrIsInfiniteF(r))
            {
                int cur = tf.PrGetF(r), max = Mathf.Max(cur, tf.PrMaxF(r));
                int add = paidResupplyRefillAmount <= 0 ? max - cur : paidResupplyRefillAmount;
                tf.PrSetF(r, Mathf.Clamp(cur + add, r == 0 ? 1 : 0, max));
            }
        }
    }

    private void PrSacrificeDrainUnit(GameManager gm, Infantry u)
    {
        if (u == null || !IsInstanceValid(u) || u == this) return;
        bool teamOk = TeamHelper.MatchesTeamRelation(team, u.team,
            prSacrificeIncludeFriendly, prSacrificeIncludeEnemy, prSacrificeIncludeAllied);
        if (!teamOk) return;
        for (int r = 0; r <= 14; r++)
            if (PrSacrificeEnabled(r) && !u.PrIsInfinite(r))
                u.PrSet(r, Mathf.Max(0, u.PrGet(r) - prSacrificeAmount));
        u.UpdateHpLabel();
    }
    private void PrSacrificeDrainWeapon(GameManager gm, Weapon wp)
    {
        if (wp == null || !IsInstanceValid(wp)) return;
        bool teamOk = TeamHelper.MatchesTeamRelation(team, wp.team,
            prSacrificeIncludeFriendly, prSacrificeIncludeEnemy, prSacrificeIncludeAllied);
        if (!teamOk) return;
        for (int r = 0; r <= 14; r++)
            if (PrSacrificeEnabled(r) && !wp.PrIsInfiniteW(r))
                wp.PrSetW(r, Mathf.Max(0, wp.PrGetW(r) - prSacrificeAmount));
    }
    private void PrSacrificeDrainFacility(GameManager gm, Facility f)
    {
        if (f == null || !IsInstanceValid(f)) return;
        bool teamOk = TeamHelper.MatchesTeamRelation(team, f.facilityTeam,
            prSacrificeIncludeFriendly, prSacrificeIncludeEnemy, prSacrificeIncludeAllied);
        if (!teamOk) return;
        for (int r = 0; r <= 14; r++)
            if (PrSacrificeEnabled(r) && !f.PrIsInfiniteF(r))
                f.PrSetF(r, Mathf.Max(0, f.PrGetF(r) - prSacrificeAmount));
    }
}

// ============ 兵器侧（受补方资源读写＋特殊弹槽；供弹执行以单位侧为入口） ============
public partial class Weapon
{
    [ExportGroup("特殊弹（第11资源）")]
    [Export] public bool useSpecialAmmo = false;                    // 启用特殊弹槽（false=该资源无限/免单）
    [Export] public int wSpecialCurrent = 0;
    [Export] public int wSpecialMax = 0;                            // 0=无限

    // 十五资源读写（兵器无燃料/生产弹/建造弹 → no-op）
    public int PrGetW(int res) => res switch
    {
        0 => health, 14 => Mathf.RoundToInt(weight * 100.0f), 2 => currentAmmo, 3 => wExplodeCurrent, 4 => wFlareCurrent,
        5 => wStealthBulletCurrent, 6 => wRevealBulletCurrent, 7 => wCamoBulletCurrent,
        10 => wSpecialCurrent, _ => 0
    };
    public int PrMaxW(int res) => res switch
    {
        0 => maxHealth, 14 => 999900, 2 => maxAmmo, 3 => wExplodeMax, 4 => wFlareMax,
        5 => wStealthBulletMax, 6 => wRevealBulletMax, 7 => wCamoBulletMax,
        10 => wSpecialMax, _ => 0
    };
    public void PrSetW(int res, int v)
    {
        switch (res)
        {
            case 0: health = v; break; case 2: currentAmmo = v; break;
            case 14: weight = Mathf.Clamp(v / 100.0f, 0.0f, 9999.0f); break;
            case 3: wExplodeCurrent = v; break; case 4: wFlareCurrent = v; break;
            case 5: wStealthBulletCurrent = v; break; case 6: wRevealBulletCurrent = v; break;
            case 7: wCamoBulletCurrent = v; break; case 10: wSpecialCurrent = v; break;
        }
    }
    public bool PrIsInfiniteW(int res)
        => res == 10 && (!useSpecialAmmo || wSpecialMax <= 0);
}

// ============ 设施侧（受补方资源读写＋特殊弹槽） ============
public partial class Facility
{
    [ExportGroup("特殊弹（第11资源）")]
    [Export] public bool useSpecialAmmo = false;                    // 启用特殊弹槽（false=该资源无限/免单）
    [Export] public int currentSpecialAmmo = 0;
    [Export] public int maxSpecialAmmo = 0;                         // 0=无限

    public int PrGetF(int res) => res switch
    {
        0 => health, 14 => Mathf.RoundToInt(weight * 100.0f), 10 => currentSpecialAmmo, _ => 0
    };
    public int PrMaxF(int res) => res switch
    {
        0 => maxHealth, 14 => 999900, 10 => maxSpecialAmmo, _ => 0
    };
    public void PrSetF(int res, int v)
    {
        switch (res)
        {
            case 0: health = v; break; case 10: currentSpecialAmmo = v; break;
            case 14: weight = Mathf.Clamp(v / 100.0f, 0.0f, 9999.0f); break;
        }
    }
    public bool PrIsInfiniteF(int res)
        => res == 10 && (!useSpecialAmmo || maxSpecialAmmo <= 0);
}
