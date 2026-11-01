// AI_Manager.cs —— 战棋 AI（效用评估 + 威胁图 + 迷雾记忆 + 全技能）
//
// 决策流程（每个 AI 小回合）：
//   1. 兵器（Weapon.ExecuteAI）逐个行动
//   2. 战术阶段：每一步为所有未行动单位重新评估"最佳方案"，先执行高价值战术（击杀/攻击/自爆/照明/完成占领/合流/修理）
//   3. 机动阶段：剩余单位按 照明→远程→近战→占领→运输→补给/修理 的顺序推进或撤退
//   4. 生产：按敌军构成反制选兵 + 占领需求 + 兵种多样性
//   5. 立即可动的新兵再走一轮机动
//
// 公平性：只用本势力视野（CalculateGlobalVisionPool + 照明弹 + 隐身规则 IsVisibleTo），不读取雾中/隐身单位。
//        被雾中或隐身敌军挡路时与玩家一样触发截断（遭遇）。
// 对外接口保持不变：gameManager / p1AIEnabled / p2AIEnabled / autoEndTurn / IsAITeam / IsCurrentPhaseAI / OnPhaseStart
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AI_Manager : Node
{
    [ExportGroup("")]
    [Export] public GameManager gameManager;

    public bool p1AIEnabled = false;
    public bool p2AIEnabled = false;
    public bool autoEndTurn = false;

    // ==================== 检查器参数（默认即可） ====================
    [ExportGroup("AI 参数")]
    [Export(PropertyHint.Range, "0,2")] public int difficulty = 2;     // 0=简单(随机扰动大/不避险) 1=普通 2=困难
    [Export] public float stepDelay = 0.4f;                             // 每个单位行动间隔(秒)
    [Export] public float moveStepTime = 0.06f;                         // 移动动画每格耗时(秒)
    [Export] public float riskAversion = 0.8f;                          // 威胁规避权重（越大越怕死）
    [Export] public float killBonusRatio = 0.6f;                        // 击杀额外价值 = 目标造价 × 该系数
    [Export] public int memoryDays = 3;                                 // 雾中敌军位置记忆天数
    [Export] public bool followCamera = true;                           // 镜头跟随（仅跟随玩家可见的单位）
    [Export] public bool logDecisions = false;                          // 输出决策日志

    // ==================== 运行时 ====================
    private Timer aiTimer;
    private bool isRunningAI = false;
    private bool isBusy = false;
    private string aiTeam = "";
    private int stage = 0;                       // 0=兵器 1=单位 2=生产 3=新兵 4=结束
    private Queue<Weapon> weaponQueue = new();
    private HashSet<Infantry> handled = new();
    private Ctx ctx;
    private readonly RandomNumberGenerator rng = new();
    private readonly Dictionary<string, Dictionary<Infantry, Memo>> memory = new();
    private readonly Dictionary<Grids, Infantry> moveClaims = new();   // 占领兵"认领"的目标据点（跨回合保留，防止扎堆）

    private struct Memo { public Grids grid; public int day; public int hp; }

    private enum Act { Wait, Attack, AttackWeapon, Capture, Merge, Load, Unload, Supply, Repair, Illuminate, Explode, Hide, Reveal }

    private class Plan
    {
        public Infantry unit;
        public Grids dest;
        public Act act = Act.Wait;
        public Infantry target;
        public Weapon weaponTarget;
        public Grids actGrid;                    // 照明中心 / 卸载格
        public Grids tpVia;                      // ✅ N屏传送：非空=先走到此格(传送起点)，再按"传送"落到 dest
        public List<(Infantry cargo, Grids to)> unloads;
        public bool preReveal;                   // 行动前先显形（免费切换）
        public bool postHide;                    // 行动后隐身（免费切换）
        public float score = float.NegativeInfinity;
        public bool tactical;                    // 属于战术阶段（优先执行）
        public string note = "";
        public List<Grids> path;
    }

    private class Reach
    {
        public Dictionary<Grids, int> cost = new();
        public Dictionary<Grids, Grids> parent = new();
    }

    private class Ctx
    {
        public string team;
        public int day;
        public bool fogOn;
        public HashSet<Grids> vision = new();
        public List<Infantry> mine = new();
        public List<Infantry> enemies = new();            // 当前可见敌军
        public List<(Infantry u, Grids g, int hp)> remembered = new();   // 雾中记忆
        public List<Weapon> enemyWeapons = new();
        public Dictionary<Grids, List<Infantry>> threat = new();
        public HashSet<Grids> rememberedThreat = new();   // 记忆中敌军的大致威胁区
        public List<Grids> capturables = new();            // 非己方据点
        public List<Crystal> crystals = new();             // 会作用到我方的黑水晶（己方 + 中立按过滤器）
        public Dictionary<Grids, Infantry> captureClaims = new();
        public Dictionary<string, Dictionary<Grids, int>> distCache = new();
    }

    // ==================== 生命周期 ====================
    public override void _Ready()
    {
        AddToGroup("ai_manager");
        rng.Randomize();
        aiTimer = new Timer { WaitTime = stepDelay, OneShot = true };
        aiTimer.Timeout += OnAIStep;
        AddChild(aiTimer);
    }

    public bool IsAITeam(string team)
    {
        if (team == TeamHelper.Player1) return p1AIEnabled;
        if (team == TeamHelper.Player2) return p2AIEnabled;
        return false;
    }

    public bool IsCurrentPhaseAI()
    {
        if (gameManager == null) return false;
        return IsAITeam(TeamHelper.GetPhaseTeamName(gameManager.turnPhase));
    }

    public void OnPhaseStart()
    {
        if (!IsCurrentPhaseAI() || isRunningAI) return;
        aiTeam = TeamHelper.GetPhaseTeamName(gameManager.turnPhase);
        isRunningAI = true;
        isBusy = false;
        stage = 0;
        handled.Clear();
        if (logDecisions)
            foreach (var u in gameManager.unitManager?.AllUnits ?? new List<Infantry>())
                if (u != null && IsInstanceValid(u) && u.team == aiTeam && (u.hasActed || u.state == UnitState.Acted))
                    GD.Print($"[AI] 回合开始即已行动: {u.GetType().Name}@{u.grid?.GridIndex} state={u.state} transported={u.isTransported}");
        weaponQueue = new Queue<Weapon>((gameManager.weaponManager?.AllWeapons ?? new List<Weapon>())
            .Where(w => w != null && IsInstanceValid(w) && !w.isDestroyed && w.team == aiTeam));
        Log($"===== {aiTeam} 回合开始 =====");
        aiTimer.WaitTime = 0.3f;
        aiTimer.Start();
    }

    private void OnAIStep()
    {
        if (!isRunningAI || isBusy) return;
        if (gameManager != null && gameManager.encounterPause) return;   // ✅ 回合中回合：遭遇决策期间AI步进冻结
        if (gameManager == null || !IsInstanceValid(gameManager) || gameManager.gameEnded || !IsCurrentPhaseAI())
        {
            isRunningAI = false;
            return;
        }

        try
        {
            switch (stage)
            {
                case 0: StepWeapons(); return;
                case 1: case 3: StepUnits(); return;
                case 2: StepProduction(); return;
                default: FinishTurn(); return;
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[AI] 出错，已跳过当前步骤: {e}");
            stage++;
            Next(stepDelay);
        }
    }

    private void Next(float delay)
    {
        if (!isRunningAI) return;
        aiTimer.WaitTime = Mathf.Max(0.05f, delay);
        aiTimer.Start();
    }

    private void FinishTurn()
    {
        isRunningAI = false;
        Log($"===== {aiTeam} 回合结束 =====");
        if (autoEndTurn && IsCurrentPhaseAI())
            gameManager.CallDeferred(nameof(GameManager.NextPhase));
    }

    // 兵种原型缓存：实例化场景（不入树）读取 Export 能力字段，用于生产时按"能力"而非类型名判断
    private static readonly Dictionary<string, Infantry> prototypes = new();

    private void Log(string msg)
    {
        if (logDecisions) GD.Print("[AI] " + msg);
    }
}
