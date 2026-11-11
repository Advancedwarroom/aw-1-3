using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · PackFrenzy（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{
    [ExportGroup("群体狂热模块")]
    [Export] public bool packFrenzyEnabled = false;  // 开关（机械狗非生物不开启）
    [Export] public int packRangeMin = 0;            // 检查范围 x
    [Export] public int packRangeMax = 1;            // 检查范围 y（临格=特殊值）
    [Export] public bool packUseWhitelist = false;   // 是否启用检查单位白名单
    [Export] public Godot.Collections.Array<string> packWhitelist = new(); // 仅注册表单位种类（不看自定义Export）
    [Export] public bool packSameSpeciesOnly = false; // 特殊态：仅同种类（同类单位）
    [Export] public bool packIncludeFriendly = true;
    [Export] public bool packIncludeEnemy = true;
    [Export] public bool packIncludeAllied = true;
    [Export] public int packMaxStacks = 3;           // 最大加成次数（看到7只也只享3次）
    [Export] public int packBonusPercent = 25;       // 加/减成百分比（负=减成）
    [Export] public bool packCanExceedCap = false;   // 是否超越上限
    [Export] public int packNewCap = 0;              // 超越上限的新上限（0=无，即不生效）
    [Export] public bool packBoostAmmoMax = false;   // 效果对象：主武器弹药最大值
    [Export] public bool packBoostFuelMax = false;   // 效果对象：燃油最大值
    [Export] public bool packBoostHpMax = false;     // 效果对象：血量最大值（不改变当前值）
    [Export] public bool packBoostAttack = false;    // 效果对象：真实攻击力（攻防表额外加成）
    [Export] public bool packBoostDefense = false;   // 效果对象：固定防御力
    [Export] public int packLingerTurns = 0;         // 不符合标准后持续N回合（再符合=覆盖不叠加）

    // ========== 群体狂热模块 ==========
    private int packFrenzyStacks = 0;        // 当前生效加成次数（已按MaxStacks封顶）
    private int packLingerRemaining = 0;     // 脱离标准后的剩余持续回合
    private int packBakAmmo = -1, packBakFuel = -1, packBakHp = -1;   // 原始上限备份（直写字段法，零新读取点）

    public virtual bool PackFrenzyActive() => packFrenzyEnabled && (packFrenzyStacks > 0 || packLingerRemaining > 0);

    /// <summary>统计 x-y 环内合标单位数（早停于 MaxStacks）。白名单只看注册表单位种类；同类模式不看任何自定义Export。</summary>
    public virtual int CountPackFrenzyQualifiers()
    {
        if (!packFrenzyEnabled || grid == null) return 0;
        var gm = gameManager;
        if (gm?.gridManager?.map == null || gm?.unitManager == null) return 0;
        var origin = grid.GridIndex;
        int count = 0;
        for (int dx = -packRangeMax; dx <= packRangeMax; dx++)
            for (int dy = -packRangeMax; dy <= packRangeMax; dy++)
            {
                int dist = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (dist < packRangeMin || dist > packRangeMax) continue;
                var pos = new Vector2I(origin.X + dx, origin.Y + dy);
                if (!gm.unitManager.IsValidGrid(pos)) continue;
                foreach (var u in gm.gridManager.map[pos.X, pos.Y].infantries)
                {
                    if (u == null || !IsInstanceValid(u) || u == this) continue;
                    if (!TeamHelper.MatchesTeamRelation(team, u.team,
                        packIncludeFriendly, packIncludeEnemy, packIncludeAllied)) continue;
                    if (packSameSpeciesOnly)
                    { if (u.unitTypeId != unitTypeId) continue; }           // 同类特殊态
                    else if (packUseWhitelist && !packWhitelist.Contains(u.unitTypeId)) continue;
                    count++;
                    if (count >= packMaxStacks) return count;               // 早停：只看前MaxStacks个
                }
            }
        return count;
    }

    /// <summary>回合开始刷新：符合→覆盖式生效；不符合→消耗持续回合，耗尽即还原。</summary>
    public virtual void OnTurnStartPackFrenzy()
    {
        if (!packFrenzyEnabled) return;
        int q = CountPackFrenzyQualifiers();
        if (q > 0)
        {
            packFrenzyStacks = Mathf.Min(q, packMaxStacks);
            packLingerRemaining = packLingerTurns;      // 规格：再次符合直接覆盖，不叠加
            ApplyPackFrenzyBoosts(true);
        }
        else if (packLingerRemaining > 0)
        {
            packLingerRemaining--;
            if (packLingerRemaining <= 0) { packFrenzyStacks = 0; ApplyPackFrenzyBoosts(false); }
        }
        else if (packFrenzyStacks > 0)
        {
            packFrenzyStacks = 0;
            ApplyPackFrenzyBoosts(false);
        }
    }

    /// <summary>直写上限字段（备份-恢复法）：不新增任何读取点，弹药/油/血上限全部现成消费。</summary>
    private void ApplyPackFrenzyBoosts(bool on)
    {
        int pct = on ? Mathf.Max(-99, packBonusPercent) : 0;
        int Boost(int baseVal) => baseVal * (100 + pct) / 100;
        if (packBoostAmmoMax)
        {
            if (on) { if (packBakAmmo < 0) packBakAmmo = maxPrimaryAmmo;
                int v = Boost(packBakAmmo);
                maxPrimaryAmmo = (packCanExceedCap && packNewCap > 0) ? Mathf.Min(v, packNewCap) : v; }
            else if (packBakAmmo >= 0) { maxPrimaryAmmo = packBakAmmo; packBakAmmo = -1; }
        }
        if (packBoostFuelMax)
        {
            if (on) { if (packBakFuel < 0) packBakFuel = maxFuel;
                int v = Boost(packBakFuel);
                maxFuel = (packCanExceedCap && packNewCap > 0) ? Mathf.Min(v, packNewCap) : v; }
            else if (packBakFuel >= 0) { maxFuel = packBakFuel; packBakFuel = -1; }
        }
        if (packBoostHpMax)
        {
            if (on) { if (packBakHp < 0) packBakHp = maxHealth;
                int v = Boost(packBakHp);
                maxHealth = (packCanExceedCap && packNewCap > 0) ? Mathf.Min(v, packNewCap) : v; }
            else if (packBakHp >= 0)
            {
                maxHealth = packBakHp; packBakHp = -1;
                if (health > maxHealth) { health = maxHealth; UpdateHpLabel(); }   // 上限回收时钳制当前值
            }
        }
    }
}
