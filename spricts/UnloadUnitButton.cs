// UnloadUnitButton.cs - 完整修复版
using Godot;
using System;

public partial class UnloadUnitButton : Control
{
    [Export] public AnimatedSprite2D unitAnim;
    [Export] public Label hpLabel;
    [Export] public TextureRect fuelIcon;
    [Export] public TextureRect ammoIcon;

    [Export] public Button unloadbutton;
    
    private Infantry targetUnit;
    private AnimatedSprite2D sourceAnim;
    private bool isInitialized = false;
    
    public override void _Ready()
    {
        // 防御式获取组件
        if (unitAnim == null)
            unitAnim = GetNodeOrNull<AnimatedSprite2D>("MarginContainer/HBoxContainer/UnitDisplay/AnimatedSprite2D");
        if (hpLabel == null)
            hpLabel = GetNodeOrNull<Label>("MarginContainer/HBoxContainer/StatusOverlay/HpLabel");
        if (fuelIcon == null)
            fuelIcon = GetNodeOrNull<TextureRect>("MarginContainer/HBoxContainer/StatusOverlay/FuelIcon");
        if (ammoIcon == null)
            ammoIcon = GetNodeOrNull<TextureRect>("MarginContainer/HBoxContainer/StatusOverlay/AmmoIcon");
        if (unloadbutton == null)
            unloadbutton = GetNodeOrNull<Button>("UnloadButton"); 
        
        if (unloadbutton != null)
        {
            MouseFilter = MouseFilterEnum.Ignore; // Control 本身不拦截
            unloadbutton.MouseFilter = MouseFilterEnum.Stop; // 按钮拦截
        }
        else
        {
            MouseFilter = MouseFilterEnum.Ignore; // 原有逻辑
        }
        
        isInitialized = true;
        if (targetUnit != null)
            InitializeAnimation();
        

        
        // 如果已经设置了单位，立即初始化
        if (targetUnit != null)
            InitializeAnimation();
    }
    
    public Button GetUnLoadButton() => unloadbutton;
    public void SetupDisplay(Infantry unit)
    {
        targetUnit = unit;
        if (unit == null || !IsInstanceValid(unit))
        {
            return;
        }
        
        
        if (isInitialized)
            InitializeAnimation();
    }
    
    private void InitializeAnimation()
    {
        // 获取源动画
        sourceAnim = GetSourceAnim(targetUnit);
        
        if (sourceAnim == null)
        {
            return;
        }
        
        
        // 复制 SpriteFrames
        if (unitAnim != null)
        {
            unitAnim.SpriteFrames = sourceAnim.SpriteFrames;
            
            // 确定播放哪个动画
            string animToPlay = GetAnimationName(targetUnit);
            
            if (unitAnim.SpriteFrames.HasAnimation(animToPlay))
            {
                unitAnim.Animation = animToPlay;
                unitAnim.Play();
            }
            else if (unitAnim.SpriteFrames.HasAnimation("idle"))
            {
                unitAnim.Animation = "idle";
                unitAnim.Play();
            }
            else
{
    // 随便找一个动画播放
    var anims = unitAnim.SpriteFrames.GetAnimationNames();
    
    // 使用 foreach 避免 Count/Length 问题
    foreach (var animName in anims)
    {
        unitAnim.Animation = animName;
        unitAnim.Play();
        break; // 只播放第一个
    }
}
            }
        }
    
    
    // 智能获取单位的动画组件
    private AnimatedSprite2D GetSourceAnim(Infantry u)
    {
        // 1. 直接检查已知的动画字段
        if (u is Mech m && m.animSprite != null) 
        {
            return m.animSprite;
        }
        if (u is LightTank t && t.animSprite != null) 
        {
            return t.animSprite;
        }
        if (u is APC a && a.animSprite != null) 
        {
            return a.animSprite;
        }
        if (u is Oozium o && o.animSprite != null) 
        {
            return o.animSprite;
        }
        if (u is Artillery ar && ar.animSprite != null) 
        {
            return ar.animSprite;
        }
        
        // 2. 递归查找所有子节点
        var foundAnim = FindAnimRecursive(u);
        if (foundAnim != null)
        {
            return foundAnim;
        }
        
        return null;
    }
    
    // 递归查找 AnimatedSprite2D
    private AnimatedSprite2D FindAnimRecursive(Node node)
    {
        if (node == null) return null;
        
        // 检查当前节点
        if (node is AnimatedSprite2D anim && anim != null)
        {
            return anim;
        }
        
        // 递归检查子节点
        foreach (var child in node.GetChildren())
        {
            var result = FindAnimRecursive(child);
            if (result != null) return result;
        }
        
        return null;
    }
    
    // 根据单位类型获取默认动画名
    private string GetAnimationName(Infantry u)
    {
        string teamSuffix = u.team == "Player2" ? "2" : "1";
        
        return u switch
        {
            Mech => $"mech{teamSuffix}",
            LightTank => $"lighttank{teamSuffix}",
            APC => $"apc{teamSuffix}",
            Oozium => "breath",
            Artillery => $"artillery{teamSuffix}",
            Infantry => $"infantry{teamSuffix}",
            _ => "idle"
        };
    }
    
    public override void _Process(double delta)
    {
        if (targetUnit == null || !IsInstanceValid(targetUnit)) return;
        if (sourceAnim == null || unitAnim == null) return;
        
        // 同步动画帧和状态
        if (sourceAnim.IsPlaying())
        {
            unitAnim.Frame = sourceAnim.Frame;
        }
        unitAnim.Modulate = sourceAnim.Modulate;
        unitAnim.FlipH = sourceAnim.FlipH;
        unitAnim.FlipV = sourceAnim.FlipV;
        
        // 更新数值显示
        UpdateDisplay();
    }
    
    private void UpdateDisplay()
    {
        if (targetUnit == null) return;
        
        // 血量
        if (hpLabel != null)
        {
            int bars = Mathf.CeilToInt(targetUnit.health / 10f);
            hpLabel.Text = bars.ToString();
        }
        
        // 油量
        if (fuelIcon != null)
        {
            if (!targetUnit.consumeFuel)
            {
                fuelIcon.Visible = false;
            }
            else
            {
                fuelIcon.Visible = true;
                fuelIcon.Modulate = targetUnit.fuel <= 0 ? Colors.Red : 
                                   (targetUnit.fuel <= targetUnit.lowFuelThreshold ? Colors.Yellow : Colors.Green);
            }
        }
        
        // 弹药
        if (ammoIcon != null)
        {
            if (!targetUnit.hasPrimaryWeapon)
            {
                ammoIcon.Visible = false;
            }
            else
            {
                ammoIcon.Visible = true;
                ammoIcon.Modulate = !targetUnit.CanUsePrimaryWeapon() ? Colors.Red : 
                                   (targetUnit.currentPrimaryAmmo <= 3 ? Colors.Yellow : Colors.Green);
            }
        }
    }
}