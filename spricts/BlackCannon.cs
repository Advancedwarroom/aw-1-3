// BlackCannon.cs - 黑炮：三角形范围，固定百分比伤害，支持自定义目标选择和改良伤害模式
// ✅ 新增：完整弹药系统 + 多回合冷却系统（还原原版高级战争原型炮/完全体差异）
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class BlackCannon : Weapon
{
    [ExportGroup("黑炮专用配置")]
    [Export] public int maxAttackDepth = 9;                    // 射程深度（直线距离）
    [Export] public float fixedDamagePercent = 0.3f;           // 固定扣血30%（传统模式）
    [Export] public CannonDirection direction = CannonDirection.Up;
    [Export] public bool CanDestroy = false;

    // ✅ 新增：能否攻击兵器
    // ✅ 究极自由：使用基类的 CanAttackWeapon 属性

    // ✅ 新增：目标选择模式
    public enum TargetSelectionMode
    {
        AllSelect,      // 可选择所有单位（敌方+己方）
        OnlyEnemyUnits, // 只能选择敌方单位（默认）
        OnlyUserUnits   // 只能选择己方单位
    }
    [Export] public TargetSelectionMode targetSelectionMode = TargetSelectionMode.OnlyEnemyUnits;

    // ✅ 新增：改良伤害模式（支持回血）
    [ExportGroup("改良伤害模式（覆盖传统扣血）")]
    [Export] public bool useModifiedDamage = false;            // 是否使用改良伤害模式
    [Export] public float modifiedHealthPercent = 0.3f;        // 正=扣血，负=回血（如-0.5=回50%血）
    [Export] public float modifiedAmmoPercent = 0f;            // 正=扣弹药，负=回弹药（百分比）
    [Export] public float modifiedFuelPercent = 0f;            // 正=扣油，负=回油（百分比）
    [Export] public bool canOverMaxHp = false;                 // ✅ 新增：是否允许血量超过MaxHp（默认false）

    // ========== ✅ 新增：弹药系统（已在Weapon基类中定义，此处使用override）==========
    // useAmmoSystem, currentAmmo, maxAmmo 继承自 Weapon 基类

    // ========== ✅ 新增：冷却系统 ==========
    [ExportGroup("冷却系统（多回合CD，默认关闭）")]
    [Export] public bool useCooldownSystem = false;              // 🔧 开关：默认false=无冷却（每回合重置）
    [Export] public int cooldownTurns = 2;                     // 冷却周期（回合数）。如2=每2回合才能攻击
    [Export] public int attacksPerCooldown = 1;                // 每个冷却周期内的攻击次数配额
    [Export] public bool storeAttacks = true;                  // 存次数：本回合未用完的次数是否保留到后续回合

    // BlackCannon.cs - 在类顶部添加
private Vector2 swipeStartPos = Vector2.Zero;
private bool isSwiping = false;
private const float SWIPE_THRESHOLD = 50f; // 滑动超过50px算有效

public override void _Input(InputEvent @event)
{
    if (isDestroyed) return; // ✅ 已摧毁的兵器不接受输入
    var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
    
    // 只有本炮被选中、且当前回合是己方、且还没行动才能转
    if (gm?.selectedWeapon != this || !gm.IsTurnPhaseValid(team) || hasActed)
        return;

    // R键（保留）
    if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.R)
    {
        RotateDirection();
        GetViewport().SetInputAsHandled();
        return;
    }

    // ✅ 滑动手势
    if (@event is InputEventScreenTouch touch)
    {
        if (touch.Pressed)
        {
            swipeStartPos = touch.Position;
            isSwiping = true;
        }
        else if (isSwiping)
        {
            Vector2 delta = touch.Position - swipeStartPos;
            if (Mathf.Abs(delta.X) > SWIPE_THRESHOLD)
            {
                if (delta.X > 0)
                {
                    // 向右滑：顺时针转
                    RotateDirection();
                }
                else
                {
                    // 向左滑：逆时针转
                    direction = (CannonDirection)(((int)direction + 3) % 4); // +3 = -1 (mod 4)
                    UpdateDirectionVisual();
                }
                GetViewport().SetInputAsHandled();
            }
            isSwiping = false;
        }
    }
    
    // 鼠标拖拽（PC端模拟滑动）
    if (@event is InputEventMouseButton mouseBtn)
    {
        if (mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
        {
            swipeStartPos = mouseBtn.Position;
            isSwiping = true;
        }
        else if (!mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left && isSwiping)
        {
            Vector2 delta = mouseBtn.Position - swipeStartPos;
            if (Mathf.Abs(delta.X) > SWIPE_THRESHOLD)
            {
                if (delta.X > 0)
                    RotateDirection();
                else
                {
                    direction = (CannonDirection)(((int)direction + 3) % 4);
                    UpdateDirectionVisual();
                }
                GetViewport().SetInputAsHandled();
            }
            isSwiping = false;
        }
    }
}

    // ========== ✅ 新增：运行时冷却状态 ==========
    public int turnsSinceLastAttack = 0;                       // 距离上次攻击开始的回合数（0=本周期刚开始）
    public int attacksRemainingInCycle = 0;                    // 当前周期内剩余攻击次数
    public bool cooldownReady = true;                          // 冷却是否就绪（周期是否可用）
    public int totalTurnsPassed = 0;                           // 总回合计数（用于周期判断）

    public enum CannonDirection { Up, Right, Down, Left }

    // 方向向量（上、右、下、左）
    protected static readonly Vector2I[] DirectionVectors = {
        new Vector2I(0, -1),   // Up
        new Vector2I(1, 0),    // Right
        new Vector2I(0, 1),    // Down
        new Vector2I(-1, 0)    // Left
    };

    // 垂直向量（用于计算横向扩散）
    protected static readonly Vector2I[] PerpVectors = {
        new Vector2I(1, 0),    // Up的垂直方向是Right
        new Vector2I(0, 1),    // Right的垂直方向是Down
        new Vector2I(-1, 0),   // Down的垂直方向是Left
        new Vector2I(0, -1)    // Left的垂直方向是Up
    };



    private Label rotateTip;

    // 当 GameManager 选中/取消选中本炮时会触发
    public override void SetVisualNormal()
    {
        base.SetVisualNormal();
        UpdateRotateTip();      // 被选中
    }

    public override void SetVisualDark()
    {
        base.SetVisualDark();
        UpdateRotateTip();      // 被取消
    }

    private void UpdateRotateTip()
    {
        if (rotateTip == null) return;
        bool show = !hasActed && IsSelected();   // 可转且选中
        rotateTip.Visible = show;
    }

    private bool IsSelected()
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        return gm?.selectedWeapon == this;
    }
