// RelayJunction.cs - 中转口（Rig 在原管道接缝位置建造的供能中继站）
// 机制（用户规格）：
// 1. Rig 在管道废墟（原接缝位置）临格建造，20/20 判定、2 回合（复用建造模块：总进度20/每回合10）
// 2. 期间 Rig 可被攻击扣血（建造进度受 HP 影响）、移动或被摧毁则从头来（建造模块既有行为）
// 3. 建造完成 → 中转口落地，绑定最近的超级兵工厂 → 兵工厂归属改为建造方（触发缴获红利）
// 4. 中转口是为了利用超级兵工厂，不是为了打开通路
using Godot;
using System.Linq;

public partial class RelayJunction : Facility
{
    /// <summary>绑定的兵工厂锚点相对坐标（相对本格）；空=自动找最近的超级兵工厂</summary>
    [Export] public Godot.Collections.Array<Vector2I> boundFactoryOffsets = new();
    [Export] public bool allowCrossScreenBind = true;   // 🌐 是否可跨屏绑定：false=兵工厂必须与本口同屏，跨屏不接管

    public override void _Ready()
    {
        base._Ready();
        CallDeferred(nameof(TakeOverBoundFactories));
    }

    /// <summary>中转口有队伍 → 全变色（用灰度重着色按势力主色染色）</summary>
    protected override void ApplyPaletteMaterial()
    {
        if (animSprite == null || !IsInstanceValid(animSprite)) return;
        var grayMat = FactionPaletteManager.GetGrayscaleMaterial(facilityTeam);
        if (grayMat == null) return;
        // 每实例独立副本：避免待机变暗 tone 污染共享缓存
        animSprite.Material = (ShaderMaterial)grayMat.Duplicate();
    }

    /// <summary>建造完成落地：接管绑定的超级兵工厂</summary>
    public void TakeOverBoundFactories()
    {
        var gm = GetTree()?.GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.weaponManager == null) return;

        string builderTeam = facilityTeam;
        if (string.IsNullOrEmpty(builderTeam)) builderTeam = TeamHelper.Player1;

        var factories = gm.weaponManager.AllWeapons
            .Where(w => w is SuperFactory && IsInstanceValid(w) && !w.IsQueuedForDeletion())
            .Cast<SuperFactory>()
            .ToList();

        if (factories.Count == 0) return;

        if (boundFactoryOffsets.Count > 0)
        {
            // 按相对坐标绑定
            var map = gm.gridManager?.map;
            var myIdx = ParentGrid?.GridIndex ?? new Vector2I(-9999, -9999);
            foreach (var f in factories)
            {
                if (f.grid == null) continue;
                if (!allowCrossScreenBind && !gm.gridManager.IsSameScreen(myIdx, f.grid.GridIndex)) continue;   // 🌐 异屏厂不接管
                foreach (var off in boundFactoryOffsets)
                {
                    if (f.grid.GridIndex == myIdx + off)
                    {
                        f.TakeOverByRelay(builderTeam);
                        break;
                    }
                }
            }
        }
        else
        {
            // 自动绑定最近的超级兵工厂
            var mine = ParentGrid;
            if (mine == null) return;
            SuperFactory nearest = null;
            float best = float.MaxValue;
            foreach (var f in factories)
            {
                if (f.grid == null) continue;
                if (!allowCrossScreenBind && !gm.gridManager.IsSameScreen(mine.GridIndex, f.grid.GridIndex)) continue;   // 🌐 自动最近也限同屏
                float d = f.grid.GlobalPosition.DistanceSquaredTo(mine.GlobalPosition);
                if (d < best) { best = d; nearest = f; }
            }
            nearest?.TakeOverByRelay(builderTeam);
        }
    }
}
