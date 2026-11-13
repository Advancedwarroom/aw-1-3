// Laser.cs - 高级激光炮系统（修复版）
// 修复内容：
// 1. 多次攻击同步 remainingAttacks 和 attacksRemainingInCycle
// 2. 添加局内角度配置API（EnableAngle/DisableAngle/SetFireAngles等）
// 3. ✅ 移除 attackedThisRound 限制：允许同一回合多次攻击同一目标
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Laser : Weapon
{
    // ========== Export配置 ==========

    [ExportGroup("激光角度系统")]
    [Export] public Godot.Collections.Dictionary<float, bool> angleConfig = new();
    [Export] public Godot.Collections.Dictionary<float, bool> initialFireAngles = new();
    [Export] public bool allowDiagonalAngles = true;
    [Export] public bool allowCustomAngles = true;

    [ExportGroup("激光射程配置")]
    [Export] public int maxLaserLength = 99;
    [Export] public bool useInfiniteRange = true;

    [ExportGroup("基础伤害配置（二选一）")]
    [Export] public float fixedDamagePercent = 0.3f;
    [Export] public bool useModifiedDamage = false;
    [Export] public float modifiedHealthPercent = 0.3f;
    [Export] public float modifiedAmmoPercent = 0f;
    [Export] public float modifiedFuelPercent = 0f;
    [Export] public bool canOverMaxHp = false;
    [Export] public bool CanDestroy = false;

    [ExportGroup("接触面积衰减系数（可选叠加）")]
    [Export] public bool useContactAreaDamage = false;
    [Export] public float maxContactDamagePercent = 0.5f;
    [Export] public float minContactDamagePercent = 0.1f;

    [ExportGroup("穿透系统")]
    [Export] public int maxPiercePerLine = 3;

    [ExportGroup("目标选择模式")]
    [Export] public LaserTargetMode targetSelectionMode = LaserTargetMode.OnlyEnemyUnits;
    [Export] public bool laserCanTargetEnemies = false;                // ✅ 开创人2026-11-14 三军勾选（敌/友/盟独立勾选），全false回退旧枚举，存量行为零变化
    [Export] public bool laserCanTargetFriendlies = false;             // 友军=同势力
    [Export] public bool laserCanTargetAllies = false;                 // 盟军=盟约势力
    [Export] public bool canAttackWeapons = false;

    // ========== ✅ 弹药系统已在 Weapon 基类中定义（useAmmoSystem, currentAmmo, maxAmmo）

    [ExportGroup("冷却系统")]
    [Export] public bool useCooldownSystem = false;
    [Export] public int cooldownTurns = 1;
    [Export] public int attacksPerCooldown = 1;
    [Export] public bool storeAttacks = true;

    [ExportGroup("旋转与交互")]
    [Export] public bool useSwipeRotation = true;

    [ExportGroup("AI自动操作")]
    [Export] public new bool enableAutoAI = false;
    [Export] public bool aiAutoRotate = true;
    [Export] public int aiMaxRotationAttempts = 10;
    [Export] public bool aiUseAllAttacks = true;
    [Export] public bool aiShowEffects = true;

    // ========== 运行时状态 ==========
    public List<float> enabledAngles = new();
    public List<float> currentFireAngles = new();

    public int turnsSinceLastAttack = 0;
    public int attacksRemainingInCycle = 0;
    public bool cooldownReady = true;
    public int totalTurnsPassed = 0;

    // ✅ 已移除 attackedThisRound 限制：允许同一回合多次攻击同一目标
    private bool isAIExecuting = false;
    private int aiCurrentRotationAttempts = 0;
    private bool aiMarkedActed = false;

    private Vector2 swipeStartPos = Vector2.Zero;
    private bool isSwiping = false;
    private const float SWIPE_THRESHOLD = 50f;

    public Dictionary<float, List<RayGridInfo>> rayCache = new();

    private AnimatedSprite2D _animSprite;
    private Label _ammoLabel;

    // ========== 数据结构 ==========
    public struct LaserTargetInfo
    {
        public Node2D Target;
        public Grids Grid;
        public float Angle;
        public int Distance;
        public int PierceIndex;
        public float ContactRatio;
        public float BaseDamagePercent;
    }

    public struct RayGridInfo
    {
        public Grids Grid;
        public float ContactRatio;
        public float EntryT;
        public float ExitT;
        public bool IsBlocked;
    }

    public override void _Ready()
    {
        base._Ready();
        _animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        _ammoLabel = GetNodeOrNull<Label>("AmmoLabel");
        InitializeAngleSystem();
        InitializeCooldownState();
        UpdateAmmoVisual();
        base.canRotate = this.canRotate;
        base.CanAttackWeapon = this.canAttackWeapons;
        base.maxAttacksPerTurn = this.attacksPerCooldown;
        // 造价以检查器 cost 为准（已写入 .tscn），不在代码里覆盖
        base.CanAttackWeapon = this.canAttackWeapons;
        base.maxAttacksPerTurn = this.attacksPerCooldown;
        UpdateLaserVisual();

        if (enableAutoAI && !hasActed && HasAmmoAndCooldown())
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm != null && gm.IsTurnPhaseValid(team))
            {
                var timer = GetTree().CreateTimer(0.3f);
                timer.Timeout += () => {
                    if (IsInstanceValid(this) && !hasActed && HasAmmoAndCooldown())
                    {
                        ExecuteAIAutoAttack();
                    }
                };
            }
        }
    }

    // ========== 输入处理 ==========

    public override void _Input(InputEvent @event)
    {
        if (enableAutoAI && hasActed) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.selectedWeapon != this || !gm.IsTurnPhaseValid(team) || hasActed) return;

        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.R)
        {
            RotateLaser(true);
            GetViewport()?.SetInputAsHandled();
            return;
        }
        if (useSwipeRotation)
            HandleSwipeInput(@event);
    }

    private void HandleSwipeInput(InputEvent @event)
    {
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed) { swipeStartPos = touch.Position; isSwiping = true; }
            else if (isSwiping)
            {
                Vector2 delta = touch.Position - swipeStartPos;
                if (Mathf.Abs(delta.X) > SWIPE_THRESHOLD)
                { RotateLaser(delta.X > 0); GetViewport()?.SetInputAsHandled(); }
                isSwiping = false;
            }
        }
        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
            { swipeStartPos = mouseBtn.Position; isSwiping = true; }
            else if (!mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left && isSwiping)
            {
                Vector2 delta = mouseBtn.Position - swipeStartPos;
                if (Mathf.Abs(delta.X) > SWIPE_THRESHOLD)
                { RotateLaser(delta.X > 0); GetViewport()?.SetInputAsHandled(); }
                isSwiping = false;
            }
        }
    }

    private int CalculateGridDistance(Vector2I a, Vector2I b)
    {
        return Mathf.RoundToInt(Mathf.Sqrt(Mathf.Pow(a.X - b.X, 2) + Mathf.Pow(a.Y - b.Y, 2)));
    }

    public override void SetVisualNormal()
    {
        base.SetVisualNormal();
        UpdateLaserVisual();
    }

    public override void SetVisualDark()
    {
        base.SetVisualDark();
        if (_animSprite != null)
            _animSprite.Modulate = new Color(0.5f, 0.5f, 0.5f, 1f);
    }

    // ✅ 修复：CanAttack 使用同步后的 remainingAttacks
    public override bool CanAttack() => !hasActed && remainingAttacks > 0 && HasAmmoAndCooldown();
}
