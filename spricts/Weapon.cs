// Weapon.cs - 兵器基类（添加独立视野加成矩阵）
using Godot;
using System;
using System.Collections.Generic;

public partial class Weapon : Node2D
{
    // ===== 子类共享的通用虚方法桩（BlackCannon 族/管道/工厂族覆写接口）=====
    public virtual string GetAnimName() => GetType().Name;
    public virtual string GetIdleAnimName() => GetType().Name;
    public virtual string GetBlackCannonFullInfo() => "";

    [Export] public bool CanAttackWeapon = false;

    [ExportGroup("攻击准入")]
    [Export] public string[] excludedAttackerTypes = System.Array.Empty<string>();  // ⚔ 不能被这些单位类型攻击（填类名，如 "Fighter"）
    [Export] public bool canCrossScreens = false;   // ✅ N屏：能否跨图（跨屏攻击；全屏导弹默认仅限己图，开此Export才可跨图）

    // ✅ N屏：目标格是否被屏隔离挡住（canCrossScreens=true 时忽略屏界）
    public bool IsScreenBlocked(Grids target)
    {
        if (canCrossScreens || target == null || grid == null) return false;
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gmm = gm?.gridManager;
        if (gmm == null || gmm.GetScreenCount() <= 1) return false;
        return !gmm.IsSameScreen(grid.GridIndex, target.GridIndex);
    }

    // ✅ 伤害单位类别限制（可拼装模块）：按攻击者的空军/陆军/海军/防空分类过滤，全true=不限制（默认）
    [ExportGroup("伤害单位类别限制")]
    [Export] public bool restrictAttackerCategories = false;   // 是否启用单位类别限制
    [Export] public bool allowAttackerAirUnits = true;         // 允许空军/天空军单位造成伤害
    [Export] public bool allowAttackerLandUnits = true;        // 允许陆军单位造成伤害
    [Export] public bool allowAttackerNavalUnits = true;       // 允许海军单位造成伤害
    [Export] public bool allowAttackerAntiAirUnits = true;     // 允许防空/反空中单位造成伤害

    [ExportGroup("多格占据")]
    [Export] public Vector2I size = new Vector2I(1, 1);
    [Export] public bool isMultiTile = false;
    public List<Grids> occupiedGrids = new List<Grids>();
    public bool isDestroyed = false;

    [ExportGroup("胜利判定介入")]
    [Export] public bool contributesToVictory = false;
    [Export] public int victoryCountRequired = 1;
    [Export] public string victoryTargetTeam = "Player2";

    [ExportGroup("基础属性")]
    [Export] public string team = "Player1";
    [Export] public int maxHealth = 999;
    [Export] public int health = 999;
    [Export] public bool canRotate = true;
    [Export] public int maxAttacksPerTurn = 1;
    [Export] public int cost = 0;

    // ✅ 力场bypass：坠落等引擎流程内部使用（黑弧护卫全毁坠落时绕过力场）
    public bool ignoreForceField = false; 

    // ========== ✅ 新增：通用弹药系统（子类可覆盖）==========
        [ExportGroup("兵器被击伤害表")]
    [Export] public bool useWeaponDamageTable = false;
    // 动态伤害表：键=攻击方单位类型ID，值=对本兵器的伤害值（-1=不覆盖，使用攻击方原始矩阵）
    [Export] public Godot.Collections.Dictionary<string, int> damageFromUnits = new();

[ExportGroup("弹药系统")]
    [Export] public bool useAmmoSystem = false;
    [Export] public int currentAmmo = 0;
    [Export] public int maxAmmo = 0;

