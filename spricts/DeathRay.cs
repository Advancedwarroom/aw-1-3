// DeathRay.cs - 死光炮（Death Ray）
// 还原原版AW2 Final Front的DeathRay机制：
// - 3×3多格占据，完全不可通行（摧毁后也不可通行）
// - 四方向发射（上/下/左/右），激光宽度3格
// - 弱点格子：Up/Down在下中(1,2)，Left/Right在发射方向侧中
// - 摧毁后切换为Broken形态，仍全部不可通行
// - 本质上 = 上下左右的Laser围了一圈不可通行的格子
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class DeathRay : BlackCannon
{
    [ExportGroup("死光炮配置")]
    [Export] public Vector2I multiTileSize = new Vector2I(3, 3);
    [Export] public bool canRotate = true;
    [Export] public int maxLaserLength = 99;
    [Export] public bool useInfiniteRange = true;
    [Export] public float fixedDamagePercent = 0.8f;  // 默认30%伤害（原版约8HP/10HP ≈ 80%，但这里用百分比）
    [Export] public bool CanDestroy = false;            // 原版不会杀死单位（至少留1HP）
    [Export] public bool useCooldownSystem = true;     // 原版每7天发射，可用冷却系统模拟
    [Export] public int cooldownTurns = 7; // 原版DeathRay每7天发射一次
    [Export] public int attacksPerCooldown = 1;
    [Export] public bool storeAttacks = true;
    [Export] public int laserBeamWidth = 3;             // 激光束宽度：3格

    // 炮口（发射台）偏移：激光发射的起点
    private Vector2I firePointOffset = new Vector2I(1, 0);
    // 弱点偏移：可被攻击扣血的格子
    private Vector2I weakPointOffset = new Vector2I(1, 2);

    // 运行时冷却状态（继承BlackCannon的冷却系统但独立配置）
    public new int turnsSinceLastAttack = 0;
    public new int attacksRemainingInCycle = 0;
    public new bool cooldownReady = true;
    public new int totalTurnsPassed = 0;

    public override void _Ready()
    {
        // 在base._Ready之前设置多格属性
        isMultiTile = true;
        size = multiTileSize;

        // 设置DeathRay特有的默认值（在base._Ready之前覆盖）
        maxAttackDepth = maxLaserLength;

        base._Ready();

        // 更新偏移（基于当前方向）
        UpdateOffsetsForDirection();

        // 初始化DeathRay特有的冷却状态
        InitializeDeathRayCooldownState();

        cost = 25000;  // DeathRay造价（比LargeCannon更贵）
    }

    // ========== 方向与偏移系统 ==========

    // 炮口位置：激光发射的起点
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
    // Up:    (1,2) - 下中（固定位置！和发射方向不一致！）
    // Down:  (1,2) - 下中（固定位置！）
    public Vector2I GetWeakPointOffsetForDirection(CannonDirection dir)
    {
        return dir switch
        {
            CannonDirection.Right => new Vector2I(2, 1),
            CannonDirection.Left  => new Vector2I(0, 1),
            CannonDirection.Up    => new Vector2I(1, 2),  // ⚠️ 上方向的弱点在下中！
            CannonDirection.Down  => new Vector2I(1, 2),  // 下方向的弱点也在下中
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
        if (!canRotate) return;
        base.RotateDirection();
        UpdateOffsetsForDirection();
        UpdateMultiTileVisual();
    }

    public override void UpdateDirectionVisual()
    {
        UpdateOffsetsForDirection();
        base.UpdateDirectionVisual();
        UpdateMultiTileVisual();
    }

    public override void UpdateMultiTileVisual()
    {
        // 大贴图位置调整：锚点是(0,0)，但贴图中心需要覆盖3×3区域
        // 3×3的中心是(1,1)，相对于锚点的偏移是(1,1)格 = 32,32像素
        if (animSprite != null)
        {
            animSprite.Position = new Vector2(
                16 + (multiTileSize.X - 1) * 16f,
                16 + (multiTileSize.Y - 1) * 16f
            );
        }
    }

    // ========== 炮口与弱点格子获取 ==========

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

    // ========== 攻击范围计算：3格宽激光束 ==========

    public override List<Grids> CalculateAttackRange()
    {
        var range = new List<Grids>();
        var firePointGrid = GetFirePointGrid();
        if (firePointGrid == null) return range;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return range;

        Vector2I dir = DirectionVectors[(int)direction];
        Vector2I start = firePointGrid.GridIndex;

        // 根据方向计算激光束覆盖的所有格子
        // 激光从firePoint开始，沿方向延伸，宽度为3格（垂直于方向）
        int maxSteps = useInfiniteRange ? 100 : maxLaserLength;

        for (int step = 1; step <= maxSteps; step++)
        {
            // 当前深度的中心点
            Vector2I center = start + dir * step;

            // 计算宽度为3的横向格子
            Vector2I perpDir;
            if (dir.X != 0) // 水平方向（Left/Right），垂直方向是上下
                perpDir = new Vector2I(0, 1);
            else // 垂直方向（Up/Down），垂直方向是左右
                perpDir = new Vector2I(1, 0);

            for (int w = -1; w <= 1; w++)  // 宽度3格：-1, 0, +1
            {
                Vector2I checkPos = center + perpDir * w;

                if (checkPos.X < 0 || checkPos.X >= gm.gridManager.searchRange.X ||
                    checkPos.Y < 0 || checkPos.Y >= gm.gridManager.searchRange.Y)
                    continue;

                var targetGrid = gm.gridManager.map[checkPos.X, checkPos.Y];
                if (targetGrid != null && targetGrid.gridType != GridType.METEORITE)
                {
                    if (!range.Contains(targetGrid))
                        range.Add(targetGrid);
                }
            }

            // 如果不是无限射程，检查是否已到达最大射程
            if (!useInfiniteRange && step >= maxLaserLength)
                break;
        }

        return range;
    }

    // ========== 攻击执行：3格宽激光 ==========

    public override void PerformAttack(Infantry target)
    {
        if (isDestroyed) return;
        if (!CanAttack()) return;

        var allTargets = GetAllTargetsInLaserBeam();

        foreach (var t in allTargets)
        {
            if (!IsInstanceValid(t)) continue;

            int damage = Mathf.RoundToInt(t.maxHealth * fixedDamagePercent);
            damage = Mathf.Max(1, damage);

            if (!CanDestroy)
                t.health = Mathf.Max(1, t.health - damage);
            else
                t.health -= damage;

            t.UpdateHpLabel();
            if (t.health <= 0 && CanDestroy)
            {
                var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
                gm?.RemovePiece(t);
                t.CallDeferred("QueueFree");
            }
        }

        // 播放激光特效
        PlayDeathRayEffect();

        // 消耗攻击次数
        remainingAttacks--;
        HandlePostAttack();
    }

    private List<Infantry> GetAllTargetsInLaserBeam()
    {
        var result = new List<Infantry>();
        var firePointGrid = GetFirePointGrid();
        if (firePointGrid == null) return result;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return result;

        Vector2I dir = DirectionVectors[(int)direction];
        Vector2I start = firePointGrid.GridIndex;

        Vector2I perpDir;
        if (dir.X != 0)
            perpDir = new Vector2I(0, 1);
        else
            perpDir = new Vector2I(1, 0);

        int maxSteps = useInfiniteRange ? 100 : maxLaserLength;

        for (int step = 1; step <= maxSteps; step++)
        {
            Vector2I center = start + dir * step;

            for (int w = -1; w <= 1; w++)
            {
                Vector2I checkPos = center + perpDir * w;

                if (checkPos.X < 0 || checkPos.X >= gm.gridManager.searchRange.X ||
                    checkPos.Y < 0 || checkPos.Y >= gm.gridManager.searchRange.Y)
                    continue;

                var targetGrid = gm.gridManager.map[checkPos.X, checkPos.Y];
                if (targetGrid == null) continue;

                // 获取该格子中的所有敌方单位（原版DeathRay只打非Black Hole单位）
                var enemies = targetGrid.infantries
                    .Where(i => IsInstanceValid(i) && i.team != team)
                    .ToList();

                foreach (var enemy in enemies)
                {
                    if (!result.Contains(enemy))
                        result.Add(enemy);
                }
            }
        }

        return result;
    }

    // ========== 特效 ==========

    private void PlayDeathRayEffect()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        var firePointGrid = GetFirePointGrid();
        if (firePointGrid == null) return;

        Vector2I dir = DirectionVectors[(int)direction];
        Vector2I startIdx = firePointGrid.GridIndex;

        // 计算激光束的视觉端点（找到最后一个有效的格子）
        Vector2I endIdx = startIdx;
        int maxSteps = useInfiniteRange ? 100 : maxLaserLength;

        for (int step = 1; step <= maxSteps; step++)
        {
            Vector2I center = startIdx + dir * step;
            bool anyValid = false;

            Vector2I perpDir = dir.X != 0 ? new Vector2I(0, 1) : new Vector2I(1, 0);
            for (int w = -1; w <= 1; w++)
            {
                Vector2I checkPos = center + perpDir * w;
                if (checkPos.X >= 0 && checkPos.X < gm.gridManager.searchRange.X &&
                    checkPos.Y >= 0 && checkPos.Y < gm.gridManager.searchRange.Y)
                {
                    anyValid = true;
                    endIdx = checkPos;
                }
            }

            if (!anyValid) break;
        }

        // 创建3条平行的激光线
        Vector2I perp = dir.X != 0 ? new Vector2I(0, 1) : new Vector2I(1, 0);
        Color laserColor = new Color(0.9f, 0.1f, 0.9f, 0.9f); // 紫色激光

        for (int w = -1; w <= 1; w++)
        {
            Vector2I beamStartIdx = startIdx + perp * w;
            Vector2I beamEndIdx = endIdx;

            if (beamStartIdx.X >= 0 && beamStartIdx.X < gm.gridManager.searchRange.X &&
                beamStartIdx.Y >= 0 && beamStartIdx.Y < gm.gridManager.searchRange.Y)
            {
                var startGrid = gm.gridManager.map[beamStartIdx.X, beamStartIdx.Y];
                var endGrid = gm.gridManager.map[beamEndIdx.X, beamEndIdx.Y];
                if (startGrid != null && endGrid != null)
                {
                    var laserLine = new LaserLineEffect();
                    laserLine.Setup(
                        startGrid.Position + new Vector2(16, 16),
                        endGrid.Position + new Vector2(16, 16),
                        laserColor
                    );
                    GetTree()?.CurrentScene?.AddChild(laserLine);
                }
            }
        }

        // 屏幕震动
        var cam = GetViewport()?.GetCamera2D();
        if (cam != null)
        {
            var tween = cam.CreateTween().SetParallel(false);
            for (int i = 0; i < 5; i++)
            {
                var rnd = new Vector2(GD.Randf() * 8f - 4f, GD.Randf() * 8f - 4f);
                tween.TweenProperty(cam, "position", cam.Position + rnd, 0.05f);
            }
            tween.TweenProperty(cam, "position", cam.Position, 0.05f);
        }
    }

    // ========== 冷却系统（DeathRay特有）==========

    private void InitializeDeathRayCooldownState()
    {
        hasActed = false;
        remainingAttacks = maxAttacksPerTurn;

        if (useCooldownSystem)
        {
            turnsSinceLastAttack = 0;
            cooldownReady = true;
            attacksRemainingInCycle = attacksPerCooldown;
            totalTurnsPassed = 0;
        }
        else
        {
            turnsSinceLastAttack = 0;
            cooldownReady = true;
            attacksRemainingInCycle = maxAttacksPerTurn;
        }
    }

    public override void OnTurnStart()
    {
        if (isDestroyed) return;

        base.OnTurnStart();

        if (useCooldownSystem)
        {
            totalTurnsPassed++;
            turnsSinceLastAttack++;

            // 检查是否完成一个冷却周期
            if (turnsSinceLastAttack >= cooldownTurns)
            {
                cooldownReady = true;
                attacksRemainingInCycle = attacksPerCooldown;
                turnsSinceLastAttack = 0;
            }

            // 同步remainingAttacks
            remainingAttacks = attacksRemainingInCycle;
        }
    }

    public override bool CanAttack()
    {
        if (isDestroyed) return false;

        if (useCooldownSystem)
        {
            if (!cooldownReady || attacksRemainingInCycle <= 0)
                return false;
        }

        return !hasActed && remainingAttacks > 0;
    }

    // ========== 伤害与摧毁 ==========

    public override void TakeDamage(int damage)
    {
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

        // 恢复所有占据格子的weapon引用，确保摧毁后仍不可通行
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

            // 恢复视觉：从透明变回正常显示Broken动画
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
        return $"DeathRay{dirSuffix}";
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

    // ========== 信息面板 ==========

    public override string GetBlackCannonFullInfo()
    {
        string info = base.GetBlackCannonFullInfo();
        info = info.Replace("[黑炮]", "[死光炮]");
        info += $"\n=== 死光炮特性 ===\n";
        info += $"尺寸: {size.X}×{size.Y}\n";
        info += $"激光方向: {direction}\n";
        info += $"弱点位置: {direction} ({GetWeakPointOffsetForDirection(direction)})\n";
        info += $"激光宽度: {laserBeamWidth}格\n";
        info += $"射程: {(useInfiniteRange ? "无限" : maxLaserLength.ToString())}\n";
        info += $"可旋转: {(canRotate ? "是" : "否")}\n";
        info += $"状态: {(isDestroyed ? "已摧毁" : "运作中")}\n";
        if (contributesToVictory)
        {
            info += $"\n[胜利条件] 摧毁此兵器计入胜利判定\n";
        }
        return info;
    }

    // ========== 攻击范围显示覆盖 ==========

    public override void ShowAttackRange()
    {
        if (isDestroyed) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        if (!CanAttack())
        {
            ShowAttackRangeAsDisabled(gm);
            return;
        }

        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        var rangeGrids = CalculateAttackRange();
        var fog = gm.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        foreach (var g in rangeGrids)
        {
            if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) continue;

            bool hasValidTarget = g.infantries.Any(i => IsInstanceValid(i) && i.team != team);

            if (hasValidTarget)
            {
                g.attackRangeIcon?.Show();
                if (g.attackRangeIcon != null)
                    g.attackRangeIcon.Modulate = new Color(0.9f, 0.1f, 0.9f, 0.9f); // 紫色激光

                g.OnClickGrid = (to) => {
                    if (!IsInstanceValid(this)) {
                        gm.gridManager.ClearWeaponRange();
                        Grids.IsForceActionMode = false;
                        return;
                    }
                    // 执行激光攻击（攻击激光束中的所有目标）
                    PerformAttack(null);
                };
                gm.gridManager.OverrideUnitInput(g, true);
                g.IsInWeaponRange = true;
            }
            else
            {
                g.pathIcon?.Show();
                if (g.pathIcon != null)
                    g.pathIcon.Modulate = new Color(0.5f, 0.2f, 0.5f, 0.3f); // 淡紫色

                g.OnClickGrid = (to) => {
                    if (!IsInstanceValid(this)) {
                        gm.gridManager.ClearWeaponRange();
                        Grids.IsForceActionMode = false;
                        return;
                    }
                    gm.gridManager.ClearWeaponRange();
                    Grids.IsForceActionMode = false;
                    if (!hasActed)
                    {
                        var menu = GetTree()?.GetFirstNodeInGroup("action_menu") as ActionMenu;
                        menu?.ShowWeaponMenu(this);
                    }
                };
                gm.gridManager.OverrideUnitInput(g, true);
                g.IsInWeaponRange = true;
            }
        }

        // 范围外点击
        foreach (var g in gm.gridManager.grids)
        {
            if (!rangeGrids.Contains(g))
            {
                g.OnClickEmpty = () => {
                    if (!IsInstanceValid(this)) {
                        gm.gridManager.ClearWeaponRange();
                        return;
                    }
                    gm.gridManager.ClearWeaponRange();
                    Grids.IsForceActionMode = false;
                    if (!hasActed)
                    {
                        var menu = GetTree()?.GetFirstNodeInGroup("action_menu") as ActionMenu;
                        menu?.ShowWeaponMenu(this);
                    }
                };
            }
        }
    }

    private void ShowAttackRangeAsDisabled(GameManager gm)
    {
        gm.gridManager.ClearWeaponRange();
        var rangeGrids = CalculateAttackRange();
        var fog = gm.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        foreach (var g in rangeGrids)
        {
            if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) continue;
            g.pathIcon?.Show();
            if (g.pathIcon != null)
                g.pathIcon.Modulate = new Color(0.3f, 0.1f, 0.3f, 0.3f);
            g.IsInWeaponRange = true;
        }

        foreach (var g in gm.gridManager.grids)
        {
            if (!rangeGrids.Contains(g))
            {
                g.OnClickEmpty = () => {
                    if (!IsInstanceValid(this)) {
                        gm.gridManager.ClearWeaponRange();
                        return;
                    }
                    gm.gridManager.ClearWeaponRange();
                    var menu = GetTree()?.GetFirstNodeInGroup("action_menu") as ActionMenu;
                    menu?.ShowWeaponMenu(this);
                };
            }
        }
    }
}
