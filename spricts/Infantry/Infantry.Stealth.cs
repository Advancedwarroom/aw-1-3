using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Stealth（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

    [ExportGroup("隐身模块")]
    [Export] public bool terrainConcealment = true;
    [Export] public bool canStealth = false;
    // ✅ 隐身按钮文字（检查器可改；留空=默认「隐身/显形」）。潜艇填「下潜/上浮」直接复用隐身模块。
    [Export] public string stealthHideButtonText = "";   // 未隐身时按钮文字（动作=进入隐身）
    [Export] public string stealthShowButtonText = "";  // 已隐身时按钮文字（动作=解除隐身）
    [Export] public bool stealthToggleCostsAction = true;    // 隐身/显形是否算独立操作：true=切换后待机；false=切换后仍可攻击移动                            // 可否隐身
    [Export] public bool isStealthed = false;                           // 运行时：当前是否隐身
    // —— 每日消耗（维持隐身时扣，0=该项不扣）——
    [Export] public int stealthFuelCostPerDay = 0;                      // 燃油
    [Export] public int stealthHpCostPerDay = 0;                        // 血量
    [Export] public int stealthPrimaryAmmoCostPerDay = 0;             // 普通弹药
    [Export] public int stealthExplodeAmmoCostPerDay = 0;             // 自爆弹
    [Export] public int stealthFlareAmmoCostPerDay = 0;               // 照明弹
    [Export] public bool stealthForceOneShot = false;    // 阈值效果一次性：true=触发一次后永久锁存；false=持续模式(条件成立期间强制且玩家不可改)
    [Export] public bool stealthFormLocked = false;      // 形态锁定：true=玩家不可隐/现切换（阈值强制不受限，照常在条件满足时改写形态）
    bool stealthForceLatched = false;                    // 运行时：一次性锁存标记
    public bool stealthThresholdLocked = false;          // 运行时：持续模式阈值锁（与手动锁分开存，修复阈值判定覆盖编辑器手动锁）
    public bool IsStealthFormLocked => stealthFormLocked || stealthThresholdLocked;   // ✅ 有效锁定 = 手动锁 或 阈值锁
    [Export] public bool stealthThresholdRevealWins = true;    // 强制隐/强制显同时命中：true=显形优先(原行为) false=隐身优先
    // —— 阈值矩阵：双方向(强制隐/强制显) × 双阈值(高/低) × 八资源 ——
    // 语义=手机电量：高于等于高阈值触发(≥80%性能模式)，低于等于低阈值触发(≤20%省电模式)，两侧持续判定
    [Export] public bool stealthForceHideOneShot = false;      // 强制隐阈值：true=只触发一次(终身)
    [Export] public bool stealthForceRevealOneShot = false;    // 强制显阈值：true=只触发一次(终身)
    [Export] public int stealthForceHideHpHigh = 0;        [Export] public int stealthForceHideHpLow = 0;
    [Export] public int stealthForceRevealHpHigh = 0;      [Export] public int stealthForceRevealHpLow = 0;
    [Export] public int stealthForceHideFuelHigh = 0;      [Export] public int stealthForceHideFuelLow = 0;
    [Export] public int stealthForceRevealFuelHigh = 0;    [Export] public int stealthForceRevealFuelLow = 0;
    [Export] public int stealthForceHidePrimaryHigh = 0;   [Export] public int stealthForceHidePrimaryLow = 0;
    [Export] public int stealthForceRevealPrimaryHigh = 0; [Export] public int stealthForceRevealPrimaryLow = 0;
    [Export] public int stealthForceHideExplodeHigh = 0;   [Export] public int stealthForceHideExplodeLow = 0;
    [Export] public int stealthForceRevealExplodeHigh = 0; [Export] public int stealthForceRevealExplodeLow = 0;
    [Export] public int stealthForceHideFlareHigh = 0;     [Export] public int stealthForceHideFlareLow = 0;
    [Export] public int stealthForceRevealFlareHigh = 0;   [Export] public int stealthForceRevealFlareLow = 0;
    [Export] public int stealthForceHideStealthBulletHigh = 0;   [Export] public int stealthForceHideStealthBulletLow = 0;
    [Export] public int stealthForceRevealStealthBulletHigh = 0; [Export] public int stealthForceRevealStealthBulletLow = 0;
    [Export] public int stealthForceHideRevealBulletHigh = 0;   [Export] public int stealthForceHideRevealBulletLow = 0;
    [Export] public int stealthForceRevealRevealBulletHigh = 0; [Export] public int stealthForceRevealRevealBulletLow = 0;
    [Export] public int stealthForceHideCamoBulletHigh = 0;   [Export] public int stealthForceHideCamoBulletLow = 0;
    [Export] public int stealthForceRevealCamoBulletHigh = 0; [Export] public int stealthForceRevealCamoBulletLow = 0;
    // —— 感知规则 ——
    [Export] public bool stealthVisibleWhenAdjacent = true;           // 贴身可见（X-Y范围）
    [Export] public int stealthVisibleMinRange = 1;                   // 贴身范围X
    [Export] public int stealthVisibleMaxRange = 1;                   // 贴身范围Y
    [Export] public bool stealthVisibleOnTile = true;                 // 同格单位可见（建筑判定见②）
    [Export] public string[] stealthAttackableBy = System.Array.Empty<string>();  // 攻击白名单(空=隐身时无敌)
    // —— 现形规则 ——
    [Export] public bool stealthRevealOnAttack = false;             // 攻击后现形
    [Export] public bool stealthRevealOnDamage = true;              // 受伤现形
    [Export(PropertyHint.Enum, "仅单位攻击,任何掉血")] public int stealthRevealDamageScope = 1;   // 受伤现形的"受伤"范围：0=狭义(只有单位攻击/反击)，1=广义(自爆/兵器/地格/修理扣血等任何掉血)
    [Export] public bool stealthRevealOnFlare = true;               // 照明弹使其现形
    // —— 隐身中行为锁 ——
    [Export] public bool canAttackWhileStealthed = true;            // 隐身时可攻击（AW3=true）
    [Export] public bool canMoveWhileStealthed = true;              // 隐身时可移动
    [Export] public bool canLoadUnloadWhileStealthed = false;         // 隐身时可搭载/卸载
    [Export] public bool canCustomActionWhileStealthed = false;       // 隐身时可自爆等其他自定义动作
    [Export] public bool canCaptureWhileStealthed = false;          // 隐身时可占点
    // —— 照明弹显形 ——
    [Export] public bool stealthFlareRevealPermanent = false;      // true=永久现形；false=仅照明生效期间显形
    [Export] public bool stealthFlareRevealed = false;            // 运行时：照明弹临时显形标记
    // —— 受伤现形阈值 ——
    [Export] public bool stealthRevealOnDamageUseThreshold = false; // true=受伤后血量≤阈值才现形
    [Export] public int stealthRevealOnDamageHpThreshold = 0;       // 受伤现形血量阈值
    // —— 隐身弹系统（每单位三选一，不能通吃！）——
    [Export] public int stealthAmmoMode = 0;                      // 0=无弹免费切换 / 1=双弹种(隐身弹+显形弹) / 2=迷彩弹(通用)
    [Export] public int maxStealthBullets = 0;                    // 隐身弹上限
    [Export] public int currentStealthBullets = 0;                // 隐身弹现有
    [Export] public int maxRevealBullets = 0;                     // 显形弹上限
    [Export] public int currentRevealBullets = 0;                 // 显形弹现有
    [Export] public int maxCamoBullets = 0;                         // 迷彩弹上限
    [Export] public int currentCamoBullets = 0;                     // 迷彩弹现有
    [Export] public int stealthBulletCostPerDay = 0;              // 隐身弹日耗
    [Export] public int revealBulletCostPerDay = 0;             // 显形弹日耗
    [Export] public int camoBulletCostPerDay = 0;                   // 迷彩弹日耗
    // —— 隐身中改写（②提交） ——
    [Export] public int stealthMoveTypeOverride = -1;                 // 隐身移动方式改写(-1=不改)
    [Export] public int stealthOverlapMode = 0;
    [Export] public Godot.Collections.Dictionary<string, int> stealthMatrixOverride = new();  // 隐身时替换的攻防表（键"攻击方_目标"，空=用默认表）                       // 0=不改 1=可重叠 2=不可重叠   // 🔁 合流后再行动：true=合流完成后可继续操作一次（默认关=AW原作）
    private bool stealthVisualApplied = false;   // 运行时：当前已应用的隐身外观   // 运行时：已排队坠毁，防重复

