using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Infantry · Visual（由 Infantry.cs 按模块拆分；partial class，Export 分组已显式标注）
public partial class Infantry
{
    public Tween breathTween;

    public  Color normal = Colors.White;
    public  Color dim = new Color(0.7f, 0.7f, 0.7f, 1.0f);

    // ✅ 视觉组件原始 scale（用于呼吸动画基准，不覆盖场景中的设计 scale）
    private Vector2 _originalVisualScale = Vector2.One;

// ========== 子类覆盖：动画名称映射 ==========
public virtual string GetIdleAnimName() => null;
public virtual string GetMoveAnimName() => null;

private void UpdateActiveSprite()
{
    // ✅ 调色板模式：不再根据team切换spriteRed/spriteBlue
    // 势力颜色由Shader根据team动态替换
    if (spriteRed != null && spriteBlue != null)
    {
        // 旧双贴图模式fallback：显示spriteRed作为中性模板
        spriteRed.Show();
        spriteBlue.Hide();
        sprite = spriteRed;
    }
}

// ✅ 新增：切换队伍时刷新视觉
public virtual void UpdateTeamVisual()
{
    UpdateActiveSprite();
    
    // ✅ normal/dim 用于 shader 的 tone（全局变暗/变亮），保持 White/Gray
    //    势力颜色由 ApplyPaletteMaterial() 通过 primary_color/secondary_color 设置
    normal = Colors.White;
    dim = new Color(0.6f, 0.6f, 0.6f, 1.0f);
    
    // ✅ 应用调色板Shader
    ApplyPaletteMaterial();
    
    // ✅ 刷新动画状态（team 改变后同步动画名和 tone）
    if (animSprite != null && IsInstanceValid(animSprite))
    {
        string animName = GetIdleAnimName();
        if (!string.IsNullOrEmpty(animName))
            ShowAnimState(animName);
        else
            ShowAnimState("idle");
    }
    else if (sprite != null && IsInstanceValid(sprite))
    {
        ShowAnimState(null);
    }
    
    // 确保呼吸动画作用到正确的 sprite
    if (breathTween != null && breathTween.IsValid())
    {
        breathTween.Kill();
        breathTween = null;
    }
    
    if (sprite != null && IsInstanceValid(sprite) && !isMoved && state != UnitState.Acted)
    {
        sprite.Scale = _originalVisualScale;
        SetWaitVisual(false);
        StartBreath();
    }
    else if (animSprite != null && IsInstanceValid(animSprite) && !isMoved && state != UnitState.Acted)
    {
        animSprite.Scale = _originalVisualScale;
        SetWaitVisual(false);
        StartBreath();
    }
}

        /// <summary>
    /// ✅ 为视觉组件应用调色板Shader（LUT方案C + 双图层方案）
    /// </summary>
    protected virtual void ApplyPaletteMaterial()
    {
        // ✅ 双图层 AnimatedSprite2D 方案（优先）
        if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite) && 
            normalAnimSprite != null && IsInstanceValid(normalAnimSprite))
        {
            var baseMat = FactionPaletteManager.GetBaseLayerMaterial(team);
            if (baseMat != null)
            {
                baseAnimSprite.Material = (ShaderMaterial)baseMat.Duplicate();
                baseAnimSprite.Show();
            }
            normalAnimSprite.Material = null;
            normalAnimSprite.Show();
            // 隐藏旧版单图层组件，避免重叠
            if (animSprite != null) animSprite.Hide();
            if (sprite != null) sprite.Hide();
            if (spriteRed != null) spriteRed.Hide();
            if (spriteBlue != null) spriteBlue.Hide();
            return;
        }
        
        // ✅ 双图层 Sprite2D 方案
        if (baseSprite != null && IsInstanceValid(baseSprite) && 
            normalSprite != null && IsInstanceValid(normalSprite))
        {
            // ⚠️ 临时诊断（2026-11-13）：读取后删除。排查犀牛底图不可见。
            var baseMat = FactionPaletteManager.GetBaseLayerMaterial(team);
            if (baseMat != null)
            {
                baseSprite.Material = (ShaderMaterial)baseMat.Duplicate();
                baseSprite.Show();
            }
            normalSprite.Material = null;
            normalSprite.Show();
            // 隐藏旧版单图层组件
            if (animSprite != null) animSprite.Hide();
            if (sprite != null) sprite.Hide();
            if (spriteRed != null) spriteRed.Hide();
            if (spriteBlue != null) spriteBlue.Hide();
            return;
        }
        
        // Fallback：兼容模式（现有红蓝双色贴图）
        // 先隐藏双图层节点
        if (baseAnimSprite != null) baseAnimSprite.Hide();
        if (normalAnimSprite != null) normalAnimSprite.Hide();
        if (baseSprite != null) baseSprite.Hide();
        if (normalSprite != null) normalSprite.Hide();
        
        var mat = FactionPaletteManager.GetMaterial(team);
        if (mat == null) return;
        
        // 给animSprite应用调色板材质
        if (animSprite != null && IsInstanceValid(animSprite))
        {
            var animMat = (ShaderMaterial)mat.Duplicate();
            animSprite.Material = animMat;
            animSprite.Show();
        }
        
        // 给sprite应用调色板材质
        if (sprite != null && IsInstanceValid(sprite))
        {
            var spriteMat = (ShaderMaterial)mat.Duplicate();
            sprite.Material = spriteMat;
            sprite.Show();
        }
    }

