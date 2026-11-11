// GameManager.cs - 添加完整的单位统计系统
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// GameManager · Funds（由 GameManager.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class GameManager
{

    // 资金系统
    [ExportGroup("资金系统配置")]
    [Export] public int p1Funds = 0;
    [Export] public int p2Funds = 0;
    [Export] public int p1FundsMax = 999999;
    [Export] public int p2FundsMax = 999999;
    
    /// <summary>获取势力资金（支持任意数量势力）</summary>
    public int GetTeamFunds(string team)
    {
        if (team == "Player1") return p1Funds;
        if (team == "Player2") return p2Funds;
        return _teamFunds.GetValueOrDefault(team, 0);
    }
    
    /// <summary>设置势力资金（支持任意数量势力）</summary>
    public void SetTeamFunds(string team, int value)
    {
        int max = GetTeamFundsMax(team);
        int clamped = Mathf.Clamp(value, 0, max);
        if (team == "Player1") p1Funds = clamped;
        else if (team == "Player2") p2Funds = clamped;
        else _teamFunds[team] = clamped;
        UpdateUnitLists();
    }
    
    /// <summary>设置势力资金，不应用常规资金上限（由明确允许超上限的模块调用）</summary>
    public void SetTeamFundsUncapped(string team, int value)
    {
        int safeValue = Math.Max(0, value);
        if (team == "Player1") p1Funds = safeValue;
        else if (team == "Player2") p2Funds = safeValue;
        else _teamFunds[team] = safeValue;
        UpdateUnitLists();
    }

    /// <summary>设置势力资金上限（支持任意数量势力）</summary>
    public void SetTeamFundsMax(string team, int value)
    {
        if (team == "Player1") p1FundsMax = value;
        else if (team == "Player2") p2FundsMax = value;
        else _teamFundsMax[team] = value;
    }

    /// <summary>获取势力资金上限</summary>
    public int GetTeamFundsMax(string team)
    {
        if (team == "Player1") return p1FundsMax;
        if (team == "Player2") return p2FundsMax;
        return _teamFundsMax.GetValueOrDefault(team, 999999);
    }

    // ✅ 资金系统：给指定势力增加设施资金收入
    // ✅ 修改：P-1公共设施给所有势力提供收入
    private void AddFundsForTeam(string team)
    {
        if (gridManager?.grids == null) return;
        int income = 0;
        foreach (var grid in gridManager.grids)
        {
            if (grid?.city == null) continue;

            // ✅ P-1公共设施：给所有势力提供收入
            if (grid.city.facilityTeam == TeamHelper.Player || grid.city.isPublicFacility)
            {
                if (grid.city is City c)
                {
                    income += c.fundsPerTurn;
                }
            }
            else if (grid.city.facilityTeam == team)
            {
                if (grid.city is City c)
                {
                    income += c.fundsPerTurn;
                }
            }
        }

        // ✅ 支持任意势力
        // ✅ CO 模块系统·模块11（经济模块）：收入百分比增减
        income = Mathf.Max(0, Mathf.RoundToInt(income * (100f + CoModuleSystem.GetIncomeModifierPercent(team)) / 100f));
        int maxFunds = GetTeamFundsMax(team);
        int current = GetTeamFunds(team);
        SetTeamFunds(team, current + income);

        UpdateUnitLists();
    }
}
