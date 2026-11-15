using Godot;
using System.Collections.Generic;

// GameManager.EventScopes.cs —— 事件引擎三 scope 栈数登记（A9 v2，开创人2026-11-15）
// 全军/全局作用域的叠层计数挂在场景级 GameManager：新游戏=新实例=自动归零，
// 零静态泄漏（自对弈连打 60 阶段不串账）。
public partial class GameManager
{
    public Dictionary<string, int> armyEventStacks = new();
    public Dictionary<string, int> globalEventStacks = new();

    public int GetArmyEventStack(string team, string eventKey)
        => armyEventStacks.TryGetValue($"{team}|{eventKey}", out var n) ? n : 0;
    public int GetGlobalEventStack(string eventKey)
        => globalEventStacks.TryGetValue(eventKey, out var n) ? n : 0;
    public void AddArmyEventStack(string team, string eventKey, int delta)
    {
        var k = $"{team}|{eventKey}";
        armyEventStacks[k] = GetArmyEventStack(team, eventKey) + delta;
        if (armyEventStacks[k] <= 0) armyEventStacks.Remove(k);
    }
    public void AddGlobalEventStack(string eventKey, int delta)
    {
        globalEventStacks[eventKey] = GetGlobalEventStack(eventKey) + delta;
        if (globalEventStacks[eventKey] <= 0) globalEventStacks.Remove(eventKey);
    }
}