    [ExportGroup("弹药系统")]
    // ✅ 8弹药适配：旧池(useAmmoSystem/currentAmmo/maxAmmo)=普通弹；新增五弹仓(0=关闭)
    [Export] public int fireConsumesAmmoType = 0;      // 0=自动(旧useAmmoSystem→普通弹1发/否则不耗) 1=普通 2=自爆 3=照明 4=隐身弹 5=显形弹 6=迷彩弹
    [Export] public int fireConsumesAmmoAmount = 1;    // 每次开火消耗数量
    [Export] public int wExplodeMax = 0, wExplodeCurrent = 0;
    [Export] public int wFlareMax = 0, wFlareCurrent = 0;
    [Export] public int wStealthBulletMax = 0, wStealthBulletCurrent = 0;
    [Export] public int wRevealBulletMax = 0, wRevealBulletCurrent = 0;
    [Export] public int wCamoBulletMax = 0, wCamoBulletCurrent = 0;
    [Export] public string[] resupplyWhitelist = System.Array.Empty<string>();  // 允许补给的弹种: primary/explode/flare/stealth/reveal/camo（空=全允许）
    [Export] public string[] resupplyBlacklist = System.Array.Empty<string>();

    // ========== ✅ 战争迷雾视野系统 ==========
    [ExportGroup("战争迷雾视野")]
    [Export] public int visionRange = -1;
    [Export] public bool useConfigVision = true;
    [Export] public VisionMode visionMode = VisionMode.Normal;

    // ✅ 新增：兵器独立的视野加成矩阵（覆盖全局默认值）
    [ExportGroup("兵器独立地形视野加成")]
    [Export] public Godot.Collections.Dictionary<GridType, int> weaponTerrainVisionBonus = new();
    [Export] public bool overrideGlobalTerrainBonus = false;

    // ✅ 新增：P-1 兵器视野共享标记
    [ExportGroup("P-1 视野共享")]
    [Export] public bool isPublicVisionWeapon = false;  // 是否将视野共享给所有势力（P-1兵器默认true）

    public int ActualVisionRange
    {
        get
        {
            if (!useConfigVision && visionRange >= 0) return visionRange;
            string typeName = this.GetType().Name;
            return VisionConfig.GetWeaponVisionRange(typeName);
        }
    }

    public bool IsIndependentMode
    {
        get
        {
            if (visionMode == VisionMode.Independent) return true;
            return VisionConfig.IsWeaponIndependentMode(this.GetType().Name);
        }
    }

    public bool hasActed = false;
    public int remainingAttacks = 1;
    public Grids grid;
    public AnimatedSprite2D animSprite;
    public Label hpLabel;
    public Action<Weapon> OnClickWeapon;

    public virtual List<Grids> CalculateAttackRange() { return new List<Grids>(); }

    public virtual List<Vector2I> GetOccupiedIndices(Vector2I anchorIndex)
    {
        var result = new List<Vector2I>();
        for (int x = 0; x < size.X; x++)
            for (int y = 0; y < size.Y; y++)
                result.Add(anchorIndex + new Vector2I(x, y));
        return result;
    }

    public virtual bool IsWeakPointGrid(Grids checkGrid)
    {
        if (!isMultiTile || occupiedGrids.Count == 0) return true;
        return checkGrid == GetWeakPointGrid();
    }

    public virtual Grids GetWeakPointGrid()
    {
        if (!isMultiTile || occupiedGrids.Count == 0) return grid;
        return grid;
    }

    public virtual bool IsOccupiedGrid(Grids checkGrid)
    {
        if (!isMultiTile) return checkGrid == grid;
        return occupiedGrids.Contains(checkGrid);
    }

    /// <summary>
    /// ✅ 通行判定：该兵器是否允许 mover 通过其占据的格子
    /// 默认 false（兵器阻挡一切移动，原行为）；导弹井/Gate等子类按需覆盖
    /// </summary>
    public virtual bool AllowsPassage(Infantry mover)
    {
        return false;
    }

