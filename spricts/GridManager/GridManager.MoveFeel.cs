using Godot;
using System;
using System.Collections.Generic;

// GridManager · MoveFeel（复刻 CoW 移动手感：沿路径行走动画 + 路径方向箭头；[Export] 全自由化）
public partial class GridManager
{
    [ExportGroup("移动手感 (CoW)")]
    [Export] public bool enableWalkAnim = true;                          // 移动时沿路径行走动画（关=旧瞬移）
    [Export(PropertyHint.Range, "0.02,0.5,0.01")] public float walkStepTime = 0.11f;  // 行走每格耗时(秒)
    [Export] public bool walkFlipFacing = true;                          // 行走时按水平方向翻转朝向
    [Export] public bool enableMoveArrows = true;                        // 悬停/画路径时显示方向箭头（CoW arrow+unit）
    [Export] public Texture2D moveArrowTexture;                          // 箭头图集(10列x16px)，空=自动加载默认
    [Export] public bool moveArrowsReplaceLine = false;                  // true=只显示箭头不显示黄线

    private const string DefaultMoveArrowPath = "res://asscets/UI/move_arrows.png";

    // CoW arrow+unit.png 列序（16px/列）
    private const int ArrowLeftRight = 0, ArrowUpDown = 1, ArrowUpRight = 2, ArrowUpLeft = 3,
                      ArrowDownRight = 4, ArrowDownLeft = 5, ArrowRight = 6, ArrowLeft = 7, ArrowUp = 8, ArrowDown = 9;

    private Node2D moveArrowLayer;
    private int _walkAnimLocks;
    public bool IsWalkAnimPlaying => _walkAnimLocks > 0;   // 行走动画播放中（用于锁定点击）

    // ========== 路径方向箭头 ==========
    public void UpdateMoveArrows(int pointCount = -1)
    {
        if (!enableMoveArrows) { ClearMoveArrows(); return; }
        EnsureArrowLayer();
        ClearMoveArrows();
        if (previewPath == null || previewPath.Count < 2) return;

        var tex = moveArrowTexture ?? (moveArrowTexture = GD.Load<Texture2D>(DefaultMoveArrowPath));
        if (tex == null) return;
        int count = pointCount < 0 ? previewPath.Count : Mathf.Min(pointCount, previewPath.Count);
        if (count < 2) return;

        float cell = gridSize.X;
        float scale = cell / 16f;
        int cols = Mathf.Max(1, tex.GetWidth() / 16);

        for (int i = 1; i < count; i++)
        {
            var prev = previewPath[i - 1];
            var cur = previewPath[i];
            var next = i + 1 < count ? previewPath[i + 1] : null;
            if (prev == null || cur == null) continue;

            int col;
            if (next == null)
            {
                // 终点：行进方向箭头尖
                int dx = cur.GridIndex.X - prev.GridIndex.X;
                int dy = cur.GridIndex.Y - prev.GridIndex.Y;
                col = dx > 0 ? ArrowRight : dx < 0 ? ArrowLeft : dy > 0 ? ArrowDown : ArrowUp;
            }
            else
            {
                // 中间点：按"连接两侧方向"选直箭/弯箭（与 CoW createArrow 同规则）
                int px = prev.GridIndex.X - cur.GridIndex.X, py = prev.GridIndex.Y - cur.GridIndex.Y;
                int nx = next.GridIndex.X - cur.GridIndex.X, ny = next.GridIndex.Y - cur.GridIndex.Y;
                bool prevH = px != 0, nextH = nx != 0;
                if (prevH && nextH) col = ArrowLeftRight;
                else if (!prevH && !nextH) col = ArrowUpDown;
                else
                {
                    bool connectsDown = py > 0 || ny > 0;
                    bool connectsUp = py < 0 || ny < 0;
                    bool connectsRight = px > 0 || nx > 0;
                    bool connectsLeft = px < 0 || nx < 0;
                    if (connectsDown && connectsRight) col = ArrowDownRight;
                    else if (connectsDown && connectsLeft) col = ArrowDownLeft;
                    else if (connectsUp && connectsRight) col = ArrowUpRight;
                    else col = ArrowUpLeft;
                }
            }
            col = Mathf.Clamp(col, 0, cols - 1);

            var spr = new Sprite2D
            {
                Texture = new AtlasTexture { Atlas = tex, Region = new Rect2(col * 16, 0, 16, 16) },
                Position = cur.Position + new Vector2(cell * 0.5f, cell * 0.5f),
                Scale = new Vector2(scale, scale),
            };
            moveArrowLayer.AddChild(spr);
        }
    }