// ✅ 新增：计算对兵器的伤害
    // ✅ 照明弹显形：本格是否处于照明弹亮格内
    public bool IsFlareLit()
    {
        if (grid == null || !stealthRevealOnFlare) return false;
        var gmx = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        return gmx?.fogOfWarManager?.IsGridIlluminated(grid) == true;
    }

    // ✅ 隐身视觉中枢：Modulate + 头顶"隐"指示器（所有隐/现/强制改写的唯一出口）
    private Label _stealthIcon;
    public void UpdateStealthVisual()
    {
        Modulate = isStealthed ? new Color(1, 1, 1, 0.45f) : new Color(1, 1, 1, 1);
        if (_stealthIcon == null)
        {
            _stealthIcon = new Label();
            _stealthIcon.Text = "隐";
            _stealthIcon.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 1f));
            _stealthIcon.AddThemeFontSizeOverride("font_size", 12);
            _stealthIcon.Position = new Vector2(-6, -34);
            AddChild(_stealthIcon);
        }
        _stealthIcon.Visible = isStealthed;
    }

    // ==================== 隐身模块 ====================
    /// <summary>隐身按钮文字（未隐身时，动作=进入隐身；留空=「隐身」）</summary>
    public string GetStealthHideButtonText()
        => string.IsNullOrEmpty(stealthHideButtonText) ? TranslateGroup.T("隐身") : TranslateGroup.T(stealthHideButtonText);

    /// <summary>隐身按钮文字（已隐身时，动作=解除隐身；留空=「显形」）</summary>
    public string GetStealthShowButtonText()
        => string.IsNullOrEmpty(stealthShowButtonText) ? TranslateGroup.T("显形") : TranslateGroup.T(stealthShowButtonText);

    // ==================== 隐身模块 ====================
    public void ToggleStealth()
    {
        if (!canStealth) return;
        if (IsStealthFormLocked) return;   // ✅ 形态锁定（手动锁/阈值锁）：玩家不可操作（阈值强制改写不受此限）
        // ✅ 弹系统（三选一模式，不能通吃）：切换扣弹，无弹拒绝
        if (stealthAmmoMode == 1)
        {
            // ✅ 统一弹药系统：下潜弹/上浮弹
            if (!isStealthed) { if (!TryConsumeAmmo(AmmoKind.StealthBullet)) return; }
            else { if (!TryConsumeAmmo(AmmoKind.RevealBullet)) return; }
        }
        else if (stealthAmmoMode == 2)
        {
            if (!TryConsumeAmmo(AmmoKind.CamoBullet)) return;
        }
        isStealthed = !isStealthed;
        if (stealthToggleCostsAction)
        {
            SetWaitVisual(true);
            state = UnitState.Acted;   // 算独立操作：切换即待机
        }
        // 不算独立操作：state 保持，切换后仍可攻击/移动
        UpdateStealthVisual();   // ✅ 中枢：半透明+指示器
        CheckStealthThresholds();
    }

    public void RevealStealth()
    {
        if (!isStealthed && !stealthFlareRevealed) return;
        isStealthed = false;
        stealthFlareRevealed = false;
        UpdateStealthVisual();
    }

    public bool HasAnyStealthCost()
    {
        return stealthFuelCostPerDay > 0 || stealthHpCostPerDay > 0
            || stealthPrimaryAmmoCostPerDay > 0 || stealthExplodeAmmoCostPerDay > 0
            || stealthFlareAmmoCostPerDay > 0
            || stealthBulletCostPerDay > 0 || revealBulletCostPerDay > 0 || camoBulletCostPerDay > 0;
    }

    public void ApplyStealthDailyCost()   // 由 ConsumeDailyFuel 末尾调用
    {
        if (!isStealthed) return;
        fuel -= stealthFuelCostPerDay;
        if (stealthHpCostPerDay > 0) { health = Math.Max(1, health - stealthHpCostPerDay); }  // 标签刷新走接线阶段
        if (stealthPrimaryAmmoCostPerDay > 0) currentPrimaryAmmo = Math.Max(0, currentPrimaryAmmo - stealthPrimaryAmmoCostPerDay);
        if (stealthExplodeAmmoCostPerDay > 0) currentExplodeAmmo = Math.Max(0, currentExplodeAmmo - stealthExplodeAmmoCostPerDay);
        if (stealthFlareAmmoCostPerDay > 0) currentFlareAmmo = Math.Max(0, currentFlareAmmo - stealthFlareAmmoCostPerDay);
        if (stealthBulletCostPerDay > 0) currentStealthBullets = Math.Max(0, currentStealthBullets - stealthBulletCostPerDay);
        if (revealBulletCostPerDay > 0) currentRevealBullets = Math.Max(0, currentRevealBullets - revealBulletCostPerDay);
        if (camoBulletCostPerDay > 0) currentCamoBullets = Math.Max(0, currentCamoBullets - camoBulletCostPerDay);
        CheckStealthThresholds();
        // 坠毁判定统一在 ConsumeDailyFuel 末尾（防重复销毁）
    }

    private bool forceHideTriggered = false;    // 运行时：强制隐已触发(OneShot用)
    private bool forceRevealTriggered = false;  // 运行时：强制显已触发(OneShot用)

    // ✅ 阈值矩阵统一判定：双方向(强制隐/强制显)×双阈值(高/低)×八资源，0=关，到达即触发
    private void ForceStealthNow()   // 阈值强制隐：被动效果，不耗行动/不扣弹
    {
        if (isStealthed || !canStealth) return;
        isStealthed = true;
        UpdateStealthVisual();
    }

    private static bool ThresholdHit(int value, int high, int low)
        => (high > 0 && value >= high) || (low > 0 && value <= low);

    public void CheckStealthThresholds()
    {
        // 一次性模式：已锁存则永不重判
        if (stealthForceOneShot && stealthForceLatched) return;

        bool hideHit = false, revealHit = false;

        // —— Hp：高阈≥触发 / 低阈≤触发，中间不触发 ——
        if (stealthForceHideHpHigh > 0 && health >= stealthForceHideHpHigh) hideHit = true;
        if (stealthForceHideHpLow > 0 && health <= stealthForceHideHpLow) hideHit = true;
        if (stealthForceRevealHpHigh > 0 && health >= stealthForceRevealHpHigh) revealHit = true;
        if (stealthForceRevealHpLow > 0 && health <= stealthForceRevealHpLow) revealHit = true;
        // —— Fuel：高阈≥触发 / 低阈≤触发，中间不触发 ——
        if (stealthForceHideFuelHigh > 0 && fuel >= stealthForceHideFuelHigh) hideHit = true;
        if (stealthForceHideFuelLow > 0 && fuel <= stealthForceHideFuelLow) hideHit = true;
        if (stealthForceRevealFuelHigh > 0 && fuel >= stealthForceRevealFuelHigh) revealHit = true;
        if (stealthForceRevealFuelLow > 0 && fuel <= stealthForceRevealFuelLow) revealHit = true;
        // —— Primary：高阈≥触发 / 低阈≤触发，中间不触发 ——
        if (stealthForceHidePrimaryHigh > 0 && currentPrimaryAmmo >= stealthForceHidePrimaryHigh) hideHit = true;
        if (stealthForceHidePrimaryLow > 0 && currentPrimaryAmmo <= stealthForceHidePrimaryLow) hideHit = true;
        if (stealthForceRevealPrimaryHigh > 0 && currentPrimaryAmmo >= stealthForceRevealPrimaryHigh) revealHit = true;
        if (stealthForceRevealPrimaryLow > 0 && currentPrimaryAmmo <= stealthForceRevealPrimaryLow) revealHit = true;
        // —— Explode：高阈≥触发 / 低阈≤触发，中间不触发 ——
        if (stealthForceHideExplodeHigh > 0 && currentExplodeAmmo >= stealthForceHideExplodeHigh) hideHit = true;
        if (stealthForceHideExplodeLow > 0 && currentExplodeAmmo <= stealthForceHideExplodeLow) hideHit = true;
        if (stealthForceRevealExplodeHigh > 0 && currentExplodeAmmo >= stealthForceRevealExplodeHigh) revealHit = true;
        if (stealthForceRevealExplodeLow > 0 && currentExplodeAmmo <= stealthForceRevealExplodeLow) revealHit = true;
        // —— Flare：高阈≥触发 / 低阈≤触发，中间不触发 ——
        if (stealthForceHideFlareHigh > 0 && currentFlareAmmo >= stealthForceHideFlareHigh) hideHit = true;
        if (stealthForceHideFlareLow > 0 && currentFlareAmmo <= stealthForceHideFlareLow) hideHit = true;
        if (stealthForceRevealFlareHigh > 0 && currentFlareAmmo >= stealthForceRevealFlareHigh) revealHit = true;
        if (stealthForceRevealFlareLow > 0 && currentFlareAmmo <= stealthForceRevealFlareLow) revealHit = true;
        // —— StealthBullet：高阈≥触发 / 低阈≤触发，中间不触发 ——
        if (stealthForceHideStealthBulletHigh > 0 && currentStealthBullets >= stealthForceHideStealthBulletHigh) hideHit = true;
        if (stealthForceHideStealthBulletLow > 0 && currentStealthBullets <= stealthForceHideStealthBulletLow) hideHit = true;
        if (stealthForceRevealStealthBulletHigh > 0 && currentStealthBullets >= stealthForceRevealStealthBulletHigh) revealHit = true;
        if (stealthForceRevealStealthBulletLow > 0 && currentStealthBullets <= stealthForceRevealStealthBulletLow) revealHit = true;
        // —— RevealBullet：高阈≥触发 / 低阈≤触发，中间不触发 ——
        if (stealthForceHideRevealBulletHigh > 0 && currentRevealBullets >= stealthForceHideRevealBulletHigh) hideHit = true;
        if (stealthForceHideRevealBulletLow > 0 && currentRevealBullets <= stealthForceHideRevealBulletLow) hideHit = true;
        if (stealthForceRevealRevealBulletHigh > 0 && currentRevealBullets >= stealthForceRevealRevealBulletHigh) revealHit = true;
        if (stealthForceRevealRevealBulletLow > 0 && currentRevealBullets <= stealthForceRevealRevealBulletLow) revealHit = true;
        // —— CamoBullet：高阈≥触发 / 低阈≤触发，中间不触发 ——
        if (stealthForceHideCamoBulletHigh > 0 && currentCamoBullets >= stealthForceHideCamoBulletHigh) hideHit = true;
        if (stealthForceHideCamoBulletLow > 0 && currentCamoBullets <= stealthForceHideCamoBulletLow) hideHit = true;
        if (stealthForceRevealCamoBulletHigh > 0 && currentCamoBullets >= stealthForceRevealCamoBulletHigh) revealHit = true;
        if (stealthForceRevealCamoBulletLow > 0 && currentCamoBullets <= stealthForceRevealCamoBulletLow) revealHit = true;

        // ✅ 分方向一次性：该方向已触发过则不再生效（此前这两个开关未接线）
        if (stealthForceHideOneShot && forceHideTriggered) hideHit = false;
        if (stealthForceRevealOneShot && forceRevealTriggered) revealHit = false;

        // ✅ 冲突裁决：同时命中只执行一个（此前两者都执行，后执行的显形静默覆盖）
        if (hideHit && revealHit)
        {
            if (stealthThresholdRevealWins) hideHit = false; else revealHit = false;
        }
        if (hideHit) { ForceStealthNow(); forceHideTriggered = true; }
        if (revealHit) { RevealStealth(); forceRevealTriggered = true; }
        bool anyActive = hideHit || revealHit;

        // ✅ 模式结算
        if (stealthForceOneShot)
        {
            if (anyActive) stealthForceLatched = true;   // 一次性：触发即锁存，永不重判
        }
        else
        {
            stealthThresholdLocked = anyActive;   // 持续模式：写运行时阈值锁，不再覆盖编辑器手动锁
        }
    }

    // 感知：viewerTeam 是否"看见"该单位（视觉层）
    public static bool IsVisibleTo(Infantry unit, string viewerTeam, System.Collections.Generic.List<Grids> allGrids)
    {
        if (unit == null) return false;
        if (unit.team == viewerTeam) return true;
        // ✅ CO 模块14（隐藏/反隐藏）：观察方 CO 拥有反隐（antiStealth）→ 看破一切隐身/隐蔽
        if (CoModuleSystem.GetAntiStealth(viewerTeam)) return true;
        if (unit.stealthFlareRevealed) return true;   // 被照明弹照出的照常
        if (!unit.isStealthed)
        {
            // ✅ 林地/礁岩隐蔽（AW规则）：蹲在森林/礁岩的单位，对无贴身敌人的观察者隐身
            if (unit.terrainConcealment && unit.grid != null
                && (unit.grid.gridType == GridType.FOREST || unit.grid.gridType == GridType.REEF))
            {
                bool adjEnemy = false;
                if (allGrids != null)
                    foreach (var g in allGrids)
                    {
                        if (g == null || g == unit.grid) continue;
                        int dist = Math.Abs(g.GridIndex.X - unit.grid.GridIndex.X) + Math.Abs(g.GridIndex.Y - unit.grid.GridIndex.Y);
                        if (dist != 1) continue;
                        foreach (var u in g.infantries)
                            if (u != null && u.team == viewerTeam && (u.strongSight || u.strongDetect)) { adjEnemy = true; break; }
                        if (adjEnemy) break;
                    }
                if (!adjEnemy) return false;
            }
            return true;
        }
        if (unit.stealthVisibleOnTile && unit.grid != null)
        {
            foreach (var u in unit.grid.infantries)
                if (u != null && u != unit && u.team == viewerTeam) return true;   // 同格单位看得见
            var fac = unit.grid.city;   // ✅ 脚下建筑：所属方看得见（AW3规则，接线完成）
            if (fac != null && fac.facilityTeam == viewerTeam) return true;
        }
        if (unit.stealthVisibleWhenAdjacent && allGrids != null && unit.grid != null)
            foreach (var g in allGrids)
            {
                if (g == null || g == unit.grid) continue;
                int dist = Math.Abs(g.GridIndex.X - unit.grid.GridIndex.X) + Math.Abs(g.GridIndex.Y - unit.grid.GridIndex.Y);
                if (dist < unit.stealthVisibleMinRange || dist > unit.stealthVisibleMaxRange) continue;
                foreach (var u in g.infantries)
                    if (u != null && u.team == viewerTeam && (u.strongDetect || u.strongSight)) return true;
            }
        if (unit.stealthRevealOnFlare && unit.IsFlareLit()) return true;   // ✅ 照明弹照出（实时查询，离开亮格自动恢复）
        return false;
    }

    // 攻击准入：attacker 能否打 target（隐身白名单）
    public static bool CanTargetStealth(Infantry attacker, Infantry target)
    {
        if (target == null || !target.isStealthed) return true;
        // ✅ CO 模块14（隐藏/反隐藏）：攻击方 CO 拥有反隐（antiStealth）→ 可直接攻击隐身单位
        if (attacker != null && CoModuleSystem.GetAntiStealth(attacker.team)) return true;
        if (target.stealthRevealOnFlare && target.IsFlareLit()) return true;   // ✅ 照明弹照出：可攻击
        if (target.stealthAttackableBy == null || target.stealthAttackableBy.Length == 0) return false;
        return System.Array.IndexOf(target.stealthAttackableBy, attacker.UnitTypeId) >= 0;
    }

    // 现形三触发器之"受伤"（在战斗伤害点调用）
    // ✅ 广义受伤现形：监听血量下降（覆盖自爆/兵器/激光/死光/地格伤害/修理扣血等所有来源）
    private int damageRevealLastHp = -1;
    public void SyncDamageRevealTracker() => damageRevealLastHp = health;

    public static void ApplyStealthRevealOnDamage(Infantry damaged)
    {
        if (damaged == null || !damaged.stealthRevealOnDamage) return;
        // 阈值模式：血量已在此调用前扣减，≤阈值才现形；非阈值模式受任何伤即现形
        if (!damaged.stealthRevealOnDamageUseThreshold || damaged.health <= damaged.stealthRevealOnDamageHpThreshold)
            damaged.RevealStealth();
    }

    // ==================== 舱内隐身（遥控搭载模块 · 条款5同步）====================
    public virtual Infantry GetFirstStealthCapableLoadedUnit()
        => transportedUnits?.FirstOrDefault(u => u != null && IsInstanceValid(u) && u.canStealth);

    public virtual bool CanPerformLoadedStealth()
    {
        if (!canOperateLoadedUnits) return false;
        if (IsActionBlacklisted("Stealth")) return false;
        if (GetRemainingLoadedOperations() <= 0) return false;
        var loaded = GetFirstStealthCapableLoadedUnit();
        return loaded != null && !loaded.isAttacked;
    }

    public virtual void PerformLoadedStealth()
    {
        if (!CanPerformLoadedStealth()) return;
        var loaded = GetFirstStealthCapableLoadedUnit();
        loaded.ToggleStealth();
        if (stealthSyncsToTransport)
        {
            isStealthed = loaded.isStealthed;
            Modulate = loaded.Modulate;
        }
        ConsumeLoadedOperation();
    }
}