    public virtual void OnMultiTileDestroyed()
    {
        isDestroyed = true;
        hasActed = true;
        remainingAttacks = 0;
        if (animSprite != null)
        {
            string brokenAnim = GetBrokenAnimName();
            if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(brokenAnim))
                animSprite.Play(brokenAnim);
            else
                animSprite.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.8f);
        }
    }

    public virtual string GetBrokenAnimName() { return ""; }
    // GetIdleAnimName 虚方法已由上游 6ae660d 补齐（返回 GetType().Name）

    public virtual void UpdateMultiTileVisual()
    {
        // 子类可覆盖，用于旋转时更新大贴图位置
    }

    // ✅ 双图层组件
    [Export] public Sprite2D baseSprite;
    [Export] public Sprite2D normalSprite;
    [Export] public AnimatedSprite2D baseAnimSprite;
    [Export] public AnimatedSprite2D normalAnimSprite;

    public override void _Ready()
    {
        ZIndex = 10;
        AddToGroup("weapons");
        hpLabel = GetNodeOrNull<Label>("HpLabel");
        animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        // ✅ 双图层组件自动获取（Sprite2D 版）
        if (baseSprite == null)
            baseSprite = GetNodeOrNull<Sprite2D>("BaseSprite");
        if (normalSprite == null)
            normalSprite = GetNodeOrNull<Sprite2D>("NormalSprite");
        // ✅ 双图层组件自动获取（AnimatedSprite2D 版）
        if (baseAnimSprite == null)
            baseAnimSprite = GetNodeOrNull<AnimatedSprite2D>("BaseAnimSprite");
        if (baseAnimSprite == null)
            baseAnimSprite = GetNodeOrNull<AnimatedSprite2D>("BaseAnimatedSprite");
        if (normalAnimSprite == null)
            normalAnimSprite = GetNodeOrNull<AnimatedSprite2D>("NormalAnimSprite");
        if (normalAnimSprite == null)
            normalAnimSprite = GetNodeOrNull<AnimatedSprite2D>("NormalAnimatedSprite");
        
        // ✅ 双图层 Sprite2D：把 Normal 层挂到 Base 层下
        if (baseSprite != null && normalSprite != null && normalSprite.GetParent() != baseSprite)
        {
            normalSprite.Reparent(baseSprite, true);
        }
        // ✅ 双图层 AnimatedSprite2D：同理
        if (baseAnimSprite != null && normalAnimSprite != null && normalAnimSprite.GetParent() != baseAnimSprite)
        {
            normalAnimSprite.Reparent(baseAnimSprite, true);
        }
        
        UpdateHpLabel();
        if (hpLabel != null)
        {
            hpLabel.AddThemeConstantOverride("outline_size", 2);
            hpLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            if (visionRange < 0 && useConfigVision)
            {
                visionRange = VisionConfig.GetWeaponVisionRange(this.GetType().Name);
                if (VisionConfig.IsWeaponIndependentMode(this.GetType().Name)) visionMode = VisionMode.Independent;
            }
        }

        // ✅ P-1 兵器默认开启视野共享
        if (team == TeamHelper.Player && !isPublicVisionWeapon)
        {
            isPublicVisionWeapon = true;
        }
        
        // ✅ 应用调色板Shader
        ApplyPaletteMaterial();
        
        var area2D = GetNodeOrNull<Area2D>("Area2D");
        if (area2D != null) area2D.InputEvent += OnInputEvent;
    }
    
    /// <summary>
    /// ✅ 为兵器应用调色板Shader（双图层 + 兼容模式）
    /// </summary>
    /// <summary>势力变更/势力配色修改后刷新外观（子类按需重选动画）</summary>
    public virtual void RefreshTeamVisual()
    {
        ApplyPaletteMaterial();
    }

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
            // 隐藏旧版单图层组件
            if (animSprite != null) animSprite.Hide();
            if (baseSprite != null) baseSprite.Hide();
            if (normalSprite != null) normalSprite.Hide();
            return;
        }
        
        // ✅ 双图层 Sprite2D 方案
        if (baseSprite != null && IsInstanceValid(baseSprite) &&
            normalSprite != null && IsInstanceValid(normalSprite))
        {
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
            if (baseAnimSprite != null) baseAnimSprite.Hide();
            if (normalAnimSprite != null) normalAnimSprite.Hide();
            return;
        }
        
        // Fallback：兼容模式
        // 先隐藏双图层节点
        if (baseAnimSprite != null) baseAnimSprite.Hide();
        if (normalAnimSprite != null) normalAnimSprite.Hide();
        if (baseSprite != null) baseSprite.Hide();
        if (normalSprite != null) normalSprite.Hide();
        
        var mat = FactionPaletteManager.GetMaterial(team);
        if (mat == null) return;
        
        if (animSprite != null && IsInstanceValid(animSprite))
        {
            var animMat = (ShaderMaterial)mat.Duplicate();
            animSprite.Material = animMat;
            animSprite.Show();
        }
    }

    public virtual void OnTurnStart()
    {
        hasActed = false;
        remainingAttacks = maxAttacksPerTurn;
        SetVisualNormal();
        if (enableAutoAI && !hasActed) CallDeferred(nameof(TryExecuteAI));
    }

    // ✅ P0/P-1 兵器 AI 入口
    public virtual void ExecuteAI() { }

    public virtual void OnTurnEnd() { }
    public virtual void ShowAttackRange() { }
    public virtual void PerformAttack(Infantry target) { }
    public virtual void RotateDirection() { if (!canRotate) return; }

    [ExportGroup("AI控制")]
    [Export] public bool isAIControlled = false;
    [Export] public bool enableAutoAI = false;
    protected virtual void TryExecuteAI() { }

    // ✅ 新增：通用补给弹药方法（子类可覆盖）
    public virtual bool ResupplyAmmo()
    {
        if (!useAmmoSystem) return false;
        if (!ResupplyAllowed("primary")) return false;
        if (currentAmmo >= maxAmmo) return false;
        if (!AmmoTypeLocked(1)) currentAmmo = maxAmmo; // 力场锁弹
        return true;
    }

    // ===== ✅ 8弹药系统工具族 =====
    public int GetAmmoByType(int type) => type switch
    {
        1 => currentAmmo,
        2 => wExplodeCurrent,
        3 => wFlareCurrent,
        4 => wStealthBulletCurrent,
        5 => wRevealBulletCurrent,
        6 => wCamoBulletCurrent,
        _ => 0,
    };

    public int GetAmmoMaxByType(int type) => type switch
    {
        1 => maxAmmo,
        2 => wExplodeMax,
        3 => wFlareMax,
        4 => wStealthBulletMax,
        5 => wRevealBulletMax,
        6 => wCamoBulletMax,
        _ => 0,
    };

    public void SetAmmoByType(int type, int value)
    {
        switch (type)
        {
            case 1: currentAmmo = value; break;
            case 2: wExplodeCurrent = value; break;
            case 3: wFlareCurrent = value; break;
            case 4: wStealthBulletCurrent = value; break;
            case 5: wRevealBulletCurrent = value; break;
            case 6: wCamoBulletCurrent = value; break;
        }
    }

    // 开火弹药许可（type=0 自动：旧useAmmoSystem→普通弹1发；否则不耗）
    public bool HasFireAmmo()
    {
        int type = fireConsumesAmmoType, amount = Mathf.Max(1, fireConsumesAmmoAmount);
        if (type == 0) { if (!useAmmoSystem) return true; type = 1; amount = 1; }
        return GetAmmoByType(type) >= amount;
    }

    // 开火消耗弹药（false=弹尽，拒绝开火）
    public bool ConsumeFireAmmo()
    {
        int type = fireConsumesAmmoType, amount = Mathf.Max(1, fireConsumesAmmoAmount);
        if (type == 0) { if (!useAmmoSystem) return true; type = 1; amount = 1; }
        if (GetAmmoByType(type) < amount) return false;
        if (!AmmoTypeLocked(type)) SetAmmoByType(type, GetAmmoByType(type) - amount); // 力场锁弹
        return true;
    }

    // 补给白/黑名单：白名单空=全允许；否则须在名单内且不在黑名单
    public bool ResupplyAllowed(string key)
    {
        if (resupplyBlacklist != null && System.Array.IndexOf(resupplyBlacklist, key) >= 0) return false;
        return resupplyWhitelist == null || resupplyWhitelist.Length == 0 || System.Array.IndexOf(resupplyWhitelist, key) >= 0;
    }

    // 全弹种补给（尊重白/黑名单），返回是否有实际补给
    public bool ResupplyAllAmmoTypes()
    {
        bool any = false;
        if (useAmmoSystem && ResupplyAllowed("primary") && currentAmmo < maxAmmo) { currentAmmo = maxAmmo; any = true; }
        if (ResupplyAllowed("explode") && wExplodeCurrent < wExplodeMax) { wExplodeCurrent = wExplodeMax; any = true; }
        if (ResupplyAllowed("flare") && wFlareCurrent < wFlareMax) { wFlareCurrent = wFlareMax; any = true; }
        if (ResupplyAllowed("stealth") && wStealthBulletCurrent < wStealthBulletMax) { wStealthBulletCurrent = wStealthBulletMax; any = true; }
        if (ResupplyAllowed("reveal") && wRevealBulletCurrent < wRevealBulletMax) { wRevealBulletCurrent = wRevealBulletMax; any = true; }
        if (ResupplyAllowed("camo") && wCamoBulletCurrent < wCamoBulletMax) { wCamoBulletCurrent = wCamoBulletMax; any = true; }
        return any;
    }

    /// <summary>
    /// 同步伤害表：运行时从UnitRegistry自动填充所有单位类型（默认-1=不覆盖）
    /// </summary>
    public void SyncDamageTable()
    {
        if (GameManager.Instance?.unitRegistry == null) return;
        foreach (var unitId in GameManager.Instance.unitRegistry.Configs.Keys)
        {
            if (!damageFromUnits.ContainsKey(unitId))
                damageFromUnits[unitId] = -1;
        }
    }

    /// <summary>
    /// 获取指定攻击方对本兵器的伤害值（-1=不覆盖，使用攻击方原始矩阵）
    /// </summary>
    public int GetDamageFrom(string attackerType)
    {
        if (!useWeaponDamageTable) return -1;
        if (damageFromUnits.TryGetValue(attackerType, out int dmg))
            return dmg;
        return -1;
    }

    public virtual void TakeDamage(int damage, Infantry attacker = null)
    {
        if (healthLocked) return; // 力场锁血
        // ✅ 力场保护：被存活黑弧力场保护的地图目标物，攻击全部无效（坠落流程带bypass）
        if (!ignoreForceField && BlackArc.IsGridForceFieldProtected(grid)) return;

        // ========== 兵器被击伤害表（具体数值覆盖攻击方原始矩阵）==========
        if (useWeaponDamageTable && attacker != null)
        {
            int tableDamage = GetDamageFrom(attacker.unitTypeId);
            if (tableDamage >= 0)
                damage = tableDamage;
        }
        health -= damage;
        health = Mathf.Max(0, health);
        UpdateHpLabel();
        var targetGrid = grid;
        if (animSprite != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(animSprite, "modulate", Colors.Red, 0.1f);
            tween.TweenProperty(animSprite, "modulate", Colors.White, 0.1f);
        }
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.selectedInfantry != null)
        {
            var attacker = gm.selectedInfantry;
            attacker.isAttacked = true;
            attacker.state = UnitState.Acted;
            attacker.originalGrid = null;
            attacker.SetWaitVisual(true);
            gm.gridManager.HideAttackRange();
            gm.ClearSelectedInfantry();
        }
        if (health <= 0)
        {
            if (grid != null && grid.gridType == GridType.PIPESEAM)
            {
                targetGrid.gridType = GridType.BROKENPIPE;
                var sprite = targetGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
                if (sprite != null) { var tween = CreateTween(); tween.TweenProperty(sprite, "modulate", new Color(0.3f, 0.3f, 0.3f), 0.5f); }
            }
            else if (grid != null && grid.gridType == GridType.METEORITE)
            {
                // ✅ 陨石被毁：原格恢复平原 + 相连等离子全部消失（DoR原作机制）
                RemoveConnectedPlasma();
                targetGrid.gridType = GridType.GROUND;
            }
            var area = GetNodeOrNull<Area2D>("Area2D");
            if (area != null) area.SetDeferred("input_pickable", false);
            if (gm?.selectedWeapon == this) gm.selectedWeapon = null;
            // ✅ 护卫联动：任意兵器被毁后刷新黑弧护卫状态（护卫全毁→坠落）
            gm?.NotifyBlackArcEscortCheck();
            CallDeferred(nameof(OnDestroyed));
        }
    }

    /// <summary>
    /// ✅ 陨石被毁时：从本格出发BFS清除所有相连的等离子地形（变平原）
    /// DoR原作：击毁陨石后与其相连的等离子全部消失，原格恢复平原/海
    /// </summary>
    protected void RemoveConnectedPlasma()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        var gridMgr = gm?.gridManager;
        if (gridMgr?.map == null || grid == null) return;

        var queue = new System.Collections.Generic.Queue<Grids>();
        var visited = new System.Collections.Generic.HashSet<Grids>();
        queue.Enqueue(grid);
        visited.Add(grid);
        int removed = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in gridMgr.GetNeighbours(current))
            {
                if (neighbor == null || visited.Contains(neighbor)) continue;
                if (neighbor.gridType != GridType.PLASMA) continue;
                visited.Add(neighbor);
                queue.Enqueue(neighbor);

                // ✅ 等离子消失：变平原 + 清理占据兵器引用 + 刷新贴图
                neighbor.gridType = GridType.GROUND;
                var neighborWeapons = neighbor.weapons;
                foreach (var w in neighborWeapons.ToArray())
                {
                    if (w == null || !IsInstanceValid(w)) continue;
                    neighborWeapons.Remove(w);
                    if (neighbor.weapon == w) neighbor.weapon = neighborWeapons.Count > 0 ? neighborWeapons[0] : null;
                }
                neighbor.UpdateTerrainIcon();
                removed++;
            }
        }

        if (removed > 0)
        {
            // ✅ 多方向地形：等离子消失后刷新全图方向位掩码
            gridMgr.RefreshAllTerrainDirections();
        }
    }

    public virtual void OnDestroyed()
    {
        isDestroyed = true; // ✅ 关键：标记为已摧毁，确保胜利判定正确识别
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager?.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        if (grid != null && grid.gridType == GridType.PIPESEAM)
        {
            var targetGrid = grid;
            targetGrid.weapons.Remove(this);
            if (targetGrid.weapon == this) targetGrid.weapon = targetGrid.weapons.Count > 0 ? targetGrid.weapons[0] : null;
            targetGrid.gridType = GridType.BROKENPIPE;
            if (targetGrid.city != null)
                targetGrid.city.healAmount = 0; targetGrid.city.flareAmmoSupply = 0; targetGrid.city.explodeAmmoSupply = 0; targetGrid.city.primaryAmmoSupply = 0; targetGrid.city.fuelSupply = 0;
            var sprite = targetGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
            if (sprite != null) { var tween = CreateTween(); tween.TweenProperty(sprite, "modulate", new Color(0.4f, 0.7f, 0.3f), 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut); }
        }
        else if (grid != null && grid.gridType == GridType.METEORITE)
        {
            // ✅ 陨石被毁：原格恢复平原 + 相连等离子全部消失（DoR原作机制）
            var targetGrid = grid;
            targetGrid.weapons.Remove(this);
            if (targetGrid.weapon == this) targetGrid.weapon = targetGrid.weapons.Count > 0 ? targetGrid.weapons[0] : null;
            targetGrid.gridType = GridType.GROUND;
            var sprite = targetGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
            if (sprite != null) { var tween = CreateTween(); tween.TweenProperty(sprite, "modulate", new Color(0.4f, 0.7f, 0.3f), 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut); }
        }
        gm?.CheckVictoryCondition();
        QueueFree();
    }

    public virtual void HandlePostAttack()
    {
        if (remainingAttacks <= 0)
        {
            hasActed = true;
            SetVisualDark();
            var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
            gm?.gridManager?.ClearWeaponRange();
            if (gm != null) gm.selectedWeapon = null;
            var menu = GetTree()?.GetFirstNodeInGroup("action_menu") as ActionMenu;
            menu?.Hide();
        }
        else
        {
            var refreshTimer = GetTree()?.CreateTimer(0.15f);
            if (refreshTimer != null) refreshTimer.Timeout += () => { if (IsInstanceValid(this) && !hasActed) ShowAttackRange(); };
        }
    }

    public virtual void UpdateHpLabel()
    {
        if (hpLabel == null) return;
        int displayBars;
        if (health <= 999) { displayBars = Mathf.CeilToInt((float)health / 10f); displayBars = Mathf.Clamp(displayBars, 1, 99); }
        else { float ratio = (float)health / 999f; displayBars = Mathf.RoundToInt(99f * ratio); }
        hpLabel.Text = displayBars.ToString();
        float percent = (float)health / maxHealth;
        if (percent > 0.7f) hpLabel.Modulate = new Color(1f, 0.8f, 0.4f);
        else if (percent > 0.3f) hpLabel.Modulate = Colors.White;
        else hpLabel.Modulate = new Color(1f, 0.5f, 0.5f);
    }

    private void OnInputEvent(Node viewport, InputEvent @event, long shape_idx)
    {
        if (isDestroyed) return; // ✅ 已摧毁的兵器不接受点击
        var terrainEditor = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
        if (terrainEditor != null && terrainEditor.ShouldBlockUnitOperations()) return;
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Left) OnClickWeapon?.Invoke(this);
        }
    }

    public virtual void SetVisualDark() 
    { 
        // ✅ 双图层方案：只变暗 BaseSprite
        if (baseSprite != null && IsInstanceValid(baseSprite))
        {
            if (baseSprite.Material is ShaderMaterial baseMat)
            {
                baseMat.SetShaderParameter("tone", new Color(0.6f, 0.6f, 0.6f, 1.0f));
            }
            else
            {
                baseSprite.Modulate = new Color(0.6f, 0.6f, 0.6f, 1.0f);
            }
            return;
        }
        
        if (animSprite != null) 
        {
            // ✅ LUT模式：通过Shader tone参数变暗
            if (animSprite.Material is ShaderMaterial shaderMat)
            {
                shaderMat.SetShaderParameter("tone", new Color(0.6f, 0.6f, 0.6f, 1.0f));
            }
            else
            {
                // Fallback: 旧模式
                Color tint = TeamHelper.GetTeamColor(team) * 0.7f;
                tint.A = 1f;
                animSprite.Modulate = tint;
            }
        }
    }
    public virtual void SetVisualNormal() 
    { 
        // ✅ 双图层方案：恢复 BaseSprite tone
        if (baseSprite != null && IsInstanceValid(baseSprite))
        {
            if (baseSprite.Material is ShaderMaterial baseMat)
            {
                baseMat.SetShaderParameter("tone", new Color(1f, 1f, 1f, 1f));
            }
            else
            {
                baseSprite.Modulate = Colors.White;
            }
            return;
        }
        
        if (animSprite != null) 
        {
            // ✅ LUT模式：恢复Shader tone为正常
            if (animSprite.Material is ShaderMaterial shaderMat)
            {
                shaderMat.SetShaderParameter("tone", new Color(1f, 1f, 1f, 1f));
            }
            else
            {
                // Fallback: 旧模式
                animSprite.Modulate = TeamHelper.GetTeamColor(team);
            }
        }
    }
    public virtual bool CanAttack() { return !hasActed && remainingAttacks > 0; }
}
