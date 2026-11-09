// EmpowermentModules.cs - 三类对象共用的赋能/解能 Export。运行时由 EmpowermentRuntime 统一结算。
using Godot;

public partial class Infantry
{
    [ExportGroup("通用赋能 / 解能")]
    [Export] public bool empowermentEnabled = false;
    [Export(PropertyHint.Enum, "赋能,解能")] public int empowermentOperationType = 0;
    [Export] public bool empowermentAutomatic = true;
    [Export] public bool empowermentConsumesAction = true;
    [Export] public string empowermentActionName = "赋能";
    [Export(PropertyHint.Enum, "面范围,固定方向线,锥形")] public int empowermentShape = 0;
    [Export] public Vector2I empowermentDirection = new(1, 0);
    [Export] public int empowermentRangeMin = 0;
    [Export] public int empowermentRangeMax = 2;
    [Export] public int empowermentCastRangeMin = 0;
    [Export] public int empowermentCastRangeMax = 2;
    [Export] public bool empowermentTargetUnits = true;
    [Export] public bool empowermentTargetWeapons = true;
    [Export] public bool empowermentTargetFacilities = true;
    [Export(PropertyHint.Enum, "仅我方,仅敌方,全部")] public int empowermentTeamPolicy = 0;
    [Export] public Godot.Collections.Array<string> empowermentBlacklist = new();
    [Export] public bool empowermentIncludeSelf = true;
    [Export(PropertyHint.Range, "1,99,1")] public int empowermentMaxStacksPerCell = 1;
    [Export(PropertyHint.Range, "0,9999,1")] public int empowermentWeight = 0;
    [Export] public Godot.Collections.Dictionary<string, string> empowermentExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> empowermentExportDeltas = new();
    [Export] public Godot.Collections.Array<string> empowermentStatusAdd = new();
    [Export] public Godot.Collections.Array<string> empowermentStatusRemove = new();
    [Export(PropertyHint.Enum, "仅自己施加,全部来源")] public int empowermentDispelScope = 0;
    public bool empowermentManualActive = false;
    public Grids empowermentManualTarget = null;
    public int empowermentUsesThisTurn = 0;
    public int empowermentManualTargetX = -1;
    public int empowermentManualTargetY = -1;
}

public partial class Weapon
{
    [ExportGroup("通用赋能 / 解能")]
    [Export] public bool empowermentEnabled = false;
    [Export(PropertyHint.Enum, "赋能,解能")] public int empowermentOperationType = 0;
    [Export] public bool empowermentAutomatic = true;
    [Export] public bool empowermentConsumesAction = true;
    [Export] public string empowermentActionName = "赋能";
    [Export(PropertyHint.Enum, "面范围,固定方向线,锥形")] public int empowermentShape = 0;
    [Export] public Vector2I empowermentDirection = new(1, 0);
    [Export] public int empowermentRangeMin = 0;
    [Export] public int empowermentRangeMax = 2;
    [Export] public int empowermentCastRangeMin = 0;
    [Export] public int empowermentCastRangeMax = 2;
    [Export] public bool empowermentTargetUnits = true;
    [Export] public bool empowermentTargetWeapons = true;
    [Export] public bool empowermentTargetFacilities = true;
    [Export(PropertyHint.Enum, "仅我方,仅敌方,全部")] public int empowermentTeamPolicy = 0;
    [Export] public Godot.Collections.Array<string> empowermentBlacklist = new();
    [Export] public bool empowermentIncludeSelf = true;
    [Export(PropertyHint.Range, "1,99,1")] public int empowermentMaxStacksPerCell = 1;
    [Export(PropertyHint.Range, "0,9999,1")] public int empowermentWeight = 0;
    [Export] public Godot.Collections.Dictionary<string, string> empowermentExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> empowermentExportDeltas = new();
    [Export] public Godot.Collections.Array<string> empowermentStatusAdd = new();
    [Export] public Godot.Collections.Array<string> empowermentStatusRemove = new();
    [Export(PropertyHint.Enum, "仅自己施加,全部来源")] public int empowermentDispelScope = 0;
    public bool empowermentManualActive = false;
    public Grids empowermentManualTarget = null;
    public int empowermentUsesThisTurn = 0;
    public int empowermentManualTargetX = -1;
    public int empowermentManualTargetY = -1;
}

public partial class Facility
{
    [ExportGroup("通用赋能 / 解能")]
    [Export] public bool empowermentEnabled = false;
    [Export(PropertyHint.Enum, "赋能,解能")] public int empowermentOperationType = 0;
    [Export] public bool empowermentAutomatic = true;
    [Export] public bool empowermentConsumesAction = true;
    [Export] public string empowermentActionName = "赋能";
    [Export(PropertyHint.Enum, "面范围,固定方向线,锥形")] public int empowermentShape = 0;
    [Export] public Vector2I empowermentDirection = new(1, 0);
    [Export] public int empowermentRangeMin = 0;
    [Export] public int empowermentRangeMax = 2;
    [Export] public int empowermentCastRangeMin = 0;
    [Export] public int empowermentCastRangeMax = 2;
    [Export] public bool empowermentTargetUnits = true;
    [Export] public bool empowermentTargetWeapons = true;
    [Export] public bool empowermentTargetFacilities = true;
    [Export(PropertyHint.Enum, "仅我方,仅敌方,全部")] public int empowermentTeamPolicy = 0;
    [Export] public Godot.Collections.Array<string> empowermentBlacklist = new();
    [Export] public bool empowermentIncludeSelf = true;
    [Export(PropertyHint.Range, "1,99,1")] public int empowermentMaxStacksPerCell = 1;
    [Export(PropertyHint.Range, "0,9999,1")] public int empowermentWeight = 0;
    [Export] public Godot.Collections.Dictionary<string, string> empowermentExportOverrides = new();
    [Export] public Godot.Collections.Dictionary<string, float> empowermentExportDeltas = new();
    [Export] public Godot.Collections.Array<string> empowermentStatusAdd = new();
    [Export] public Godot.Collections.Array<string> empowermentStatusRemove = new();
    [Export(PropertyHint.Enum, "仅自己施加,全部来源")] public int empowermentDispelScope = 0;
    public bool empowermentManualActive = false;
    public Grids empowermentManualTarget = null;
    public int empowermentUsesThisTurn = 0;
    public int empowermentManualTargetX = -1;
    public int empowermentManualTargetY = -1;
}