public void StartBreath()
{
    UpdateActiveSprite(); // 确保呼吸作用到正确的 sprite
    
    breathTween?.Kill();

    // ✅ 双图层 AnimatedSprite2D：BaseAnimSprite 做呼吸，NormalAnimSprite 自动跟随
    if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite) &&
        normalAnimSprite != null && IsInstanceValid(normalAnimSprite))
    {
        baseAnimSprite.Scale = _originalVisualScale;
        
        breathTween = CreateTween();
        if (breathTween == null) return;
        breathTween.SetLoops();
        breathTween.SetProcessMode(Tween.TweenProcessMode.Idle);
        
        breathTween.TweenProperty(baseAnimSprite, "scale", _originalVisualScale * 1.05f, 1.2f)
                   .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        breathTween.TweenProperty(baseAnimSprite, "scale", _originalVisualScale, 1.2f)
                   .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        return;
    }

    // ✅ 双图层 Sprite2D：BaseSprite 做呼吸，NormalSprite 自动跟随
    if (baseSprite != null && IsInstanceValid(baseSprite) &&
        normalSprite != null && IsInstanceValid(normalSprite))
    {
        baseSprite.Scale = _originalVisualScale;
        
        breathTween = CreateTween();
        if (breathTween == null) return;
        breathTween.SetLoops();
        breathTween.SetProcessMode(Tween.TweenProcessMode.Idle);
        
        breathTween.TweenProperty(baseSprite, "scale", _originalVisualScale * 1.05f, 1.2f)
                   .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        breathTween.TweenProperty(baseSprite, "scale", _originalVisualScale, 1.2f)
                   .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        return;
    }

    // ✅ 支持 animSprite 和 sprite 两种模式（单图层）
    Node target = animSprite != null && IsInstanceValid(animSprite) ? animSprite : sprite;
    if (target == null || !IsInstanceValid(target))
        return;

    if (target is Node2D node2d)
        node2d.Scale = _originalVisualScale;

    breathTween = CreateTween();
    if (breathTween == null) return;

    breathTween.SetLoops();
    breathTween.SetProcessMode(Tween.TweenProcessMode.Idle);

    breathTween.TweenProperty(target, "scale", _originalVisualScale * 1.05f, 1.2f)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    breathTween.TweenProperty(target, "scale", _originalVisualScale, 1.2f)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
}

public void StopBreath()
{
    UpdateActiveSprite();
    
    breathTween?.Kill();
    
    // ✅ 双图层 AnimatedSprite2D：重置 BaseAnimSprite
    if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite))
        baseAnimSprite.Scale = _originalVisualScale;
    
    // ✅ 双图层 Sprite2D：重置 BaseSprite
    if (baseSprite != null && IsInstanceValid(baseSprite))
        baseSprite.Scale = _originalVisualScale;
    
    // ✅ 单图层模式
    Node target = animSprite != null && IsInstanceValid(animSprite) ? animSprite : sprite;
    if (target is Node2D node2d)
        node2d.Scale = _originalVisualScale;
}

public virtual void UpdateHpLabel()
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

