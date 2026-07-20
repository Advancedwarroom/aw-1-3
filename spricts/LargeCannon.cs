// LargeCannon.cs - 大型多格黑炮（3×3占据），继承BlackCannon
// 还原原版AW Black Cannon的大型版本：3×3不可通行，仅弱点可攻击，摧毁后Broken形态仍不可通行
// 分离炮口（firePoint）和弱点（weakPoint）：Up时炮口在上中，弱点在下中
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class LargeCannon : BlackCannon
{
    [ExportGroup("大型多格配置")]
    [Export] public Vector2I multiTileSize = new Vector2I(3, 3);
    [Export] public bool canRotate = true; // 旋转开关：默认开启（LargeCannon支持旋转）

    // 炮口（发射台）偏移：炮弹发射的起点
    private Vector2I firePointOffset = new Vector2I(1, 0);
    // 弱点偏移：可被攻击扣血的格子
    private Vector2I weakPointOffset = new Vector2I(1, 2);

    public override void _Ready()
    {
        // 在base._Ready之前设置多格属性
        isMultiTile = true;
        size = multiTileSize;
        base._Ready();
        
        // 更新偏移（基于当前方向）
        UpdateOffsetsForDirection();
    }

    // 炮口位置：炮弹发射的起点（攻击范围计算从这里开始）
    // Right: (2,1) - 最右列中间（炮口朝右）
    // Left:  (0,1) - 最左列中间（炮口朝左）
    // Up:    (1,0) - 上中（炮口朝上）
    // Down:  (1,2) - 下中（炮口朝下）
    public Vector2I GetFirePointOffsetForDirection(CannonDirection dir)
    {
        return dir switch
        {
            CannonDirection.Right => new Vector2I(2, 1),
            CannonDirection.Left  => new Vector2I(0, 1),
            CannonDirection.Up    => new Vector2I(1, 0),
            CannonDirection.Down  => new Vector2I(1, 2),
            _ => new Vector2I(1, 0)
        };
    }

    // 弱点位置：可被攻击的格子（被攻击时扣血的点）
    // Right: (2,1) - 最右列中间
    // Left:  (0,1) - 最左列中间
    // Up:    (1,2) - 下中（固定位置！）
    // Down:  (1,2) - 下中（固定位置！）
    public Vector2I GetWeakPointOffsetForDirection(CannonDirection dir)
    {
        return dir switch
        {
            CannonDirection.Right => new Vector2I(2, 1),
            CannonDirection.Left  => new Vector2I(0, 1),
            CannonDirection.Up    => new Vector2I(1, 2),
            CannonDirection.Down  => new Vector2I(1, 2),
            _ => new Vector2I(1, 2)
        };
    }

    private void UpdateOffsetsForDirection()
    {
        firePointOffset = GetFirePointOffsetForDirection(direction);
        weakPointOffset = GetWeakPointOffsetForDirection(direction);
    }

    public override void RotateDirection()
    {
        if (!canRotate) return; // 旋转开关关闭时不旋转
        base.RotateDirection();
        UpdateOffsetsForDirection();
        UpdateMultiTileVisual();
    }

    public override void UpdateDirectionVisual()
    {
        UpdateOffsetsForDirection(); // 确保偏移同步更新
        base.UpdateDirectionVisual();
        UpdateMultiTileVisual();
    }

    public override void UpdateMultiTileVisual()
    {
        // 大贴图位置调整：锚点是(0,0)，但贴图中心需要覆盖3×3区域
        // 3×3的中心是(1,1)，相对于锚点的偏移是(1,1)格 = 32,32像素
        if (animSprite != null)
        {
            // 将贴图中心对准3×3的中心
            animSprite.Position = new Vector2(
                16 + (multiTileSize.X - 1) * 16f,
                16 + (multiTileSize.Y - 1) * 16f
            );
        }
    }

    // 获取炮口格子（发射台）
    public Grids GetFirePointGrid()
    {
        if (occupiedGrids.Count == 0) return grid;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return grid;
        
        var anchorIndex = grid?.GridIndex ?? new Vector2I(0, 0);
        var fpIndex = anchorIndex + firePointOffset;
        
        if (fpIndex.X >= 0 && fpIndex.X < gm.gridManager.searchRange.X &&
            fpIndex.Y >= 0 && fpIndex.Y < gm.gridManager.searchRange.Y)
        {
            return gm.gridManager.map[fpIndex.X, fpIndex.Y];
        }
        return grid;
    }

    public override Grids GetWeakPointGrid()
    {
        if (occupiedGrids.Count == 0) return grid;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return grid;
        
        var anchorIndex = grid?.GridIndex ?? new Vector2I(0, 0);
        var wpIndex = anchorIndex + weakPointOffset;
        
        if (wpIndex.X >= 0 && wpIndex.X < gm.gridManager.searchRange.X &&
            wpIndex.Y >= 0 && wpIndex.Y < gm.gridManager.searchRange.Y)
        {
            return gm.gridManager.map[wpIndex.X, wpIndex.Y];
        }
        return grid;
    }

    public override bool IsWeakPointGrid(Grids checkGrid)
    {
        if (!isMultiTile || occupiedGrids.Count == 0) return true;
        return checkGrid == GetWeakPointGrid();
    }

    // 计算攻击范围：从炮口格子（firePoint）开始，而不是从弱点格子开始
    public override List<Grids> CalculateAttackRange()
    {
        var range = new List<Grids>();
        var firePointGrid = GetFirePointGrid();
        if (firePointGrid == null) return range;

        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return range;

        Vector2I dir = DirectionVectors[(int)direction];
        Vector2I perp = PerpVectors[(int)direction];
        Vector2I start = firePointGrid.GridIndex;

        // 遍历每一层深度（距离）
        for (int depth = 1; depth <= maxAttackDepth; depth++)
        {
            Vector2I center = start + dir * depth;
            int halfWidth = depth;

            for (int w = -halfWidth; w <= halfWidth; w++)
            {
                Vector2I checkPos = center + perp * w;

                if (checkPos.X < 0 || checkPos.X >= gm.gridManager.searchRange.X ||
                    checkPos.Y < 0 || checkPos.Y >= gm.gridManager.searchRange.Y)
                    continue;

                var targetGrid = gm.gridManager.map[checkPos.X, checkPos.Y];
                if (targetGrid != null && targetGrid.gridType != GridType.METEORITE)
                {
                    range.Add(targetGrid);
                }
            }
        }

        return range;
    }

    public override void TakeDamage(int damage)
    {
        // 如果被摧毁，不再接受伤害
        if (isDestroyed) return;
        
        base.TakeDamage(damage);
    }

    public override void OnDestroyed()
    {
        // 标记为多格摧毁状态
        OnMultiTileDestroyed();
        
        // 从所有管理结构中移除，但格子仍保持被占据（不可通行）
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager?.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        
        if (gm != null && gm.selectedWeapon == this)
            gm.selectedWeapon = null;
        
        // 禁用交互
        var area = GetNodeOrNull<Area2D>("Area2D");
        if (area != null)
        {
            area.InputPickable = false;
            area.Monitoring = false;
            area.Monitorable = false;
        }
        
        // 隐藏血条和旋转提示
        if (hpLabel != null) hpLabel.Hide();
        var rotateTip = GetNodeOrNull<Label>("RotateTip");
        if (rotateTip != null) rotateTip.Hide();
        
        // 播放摧毁特效
        ShakeCamera(amplitude: 10f, times: 5);
        var flash = new CannonFlash();
        flash.Position = GlobalPosition;
        GetTree().CurrentScene?.AddChild(flash);
        
        // ✅ 恢复所有占据格子的weapon引用，确保摧毁后仍不可通行
        // base.TakeDamage可能在调用OnDestroyed前清除了锚点格子的引用
        if (occupiedGrids.Count > 0)
        {
            foreach (var g in occupiedGrids)
            {
                if (g != null && IsInstanceValid(g))
                {
                    if (!g.weapons.Contains(this)) g.weapons.Add(this);
                    if (g.weapon == null) g.weapon = this;
                }
            }
            // 恢复自身锚点引用
            grid = occupiedGrids[0];
            
            // ✅ 恢复视觉：从透明变回正常显示Broken动画
            if (animSprite != null)
            {
                animSprite.Modulate = Colors.White;
                string brokenAnim = GetBrokenAnimName();
                if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(brokenAnim))
                    animSprite.Play(brokenAnim);
            }
        }
        
        // 胜利判定检查
        gm?.CheckVictoryCondition();
    }

    public override string GetBrokenAnimName()
    {
        string dirSuffix = direction switch
        {
            CannonDirection.Up    => "Up",
            CannonDirection.Down  => "Down",
            CannonDirection.Left  => "Left",
            CannonDirection.Right => "Right",
            _ => "Up"
        };
        return $"Broken{dirSuffix}";
    }

    // ✅ 覆盖动画名称：匹配 LargeCannon.tres 中的动画命名
    protected override string GetAnimName()
    {
        string dirSuffix = direction switch
        {
            CannonDirection.Up    => "Up",
            CannonDirection.Down  => "Down",
            CannonDirection.Left  => "Left",
            CannonDirection.Right => "Right",
            _ => "Up"
        };
        return $"LargeCannon{dirSuffix}";
    }

    private void ShakeCamera(float amplitude, int times)
    {
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;
        var tween = cam.CreateTween().SetParallel(false);
        for (int i = 0; i < times; i++)
        {
            var rnd = new Vector2(GD.Randf() * amplitude - amplitude * 0.5f,
                                  GD.Randf() * amplitude - amplitude * 0.5f);
            tween.TweenProperty(cam, "position", cam.Position + rnd, 0.05f);
        }
        tween.TweenProperty(cam, "position", cam.Position, 0.05f);
    }

    public override string GetBlackCannonFullInfo()
    {
        string info = base.GetBlackCannonFullInfo();
        info = info.Replace("[黑炮]", "[大型黑炮]");
        info += $"\n=== 多格占据 ===\n";
        info += $"尺寸: {size.X}×{size.Y}\n";
        info += $"炮口位置: {direction} ({GetFirePointOffsetForDirection(direction)})\n";
        info += $"弱点位置: {direction} ({GetWeakPointOffsetForDirection(direction)})\n";
        info += $"可旋转: {(canRotate ? "是" : "否")}\n";
        info += $"状态: {(isDestroyed ? "已摧毁" : "运作中")}\n";
        if (contributesToVictory)
        {
            info += $"\n[胜利条件] 摧毁此兵器计入胜利判定\n";
        }
        return info;
    }

    public override void OnTurnStart()
    {
        if (isDestroyed) return; // 已摧毁不再行动
        base.OnTurnStart();
    }

    public override void ShowAttackRange()
    {
        if (isDestroyed) return; // 已摧毁不显示范围
        base.ShowAttackRange();
    }

    public override bool CanAttack()
    {
        if (isDestroyed) return false;
        return base.CanAttack();
    }
}