    public void ClearMoveArrows()
    {
        if (moveArrowLayer == null) return;
        foreach (var child in moveArrowLayer.GetChildren())
            child.QueueFree();
    }

    private void EnsureArrowLayer()
    {
        if (moveArrowLayer != null && IsInstanceValid(moveArrowLayer)) return;
        moveArrowLayer = new Node2D { Name = "MoveArrowLayer", ZIndex = 100 };
        AddChild(moveArrowLayer);
    }

    // ========== 沿路径行走动画 ==========
    public void StartWalkAlongPath(Infantry infantry, List<Grids> path, Action onFinished)
    {
        if (!enableWalkAnim || infantry == null || path == null || path.Count <= 1)
        {
            onFinished?.Invoke();
            return;
        }
        _walkAnimLocks++;

        var spr = infantry.animSprite != null && IsInstanceValid(infantry.animSprite)
            ? infantry.animSprite
            : infantry.baseAnimSprite;
        string moveAnim = infantry.GetMoveAnimName();
        string idleAnim = infantry.GetIdleAnimName();
        bool animSwitched = false;
        if (spr != null && moveAnim != null && spr.SpriteFrames != null && spr.SpriteFrames.HasAnimation(moveAnim))
        {
            spr.Play(moveAnim);
            animSwitched = true;
        }

        float step = Mathf.Max(0.02f, walkStepTime);
        var tween = infantry.CreateTween();
        for (int i = 1; i < path.Count; i++)
        {
            if (walkFlipFacing)
            {
                int dx = path[i].GridIndex.X - path[i - 1].GridIndex.X;
                if (dx != 0)
                {
                    bool faceLeft = dx < 0;
                    tween.TweenCallback(Callable.From(() => SetWalkFacing(infantry, faceLeft)));
                }
            }
            tween.TweenProperty(infantry, "position", path[i].Position, step).SetTrans(Tween.TransitionType.Linear);
        }
        tween.TweenCallback(Callable.From(() =>
        {
            SetWalkFacing(infantry, false);
            if (animSwitched && spr != null && IsInstanceValid(spr) && idleAnim != null
                && spr.SpriteFrames != null && spr.SpriteFrames.HasAnimation(idleAnim))
                spr.Play(idleAnim);
            _walkAnimLocks = Math.Max(0, _walkAnimLocks - 1);
            onFinished?.Invoke();
        }));

        // 容错：单位中途被销毁也必须解锁
        infantry.Connect(Node.SignalName.TreeExited, Callable.From(() =>
        {
            _walkAnimLocks = Math.Max(0, _walkAnimLocks - 1);
        }), (uint)GodotObject.ConnectFlags.OneShot);
    }

    private void SetWalkFacing(Infantry u, bool faceLeft)
    {
        if (u == null) return;
        if (u.animSprite != null && IsInstanceValid(u.animSprite)) u.animSprite.FlipH = faceLeft;
        if (u.baseAnimSprite != null && IsInstanceValid(u.baseAnimSprite)) u.baseAnimSprite.FlipH = faceLeft;
        // ✅ issue#1 多格机动单位：FlipH 时按足印宽度补偿偏移，翻转后的身体始终盖在足印上（贴图默认朝右绘制）
        if (u.isMultiTileUnit)
            u.ApplyFacingFlipOffset(faceLeft, gridSize.X);
    }
}
