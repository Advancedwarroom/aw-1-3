// Laser.cs - 高级激光炮系统（修复版）
// 修复内容：
// 1. 多次攻击同步 remainingAttacks 和 attacksRemainingInCycle
// 2. 添加局内角度配置API（EnableAngle/DisableAngle/SetFireAngles等）
// 3. ✅ 移除 attackedThisRound 限制：允许同一回合多次攻击同一目标
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum LaserTargetMode { AllSelect, OnlyEnemyUnits, OnlyUserUnits }

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
    [Export] public bool canAttackWeapons = false;

    // ========== ✅ 弹药系统已在 Weapon 基类中定义（useAmmoSystem, currentAmmo, maxAmmo）

    [ExportGroup("弹药系统")]
    [Export] public bool useAmmoSystem = false;
    [Export] public int maxAmmo = 3;
    [Export] public int currentAmmo = 3;

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
        cost = 19000;  // Laser造价
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

    // ========== ✅ 局内角度配置API ==========

    public void EnableAngle(float angle)
    {
        float normalized = NormalizeAngle(angle);
        if (!enabledAngles.Contains(normalized))
        {
            enabledAngles.Add(normalized);
            enabledAngles.Sort();
            angleConfig[normalized] = true;
        }
    }

    public void DisableAngle(float angle)
    {
        float normalized = NormalizeAngle(angle);
        if (enabledAngles.Contains(normalized))
        {
            enabledAngles.Remove(normalized);
            if (currentFireAngles.Contains(normalized))
            {
                currentFireAngles.Remove(normalized);
                if (currentFireAngles.Count == 0 && enabledAngles.Count > 0)
                    currentFireAngles.Add(enabledAngles[0]);
            }
            angleConfig[normalized] = false;
            rayCache.Clear();
        }
    }

    public void SetFireAngles(List<float> angles)
    {
        currentFireAngles.Clear();
        foreach (var angle in angles)
        {
            float normalized = NormalizeAngle(angle);
            if (enabledAngles.Contains(normalized) && !currentFireAngles.Contains(normalized))
                currentFireAngles.Add(normalized);
        }
        if (currentFireAngles.Count == 0 && enabledAngles.Count > 0)
            currentFireAngles.Add(enabledAngles[0]);
        currentFireAngles.Sort((a, b) => enabledAngles.IndexOf(a).CompareTo(enabledAngles.IndexOf(b)));
        initialFireAngles.Clear();
        foreach (var angle in currentFireAngles)
            initialFireAngles[angle] = true;
        rayCache.Clear();
        UpdateLaserVisual();
    }

    public void AddFireAngle(float angle)
    {
        float normalized = NormalizeAngle(angle);
        if (enabledAngles.Contains(normalized) && !currentFireAngles.Contains(normalized))
        {
            currentFireAngles.Add(normalized);
            currentFireAngles.Sort((a, b) => enabledAngles.IndexOf(a).CompareTo(enabledAngles.IndexOf(b)));
            initialFireAngles[normalized] = true;
            rayCache.Clear();
            UpdateLaserVisual();
        }
    }

    public void RemoveFireAngle(float angle)
    {
        float normalized = NormalizeAngle(angle);
        if (currentFireAngles.Contains(normalized))
        {
            currentFireAngles.Remove(normalized);
            initialFireAngles[normalized] = false;
            rayCache.Clear();
            UpdateLaserVisual();
        }
    }

    public List<float> GetEnabledAngles() => new List<float>(enabledAngles);
    public List<float> GetCurrentFireAngles() => new List<float>(currentFireAngles);

    // ========== 角度系统 ==========

    public void InitializeAngleSystem()
    {
        enabledAngles.Clear();
        currentFireAngles.Clear();

        foreach (var kvp in angleConfig)
        {
            if (kvp.Value)
            {
                float normalizedAngle = NormalizeAngle(kvp.Key);
                if (!enabledAngles.Contains(normalizedAngle))
                    enabledAngles.Add(normalizedAngle);
            }
        }

        if (enabledAngles.Count == 0)
        {
            enabledAngles.Add(0f);
            enabledAngles.Add(90f);
            enabledAngles.Add(180f);
            enabledAngles.Add(270f);
        }

        enabledAngles.Sort();

        foreach (var kvp in initialFireAngles)
        {
            if (kvp.Value)
            {
                float normalizedAngle = NormalizeAngle(kvp.Key);
                if (enabledAngles.Contains(normalizedAngle) && !currentFireAngles.Contains(normalizedAngle))
                    currentFireAngles.Add(normalizedAngle);
            }
        }

        if (currentFireAngles.Count == 0 && enabledAngles.Count > 0)
            currentFireAngles.Add(enabledAngles[0]);

        currentFireAngles.Sort();
    }

    private float NormalizeAngle(float angle)
    {
        while (angle < 0) angle += 360;
        while (angle >= 360) angle -= 360;
        return angle;
    }

    public void RotateLaser(bool clockwise)
    {
        if (!canRotate || enabledAngles.Count == 0) return;

        var newFireAngles = new List<float>();
        foreach (float currentAngle in currentFireAngles)
        {
            int currentIndex = enabledAngles.IndexOf(currentAngle);
            if (currentIndex < 0) continue;
            int newIndex = clockwise
                ? (currentIndex + 1) % enabledAngles.Count
                : (currentIndex - 1 + enabledAngles.Count) % enabledAngles.Count;
            float newAngle = enabledAngles[newIndex];
            if (!newFireAngles.Contains(newAngle))
                newFireAngles.Add(newAngle);
        }
        newFireAngles.Sort((a, b) => enabledAngles.IndexOf(a).CompareTo(enabledAngles.IndexOf(b)));
        currentFireAngles = newFireAngles;
        rayCache.Clear();
        UpdateLaserVisual();
    }

    // ========== 射线计算 ==========

    public List<RayGridInfo> CalculateRayGrids(float angleDegrees)
    {
        if (rayCache.ContainsKey(angleDegrees))
            return new List<RayGridInfo>(rayCache[angleDegrees]);

        var result = new List<RayGridInfo>();
        if (grid == null) return result;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return result;

        float angleRad = Mathf.DegToRad(angleDegrees);
        Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        float cellSize = gm.gridManager.gridSize.X;
        Vector2 startPos = grid.Position + new Vector2(cellSize * 0.5f, cellSize * 0.5f);
        int mapWidth = gm.gridManager.searchRange.X;
        int mapHeight = gm.gridManager.searchRange.Y;
        int maxSteps = useInfiniteRange ? 1000 : maxLaserLength;

        Vector2 currentPos = startPos;
        int currentX = grid.GridIndex.X;
        int currentY = grid.GridIndex.Y;
        int stepX = dir.X > 0 ? 1 : (dir.X < 0 ? -1 : 0);
        int stepY = dir.Y > 0 ? 1 : (dir.Y < 0 ? -1 : 0);

        HashSet<Grids> visited = new HashSet<Grids>();
        float totalDist = 0f;

        for (int step = 0; step < maxSteps * 2; step++)
        {
            float x0 = gm.gridManager.startPos.X + currentX * cellSize;
            float x1 = x0 + cellSize;
            float y0 = gm.gridManager.startPos.Y + currentY * cellSize;
            float y1 = y0 + cellSize;

            float tX = float.MaxValue, tY = float.MaxValue;
            if (stepX > 0) tX = (x1 - currentPos.X) / dir.X;
            else if (stepX < 0) tX = (x0 - currentPos.X) / dir.X;
            if (stepY > 0) tY = (y1 - currentPos.Y) / dir.Y;
            else if (stepY < 0) tY = (y0 - currentPos.Y) / dir.Y;

            float tStep = Mathf.Min(tX, tY);
            if (tStep < 0.0001f) tStep = 0.0001f;

            var targetGrid = gm.gridManager.map[currentX, currentY];
            if (targetGrid != null && !visited.Contains(targetGrid))
            {
                float segmentLength = tStep * dir.Length();
                if (segmentLength > cellSize * 0.001f)
                {
                    visited.Add(targetGrid);
                    float diagonal = cellSize * Mathf.Sqrt(2);
                    float contactRatio = Mathf.Clamp(segmentLength / diagonal, 0f, 1f);
                    bool isBlocked = targetGrid.gridType == GridType.METEORITE;
                    result.Add(new RayGridInfo
                    {
                        Grid = targetGrid,
                        ContactRatio = contactRatio,
                        EntryT = totalDist,
                        ExitT = totalDist + segmentLength,
                        IsBlocked = isBlocked
                    });
                    if (isBlocked)
                    {
                        rayCache[angleDegrees] = result;
                        return result;
                    }
                }
            }

            currentPos += dir * tStep;
            totalDist += tStep * dir.Length();
            bool xExit = Mathf.Abs(tX - tStep) < 0.0001f;
            bool yExit = Mathf.Abs(tY - tStep) < 0.0001f;
            if (xExit && yExit) { currentX += stepX; currentY += stepY; }
            else if (xExit) currentX += stepX;
            else if (yExit) currentY += stepY;

            if (currentX < 0 || currentX >= mapWidth || currentY < 0 || currentY >= mapHeight)
                break;

            if (!useInfiniteRange)
            {
                int manhattanDist = Mathf.Abs(currentX - grid.GridIndex.X) + Mathf.Abs(currentY - grid.GridIndex.Y);
                if (manhattanDist > maxLaserLength) break;
            }
        }

        rayCache[angleDegrees] = result;
        return result;
    }

    private float CalculateBaseDamagePercent(float contactRatio)
    {
        if (useContactAreaDamage)
            return minContactDamagePercent + (maxContactDamagePercent - minContactDamagePercent) * contactRatio;
        return fixedDamagePercent;
    }

    public List<LaserTargetInfo> GetAllValidTargets()
    {
        var allTargets = new List<LaserTargetInfo>();
        foreach (float angle in currentFireAngles)
        {
            var rayGrids = CalculateRayGrids(angle);
            int pierceCount = 0;
            foreach (var rayInfo in rayGrids)
            {
                var g = rayInfo.Grid;
                if (maxPiercePerLine > 0 && pierceCount >= maxPiercePerLine) break;
                var targetsInGrid = GetValidTargetsInGrid(g);
                foreach (var target in targetsInGrid)
                {
                    int distance = CalculateGridDistance(grid.GridIndex, g.GridIndex);
                    float baseDamagePercent = CalculateBaseDamagePercent(rayInfo.ContactRatio);
                    allTargets.Add(new LaserTargetInfo
                    {
                        Target = target,
                        Grid = g,
                        Angle = angle,
                        Distance = distance,
                        PierceIndex = pierceCount,
                        ContactRatio = rayInfo.ContactRatio,
                        BaseDamagePercent = baseDamagePercent
                    });
                    pierceCount++;
                }
            }
        }
        return allTargets;
    }

    private List<Node2D> GetValidTargetsInGrid(Grids g)
    {
        var targets = new List<Node2D>();

        // P0 兵器：无差别攻击，除了自己谁都能打，跳过友军/敌军判定
        if (team == TeamHelper.Player0)
        {
            targets.AddRange(g.infantries.Where(i => IsInstanceValid(i)).Cast<Node2D>());
            if (canAttackWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon != this)
                targets.Add(g.weapon);
            return targets;
        }

        switch (targetSelectionMode)
        {
            case LaserTargetMode.AllSelect:
                targets.AddRange(g.infantries.Where(i => IsInstanceValid(i)).Cast<Node2D>());
                if (canAttackWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon != this)
                    targets.Add(g.weapon);
                break;
            case LaserTargetMode.OnlyEnemyUnits:
                targets.AddRange(g.infantries.Where(i => IsInstanceValid(i) && i.team != team).Cast<Node2D>());
                if (canAttackWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon.team != team && g.weapon != this)
                    targets.Add(g.weapon);
                break;
            case LaserTargetMode.OnlyUserUnits:
                targets.AddRange(g.infantries.Where(i => IsInstanceValid(i) && i.team == team).Cast<Node2D>());
                if (canAttackWeapons && g.weapon != null && IsInstanceValid(g.weapon) && g.weapon.team == team && g.weapon != this)
                    targets.Add(g.weapon);
                break;
        }
        return targets;
    }

    // ========== 攻击范围显示 ==========

    public override void ShowAttackRange()
    {
        if (enableAutoAI && hasActed) return;

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return;

        if (!HasAmmoAndCooldown())
        {
            ShowAttackRangeAsDisabled(gm);
            return;
        }

        gm.gridManager.ClearWeaponRange();
        Grids.IsForceActionMode = true;

        var allRayGrids = new HashSet<Grids>();
        var targetableGrids = new HashSet<Grids>();
        var fog = gm.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        foreach (float angle in currentFireAngles)
        {
            var rayGrids = CalculateRayGrids(angle);
            int pierceCount = 0;
            foreach (var rayInfo in rayGrids)
            {
                var g = rayInfo.Grid;
                if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) continue;
                allRayGrids.Add(g);
                if (maxPiercePerLine > 0 && pierceCount >= maxPiercePerLine) break;
                var targets = GetValidTargetsInGrid(g);
                if (targets.Count > 0)
                {
                    targetableGrids.Add(g);
                    pierceCount += targets.Count;
                }
            }
        }

        foreach (var g in allRayGrids)
        {
            g.pathIcon?.Show();
            var gridTargets = GetValidTargetsInGrid(g);
            // 不再限制同一回合重复攻击同一目标
            var hasTargets = gridTargets.Count > 0;

            if (targetableGrids.Contains(g) && hasTargets)
                g.pathIcon.Modulate = new Color(1, 0.2f, 0.2f, 0.7f);
            else if (targetableGrids.Contains(g) && !hasTargets)
                g.pathIcon.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            else
                g.pathIcon.Modulate = new Color(0.8f, 0.3f, 0.3f, 0.3f);

            g.IsInWeaponRange = true;
        }

        foreach (var g in allRayGrids)
        {
            g.OnClickGrid = to => ExecuteGlobalAttack();
            gm.gridManager.OverrideUnitInput(g, true);
        }

        foreach (var g in gm.gridManager.grids)
        {
            if (!allRayGrids.Contains(g))
            {
                g.OnClickEmpty = () => {
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

    // ========== ✅ 核心修复：多次攻击逻辑 ==========

    private void ExecuteGlobalAttack()
    {
        if (!IsInstanceValid(this) || remainingAttacks <= 0) return;
        if (!HasAmmoAndCooldown()) return;

        var allTargets = GetAllValidTargets();
        // 不再限制同一回合重复攻击同一目标
        var newTargets = allTargets;

        if (newTargets.Count == 0) return;

        if (!ConsumeAmmo()) return;

        // ✅ 修复：统一递减逻辑
        if (useCooldownSystem)
        {
            attacksRemainingInCycle--;
            // 同步 remainingAttacks，保持两者一致
            remainingAttacks = attacksRemainingInCycle;
        }
        else
        {
            remainingAttacks--;
        }

        foreach (var targetInfo in newTargets)
        {
            if (targetInfo.Target is Infantry infantry)
                ApplyDamageToTarget(infantry, targetInfo.BaseDamagePercent);
            else if (targetInfo.Target is Weapon weapon)
                ApplyDamageToWeapon(weapon, targetInfo.BaseDamagePercent);
        }

        PlayGlobalLaserEffect(newTargets);

        // ✅ 修复：根据正确的剩余次数判断
        int actualRemaining = useCooldownSystem ? attacksRemainingInCycle : remainingAttacks;

        if (actualRemaining > 0)
        {
            // 攻击后刷新范围显示，允许继续攻击（包括已攻击过的目标）
            var timer = GetTree().CreateTimer(0.3f);
            timer.Timeout += () => {
                if (IsInstanceValid(this) && !hasActed)
                    ShowAttackRange();
            };
        }
        else
        {
            HandlePostAttack();
        }
    }

    // ========== 伤害应用 ==========

    private void ApplyDamageToTarget(Infantry target, float baseDamagePercent)
    {
        if (!IsInstanceValid(target)) return;
        if (useModifiedDamage)
            ApplyModifiedDamage(target, baseDamagePercent);
        else
            ApplyTraditionalDamage(target, baseDamagePercent);
    }

    private void ApplyTraditionalDamage(Infantry target, float baseDamagePercent)
    {
        int damage = Mathf.RoundToInt(target.maxHealth * baseDamagePercent);
        damage = Mathf.Max(1, damage);
        if (!CanDestroy)
            target.health = Mathf.Max(1, target.health - damage);
        else
            target.health -= damage;
        target.UpdateHpLabel();
        if (target.health <= 0)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.RemovePiece(target);
            target.CallDeferred("QueueFree");
        }
    }

    private void ApplyModifiedDamage(Infantry target, float baseDamagePercent)
    {
        int healthChange = Mathf.RoundToInt(target.maxHealth * Mathf.Abs(baseDamagePercent));
        if (modifiedHealthPercent > 0)
        {
            if (!CanDestroy)
                target.health = Mathf.Max(1, target.health - healthChange);
            else
                target.health -= healthChange;
        }
        else if (modifiedHealthPercent < 0)
        {
            if (canOverMaxHp)
                target.health += healthChange;
            else
                target.health = Mathf.Min(target.maxHealth, target.health + healthChange);
        }

        if (modifiedAmmoPercent != 0 && target.hasPrimaryWeapon && target.primaryHasLimitedAmmo)
        {
            float ammoEffectRatio = baseDamagePercent / fixedDamagePercent;
            int ammoChange = Mathf.RoundToInt(target.maxPrimaryAmmo * Mathf.Abs(modifiedAmmoPercent) * ammoEffectRatio);
            if (modifiedAmmoPercent > 0)
                target.currentPrimaryAmmo = Mathf.Max(0, target.currentPrimaryAmmo - ammoChange);
            else
                target.currentPrimaryAmmo = Mathf.Min(target.maxPrimaryAmmo, target.currentPrimaryAmmo + ammoChange);
        }

        if (modifiedFuelPercent != 0 && target.consumeFuel)
        {
            float fuelEffectRatio = baseDamagePercent / fixedDamagePercent;
            int fuelChange = Mathf.RoundToInt(target.maxFuel * Mathf.Abs(modifiedFuelPercent) * fuelEffectRatio);
            if (modifiedFuelPercent > 0)
                target.fuel = Mathf.Max(0, target.fuel - fuelChange);
            else
                target.fuel = Mathf.Min(target.maxFuel, target.fuel + fuelChange);
        }

        target.UpdateHpLabel();
        if (target.health <= 0 && CanDestroy)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.RemovePiece(target);
            target.CallDeferred("QueueFree");
        }
    }

    private void ApplyDamageToWeapon(Weapon target, float baseDamagePercent)
    {
        if (!IsInstanceValid(target)) return;
        int damage = Mathf.RoundToInt(target.maxHealth * baseDamagePercent);
        damage = Mathf.Max(1, damage);
        if (!CanDestroy)
            target.health = Mathf.Max(1, target.health - damage);
        else
            target.health -= damage;
        target.UpdateHpLabel();
        if (target.health <= 0)
            target.CallDeferred("QueueFree");
    }

    // ========== 激光特效 ==========

    private Vector2 CalculateLaserEffectEndPoint(float angleDegrees, List<LaserTargetInfo> angleTargets)
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager == null) return grid.Position + new Vector2(16, 16);

        float angleRad = Mathf.DegToRad(angleDegrees);
        Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        Vector2 startPos = grid.Position + new Vector2(16, 16);

        if (maxPiercePerLine == 0 && useInfiniteRange)
        {
            var allRayGrids = CalculateRayGrids(angleDegrees);
            if (angleTargets.Count > 0)
            {
                var lastTarget = angleTargets.OrderByDescending(t => t.Distance).First();
                return ExtendToBoundary(lastTarget.Grid.Position + new Vector2(16, 16), dir, gm);
            }
            if (allRayGrids.Count > 0)
                return ExtendToBoundary(allRayGrids.Last().Grid.Position + new Vector2(16, 16), dir, gm);
            return startPos + dir * 1000f;
        }

        if (maxPiercePerLine > 0 && useInfiniteRange)
        {
            var allRayGrids = CalculateRayGrids(angleDegrees);
            int pierceCount = 0;
            Grids lastAttackableGrid = null;
            foreach (var rayInfo in allRayGrids)
            {
                if (pierceCount >= maxPiercePerLine) break;
                var targets = GetValidTargetsInGrid(rayInfo.Grid);
                if (targets.Count > 0)
                {
                    lastAttackableGrid = rayInfo.Grid;
                    pierceCount += targets.Count;
                }
            }
            if (lastAttackableGrid != null)
                return lastAttackableGrid.Position + new Vector2(16, 16);
            if (allRayGrids.Count > 0)
                return ExtendToBoundary(allRayGrids.Last().Grid.Position + new Vector2(16, 16), dir, gm);
            return startPos + dir * 1000f;
        }

        if (!useInfiniteRange)
        {
            Vector2 endPos = startPos + dir * maxLaserLength * gm.gridManager.gridSize.X;
            return ExtendToBoundary(endPos, dir, gm);
        }

        return startPos + dir * 500f;
    }

    private Vector2 ExtendToBoundary(Vector2 fromPos, Vector2 dir, GameManager gm)
    {
        float cellSize = gm.gridManager.gridSize.X;
        int mapWidth = gm.gridManager.searchRange.X;
        int mapHeight = gm.gridManager.searchRange.Y;
        float maxDistX = float.MaxValue, maxDistY = float.MaxValue;
        if (dir.X > 0.001f) maxDistX = ((gm.gridManager.startPos.X + mapWidth * cellSize) - fromPos.X) / dir.X;
        else if (dir.X < -0.001f) maxDistX = (gm.gridManager.startPos.X - fromPos.X) / dir.X;
        if (dir.Y > 0.001f) maxDistY = ((gm.gridManager.startPos.Y + mapHeight * cellSize) - fromPos.Y) / dir.Y;
        else if (dir.Y < -0.001f) maxDistY = (gm.gridManager.startPos.Y - fromPos.Y) / dir.Y;
        float maxDist = Mathf.Min(maxDistX, maxDistY);
        if (maxDist < 0 || maxDist > 10000f) maxDist = 1000f;
        return fromPos + dir * (maxDist + cellSize * 2);
    }

    private void PlayGlobalLaserEffect(List<LaserTargetInfo> targets)
    {
        var cam = GetViewport()?.GetCamera2D();
        if (cam != null)
        {
            var tween = cam.CreateTween().SetParallel(false);
            for (int i = 0; i < 3; i++)
            {
                var rnd = new Vector2(GD.Randf() * 6f - 3f, GD.Randf() * 6f - 3f);
                tween.TweenProperty(cam, "position", cam.Position + rnd, 0.05f);
            }
            tween.TweenProperty(cam, "position", cam.Position, 0.05f);
        }

        var angleGroups = targets.GroupBy(t => t.Angle);
        foreach (var group in angleGroups)
        {
            float angle = group.Key;
            var angleTargets = group.ToList();
            if (angleTargets.Count == 0) continue;
            Vector2 endPoint = CalculateLaserEffectEndPoint(angle, angleTargets);
            var laserLine = new LaserLineEffect();
            laserLine.Setup(grid.Position + new Vector2(16, 16), endPoint, GetTeamColor());
            GetTree()?.CurrentScene?.AddChild(laserLine);
            foreach (var t in angleTargets)
            {
                var hit = new LaserHitEffect();
                hit.Position = t.Target.GlobalPosition;
                GetTree()?.CurrentScene?.AddChild(hit);
            }
        }
    }

    private Color GetTeamColor()
    {
        return team == "Player2" ? new Color(0.2f, 0.4f, 1f) : new Color(1f, 0.2f, 0.2f);
    }

    // ========== AI自动操作 ==========

    public void ExecuteAIAutoAttack()
    {
        if (!enableAutoAI || hasActed || isAIExecuting) return;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || !gm.IsTurnPhaseValid(team)) return;
        if (!CanAttack()) { MarkAIActed(); return; }

        isAIExecuting = true;
        aiCurrentRotationAttempts = 0;
        aiMarkedActed = false;
        AIAttackSequence();
    }

    // ✅ P0/P-1 兵器强制 AI：绕过 phase 和 enableAutoAI 检查
    public override void ExecuteAI()
    {
        if (hasActed || isAIExecuting) return;
        if (!CanAttack()) { MarkAIActed(); return; }
        isAIExecuting = true;
        aiCurrentRotationAttempts = 0;
        aiMarkedActed = false;
        AIAttackSequence();
    }

    private void AIAttackSequence()
    {
        if (!IsInstanceValid(this) || hasActed || aiMarkedActed)
        { isAIExecuting = false; return; }

        if (!CanAttack())
        { MarkAIActed(); isAIExecuting = false; return; }

        var allTargets = GetAllValidTargets();
        var validTargets = allTargets.Where(t => IsInstanceValid(t.Target)).ToList();

        if (validTargets.Count == 0)
        {
            if (aiAutoRotate && aiCurrentRotationAttempts < aiMaxRotationAttempts)
            {
                aiCurrentRotationAttempts++;
                var bestAngle = AIFindBestAngle();
                if (bestAngle.HasValue && !currentFireAngles.Contains(bestAngle.Value))
                {
                    AIRotateToAngle(bestAngle.Value);
                    var timer = GetTree().CreateTimer(0.2f);
                    timer.Timeout += () => AIAttackSequence();
                    return;
                }
            }
            MarkAIActed();
            isAIExecuting = false;
            return;
        }

        AIExecuteGlobalAttack(validTargets);

        if (aiUseAllAttacks && CanAttack())
        {
            var timer = GetTree().CreateTimer(0.4f);
            timer.Timeout += () => AIAttackSequence();
        }
        else
        { MarkAIActed(); isAIExecuting = false; }
    }

    private float? AIFindBestAngle()
    {
        if (enabledAngles.Count == 0) return null;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager?.map == null) return null;

        var originalAngles = new List<float>(currentFireAngles);
        float? bestAngle = null;
        int bestTargetCount = 0;

        foreach (var angle in enabledAngles)
        {
            currentFireAngles = new List<float> { angle };
            rayCache.Remove(angle);
            var targets = GetAllValidTargets().Where(t => IsInstanceValid(t.Target)).ToList();
            if (targets.Count > bestTargetCount)
            {
                bestAngle = angle;
                bestTargetCount = targets.Count;
            }
        }
        currentFireAngles = originalAngles;
        return bestAngle;
    }

    private void AIRotateToAngle(float targetAngle)
    {
        if (currentFireAngles.Contains(targetAngle)) return;
        if (enabledAngles.Count == 0) return;
        float currentAngle = currentFireAngles.Count > 0 ? currentFireAngles[0] : enabledAngles[0];
        int currentIdx = enabledAngles.IndexOf(currentAngle);
        int targetIdx = enabledAngles.IndexOf(targetAngle);
        if (currentIdx < 0 || targetIdx < 0) return;
        int count = enabledAngles.Count;
        int diff = targetIdx - currentIdx;
        if (diff > count / 2) diff -= count;
        if (diff < -count / 2) diff += count;
        bool clockwise = diff > 0;
        int steps = Math.Abs(diff);
        for (int i = 0; i < steps; i++) RotateLaser(clockwise);
    }

    private void AIExecuteGlobalAttack(List<LaserTargetInfo> targets)
    {
        if (!IsInstanceValid(this)) return;
        if (useAmmoSystem)
        {
            if (currentAmmo <= 0) return;
            currentAmmo--;
            UpdateAmmoVisual();
        }
        if (useCooldownSystem)
        {
            attacksRemainingInCycle--;
            remainingAttacks = attacksRemainingInCycle;
        }
        else
        {
            remainingAttacks--;
        }

        foreach (var t in targets)
        {
            if (t.Target is Infantry infantry)
                ApplyDamageToTarget(infantry, t.BaseDamagePercent);
            else if (t.Target is Weapon weapon)
                ApplyDamageToWeapon(weapon, t.BaseDamagePercent);
        }

        if (aiShowEffects)
        {
            var cam = GetViewport()?.GetCamera2D();
            if (cam != null)
            {
                var tween = cam.CreateTween().SetParallel(false);
                for (int i = 0; i < 3; i++)
                {
                    var rnd = new Vector2(GD.Randf() * 6f - 3f, GD.Randf() * 6f - 3f);
                    tween.TweenProperty(cam, "position", cam.Position + rnd, 0.05f);
                }
                tween.TweenProperty(cam, "position", cam.Position, 0.05f);
            }
            foreach (var angleGroup in targets.GroupBy(t => t.Angle))
            {
                float angle = angleGroup.Key;
                var angleTargets = angleGroup.ToList();
                if (angleTargets.Count == 0) continue;
                Vector2 endPoint = CalculateLaserEffectEndPoint(angle, angleTargets);
                var laserLine = new LaserLineEffect();
                laserLine.Setup(grid.Position + new Vector2(16, 16), endPoint, GetTeamColor());
                GetTree()?.CurrentScene?.AddChild(laserLine);
                foreach (var t in angleTargets)
                {
                    var hit = new LaserHitEffect();
                    hit.Position = t.Target.GlobalPosition;
                    GetTree()?.CurrentScene?.AddChild(hit);
                    int damage = 0;
                    if (t.Target is Infantry inf)
                        damage = Mathf.RoundToInt(inf.maxHealth * t.BaseDamagePercent);
                    else if (t.Target is Weapon wp)
                        damage = Mathf.RoundToInt(wp.maxHealth * t.BaseDamagePercent);
                    damage = Mathf.Max(1, damage);
                    AIShowDamageNumber(t.Target.GlobalPosition, damage);
                }
            }
        }
    }

    private void AIShowDamageNumber(Vector2 position, int damage)
    {
        var label = new Label();
        label.Text = $"-{damage}";
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
        label.Position = position + new Vector2(0, -15);
        label.ZIndex = 1000;
        GetTree()?.CurrentScene?.AddChild(label);
        var tween = CreateTween();
        tween.TweenProperty(label, "position:y", position.Y - 40, 0.8f)
             .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 0.8f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }

    private void MarkAIActed()
    {
        if (aiMarkedActed) return;
        aiMarkedActed = true;
        hasActed = true;
        SetVisualDark();
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.selectedWeapon == this) gm.selectedWeapon = null;
        gm?.gridManager?.ClearWeaponRange();
    }

    // ========== ✅ 核心修复：回合开始同步 remainingAttacks ==========

    public override void OnTurnStart()
    {
        base.OnTurnStart();
        // 不再限制同一回合重复攻击同一目标

        if (useCooldownSystem)
        {
            if (!cooldownReady)
            {
                turnsSinceLastAttack++;
                totalTurnsPassed++;
                if (turnsSinceLastAttack >= cooldownTurns)
                {
                    turnsSinceLastAttack = 0;
                    cooldownReady = true;
                    if (storeAttacks)
                        attacksRemainingInCycle = Mathf.Min(attacksPerCooldown + attacksRemainingInCycle, attacksPerCooldown * 2);
                    else
                        attacksRemainingInCycle = attacksPerCooldown;
                }
            }
            else if (attacksRemainingInCycle <= 0 && !storeAttacks)
            {
                cooldownReady = false;
                turnsSinceLastAttack = 0;
            }

            // ✅ 修复：同步 remainingAttacks 为 cooldown 实际可用次数
            if (cooldownReady && attacksRemainingInCycle > 0)
                remainingAttacks = attacksRemainingInCycle;
            else
                remainingAttacks = 0;
        }
        else
        {
            // 无冷却系统：使用 maxAttacksPerTurn
            remainingAttacks = maxAttacksPerTurn;
            attacksRemainingInCycle = maxAttacksPerTurn;
        }

        UpdateAmmoVisual();
        UpdateLaserVisual();

        if (enableAutoAI && !hasActed)
        {
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            if (gm != null && gm.IsTurnPhaseValid(team))
            {
                var timer = GetTree().CreateTimer(0.5f);
                timer.Timeout += () => ExecuteAIAutoAttack();
            }
        }
    }

    public override void OnTurnEnd()
    {
        base.OnTurnEnd();
        if (useCooldownSystem && !storeAttacks && attacksRemainingInCycle > 0)
            attacksRemainingInCycle = 0;
    }

    private void InitializeCooldownState()
    {
        hasActed = false;
        remainingAttacks = maxAttacksPerTurn;

        if (useCooldownSystem)
        {
            turnsSinceLastAttack = 0;
            cooldownReady = true;
            attacksRemainingInCycle = attacksPerCooldown;
            totalTurnsPassed = 0;
            // ✅ 同步初始值
            remainingAttacks = attacksPerCooldown;
        }
        else
        {
            attacksRemainingInCycle = maxAttacksPerTurn;
        }
    }

    public bool HasAmmoAndCooldown()
    {
        if (useAmmoSystem && currentAmmo <= 0) return false;
        if (useCooldownSystem && !cooldownReady) return false;
        if (useCooldownSystem && attacksRemainingInCycle <= 0) return false;
        return true;
    }

    public bool ConsumeAmmo()
    {
        if (!useAmmoSystem) return true;
        if (currentAmmo <= 0) return false;
        currentAmmo--;
        UpdateAmmoVisual();
        return true;
    }

    public override bool ResupplyAmmo()
    {
        if (!useAmmoSystem) return false;
        if (currentAmmo >= maxAmmo) return false;
        currentAmmo = maxAmmo;
        UpdateAmmoVisual();
        return true;
    }

    // ========== 视觉更新 ==========

    public void UpdateLaserVisual()
    {
        if (_animSprite == null) return;
        string animPrefix = team == "Player2" ? "Laser2" : "Laser1";
        string animName = animPrefix;
        if (!HasAmmoAndCooldown())
            animName += "_disabled";
        else if (hasActed)
            animName += "_acted";
        if (_animSprite.SpriteFrames != null && _animSprite.SpriteFrames.HasAnimation(animName))
            _animSprite.Play(animName);
        else if (_animSprite.SpriteFrames != null && _animSprite.SpriteFrames.HasAnimation(animPrefix))
            _animSprite.Play(animPrefix);
    }

    public void UpdateAmmoVisual()
    {
        if (_ammoLabel == null) return;
        if (useAmmoSystem)
        {
            _ammoLabel.Text = $"{currentAmmo}/{maxAmmo}";
            _ammoLabel.Modulate = currentAmmo <= 0 ? Colors.Red :
                                (currentAmmo <= maxAmmo / 3 ? Colors.Yellow : Colors.White);
            _ammoLabel.Show();
        }
        else
        {
            _ammoLabel.Text = "∞";
            _ammoLabel.Modulate = Colors.Green;
            _ammoLabel.Show();
        }
    }

    private void ShowAttackRangeAsDisabled(GameManager gm)
    {
        var fog = gm.fogOfWarManager;
        bool isFogEnabled = fog != null && fog.isFogOfWarEnabled;

        foreach (float angle in currentFireAngles)
        {
            var rayGrids = CalculateRayGrids(angle);
            foreach (var rayInfo in rayGrids)
            {
                var g = rayInfo.Grid;
                if (isFogEnabled && fog != null && !fog.IsGridVisible(g)) continue;
                g.pathIcon?.Show();
                g.pathIcon.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                g.IsInWeaponRange = true;
            }
        }
        foreach (var g in gm.gridManager.grids)
        {
            if (!CalculateAttackRange().Contains(g))
            {
                g.OnClickEmpty = () => {
                    gm.gridManager.ClearWeaponRange();
                    var menu = GetTree()?.GetFirstNodeInGroup("action_menu") as ActionMenu;
                    menu?.ShowWeaponMenu(this);
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

    public override List<Grids> CalculateAttackRange()
    {
        var allGrids = new List<Grids>();
        foreach (float angle in currentFireAngles)
        {
            var rayGrids = CalculateRayGrids(angle);
            foreach (var rayInfo in rayGrids)
            {
                if (!allGrids.Contains(rayInfo.Grid))
                    allGrids.Add(rayInfo.Grid);
            }
        }
        return allGrids;
    }

    // ✅ 新增：公共方法，供 TerrainEditor 应用角度配置变更
    public void ApplyAngleConfigChanges()
    {
        InitializeAngleSystem();
        rayCache.Clear();
        UpdateLaserVisual();
    }

    public string GetLaserFullInfo()
    {
        string info = "";
        info += $"[激光炮] {Name}\n";
        info += $"团队: {team}\n";
        info += $"血量: {health}/{maxHealth}\n";
        info += $"状态: {(hasActed ? "已行动" : "待机")}\n";
        if (enableAutoAI)
        {
            info += $"\n=== AI自动操作 ===\n";
            info += $"AI模式: 已启用\n";
            info += $"自动旋转: {(aiAutoRotate ? "是" : "否")}\n";
            info += $"用完所有攻击: {(aiUseAllAttacks ? "是" : "否")}\n";
        }
        info += $"\n=== 角度系统 ===\n";
        info += $"启用角度: {string.Join(", ", enabledAngles.Select(a => $"{a}°"))}\n";
        info += $"当前发射: {string.Join(", ", currentFireAngles.Select(a => $"{a}°"))}\n";
        info += $"可旋转: {(canRotate ? "✓" : "✗")}\n";
        info += $"\n=== 射程 ===\n";
        info += $"射程: {(useInfiniteRange ? "无限" : $"{maxLaserLength}格")}\n";
        info += $"\n=== 伤害配置 ===\n";
        if (useContactAreaDamage)
        {
            info += $"[接触面积衰减模式]\n";
            info += $"穿过中心: {maxContactDamagePercent * 100:F0}%\n";
            info += $"擦边角: {minContactDamagePercent * 100:F0}%\n";
        }
        if (useModifiedDamage)
        {
            info += $"[改良模式]\n";
            info += $"血量{modifiedHealthPercent * 100:F0}% {(modifiedHealthPercent > 0 ? "扣" : "回")}\n";
            info += $"弹药: {modifiedAmmoPercent * 100:F0}%\n";
            info += $"燃料: {modifiedFuelPercent * 100:F0}%\n";
        }
        else
        {
            info += $"[传统模式]\n";
            info += $"固定伤害: {fixedDamagePercent * 100:F0}%\n";
        }
        if (useContactAreaDamage)
            info += $"(已叠加接触面积衰减)\n";
        info += $"可摧毁: {(CanDestroy ? "是" : "否（锁血1HP）")}\n";
        info += $"\n=== 穿透系统 ===\n";
        info += $"每条线穿透上限: {(maxPiercePerLine > 0 ? $"{maxPiercePerLine}个" : "无限")}\n";
        info += $"目标模式: {targetSelectionMode}\n";
        info += $"\n=== 弹药 ===\n";
        if (useAmmoSystem)
            info += $"弹药: {currentAmmo}/{maxAmmo}\n";
        else
            info += $"弹药: 无限\n";
        info += $"\n=== 冷却 ===\n";
        if (useCooldownSystem)
        {
            info += $"冷却周期: {cooldownTurns}回合\n";
            info += $"每周期次数: {attacksPerCooldown}\n";
            info += $"存次数: {(storeAttacks ? "是" : "否")}\n";
            info += $"剩余次数: {attacksRemainingInCycle}\n";
            info += $"冷却就绪: {(cooldownReady ? "✓" : "✗")}\n";
        }
        else
        {
            info += $"冷却: 无\n";
        }
        info += $"\n[操作说明]\n";
        info += $"• 左键点击任意射线格子: 所有线同时发射\n";
        info += $"• R键: 顺时针旋转\n";
        info += $"• 左滑/右滑: 旋转方向\n";
        return info;
    }
}

public partial class LaserLineEffect : Node2D
{
    private Vector2 _startPos;
    private Vector2 _endPos;
    private Color _laserColor;
    private float _t = 0f;
    private const float DURATION = 0.5f;

    public void Setup(Vector2 start, Vector2 end, Color color)
    {
        _startPos = start;
        _endPos = end;
        _laserColor = color;
        ZIndex = 500;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= DURATION) { QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float a = 1f - _t / DURATION;
        float width = 4f * (1f - _t / DURATION * 0.5f);
        DrawLine(_startPos - GlobalPosition, _endPos - GlobalPosition,
                new Color(_laserColor.R, _laserColor.G, _laserColor.B, a), width);
    }
}

public partial class LaserHitEffect : Node2D
{
    private float _t = 0f;
    private const float DURATION = 0.4f;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= DURATION) { QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float a = 1f - _t / DURATION;
        float r = 5f + _t * 30f;
        DrawCircle(Vector2.Zero, r, new Color(1, 0.8f, 0.2f, a * 0.8f));
        DrawCircle(Vector2.Zero, r * 0.5f, new Color(1, 1, 0.8f, a));
    }

}
