// Gate.cs - 门模块（贴图复用Commander Wars素材：gate+n+s / gate+e+w / destroyed变体）
// 机制（DoR/CW原作）：血量耗尽之前只有白名单能通过；血量耗尽（被摧毁）后门体残骸不挡路，全势力通行
// 地形联动：GATE 地形 + Gate 兵器；摧毁后占据格地形恢复 GROUND（原作：变平原）
// 全Export自由度：1.是否开启 2.所属势力 3.血量 4.白名单（友/敌/全）+ 高度全自由度（gateSize宽×高任意）
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Gate : Weapon
{
    // ========== 4. 白名单：所属势力为基准 ==========
    public enum GatePassPolicy
    {
        Ally = 0,  // 友：仅友军可通过（同队 + P-1公共 + P0中立，沿用导弹井惯例）
        Enemy = 1, // 敌：仅敌军可通过（非本方势力）
        All = 2    // 全：所有人可通过
    }

    // ========== 1. Export是否开启 ==========
    [ExportGroup("门 Gate 模块")]
    [Export] public bool gateOpen = true; // 1.是否开启（false=铁板一块：谁都过不去）

    // ========== 2. Export所属势力 ==========
    // 继承基类 team（默认Player1）——白名单"友/敌"判定基准

    // ========== 3. Export血量 ==========
    // 继承基类 maxHealth/health（默认999）——血量耗尽前白名单生效；耗尽=门被摧毁

    // ========== 4. 白名单 ==========
    [Export] public GatePassPolicy passPolicy = GatePassPolicy.Ally; // 4.白名单：0=友 1=敌 2=全

    // ========== 高度全自由度 ==========
    [Export] public Vector2I gateSize = new Vector2I(1, 1); // 宽×高任意（默认1×1单格；1×N=竖墙门[南北走向]；N×1=横墙门[东西走向]）
    [Export] public bool gateHorizontal = false; // 横墙门(东西走向); 多格时由 gateSize 自动推导, 1×1 时可手动/旋转切换

    public override void _Ready()
    {
        // ✅ 尺寸动态决定多格属性与贴图方向
        if (gateSize.X * gateSize.Y > 1)
        {
            isMultiTile = true;
            gateHorizontal = gateSize.X > gateSize.Y; // 多格门方向由尺寸推导
        }
        UpdateGateAnim();
        base._Ready();
    }

    // ========== 方向切换(1×1门纯视觉切换; 多格门同步交换尺寸) ==========
    public override void RotateDirection()
    {
        if (!canRotate || isDestroyed) return;
        gateHorizontal = !gateHorizontal;
        if (gateSize.X * gateSize.Y > 1)
            gateSize = new Vector2I(gateSize.Y, gateSize.X);
        UpdateGateAnim();
    }

    // ========== ✅ CoW (R,G)编码贴图 → LUT解码(随势力换色) ==========
    protected override void ApplyPaletteMaterial()
    {
        if (animSprite == null || !IsInstanceValid(animSprite)) return;
        // ✅ 2026-11: Gate改固定彩画(黄黑防护栏+红门, 取自CoW gate双文件按官方shader反解, 用户预期图验收)——plain RGBA不再过LUT(mode2/LUT对固定画都是误判), Material=null直出
        animSprite.Material = null;
    }

    /// <summary>通行判定：被毁/血尽→全通行；关闭→封死；否则按白名单</summary>
    public override bool AllowsPassage(Infantry mover)
    {
        if (isDestroyed || health <= 0) return true; // 门被摧毁→敞开（残骸不挡路）
        if (!gateOpen) return false;                  // 关闭=封死，谁都过不去
        if (mover == null) return false;
        switch (passPolicy)
        {
            case GatePassPolicy.All: return true;
            case GatePassPolicy.Enemy: return !IsFriendlyTo(mover.team);
            default: return IsFriendlyTo(mover.team); // 友
        }
    }

    /// <summary>友军判定：同队；或任一方为P-1公共/P0中立（沿用导弹井惯例）</summary>
    private bool IsFriendlyTo(string otherTeam)
    {
        if (string.IsNullOrEmpty(otherTeam)) return false;
        if (otherTeam == team) return true;
        if (team == TeamHelper.Player || otherTeam == TeamHelper.Player) return true;  // P-1公共
        if (team == TeamHelper.Player0 || otherTeam == TeamHelper.Player0) return true; // P0中立
        return false;
    }

    // ========== 高度全自由度：占据格索引 ==========
    public override List<Vector2I> GetOccupiedIndices(Vector2I anchorIndex)
    {
        var result = new List<Vector2I>();
        for (int x = 0; x < gateSize.X; x++)
            for (int y = 0; y < gateSize.Y; y++)
                result.Add(anchorIndex + new Vector2I(x, y));
        return result;
    }

    // ========== 贴图方向：gateHorizontal=横墙门(gate+e+w)，否则竖墙门(gate+n+s) ==========
    public override string GetIdleAnimName()
    {
        return gateHorizontal ? "GateH" : "Gate";
    }

    public override string GetBrokenAnimName()
    {
        return gateHorizontal ? "GateDestroyedH" : "GateDestroyed"; // 对齐 Gate.tres 动画名
    }

    /// <summary>按尺寸刷新贴图动画（尺寸/方向变更后可调用）</summary>
    public void UpdateGateAnim()
    {
        if (animSprite == null) return;
        string anim = GetIdleAnimName();
        // ✅ CoW LUT: 优先Cow编码动画(换色), 无则回落原版
        if (animSprite?.SpriteFrames != null)
        {
            string cowAnim = "Cow" + anim;
            if (animSprite.SpriteFrames.HasAnimation(cowAnim)) anim = cowAnim;
        }
        if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(anim))
            animSprite.Play(anim);
    }

    /// <summary>把占据格地形刷成 GATE（放置时由 WeaponManager.BindWeaponToGrid 调用）</summary>
    public void ApplyGateTerrain()
    {
        var cells = occupiedGrids.Count > 0 ? occupiedGrids : new List<Grids> { grid };
        foreach (var g in cells)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            if (g.gridType != GridType.GATE)
            {
                g.gridType = GridType.GATE;
                g.UpdateTerrainIcon();
            }
        }
    }

    public override void TakeDamage(int damage)
    {
        if (isDestroyed) return; // 已摧毁的残骸不再受击
        base.TakeDamage(damage);
    }

    // ========== 被摧毁：残骸保留 + 门敞开（全通行）+ 地形恢复平原 ==========
    public override void OnDestroyed()
    {
        OnMultiTileDestroyed(); // 标记摧毁状态 + Broken动画（残骸保留，不QueueFree）

        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.gridManager != null)
        {
            gm.gridManager.ClearWeaponRange();
        }
        Grids.IsForceActionMode = false;

        var area = GetNodeOrNull<Area2D>("Area2D");
        if (area != null)
        {
            area.InputPickable = false;
            area.Monitorable = false;
        }
        if (hpLabel != null) hpLabel.Hide();

        // ✅ 恢复所有占据格：武器引用保留（编辑器可选删）+ GATE地形→GROUND（残骸下可通行）
        var cells = occupiedGrids.Count > 0 ? occupiedGrids.ToList() : new List<Grids> { grid };
        foreach (var g in cells)
        {
            if (g == null || !IsInstanceValid(g)) continue;
            if (!g.weapons.Contains(this)) g.weapons.Add(this);
            if (g.weapon == null) g.weapon = this;
            if (g.gridType == GridType.GATE) g.gridType = GridType.GROUND;
        }
        if (occupiedGrids.Count > 0) grid = occupiedGrids[0];

        if (animSprite != null)
        {
            animSprite.Modulate = Colors.White;
            string brokenAnim = GetBrokenAnimName();
            if (animSprite.SpriteFrames != null && animSprite.SpriteFrames.HasAnimation(brokenAnim))
                animSprite.Play(brokenAnim);
        }

        // 胜利判定检查（门可作为过关目标）
        gm?.CheckVictoryCondition();
    }
}