// Infantry.cs - 替换 SetWaitVisual
public virtual void SetWaitVisual(bool waiting)
{
    // 确保 sprite 指向正确的阵营节点
    UpdateActiveSprite();
    
    // ✅ 双图层 AnimatedSprite2D：tone + Modulate 双重保险
    if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite))
    {
        if (baseAnimSprite.Material is ShaderMaterial baseMat)
            baseMat.SetShaderParameter("tone", waiting ? dim : normal);
        baseAnimSprite.Modulate = waiting ? dim : normal;
    }
    
    // ✅ 双图层 Sprite2D：tone + Modulate 双重保险
    if (baseSprite != null && IsInstanceValid(baseSprite))
    {
        if (baseSprite.Material is ShaderMaterial baseMat)
            baseMat.SetShaderParameter("tone", waiting ? dim : normal);
        baseSprite.Modulate = waiting ? dim : normal;
    }
    
    // ✅ LUT模式 / 旧模式：同时处理 animSprite 和 sprite
    Node2D target = animSprite != null && IsInstanceValid(animSprite) ? animSprite : sprite;
    if (target != null && IsInstanceValid(target))
    {
        if (target.Material is ShaderMaterial shaderMat)
            shaderMat.SetShaderParameter("tone", waiting ? dim : normal);
        target.Modulate = waiting ? dim : normal;
    }

    if (hpLabel != null)
        hpLabel.Modulate = Colors.White;
}

    protected Color GetTeamColor()
    {
        return TeamHelper.GetTeamColor(team);
    }


    public virtual void ShowAnimState(string animName)
    {
        // ✅ 双图层 AnimatedSprite2D：恢复 tone + 同步动画
        if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite))
        {
            if (baseAnimSprite.Material is ShaderMaterial baseMat)
                baseMat.SetShaderParameter("tone", normal);
            else
                baseAnimSprite.Modulate = normal;
            
            string actualAnim = animName;
            if (baseAnimSprite.SpriteFrames != null && !baseAnimSprite.SpriteFrames.HasAnimation(actualAnim))
                actualAnim = GetIdleAnimName();
            if (baseAnimSprite.SpriteFrames != null && baseAnimSprite.SpriteFrames.HasAnimation(actualAnim))
            {
                baseAnimSprite.Play(actualAnim);
                // 同步 Normal 层动画
                if (normalAnimSprite != null && normalAnimSprite.SpriteFrames != null &&
                    normalAnimSprite.SpriteFrames.HasAnimation(actualAnim))
                    normalAnimSprite.Play(actualAnim);
            }
        }
        
        // ✅ 双图层 Sprite2D：恢复 tone
        if (baseSprite != null && IsInstanceValid(baseSprite))
        {
            if (baseSprite.Material is ShaderMaterial baseMat)
                baseMat.SetShaderParameter("tone", normal);
            else
                baseSprite.Modulate = normal;
        }
        
        if (animSprite != null)
        {
            if (idleSprite != null) idleSprite.Hide();
            animSprite.Show();
            // ✅ LUT模式：通过Shader tone设置颜色
            if (animSprite.Material is ShaderMaterial shaderMat)
                shaderMat.SetShaderParameter("tone", normal);
            else
                animSprite.Modulate = normal;
            string actualAnim = animName;
            if (animSprite.SpriteFrames != null && !animSprite.SpriteFrames.HasAnimation(actualAnim))
                actualAnim = GetIdleAnimName();
            if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(actualAnim))
                animSprite.Play(actualAnim);
        }
        else if (sprite != null)
        {
            sprite.Modulate = normal;
        }
    }

    public virtual void ShowIdleState()
    {
        // ✅ 双图层 AnimatedSprite2D：变暗 + 同步 idle 动画
        if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite))
        {
            if (baseAnimSprite.Material is ShaderMaterial baseMat)
                baseMat.SetShaderParameter("tone", dim);
            else
                baseAnimSprite.Modulate = dim;
            
            string idleAnimName = GetIdleAnimName();
            if (baseAnimSprite.SpriteFrames != null && baseAnimSprite.SpriteFrames.HasAnimation(idleAnimName))
            {
                baseAnimSprite.Animation = idleAnimName;
                baseAnimSprite.Frame = 0;
                baseAnimSprite.Stop();
            }
            else if (baseAnimSprite.SpriteFrames != null && baseAnimSprite.SpriteFrames.HasAnimation("idle"))
            {
                baseAnimSprite.Animation = "idle";
                baseAnimSprite.Frame = 0;
                baseAnimSprite.Stop();
            }
            // 同步 Normal 层
            if (normalAnimSprite != null && normalAnimSprite.SpriteFrames != null)
            {
                if (normalAnimSprite.SpriteFrames.HasAnimation(idleAnimName))
                {
                    normalAnimSprite.Animation = idleAnimName;
                    normalAnimSprite.Frame = 0;
                    normalAnimSprite.Stop();
                }
                else if (normalAnimSprite.SpriteFrames.HasAnimation("idle"))
                {
                    normalAnimSprite.Animation = "idle";
                    normalAnimSprite.Frame = 0;
                    normalAnimSprite.Stop();
                }
                // ✅ Normal 层也变暗（shader tone 只影响自身，不影响子节点）
                if (normalAnimSprite.Material is ShaderMaterial normalMat)
                    normalMat.SetShaderParameter("tone", dim);
                else
                    normalAnimSprite.Modulate = dim;
            }
        }
        
        // ✅ 双图层 Sprite2D：变暗
        if (baseSprite != null && IsInstanceValid(baseSprite))
        {
            if (baseSprite.Material is ShaderMaterial baseMat)
                baseMat.SetShaderParameter("tone", dim);
            else
                baseSprite.Modulate = dim;
            
            // ✅ Normal 层也变暗
            if (normalSprite != null && IsInstanceValid(normalSprite))
            {
                if (normalSprite.Material is ShaderMaterial normalMat)
                    normalMat.SetShaderParameter("tone", dim);
                else
                    normalSprite.Modulate = dim;
            }
        }
        if (animSprite != null)
        {
            if (idleSprite != null) idleSprite.Hide();
            animSprite.Show();
            string idleAnimName = GetIdleAnimName();
            if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(idleAnimName))
                animSprite.Animation = idleAnimName;
            else if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation("idle"))
                animSprite.Animation = "idle";
            animSprite.Frame = 0;
            animSprite.Stop();
            // ✅ LUT模式：通过Shader tone设置变暗
            if (animSprite.Material is ShaderMaterial shaderMat)
                shaderMat.SetShaderParameter("tone", dim);
            else
                animSprite.Modulate = dim;
        }
        else if (sprite != null)
        {
            if (sprite.Material is ShaderMaterial shaderMat)
                shaderMat.SetShaderParameter("tone", dim);
            else
                sprite.Modulate = dim;
        }
    }

    public virtual void FreezeToIdleDark()
    {
        StopBreath();
        // ✅ 双图层 AnimatedSprite2D：变暗 + 同步 idle
        if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite))
        {
            if (baseAnimSprite.Material is ShaderMaterial baseMat)
                baseMat.SetShaderParameter("tone", dim);
            else
                baseAnimSprite.Modulate = dim;
            
            string idleAnimName = GetIdleAnimName();
            if (baseAnimSprite.SpriteFrames != null && baseAnimSprite.SpriteFrames.HasAnimation(idleAnimName))
            {
                baseAnimSprite.Animation = idleAnimName;
                baseAnimSprite.Frame = 0;
                baseAnimSprite.Stop();
            }
            else if (baseAnimSprite.SpriteFrames != null && baseAnimSprite.SpriteFrames.HasAnimation("idle"))
            {
                baseAnimSprite.Animation = "idle";
                baseAnimSprite.Frame = 0;
                baseAnimSprite.Stop();
            }
            // 同步 Normal 层
            if (normalAnimSprite != null && normalAnimSprite.SpriteFrames != null)
            {
                if (normalAnimSprite.SpriteFrames.HasAnimation(idleAnimName))
                {
                    normalAnimSprite.Animation = idleAnimName;
                    normalAnimSprite.Frame = 0;
                    normalAnimSprite.Stop();
                }
                else if (normalAnimSprite.SpriteFrames.HasAnimation("idle"))
                {
                    normalAnimSprite.Animation = "idle";
                    normalAnimSprite.Frame = 0;
                    normalAnimSprite.Stop();
                }
                // ✅ Normal 层也变暗（shader tone 只影响自身，不影响子节点）
                if (normalAnimSprite.Material is ShaderMaterial normalMat)
                    normalMat.SetShaderParameter("tone", dim);
                else
                    normalAnimSprite.Modulate = dim;
            }
        }
        
        // ✅ 双图层 Sprite2D：变暗
        if (baseSprite != null && IsInstanceValid(baseSprite))
        {
            if (baseSprite.Material is ShaderMaterial baseMat)
                baseMat.SetShaderParameter("tone", dim);
            else
                baseSprite.Modulate = dim;
            
            // ✅ Normal 层也变暗
            if (normalSprite != null && IsInstanceValid(normalSprite))
            {
                if (normalSprite.Material is ShaderMaterial normalMat)
                    normalMat.SetShaderParameter("tone", dim);
                else
                    normalSprite.Modulate = dim;
            }
        }
        
        if (animSprite != null)
        {
            if (idleSprite != null) idleSprite.Hide();
            animSprite.Show();
            string idleAnimName = GetIdleAnimName();
            if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(idleAnimName))
                animSprite.Animation = idleAnimName;
            else if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation("idle"))
                animSprite.Animation = "idle";
            animSprite.Frame = 0;
            animSprite.Stop();
            // ✅ LUT模式：通过Shader tone设置变暗
            if (animSprite.Material is ShaderMaterial shaderMat)
                shaderMat.SetShaderParameter("tone", dim);
            else
                animSprite.Modulate = dim;
        }
        else if (sprite != null)
        {
            if (sprite.Material is ShaderMaterial shaderMat)
                shaderMat.SetShaderParameter("tone", dim);
            else
                sprite.Modulate = dim;
        }
        
        // ✅ 保险：统一调用 SetWaitVisual 确保所有图层都被处理
        SetWaitVisual(true);
    }

    public virtual void RestoreAnimAndBreath()
    {
        string animName = GetIdleAnimName();
        ShowAnimState(animName);
        StartBreath();
    }
}
