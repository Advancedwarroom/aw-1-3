using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · SelfDestruct（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{

    // ========== ✅ 通用自爆系统（任何单位勾选即可自爆）==========
    [ExportGroup("自爆系统")]
    [Export] public bool canExplode = false;                     // 是否可自爆
    [Export] public int explosionMinRange = 0;                   // 爆炸最小范围（盲区）
    [Export] public int explosionMaxRange = 1;                   // 爆炸最大范围
    [Export] public bool explosionCanCrossScreens = false;    // 自爆影响范围是否允许跨屏
    [Export] public int explosionDamageMode = 0;                 // 0=固定值 1=百分比 2=攻击公式
    [Export] public int explosionFixedValue = 5;                 // 固定伤害值（可为负=加血）
    [Export] public float explosionPercentValue = 0.5f;          // 百分比伤害（0.5=50%）
    [Export] public int explosionTargetMode = 0;                  // 0=所有 1=仅敌方 2=仅友方
    [Export] public bool explosionDestroysSelf = true;           // 自爆是否摧毁自身
    [Export] public bool explosionSelfDamageEnabled = true;      // 自爆是否对自身造成伤害（内伤开关）
    [Export] public bool explosionCanKill = false;               // 是否能击杀其他单位（否则最低留1HP）
    [Export] public bool explosionAffectsWeapons = false;        // 爆炸是否影响兵器
    [Export] public bool explosionCanExceedMaxHealth = false;    // 回血/加血时能否超过最大HP

    // 当前占领进度（运行时）
    [ExportGroup("自爆弹模块")]
    [Export] public int maxExplodeAmmo = 0;                          // 最大自爆弹数
    [Export] public int currentExplodeAmmo = 0;                      // 当前自爆弹数

    private void DestroySelf()
    {
        if (!IsInstanceValid(this) || IsQueuedForDeletion()) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.unitManager?.RemoveUnit(this);
    }

    // ✅ 自爆接口（GameManager 调用执行）
    public virtual void OnExplode()
    {
        if (isStealthed && !canCustomActionWhileStealthed) return; // ✅ 隐身行为锁
    }

    [Export]
    public float counterMul = 0.5f;

    [Export]
    public string team = "Player";

    [Export] public Sprite2D spriteRed;   // Osinfantry - 红方P1贴图
    [Export] public Sprite2D spriteBlue; 
    [Export] public AnimatedSprite2D animSprite;   // ✅ 供子类使用（AnimatedSprite2D模式）
    [Export] public Sprite2D idleSprite;            // ✅ 供子类使用（静态贴图）
    [Export] public Sprite2D baseSprite;            // ✅ 双图层：灰白Base图（染色层）
    [Export] public Sprite2D normalSprite;          // ✅ 双图层：彩色Normal图（肤色/不变色层）
    [Export] public AnimatedSprite2D baseAnimSprite;    // ✅ 双图层动画：灰白Base动画（染色层）
    [Export] public AnimatedSprite2D normalAnimSprite;  // ✅ 双图层动画：彩色Normal动画（不变色层）
}
