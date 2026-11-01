// Lab.cs - 秘密实验室（Research Laboratory）
// 原作AW2：占领解锁新型坦克(Neotank)科技。
// 本作规格：占领后解锁"可生成单位白名单"。
// 配合 Infantry.isUpdatable（是否更新Export）：
//   - isUpdatable=false → 与Lab无关，直接可生产
//   - isUpdatable=true  → 必须被Lab解锁（加入全局白名单）才能生产
// 编辑模式放置不受限制。
using Godot;
using System.Linq;

public partial class Lab : Facility
{
    [ExportGroup("实验室 - 解锁配置")]
    /// <summary>本Lab占领后解锁的可生成单位白名单（单位名/TemplateId）</summary>
    [Export] public Godot.Collections.Array<string> labUnitWhitelist = new();
    /// <summary>是否在占领时解锁（默认开启）</summary>
    [Export] public bool unlockOnCapture = true;

    protected override (string neutral, string red, string blue)? GetTeamAnimNames()
        => ("P0Lab", "labteam1", "labteam2");

    public override void _Ready()
    {
        base._Ready();
    }

    /// <summary>占领后解锁白名单单位</summary>
    public override void OnCaptureCompleted(Infantry unit)
    {
        base.OnCaptureCompleted(unit);
        if (unlockOnCapture)
            UnlockLabUnits();
    }

    /// <summary>✅ 解锁本Lab的白名单单位（加入全局可生成单位白名单）</summary>
    public void UnlockLabUnits()
    {
        if (labUnitWhitelist == null || labUnitWhitelist.Count == 0) return;
        UnitProductionWhitelist.UnlockUnits(labUnitWhitelist.ToList());
        GD.Print($"[Lab] 解锁单位: {string.Join(\", \", labUnitWhitelist)}");
    }
}
