using System;
using System.Collections.Generic;
using System.Linq;

public static class TeamHelper
{
    // ========== 势力标识 ==========
    public const string Player = "Player";      // P-1: 玩家（无数字），P1和P2都可操作
    public const string Player0 = "Player0";    // P0: 中立，无反应，谁都打，设施无加成
    public const string Player1 = "Player1";    // P1: 正常势力
    public const string Player2 = "Player2";    // P2: 正常势力

    // 所有势力
    public static readonly string[] AllTeams = { Player, Player0, Player1, Player2 };
    // 可操作势力（用于统计）
    public static readonly string[] ActiveTeams = { Player1, Player2 };
    // 所有需要统计的势力（包括P0和P-1）
    public static readonly string[] AllCountableTeams = { Player, Player0, Player1, Player2 };

    // ========== 势力名称显示 ==========
    public static string GetTeamDisplayName(string team)
    {
        return team switch
        {
            Player => "Player(-1)",
            Player0 => "Player0",
            Player1 => "Player1",
            Player2 => "Player2",
            _ => team
        };
    }

    public static string GetTeamShortName(string team)
    {
        return team switch
        {
            Player => "P-1",
            Player0 => "P0",
            Player1 => "P1",
            Player2 => "P2",
            _ => team
        };
    }

    // ========== 操作权限 ==========
    // 当前phase是否允许操作该team的单位
    public static bool CanOperateTeam(int turnPhase, string team)
    {
        return team switch
        {
            Player0 => false,              // P0无反应，不可操作
            Player1 => turnPhase == 1,     // P1在phase1可操作
            Player2 => turnPhase == 2,     // P2在phase2可操作
            Player => turnPhase == 1 || turnPhase == 2, // P-1在P1和P2都可操作
            _ => false
        };
    }

    // 是否是当前phase的操作方
    public static bool IsCurrentPhasePlayer(int turnPhase, string team)
    {
        return turnPhase switch
        {
            1 => team == Player1,
            2 => team == Player2,
            3 => team == Player0, // P0阶段，但P0无操作
            _ => false
        };
    }

    // 获取当前phase对应的操作势力名称
    public static string GetPhaseTeamName(int turnPhase)
    {
        return turnPhase switch
        {
            1 => Player1,
            2 => Player2,
            3 => Player0,
            _ => ""
        };
    }

    // ========== 攻击目标 ==========
    // 攻击者能否攻击defender（核心规则：P-1/P0除了自己，谁都可以打；P1/P2只能打不同势力）
    public static bool CanAttackTarget(string attackerTeam, string defenderTeam)
    {
        // P-1 和 P0：不判断势力，任何目标都可攻击（实体层面不能打自己，由调用方保证）
        if (attackerTeam == Player || attackerTeam == Player0)
            return true;
        // P1 和 P2：不能打同势力的
        if (attackerTeam == defenderTeam) return false;
        return true;
    }

    // 判断defender对于attacker来说是否是有效攻击目标（用于Grid的HasEnemy筛选）
    public static bool IsEnemyForAttacker(string attackerTeam, string defenderTeam)
    {
        // P-1 和 P0：不判断势力，任何目标都是可攻击目标（自己由调用方排除）
        if (attackerTeam == Player || attackerTeam == Player0)
            return true;
        // P1 和 P2：只能攻击不同势力的
        return attackerTeam != defenderTeam;
    }

    // ========== 反击逻辑 ==========
    // defender被攻击时能否反击
    public static bool CanCounterAttack(string defenderTeam)
    {
        // P0不反击（中立无反应），其他势力正常反击
        return defenderTeam != Player0;
    }

    // ========== 设施补给 ==========
    // facilityTeam的设施能否给unitTeam回血
    public static bool CanFacilitySupply(string facilityTeam, string unitTeam)
    {
        // P0设施对任何势力无加成（包括P0自己）
        if (facilityTeam == Player0) return false;
        // P-1设施给所有势力回血
        if (facilityTeam == Player) return true;
        // P1/P2设施只给同势力的回血
        return facilityTeam == unitTeam;
    }

    // ========== 占领逻辑 ==========
    // P0设施可以被占领（CanBeCapturedBy已在Facility中处理）
    // P-1设施也可以被占领

    // ========== 设施视野 ==========
    // facilityTeam的设施是否提供视野
    public static bool DoesFacilityProvideVision(string facilityTeam)
    {
        // P0不提供视野（中立）
        if (facilityTeam == Player0) return false;
        // P-1提供视野
        if (facilityTeam == Player) return true;
        // P1/P2正常提供
        return true;
    }

    // ========== 兵器AI ==========
    // P0兵器是否应执行AI
    public static bool ShouldWeaponUseAI(string weaponTeam)
    {
        return weaponTeam == Player0;
    }

    // P-1兵器在P0阶段是否由AI执行（phase 3时P-1兵器自动攻击）
    public static bool ShouldPlayerWeaponUseAI(int turnPhase, string weaponTeam)
    {
        return weaponTeam == Player && turnPhase == 3;
    }

    // P-1兵器是否可被P1/P2手动操作（P1和P2阶段都可以）
    public static bool CanPlayerWeaponBeOperated(int turnPhase, string weaponTeam)
    {
        return weaponTeam == Player && (turnPhase == 1 || turnPhase == 2);
    }

    // ========== 视觉颜色 ==========
    public static Godot.Color GetTeamColor(string team)
    {
        return team switch
        {
            Player => new Godot.Color(0.7f, 0.4f, 0.9f), // P-1 浅紫色
            Player0 => new Godot.Color(0.7f, 0.7f, 0.7f), // P0 灰白色
            Player1 => new Godot.Color(1f, 0.3f, 0.3f),     // P1 红色
            Player2 => new Godot.Color(0.3f, 0.5f, 1f),     // P2 蓝色
            _ => Godot.Colors.White
        };
    }

    public static bool HasNumberLabel(string team)
    {
        return false; // 用户要求去掉数字显示
    }
}