public override void _Ready()
{
    base._Ready();
    InitializeCooldownState();
    
    // 造价以检查器 cost 为准（已写入 .tscn），不在代码里覆盖
    
    var love = GetNode<Sprite2D>("LoveIcon");
    if (love != null) love.Show();
    UpdateDirectionVisual();
    rotateTip = GetNodeOrNull<Label>("RotateTip");
    UpdateRotateTip();
    UpdateAmmoVisual();
}

    public override void RotateDirection()
    {
        if (isDestroyed) return; // ✅ 已摧毁的兵器不可旋转
        if (!canRotate) return;
        // 顺时针旋转90度
        direction = (CannonDirection)(((int)direction + 1) % 4);
        UpdateDirectionVisual();
    }

    public virtual void UpdateDirectionVisual()
    {
        if (animSprite == null) return;
        string anim = GetAnimName();
        if (animSprite.SpriteFrames.HasAnimation(anim))
            animSprite.Play(anim);
        
    }

    public override void RefreshTeamVisual()
    {
        base.RefreshTeamVisual();
        UpdateDirectionVisual();
    }

    public override string GetAnimName()
    {
        string teamPrefix = "cannon" + TeamHelper.ArtSuffix(team);
        string dirSuffix = direction switch
        {
            CannonDirection.Up    => "up",
            CannonDirection.Down  => "down",
            CannonDirection.Left  => "left",
            CannonDirection.Right => "right",
            _ => "up"
        };
        return $"{teamPrefix}_{dirSuffix}";
    }
}
