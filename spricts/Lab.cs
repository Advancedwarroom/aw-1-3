// Lab.cs - 秘密实验室（Research Laboratory）
// 原作AW2：占领解锁新型坦克(Neotank)科技。
// 本作规格（实验室模块）：
//   1. Export 是否开启
//   2. Export 占领n个该种建筑后新增/减少的全局白名单（1个后、2个后、3个后…无数种组合！）
//   3. 占领仅限这个建筑新增/减少的单位白名单
// 配合 Facility.isUpdatable / Infantry.isUpdatable（是否更新Export）：
//   - isUpdatable=true  → 生产受全局白名单限制（单位必须在白名单中）
//   - isUpdatable=false → 不受限制
//   - 编辑模式放置、单位使用均不受影响——仅限制生产
using Godot;
using System.Linq;

public partial class Lab : Facility
{
    [ExportGroup("实验室模块")]
    /// <summary>1. 是否开启实验室模块</summary>
    [Export] public bool labEnabled = true;

    // ========== 2. 分层占领解锁：占领n个后新增/减少的全局白名单 ==========
    [ExportGroup("实验室 - 分层占领解锁（占领n个后）")]
    /// <summary>占领数量分层（如 [1, 2, 3] 表示占领1个/2个/3个后各触发一层）</summary>
    [Export] public Godot.Collections.Array<int> tieredCaptureCounts = new();
    /// <summary>各层新增的单位（逗号分隔，与tieredCaptureCounts平行。如 ["Neotank,Megatank", "Stealth", "BlackBomb"]）</summary>
    [Export] public Godot.Collections.Array<string> tieredAddUnitStrings = new();
    /// <summary>各层减少（移除）的单位（逗号分隔，与tieredCaptureCounts平行。如 ["", "OldTank", ""]）</summary>
    [Export] public Godot.Collections.Array<string> tieredRemoveUnitStrings = new();

    // ========== 4. 被夺走时重新锁上（开创人规矩：攻防拉锯） ==========
    /// <summary>被敌方夺走时：收回本Lab解锁的单位、恢复本Lab移除的单位</summary>
    [Export] public bool relLockOnLost = false;

    // ========== 3. 占领仅限这个建筑新增/减少的单位白名单 ==========
    [ExportGroup("实验室 - 本建筑专属白名单")]
    /// <summary>占领本Lab后新增（解锁）的单位</summary>
    [Export] public Godot.Collections.Array<string> labUnitWhitelist = new();
    /// <summary>占领本Lab后减少（移除）的单位</summary>
    [Export] public Godot.Collections.Array<string> labUnitBlacklist = new();

    // ========== 运行时 ==========
    /// <summary>各势力当前持有Lab数（动态：夺回即减，层数按实时持有数判定——开创人2026-11-03修正）</summary>
    public static readonly System.Collections.Generic.Dictionary<string, int> TeamLabCounts = new();

    static int LabCountOf(string t) => TeamLabCounts.TryGetValue(t ?? "", out var c) ? c : 0;

    // 本Lab做过的解锁/移除记录（被夺走时按此反转）
    private readonly System.Collections.Generic.List<string> grantedUnits = new();
    private readonly System.Collections.Generic.List<string> removedUnits = new();

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0Lab", "labteam1", "labteam2");

    public override void _Ready()
    {
        base._Ready();
    }

