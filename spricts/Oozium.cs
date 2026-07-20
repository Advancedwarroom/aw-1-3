using Godot;
using System;
public partial class Oozium : Infantry
{
    // ✅ 改用 AnimatedSprite2D
    [Export] public AnimatedSprite2D animSprite;
    
public override void _Ready()
{
    base._Ready(); // 先走父类，再覆盖

    // Oozium 特殊值
    if (maxHealth == 100) // 只有父类设的默认值才覆盖
    {
        maxHealth = 200;
        health = 200;
    }
    
    // 防御力覆盖为10
    baseDefense = 10;

    // 其他Oozium特有
    if (overlapType == default) overlapType = UnitOverlapType.Oozium;
    if (attackType == default) attackType = AttackType.NoAttack;
    if (moveType == default) moveType = MoveType.Oozium;
    if (defaultMovePoints == 0) defaultMovePoints = 1;
    if (attackRange == 0) attackRange = 1;
    
    // 无武器（吞噬替代）
    if (!hasPrimaryWeapon && !hasSecondaryWeapon)
    {
        hasPrimaryWeapon = false;
        hasSecondaryWeapon = false;
    }

    // 不耗油
    if (maxFuel == 99) { maxFuel = 99; consumeFuel = false; }
    if (fuel == 99) fuel = maxFuel;
    
    if (counterMul == 0 || counterMul == 0.5f) counterMul = 0f;
    if (captureAbility == default) captureAbility = CaptureAbility.CannotCapture;

    cost = 20000;  // Oozium造价
    if (captureAbility == default) captureAbility = CaptureAbility.CannotCapture;

    UpdateHpLabel();
    StartBreathAnimation();
}

    public override void _Process(double delta)
{


    if (noAmmoIcon != null)
    {
        if (!hasPrimaryWeapon) 
            noAmmoIcon.Visible = false;
        else if (!CanUsePrimaryWeapon()) 
            noAmmoIcon.Visible = true;
        else if (currentPrimaryAmmo <= 3) 
            noAmmoIcon.Visible = (Time.GetTicksMsec() / 500) % 2 == 0;
        else 
            noAmmoIcon.Visible = false;
    }
}
    
    // ✅ 播放呼吸动画
    public void StartBreathAnimation()
    {
        if (animSprite != null && animSprite.SpriteFrames.HasAnimation("breath"))
        {
            animSprite.Play("breath");
        }
    }
    
    // 停止动画
    public void StopBreathAnimation()
    {
        animSprite?.Stop();
    }
        // ✅ 关键：覆盖点击检测方法！
    public override void InputClick(Node viewport, InputEvent inputs, int shape_index)
    {
        if (inputs is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Left)
            {
                
                var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
                if (gm == null) return;
                            if (Input.IsKeyPressed(Key.Ctrl))
            {
                bool IsMyTurn = gm.IsTurnPhaseValid(team);
                bool isEnemy = !IsMyTurn;
                
                if ((IsMyTurn && isMoved) || isEnemy)
                {
                    
                    gm.gridManager.CloseRange();
                    gm.gridManager.HideAttackRange();
                    var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                    actionMenu?.Hide();
                    
                    ShowUnitInfo();
                    return;
                }
            }
                        if (this.state == UnitState.Moved && this.originalGrid != null)
            {
                gm.RollbackMove();
                return;
            }
                bool isMyTurn = gm.IsTurnPhaseValid(team);
                
                if (isMyTurn)
                {
                    // 关闭其他单位的显示
                    if (gm.selectedInfantry != null && gm.selectedInfantry != this)
                    {
                        var prevUnit = gm.selectedInfantry;
                        
                        gm.gridManager.CloseRange();
                        gm.gridManager.HideAttackRange();
                        var actionMenu = GetTree().GetFirstNodeInGroup("action_menu") as ActionMenu;
                        actionMenu?.Hide();
                        
                        // 只有 NonOverlapping 类型才需要回退
                        if (prevUnit.state == UnitState.Moved && 
                            prevUnit.overlapType == UnitOverlapType.NonOverlapping)
                        {
                            gm.Call("RollbackMove");
                        }
                        else if (prevUnit.state == UnitState.Moved)
                        {
                            gm.ClearSelectedInfantry();
                        }
                    }
                    
                    // 选中自己
                    gm.OnSelectPiece(this);
                }
                else
                {
                }
            }
        }
    }
    // 覆盖移动选择
    public override void OnMoveSelected()
    {
        StopBreathAnimation();
        
        // 如果有移动动画就播放
        if (animSprite?.SpriteFrames.HasAnimation("move") == true)
        {
            animSprite.Play("move");
        }
        
        // 调用父类逻辑（通过 GameManager）
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowMoveRange(this);
    }
    
    // 覆盖攻击选择
    public override void OnAttackSelected()
    {
        StopBreathAnimation();
        
        if (animSprite?.SpriteFrames.HasAnimation("attack") == true)
        {
            animSprite.Play("attack");
        }
        
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowAttackRange(this);
    }
    
    // 覆盖等待选择
    public override void OnWaitSelected()
    {
        isMoved = true;
        isAttacked = true;
        movePoints = defaultMovePoints;
        SetWaitVisual(true);
        originalGrid = null;
        
        // 恢复呼吸
        StartBreathAnimation();
        
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
    }
    

public override void UpdateHpLabel()
{
    if (hpLabel == null) return;
    
    int bars;
    
    if (health <= maxHealth)
    {
        // 正常情况：按 maxHealth 的 1/10 为基准，缩放到 1-10
        float healthPerBar = maxHealth / 10f;
        bars = Mathf.Clamp(Mathf.RoundToInt(health / healthPerBar), 1, 10);
    }
    else
    {
        // 超出血量：基础10格 + 超出部分按比例增加
        float healthPerBar = maxHealth / 10f;  // 每格代表的血量
        bars = Mathf.RoundToInt(health / healthPerBar);
    }
    
    hpLabel.Text = bars.ToString();
}
    
    // 覆盖设置等待视觉效果
    public override void SetWaitVisual(bool waiting)
    {
        // AnimatedSprite 用动画速度表示等待状态
        if (animSprite != null)
        {
            animSprite.SpeedScale = waiting ? 0.5f : 1.0f;  // 等待时慢速呼吸
        }
        
        if (hpLabel != null)
        {
            hpLabel.Modulate = Colors.White;
        }
    }
}