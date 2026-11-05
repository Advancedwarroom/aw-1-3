// GridManager.ScreenRandom.cs - 随机屏映射（issue#1：多屏子关卡 semi-random mapping）
// 开局按种子随机生成跨屏传送链，只触发一次随后稳定；全局事件/单位能力可调用 RerollRandomScreenLinks 重洗。
// 复用现有 TeleportLink 体系（AddTeleportLink 自动补反向链），不新增数据结构。
using Godot;
using System;
using System.Collections.Generic;

public partial class GridManager
{
    [ExportGroup("随机屏映射（issue#1）")]
    [Export] public bool randomScreenLinks = false;    // 开局随机生成跨屏传送链
    [Export] public int randomLinkSeed = 0;            // 0=每场真随机；非0=固定种子（可复现的半随机）
    [Export] public int randomLinksPerPair = 3;        // 每对相邻屏生成几条链
    [Export] public bool randomLinkInnerOnly = true;   // 只在内环格生成（不靠地图外缘）

    private int _appliedRandomSeed = -1;

    /// <summary>开局调用（地图加载完成后）：随机映射只触发一次，随后整场稳定</summary>
    public void ApplyRandomScreenLinksIfEnabled()
    {
        if (!randomScreenLinks || GetScreenCount() <= 1) return;
        int seed = randomLinkSeed != 0 ? randomLinkSeed : Environment.TickCount;
        RerollRandomScreenLinks(seed);
    }

    /// <summary>重洗随机屏映射（issue：仅开局/全局事件触发；调用即全场重新半随机）</summary>
    public void RerollRandomScreenLinks(int seed)
    {
        if (GetScreenCount() <= 1 || map == null) return;
        ClearTeleportLinks();
        var rng = new Random(seed);
        for (int s = 0; s + 1 < GetScreenCount(); s++)
        {
            var a = CollectCandidateCells(s, rng);
            var b = CollectCandidateCells(s + 1, rng);
            Shuffle(a, rng);
            Shuffle(b, rng);
            int n = Mathf.Min(randomLinksPerPair, Mathf.Min(a.Count, b.Count));
            for (int i = 0; i < n; i++)
                AddTeleportLink(a[i], b[i], false);   // 自动补反向链（isOneWay=false）
        }
        _appliedRandomSeed = seed;
    }

    /// <summary>当前生效的种子（Info Bar 可显示；-1=未应用）</summary>
    public int GetAppliedRandomSeed() => _appliedRandomSeed;

    private List<Vector2I> CollectCandidateCells(int screen, Random rng)
    {
        var list = new List<Vector2I>();
        int w = map.GetLength(0), h = map.GetLength(1);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (map[x, y] == null) continue;
                if (GetScreenId(new Vector2I(x, y)) != screen) continue;
                if (randomLinkInnerOnly && (x == 0 || y == 0 || x == w - 1 || y == h - 1)) continue;
                list.Add(new Vector2I(x, y));
            }
        }
        return list;
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