    /// <summary>占领后：递增全局计数 + 应用分层规则 + 应用本Lab专属白名单</summary>
    public override void OnCaptureCompleted(Infantry unit)
    {
        var prevTeam = facilityTeam;   // base调用前：原主（空/中立=无主，不触发夺回）
        base.OnCaptureCompleted(unit);
        if (!labEnabled) return;

        // ✅ 4. 被夺走：开启时反转本Lab做过的全部解锁/移除（中立化不触发）
        if (relLockOnLost && !string.IsNullOrEmpty(prevTeam) && prevTeam != TeamHelper.Player0 && prevTeam != facilityTeam)
        {
            if (grantedUnits.Count > 0)
            {
                UnitProductionWhitelist.LockUnits(grantedUnits);
                GD.Print($"[Lab] 被{facilityTeam}夺走：收回 {string.Join(", ", grantedUnits)}");
            }
            if (removedUnits.Count > 0)
            {
                UnitProductionWhitelist.UnlockUnits(removedUnits);
                GD.Print($"[Lab] 被{facilityTeam}夺走：恢复 {string.Join(", ", removedUnits)}");
            }
        }

        // ✅ 动态层数：原主减一、新主加一，按新主当前持有数判定层级（夺回掉层、再占回重新爬层）
        if (!string.IsNullOrEmpty(prevTeam) && prevTeam != TeamHelper.Player0)
            TeamLabCounts[prevTeam] = System.Math.Max(0, LabCountOf(prevTeam) - 1);
        TeamLabCounts[facilityTeam] = LabCountOf(facilityTeam) + 1;

        // ✅ 2. 应用分层占领规则（当前持有数对应的层）
        ApplyTieredRules(LabCountOf(facilityTeam));

        // ✅ 3. 应用本Lab专属白名单（新增/减少）
        ApplyPerLabWhitelist();
    }

    /// <summary>✅ 分层规则：占领 capturedCount 个后，新增/减少对应全局白名单</summary>
    private void ApplyTieredRules(int capturedCount)
    {
        if (tieredCaptureCounts == null) return;
        for (int i = 0; i < tieredCaptureCounts.Count; i++)
        {
            if (tieredCaptureCounts[i] != capturedCount) continue;

            // 新增
            if (i < tieredAddUnitStrings.Count)
            {
                var add = ParseUnitList(tieredAddUnitStrings[i]);
                if (add.Count > 0)
                {
                    UnitProductionWhitelist.UnlockUnits(add);
                    grantedUnits.AddRange(add);   // 记账：夺回时收回
                    GD.Print($"[Lab] 占领{capturedCount}个后 解锁: {string.Join(", ", add)}");
                }
            }
            // 减少
            if (i < tieredRemoveUnitStrings.Count)
            {
                var remove = ParseUnitList(tieredRemoveUnitStrings[i]);
                if (remove.Count > 0)
                {
                    UnitProductionWhitelist.LockUnits(remove);
                    removedUnits.AddRange(remove);   // 记账：夺回时恢复
                    GD.Print($"[Lab] 占领{capturedCount}个后 移除: {string.Join(", ", remove)}");
                }
            }
        }
    }

    /// <summary>✅ 本Lab专属白名单：占领后新增/减少</summary>
    private void ApplyPerLabWhitelist()
    {
        if (labUnitWhitelist != null && labUnitWhitelist.Count > 0)
        {
            UnitProductionWhitelist.UnlockUnits(labUnitWhitelist.ToList());
            grantedUnits.AddRange(labUnitWhitelist);   // 记账
            GD.Print($"[Lab] 本Lab解锁: {string.Join(", ", labUnitWhitelist)}");
        }
        if (labUnitBlacklist != null && labUnitBlacklist.Count > 0)
        {
            UnitProductionWhitelist.LockUnits(labUnitBlacklist.ToList());
            removedUnits.AddRange(labUnitBlacklist);   // 记账
            GD.Print($"[Lab] 本Lab移除: {string.Join(", ", labUnitBlacklist)}");
        }
    }

    /// <summary>解析逗号分隔的单位名列表</summary>
    private static System.Collections.Generic.List<string> ParseUnitList(string csv)
    {
        var result = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(csv)) return result;
        foreach (var s in csv.Split(','))
        {
            var t = s.Trim();
            if (!string.IsNullOrEmpty(t)) result.Add(t);
        }
        return result;
    }

    /// <summary>重置全局Lab状态（新对局）：计数清零+解锁名单清空（修跨局泄漏）</summary>
    public static void ResetGlobalCount()
    {
        TeamLabCounts.Clear();
        UnitProductionWhitelist.UnlockedUnits.Clear();
    }
}
