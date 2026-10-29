using Godot;
using System;

public partial class Oozium : Infantry
{
    public Oozium() { canDevour = true; }   // ✅ 默认吞噬（原行为）；检查器取消勾选即变为只重叠不吞噬

    public override void _Ready()
    {
        overlapType = UnitOverlapType.Oozium;
        base._Ready();
        // Oozium 仅保留特殊视觉初始化（队伍颜色决定呼吸动画）
        StartBreathAnimation();
    }

    // ✅ 队伍颜色决定呼吸动画：红色=2, 蓝色=3, 中立=1
    public void StartBreathAnimation()
    {
        if (animSprite == null || animSprite.SpriteFrames == null) return;
        string animName = team switch
        {
            "Player1" => "oozium2",
            "Player2" => "oozium3",
            _ => "oozium1"
        };
        if (animSprite.SpriteFrames.HasAnimation(animName))
            animSprite.Play(animName);
        else if (animSprite.SpriteFrames.HasAnimation("breath"))
            animSprite.Play("breath");
    }

    public void StopBreathAnimation()
    {
        animSprite?.Stop();
    }

    public override void OnMoveSelected()
    {
        StopBreathAnimation();
        if (animSprite?.SpriteFrames.HasAnimation("move") == true)
            animSprite.Play("move");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowMoveRange(this);
    }

    public override void OnAttackSelected()
    {
        StopBreathAnimation();
        if (animSprite?.SpriteFrames.HasAnimation("attack") == true)
            animSprite.Play("attack");
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager.ShowAttackRange(this);
    }

    public override void OnWaitSelected()
    {
        isMoved = true;
        isAttacked = true;
        movePoints = defaultMovePoints;
        originalGrid = null;
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.ClearSelectedInfantry();
        StartBreathAnimation();
    }

    public override void SetWaitVisual(bool waiting)
    {
        // ✅ 先调用基类暗色逻辑
        if (baseAnimSprite != null && IsInstanceValid(baseAnimSprite))
        {
            if (baseAnimSprite.Material is ShaderMaterial baseMat)
                baseMat.SetShaderParameter("tone", waiting ? dim : normal);
            else
                baseAnimSprite.Modulate = waiting ? dim : normal;
        }
        else if (animSprite != null && IsInstanceValid(animSprite))
        {
            if (animSprite.Material is ShaderMaterial mat)
                mat.SetShaderParameter("tone", waiting ? dim : normal);
            else
                animSprite.Modulate = waiting ? dim : normal;
        }
        
        if (animSprite != null)
            animSprite.SpeedScale = waiting ? 0.5f : 1.0f;
        if (hpLabel != null)
            hpLabel.Modulate = Colors.White;
    }
}
