using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Truncation（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

    [ExportGroup("截断遭遇")]
    [Export] public bool overlongReflexArc = false;          // 反射弧过长：开=终点压隐形的冤枉费保留(不修)，关=正常退费
    // —— 被截断方（移动方）——
    [Export] public bool truncCounterEnabled = false;        // 被截断时自动反击（总开关=是否攻击；关=组一全失效）
    [Export] public int truncCounterMode = 1;                // 1主动出击(我单向打对方不还手) 2两败俱伤(互吃反击) 3先发制人(对方伏击我,我只能反击)
    [Export] public bool truncShowAttackButton = true;       // 是否显示攻击键（显示则点键发起/点边缘不攻击仅截断）
    [Export] public bool truncForcedStop = true;             // 强制截断：开=截死原作式；关=新格重绑+显示剩余移动力+二次移动
    [Export] public int truncStrikeWeight = 0;               // 截断执行权重 0~9999（矛盾仲裁）
    // —— 截断方（阻挡方=伏击手）——
    [Export] public bool truncAmbusherEnabled = false;       // 伏击手：截断他人时主动出击（本组是强制遭遇的唯一来源）
    [Export] public int truncAmbusherMode = 1;               // 1主动出击 2两败俱伤 3反将被动(对方打我,我只能反击)
    [Export] public int truncAmbusherWeight = 0;             // 伏击手权重（平权=被截断方先执行）
    [Export] public bool truncAllowMultiStrike = false;      // 允许多次截断出击：开=双方各执行；关=只执行权重高者              // ✅ 林地/礁岩隐蔽（AW规则）：无贴身敌人时藏于森林/礁岩不可见

    // ==================== 截断判定（唯一口径） ====================
    // ✅ 只有“敌方”才能截断：同队/中立公共(P-1)/运输中/阵亡/已失效的单位一律不截断，
    //    友军格可以穿过（不能停留），绝不会被友军静默截断或触发遭遇战
    public static bool IsTruncationBlocker(Infantry mover, Infantry other)
    {
        if (mover == null || other == null || other == mover) return false;
        if (mover.canPassThroughEnemyUnits) return false;   // ✅ 可穿过敌方单位：谁也别想截断我（预览/移动/触屏/AI 八处调用此唯一口径）
        if (!IsInstanceValid(other) || other.IsQueuedForDeletion() || other.health <= 0 || other.isTransported) return false;
        if (other.team == mover.team) return false;
        if (other.team == TeamHelper.Player) return false;   // P-1 公共单位不截断
        return true;
    }

    // ==================== 截断遭遇裁决引擎 ====================
    // 双方在截断点接触后的伤害仲裁：模式=伤害形状，权重=执行顺序，多次开关=是否全执行
    // ✅ AI 遭遇直觉：现场算账——打不动不打；能秒必打；换血不亏才打
    public static bool AIDecideEncounterStrike(Infantry self, Infantry foe)
    {
        if (self == null || foe == null) return false;
        int myDmg = self.GetMatrixDamage(foe, false);
        int foeDmg = foe.GetMatrixDamage(self, false);
        if (myDmg <= 0) return false;                    // 白挨打
        if (foe.health - myDmg <= 0) return true;          // 能击杀
        return myDmg >= foeDmg;                            // 换血不亏
    }

    // 入口：判定由谁决策（人类伏击手→回合中回合弹菜单冻结全局；AI→自动按条令）
    public static void ResolveTruncationEncounter(Infantry mover, Infantry blocker)
    {
        if (mover == null || blocker == null) return;
        if (!IsTruncationBlocker(mover, blocker)) return;   // ✅ 友军永不触发遭遇战
        if (!mover.truncCounterEnabled && !blocker.truncAmbusherEnabled) return;
        var gm = mover.GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var aiMgr = mover.GetTree()?.GetFirstNodeInGroup("ai_manager") as AI_Manager;
        bool blockerIsAI = aiMgr != null && aiMgr.IsAITeam(blocker.team);
        if (!blockerIsAI && blocker.truncAmbusherEnabled)
        {
            gm?.RequestEncounterDecision(mover, blocker);   // ✅ 回合中回合：冻结+人类确认菜单
            return;
        }
        // ✅ AI 遭遇策略：任一方为AI时，该方的"是否攻击"由现场算账决定（而非静态条令）
        bool moverIsAI = aiMgr != null && aiMgr.IsAITeam(mover.team);
        bool mConsent = mover.truncCounterEnabled && (!moverIsAI || AIDecideEncounterStrike(mover, blocker));
        bool bConsent = blocker.truncAmbusherEnabled && (!blockerIsAI || AIDecideEncounterStrike(blocker, mover));
        ExecuteTruncationStrikes(mover, blocker, mConsent, bConsent);
    }

    public static void ExecuteTruncationStrikes(Infantry mover, Infantry blocker, bool moverConsents, bool blockerConsents)
    {
        if (mover == null || blocker == null) return;
        if (!IsTruncationBlocker(mover, blocker)) return;   // ✅ 双保险：人类确认菜单回调也不会打友军
        bool mW = moverConsents && mover.truncCounterEnabled;
        bool bW = blockerConsents && blocker.truncAmbusherEnabled;
        if (!mW && !bW) return;

        var strikes = new System.Collections.Generic.List<(Infantry atk, Infantry def, bool counter)>();
        // 移动方模式：1=主动出击(我单向打) 2=两败俱伤(互吃反击) 3=先发制人(对方先动手,我反击)
        if (mW)
        {
            if (mover.truncCounterMode == 1) strikes.Add((mover, blocker, false));
            else if (mover.truncCounterMode == 2) strikes.Add((mover, blocker, true));
            else if (mover.truncCounterMode == 3) strikes.Add((blocker, mover, true));
        }
        // 阻挡方（伏击手）模式：1=主动出击 2=两败俱伤 3=反将被动(对方打我,我只反击)
        if (bW)
        {
            if (blocker.truncAmbusherMode == 1) strikes.Add((blocker, mover, false));
            else if (blocker.truncAmbusherMode == 2) strikes.Add((blocker, mover, true));
            else if (blocker.truncAmbusherMode == 3) strikes.Add((mover, blocker, true));
        }
        if (strikes.Count == 0) return;

        var exec = strikes;
        if (strikes.Count > 1 && !mover.truncAllowMultiStrike && !blocker.truncAllowMultiStrike)
        {
            var first = strikes[0];   // 默认移动方
            if (blocker.truncAmbusherWeight >= mover.truncStrikeWeight) first = strikes[strikes.Count - 1];  // 平权或更高=被截断方(伏击手)优先
            if (mover.truncStrikeWeight > blocker.truncAmbusherWeight) first = strikes[0];                      // 移动方权重更高
            exec = new System.Collections.Generic.List<(Infantry, Infantry, bool)> { first };
        }
        foreach (var s in exec)
        {
            // ✅ 死亡消除（跨发）：已死者取消其未执行的后续拳（同发内的反击由Attack内生存门统一裁决）
            if (s.atk == null || s.def == null || s.atk.health <= 0 || s.def.health <= 0) continue;
            s.atk.Attack(s.def, allowCounter: s.counter);   // 唯一战斗结算点：隐身门/反击门/阈值矩阵全自动
        }
    }
}
