// Weapon.cs - 兵器基类（添加独立视野加成矩阵）
using Godot;
using System;
using System.Collections.Generic;

public partial class Weapon : Node2D
{
    [Export] public bool CanAttackWeapon = false;

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

    // ========== ✅ 新增：通用弹药系统（子类可覆盖）==========
    [ExportGroup("弹药系统")]
    [Export] public bool useAmmoSystem = false;
    [Export] public int currentAmmo = 0;
    [Export] public int maxAmmo = 0;

    // ========== ✅ 战争迷雾视野系统 ==========
    [ExportGroup("战争迷雾视野")]
    [Export] public int visionRange = -1;
    [Export] public bool useConfigVision = true;
    [Export] public VisionMode visionMode = VisionMode.Normal;

    // ✅ 新增：兵器独立的视野加成矩阵（覆盖全局默认值）
    [ExportGroup("兵器独立地形视野加成")]
    [Export] public Godot.Collections.Dictionary<GridType, int> weaponTerrainVisionBonus = new();
    [Export] public bool overrideGlobalTerrainBonus = false;

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

    public virtual void UpdateMultiTileVisual()
    {
        // 子类可覆盖，用于旋转时更新大贴图位置
    }

    public override void _Ready()
    {
        ZIndex = 10;
        AddToGroup("weapons");
        hpLabel = GetNodeOrNull<Label>("HpLabel");
        animSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
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
        var area2D = GetNodeOrNull<Area2D>("Area2D");
        if (area2D != null) area2D.InputEvent += OnInputEvent;
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
        if (currentAmmo >= maxAmmo) return false;
        currentAmmo = maxAmmo;
        return true;
    }

    public virtual void TakeDamage(int damage)
    {
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
            if (grid != null && (grid.gridType == GridType.PIPESEAM || grid.gridType == GridType.METEORITE))
            {
                string terrainName = grid.gridType == GridType.PIPESEAM ? "PIPESEAM" : "METEORITE";
                targetGrid.gridType = GridType.BROKENPIPE;
                var sprite = targetGrid.GetNodeOrNull<Sprite2D>("Sprite2D");
                if (sprite != null) { var tween = CreateTween(); tween.TweenProperty(sprite, "modulate", new Color(0.3f, 0.3f, 0.3f), 0.5f); }
            }
            var area = GetNodeOrNull<Area2D>("Area2D");
            if (area != null) area.SetDeferred("input_pickable", false);
            if (gm?.selectedWeapon == this) gm.selectedWeapon = null;
            CallDeferred(nameof(OnDestroyed));
        }
    }

    public virtual void OnDestroyed()
    {
        isDestroyed = true; // ✅ 关键：标记为已摧毁，确保胜利判定正确识别
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        gm?.gridManager?.ClearWeaponRange();
        Grids.IsForceActionMode = false;
        if (grid != null && (grid.gridType == GridType.PIPESEAM || grid.gridType == GridType.METEORITE))
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
        if (animSprite != null) 
        {
            Color tint = (team == TeamHelper.Player0 || team == TeamHelper.Player)
                ? TeamHelper.GetTeamColor(team) * 0.7f
                : new Color(0.5f, 0.5f, 0.5f, 1f);
            tint.A = 1f;
            animSprite.Modulate = tint;
        }
    }
    public virtual void SetVisualNormal() 
    { 
        if (animSprite != null) 
        {
            Color tint = (team == TeamHelper.Player0 || team == TeamHelper.Player)
                ? TeamHelper.GetTeamColor(team)
                : Colors.White;
            animSprite.Modulate = tint;
        }
    }
    public virtual bool CanAttack() { return !hasActed && remainingAttacks > 0; }
}
