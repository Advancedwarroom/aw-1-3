using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// ============================================================
// 剧场式战斗动画（复刻 Commander Wars）
// · 数据驱动：攻击/反击每一轮打击记录为 Volley，战斗逻辑不变，仅包装表现层
// · 全局开关：GameManager.enableBattleTheater（[Export]）
// · 单位大图：CoW mask 贴图 + 256x256 LUT 染色（battle_recolor.gdshader）
// · 特殊规则：卫星系→巨型坦克放大×1；机械狗→步兵×5分散；其余自定义→APC×1放大
// ============================================================

public class BattleTheaterVolley
{
    public bool FromAttacker;      // true=攻击方出手，false=防守方反击
    public int Damage;             // 本轮造成的伤害
    public int TargetHpAfter;      // 挨打后目标剩余血量
    public string FireClass = "cannon"; // 武器表现类别（决定枪口闪光/音效/命中特效）
}

public class BattleTheaterSide
{
    public string DisplayName = "";
    public string Team = "Player1";
    public int HpBefore;           // 战前血量（动画起始值）
    public int MaxHealth = 100;
    public bool IsAir;
    public bool IsNaval;
    public string UnitTypeId = "";
    public string CowFolder = "apc"; // CoW battleanimations 文件夹
    public int ForceCount = -1;    // >=0 时强制分身数量
    public float ScaleMul = 1f;    // 额外缩放（卫星等放大）
    public float SpreadMul = 1f;   // 阵型分散倍数（机械狗）
    public bool IsWeapon;          // 兵器/设施：复用地图显示贴图，单个大图，不随血掉
    public Texture2D MapTexture;   // 兵器地图贴图
}

public class BattleTheaterData
{
    public BattleTheaterSide Attacker;
    public BattleTheaterSide Defender;
    public List<BattleTheaterVolley> Volleys = new();

    public void AddVolley(bool fromAttacker, int damage, int targetHpAfter, string fireClass)
    {
        Volleys.Add(new BattleTheaterVolley { FromAttacker = fromAttacker, Damage = damage, TargetHpAfter = targetHpAfter, FireClass = fireClass });
    }
}

// ========== 资源目录/目录映射/特效表（静态） ==========
public static class BattleAnimCatalog
{
    public const string UnitsDir = "res://asscets/BattleAnim/units";
    public const string FxDir = "res://asscets/BattleAnim/GeneralSprites";
    public const string SoundDir = "res://asscets/BattleAnim/sounds";
    public const string ColorTableDir = "res://asscets/BattleAnim/colortables";
    public const string ResXmlPath = "res://asscets/BattleAnim/res.xml";
    public const string RecolorShaderPath = "res://asscets/shaders/battle_recolor.gdshader";

    // aw3 单位类型 → CoW 战斗动画文件夹（有大图 mask 的标准兵种）
    private static readonly Dictionary<string, string> FolderMap = new()
    {
        ["Infantry"] = "infantry", ["Mech"] = "mech", ["Bike"] = "motorbike", ["Recon"] = "recon",
        ["Flare"] = "flare", ["LightTank"] = "light_tank", ["MdTank"] = "heavy_tank",
        ["Megatank"] = "megatank", ["Neotank"] = "neotank", ["Artillery"] = "artillery",
        ["Rocket"] = "rocketthrower", ["AntiAir"] = "flak", ["AntiAirMissile"] = "missile",
        ["AntiTank"] = "antitankcannon", ["APC"] = "apc", ["PipeRunner"] = "piperunner",
        ["Fighter"] = "fighter", ["Bomber"] = "bomber", ["Duster"] = "duster", ["Stealth"] = "stealthbomber",
        ["BCopter"] = "k_heli", ["TCopter"] = "t_heli", ["Lander"] = "lander", ["Gunboat"] = "gunboat",
        ["Cruiser"] = "cruiser", ["Battleship"] = "battleship", ["Carrier"] = "aircraftcarrier",
        ["Submarine"] = "submarine", ["Oozium"] = "hoellium", ["FlyBomb"] = "black_bomb",
        ["Seaplane"] = "waterplane",
    };

    // 特殊复用规则（用户指定）：两种卫星→巨型坦克；机械狗→步兵
    private static readonly Dictionary<string, string> ForceFolder = new()
    {
        ["ForceSatellite"] = "megatank", ["AttackSatellite"] = "megatank", ["Dog"] = "infantry",
    };

    // 血量显示（与 Infantry.UpdateHpLabel 同公式）
    public static int HpBars(int hp, int maxHp)
    {
        if (hp <= 0) return 0;
        float per = maxHp / 10f;
        if (hp <= maxHp) return Mathf.Clamp(Mathf.RoundToInt(hp / per), 1, 10);
        return Mathf.RoundToInt(hp / per);
    }

    public static BattleTheaterSide BuildSide(Infantry u)
    {
        var side = new BattleTheaterSide
        {
            DisplayName = u.UnitTypeId,
            Team = u.team,
            HpBefore = u.health,
            MaxHealth = u.maxHealth,
            IsAir = u.IsAirUnit,
            IsNaval = u.moveType == MoveType.Naval,
            UnitTypeId = u.UnitTypeId,
        };
        string id = u.UnitTypeId;
        if (ForceFolder.TryGetValue(id, out string forced))
        {
            side.CowFolder = forced;
            if (id == "Dog") { side.ForceCount = 5; side.SpreadMul = 1.7f; }        // 机械狗：5个分散开
            else { side.ForceCount = 1; side.ScaleMul = 1.5f; }                      // 卫星系：1个放大
        }
        else if (FolderMap.TryGetValue(id, out string folder))
        {
            side.CowFolder = folder;
        }
        else
        {
            // 其余自定义兵种：1 个大的，复用 APC
            side.CowFolder = "apc";
            side.ForceCount = 1;
            side.ScaleMul = 1.35f;
        }
        return side;
    }

    // ===== 兵器/设施防御方：复用地图显示贴图（黑炮/大型黑炮等新贴图就位后自动生效） =====
    public static BattleTheaterSide BuildSide(Weapon w)
    {
        var side = new BattleTheaterSide
        {
            DisplayName = w.GetType().Name,
            Team = w.team,
            HpBefore = w.health,
            MaxHealth = Math.Max(1, w.maxHealth),
            IsWeapon = true,
            ForceCount = 1,              // 单个大图
            MapTexture = CaptureWeaponTexture(w),
        };
        return side;
    }

    // 抓取兵器当前地图显示帧：animSprite 当前帧 → normal/base 静态贴图依次回退
    private static Texture2D CaptureWeaponTexture(Weapon w)
    {
        if (w.animSprite != null && GodotObject.IsInstanceValid(w.animSprite) && w.animSprite.SpriteFrames != null)
        {
            var sf = w.animSprite.SpriteFrames;
            var names = sf.GetAnimationNames();
            string anim = sf.HasAnimation(w.animSprite.Animation) ? w.animSprite.Animation : (names.Length > 0 ? names[0] : "");
            if (!string.IsNullOrEmpty(anim) && sf.GetFrameCount(anim) > 0)
            {
                int frame = Mathf.Clamp(w.animSprite.Frame, 0, sf.GetFrameCount(anim) - 1);
                var tex = sf.GetFrameTexture(anim, frame);
                if (tex != null) return tex;
            }
        }
        if (w.normalSprite != null && GodotObject.IsInstanceValid(w.normalSprite) && w.normalSprite.Texture != null) return w.normalSprite.Texture;
        if (w.baseSprite != null && GodotObject.IsInstanceValid(w.baseSprite) && w.baseSprite.Texture != null) return w.baseSprite.Texture;
        return null;
    }

    // 武器表现类别：决定枪口闪光/开火音效/命中特效
    public static string GetFireClass(Infantry shooter, WeaponType wt, Infantry target)
    {
        if (shooter == null) return "cannon";
        if (shooter.isAntiAirUnit && target != null && target.IsAirUnit) return "flak";
        if (wt == WeaponType.Secondary) return "mg";
        switch (shooter.UnitTypeId)
        {
            case "Mech": return "bazooka";
            case "Rocket": return "rocket";
            case "Artillery": return "artillery";
            case "AntiAirMissile": return "missile";
            case "Fighter":
            case "Stealth": return "missile";
            case "Bomber": return "rocket";
            case "Battleship":
            case "Cruiser":
            case "Carrier": return "cannon_heavy";
        }
        if (shooter.useMinMaxAttackRange && shooter.minAttackRange > 1) return "artillery";
        return "cannon";
    }

    // 开火表现表：枪口闪光 / 开火音 / 命中特效 / 命中音
    public readonly struct FireFx
    {
        public readonly string Shot, ShotSnd, Hit, HitSnd;
        public FireFx(string shot, string shotSnd, string hit, string hitSnd) { Shot = shot; ShotSnd = shotSnd; Hit = hit; HitSnd = hitSnd; }
    }
    private static readonly Dictionary<string, FireFx> FxMap = new()
    {
        ["mg"] = new FireFx("mg_shot.png", "mg_weapon_fire.wav", "mg_hit.png", "mg_impact.wav"),
        ["cannon"] = new FireFx("medium_shot.png", "tank_shot.wav", "cannon_hit.png", "tank_hit.wav"),
        ["cannon_heavy"] = new FireFx("battleship_shot.png", "cannon_weapon_fire.wav", "cannon_heavy_hit.png", "impact_explosion.wav"),
        ["artillery"] = new FireFx("artillery_shot.png", "cannon_weapon_fire.wav", "artillery_hit.png", "artillery_explode.wav"),
        ["rocket"] = new FireFx("rocket_launch.png", "rocket_launch.wav", "rocket_hit.png", "rockets_explode.wav"),
        ["missile"] = new FireFx("rocket_launch.png", "missile_weapon_fire.wav", "rocket_hit_air.png", "rockets_explode.wav"),
        ["flak"] = new FireFx("flak_shot_air.png", "vulcan_weapon_fire.wav", "flak_hit.png", "explosion+air.wav"),
        ["bazooka"] = new FireFx("bazooka_launch.png", "baazoka_fire.wav", "cannon_hit.png", "impact_explosion.wav"),
    };
    public static FireFx GetFx(string fireClass) => FxMap.TryGetValue(fireClass ?? "cannon", out var fx) ? fx : FxMap["cannon"];

    public static void GetDyingFx(BattleTheaterSide side, out string explosionPng, out string explosionSnd, out string dyingSnd)
    {
        if (side.IsAir) { explosionPng = "unit_explosion_air.png"; explosionSnd = "explosion+air.wav"; dyingSnd = "airunit_dying.wav"; }
        else if (side.IsNaval) { explosionPng = "unit_explosion.png"; explosionSnd = "explosion+water.wav"; dyingSnd = "ship_dying_move.wav"; }
        else { explosionPng = "unit_explosion.png"; explosionSnd = "explosion+land.wav"; dyingSnd = "tank_explode.wav"; }
    }

    // ---------- res.xml 帧数表 ----------
    private static Dictionary<string, int> _colsMap;
    private static void EnsureColsMap()
    {
        if (_colsMap != null) return;
        _colsMap = new Dictionary<string, int>();
        if (!FileAccess.FileExists(ResXmlPath)) return;
        string xml = FileAccess.GetFileAsString(ResXmlPath);
        foreach (Match m in Regex.Matches(xml, "<image\\s+file=\"([^\"]+)\"\\s+cols\\s*=\\s*\"(\\d+)\""))
            _colsMap[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
    }
    public static int GetCols(string relativeFile)
    {
        EnsureColsMap();
        return _colsMap.TryGetValue(relativeFile, out int c) ? Math.Max(1, c) : 1;
    }

    // ---------- 贴图条 → SpriteFrames（缓存） ----------
    private static readonly Dictionary<string, SpriteFrames> _framesCache = new();
    public static SpriteFrames BuildStripFrames(string resPath, int cols, float fps, bool loop)
    {
        string key = $"{resPath}|{cols}|{fps}|{loop}";
        if (_framesCache.TryGetValue(key, out var cached)) return cached;
        var tex = GD.Load<Texture2D>(resPath);
        if (tex == null) return null;
        cols = Math.Max(1, cols);
        int fw = tex.GetWidth() / cols;
        int fh = tex.GetHeight();
        var sf = new SpriteFrames();
        sf.RemoveAnimation("default");
        sf.AddAnimation("play");
        sf.SetAnimationSpeed("play", fps);
#pragma warning disable CS0618 // Godot 4.7 标记过时但功能正常；新API枚举名跨版本不稳定
        sf.SetAnimationLoop("play", loop);
#pragma warning restore CS0618
        for (int i = 0; i < cols; i++)
        {
            var at = new AtlasTexture { Atlas = tex, Region = new Rect2(i * fw, 0, fw, fh) };
            sf.AddFrame("play", at);
        }
        _framesCache[key] = sf;
        return sf;
    }

    // 单位三态动画（idle/fire/move），按 CoW 文件命名回退解析
    public static SpriteFrames BuildUnitFrames(string folder, out bool hasFire, out bool hasMove)
    {
        string idleRel = $"{folder}/{folder}+os+mask.png";
        string fireRel = $"{folder}/{folder}+os+fire+mask.png";
        string moveRel = null;
        foreach (string cand in new[] { $"{folder}+os+move+mask.png", $"{folder}+os+walk+mask.png" })
        {
            if (ResourceLoader.Exists($"{UnitsDir}/{cand}")) { moveRel = $"{folder}/{cand}"; break; }
        }
        hasFire = ResourceLoader.Exists($"{UnitsDir}/{fireRel}");
        hasMove = moveRel != null;

        var sf = new SpriteFrames();
        sf.RemoveAnimation("default");
        AddStrip(sf, "idle", $"{UnitsDir}/{idleRel}", GetCols(idleRel), 7f, true);
        if (hasFire) AddStrip(sf, "fire", $"{UnitsDir}/{fireRel}", GetCols(fireRel), 12f, false);
        if (hasMove) AddStrip(sf, "move", $"{UnitsDir}/{moveRel}", GetCols(moveRel), 10f, true);
        return sf;
    }

    private static void AddStrip(SpriteFrames sf, string anim, string resPath, int cols, float fps, bool loop)
    {
        var tex = GD.Load<Texture2D>(resPath);
        if (tex == null) return;
        cols = Math.Max(1, cols);
        int fw = tex.GetWidth() / cols;
        int fh = tex.GetHeight();
        if (!sf.HasAnimation(anim)) sf.AddAnimation(anim);
        sf.SetAnimationSpeed(anim, fps);
#pragma warning disable CS0618 // Godot 4.7 标记过时但功能正常；新API枚举名跨版本不稳定
        sf.SetAnimationLoop(anim, loop);
#pragma warning restore CS0618
        for (int i = 0; i < cols; i++)
            sf.AddFrame(anim, new AtlasTexture { Atlas = tex, Region = new Rect2(i * fw, 0, fw, fh) });
    }

    // ---------- LUT 染色 ----------
    private static Shader _recolorShader;
    private static readonly Dictionary<string, ShaderMaterial> _matCache = new();
    public static ShaderMaterial GetRecolorMaterial(string tableName)
    {
        if (string.IsNullOrEmpty(tableName)) tableName = "neutral";
        if (_matCache.TryGetValue(tableName, out var cached) && GodotObject.IsInstanceValid(cached)) return cached;
        _recolorShader ??= GD.Load<Shader>(RecolorShaderPath);
        var table = GD.Load<Texture2D>($"{ColorTableDir}/{tableName}.png");
        if (_recolorShader == null || table == null) return null;
        var mat = new ShaderMaterial { Shader = _recolorShader };
        mat.SetShaderParameter("color_table", table);
        _matCache[tableName] = mat;
        return mat;
    }

    // ---------- 音效 ----------
    private static readonly Dictionary<string, AudioStream> _sndCache = new();
    public static AudioStream LoadSound(string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        if (_sndCache.TryGetValue(file, out var cached)) return cached;
        string path = $"{SoundDir}/{file}";
        var snd = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
        _sndCache[file] = snd;
        return snd;
    }
}

// ========== 剧场本体 ==========
public partial class BattleTheater : CanvasLayer
{
    // 五分身阵型（攻击方面朝右；防守方镜像）
    private static readonly Vector2[] Formation =
    {
        new Vector2(0, 0), new Vector2(-36, 16), new Vector2(-36, -16), new Vector2(-72, 32), new Vector2(-72, -32),
    };

    private float _speed = 1f;
    private float _unitScale = 2f;
    private float _volumeDb = -8f;
    private bool _ff;                 // 点击快进
    private BattleTheaterData _data;

    private class SideNodes
    {
        public BattleTheaterSide Side;
        public Node2D Root;
        public Label NameLabel, HpLabel;
        public ColorRect BarFg;
        public List<AnimatedSprite2D> Alive = new();
        public int CurHp;
        public int MaxReps;
        public bool Dead;
        public Vector2 BasePos;
    }

    // ---------- 入口 ----------
    public static BattleTheaterData TryCapture(Infantry attacker, Infantry defender)
    {
        if (attacker?.GetTree() == null || defender == null) return null;
        var gm = attacker.GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || !gm.enableBattleTheater) return null;
        var ai = attacker.GetTree().GetFirstNodeInGroup("ai_manager") as AI_Manager;
        if (ai != null && ai.IsAiControlling(attacker.team) && !gm.battleTheaterForAI) return null;
        return new BattleTheaterData
        {
            Attacker = BattleAnimCatalog.BuildSide(attacker),
            Defender = BattleAnimCatalog.BuildSide(defender),
        };
    }

    // 兵器/设施防御方入口（单位打兵器：攻击方照常 CoW 大图，防御方复用地图贴图）
    public static BattleTheaterData TryCapture(Infantry attacker, Weapon defender)
    {
        if (attacker?.GetTree() == null || defender == null) return null;
        var gm = attacker.GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm == null || !gm.enableBattleTheater) return null;
        var ai = attacker.GetTree().GetFirstNodeInGroup("ai_manager") as AI_Manager;
        if (ai != null && ai.IsAiControlling(attacker.team) && !gm.battleTheaterForAI) return null;
        var defSide = BattleAnimCatalog.BuildSide(defender);
        if (defSide.MapTexture == null) return null;   // 拿不到贴图就不开剧场
        return new BattleTheaterData
        {
            Attacker = BattleAnimCatalog.BuildSide(attacker),
            Defender = defSide,
        };
    }

    public static void Play(BattleTheaterData data, Infantry context)
    {
        if (data == null || data.Volleys.Count == 0 || context?.GetTree() == null) return;
        var gm = context.GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        var theater = new BattleTheater { Layer = 128, _data = data };
        if (gm != null)
        {
            theater._speed = Mathf.Clamp(gm.battleTheaterSpeed, 0.25f, 4f);
            theater._unitScale = Mathf.Clamp(gm.battleTheaterUnitScale, 1f, 4f);
            theater._volumeDb = gm.battleTheaterVolumeDb;
        }
        context.GetTree().CurrentScene.AddChild(theater);
        _ = theater.RunSequence(gm);
    }

    // ---------- 主流程 ----------
    private async Task RunSequence(GameManager gm)
    {
        try
        {
            var root = BuildUi(gm);
            var sideA = BuildSideNodes(_data.Attacker, true, root);
            var sideD = BuildSideNodes(_data.Defender, false, root);
            await FadeDim(root, true);

            // 入场滑动
            await MoveIn(sideA, sideD);

            foreach (var volley in _data.Volleys)
            {
                var shooter = volley.FromAttacker ? sideA : sideD;
                var target = volley.FromAttacker ? sideD : sideA;
                if (shooter.Dead) continue;
                await PlayVolley(shooter, target, volley);
            }

            await Wait(0.5f);
            await FadeDim(root, false);
        }
        catch (Exception) { /* 表现层异常绝不影响战斗逻辑 */ }
        QueueFree();
    }

    // ---------- UI 搭建 ----------
    private Control BuildUi(GameManager gm)
    {
        bool clickSkip = gm == null || gm.battleTheaterClickSkip;
        var root = new Control { Name = "BattleTheaterRoot", MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var dim = new ColorRect { Name = "Dim", Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(dim);

        var stage = new Node2D { Name = "Stage" };
        root.AddChild(stage);

        if (clickSkip)
        {
            var hint = new Label { Name = "SkipHint", Text = TranslateGroup.T("点击屏幕跳过 ▸▸") };
            hint.AddThemeFontSizeOverride("font_size", 14);
            hint.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.55f));
            hint.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            hint.Position = new Vector2(-150, -30);
            root.AddChild(hint);
        }

        root.GuiInput += e =>
        {
            bool pressed = e is InputEventMouseButton { Pressed: true }
                        || e is InputEventScreenTouch { Pressed: true }
                        || e is InputEventKey { Pressed: true };
            if (pressed && clickSkip)
            {
                _ff = true;
                var hint = root.GetNodeOrNull<Label>("SkipHint");
                if (hint != null) hint.Visible = false;
            }
        };
        return root;
    }

    private SideNodes BuildSideNodes(BattleTheaterSide side, bool isAttacker, Control root)
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float facing = isAttacker ? 1f : -1f;
        var basePos = new Vector2(vp.X * (isAttacker ? 0.30f : 0.70f), vp.Y * (side.IsAir ? 0.56f : 0.64f));
        float scale = _unitScale * side.ScaleMul;

        var sn = new SideNodes { Side = side, BasePos = basePos, CurHp = side.HpBefore };
        sn.Root = new Node2D { Name = isAttacker ? "AttackerSide" : "DefenderSide", Position = basePos, Scale = Vector2.One * scale };

        // 地面椭圆
        if (!side.IsAir)
        {
            var pts = new Vector2[24];
            for (int i = 0; i < 24; i++)
            {
                float a = Mathf.Tau * i / 24f;
                pts[i] = new Vector2(Mathf.Cos(a) * 85f, Mathf.Sin(a) * 16f);
            }
            var ground = new Polygon2D { Polygon = pts, Color = new Color(0.14f, 0.18f, 0.13f, 0.9f) };
            sn.Root.AddChild(ground);
        }

        // 分身
        int hpDisp = BattleAnimCatalog.HpBars(side.HpBefore, side.MaxHealth);
        int count = side.ForceCount >= 0 ? side.ForceCount : Mathf.Clamp((hpDisp + 1) / 2, 1, 5);
        sn.MaxReps = count;
        if (side.IsWeapon && side.MapTexture != null)
        {
            // 兵器：单帧 AnimatedSprite2D 复用地图贴图（不翻转、不染色），按目标高度自适应放大
            var sf = new SpriteFrames();
            sf.RemoveAnimation("default");
            sf.AddAnimation("idle");
            sf.AddFrame("idle", side.MapTexture);
            var rep = new AnimatedSprite2D { SpriteFrames = sf };
            float texH = Mathf.Max(1f, side.MapTexture.GetHeight());
            rep.Scale = Vector2.One * (96f / texH);   // 根节点还会乘 _unitScale
            rep.Play("idle");
            sn.Root.AddChild(rep);
            sn.Alive.Add(rep);
        }
        else
        {
            var frames = BattleAnimCatalog.BuildUnitFrames(side.CowFolder, out bool hasFire, out bool hasMove);
            var mat = BattleAnimCatalog.GetRecolorMaterial(TableForTeam(side.Team));
            for (int i = 0; i < count; i++)
            {
                var rep = new AnimatedSprite2D { SpriteFrames = frames, Material = mat, FlipH = !isAttacker };
                int fi = i % Formation.Length;
                Vector2 off = Formation[fi] * side.SpreadMul;
                off.X *= -facing; // 防守方镜像（后排向外）
                rep.Position = off;
                if (frames != null && frames.HasAnimation("idle")) rep.Play("idle");
                sn.Root.AddChild(rep);
                sn.Alive.Add(rep);
            }
            _ = hasFire; _ = hasMove;
        }

        var stage = root.GetNode<Node2D>("Stage");
        stage.AddChild(sn.Root);

        // 名称 / HP 标签 / 血条（屏幕空间，随 side 位置）
        var nameLabel = new Label { Text = side.DisplayName };
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameLabel.AddThemeColorOverride("font_color", TeamHelper.GetTeamColor(side.Team));
        nameLabel.AddThemeConstantOverride("outline_size", 4);
        nameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        float uiX = basePos.X - 60;
        nameLabel.Position = new Vector2(uiX, basePos.Y - 118 * scale * 0.55f);
        root.AddChild(nameLabel);
        sn.NameLabel = nameLabel;

        var hpLabel = new Label { Text = BattleAnimCatalog.HpBars(side.HpBefore, side.MaxHealth).ToString() };
        hpLabel.AddThemeFontSizeOverride("font_size", 30);
        hpLabel.AddThemeColorOverride("font_color", Colors.White);
        hpLabel.AddThemeConstantOverride("outline_size", 5);
        hpLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        hpLabel.Position = new Vector2(uiX + 96, basePos.Y - 128 * scale * 0.55f);
        root.AddChild(hpLabel);
        sn.HpLabel = hpLabel;

        var barBg = new ColorRect { Color = new Color(0.1f, 0.1f, 0.1f, 0.85f), Position = new Vector2(uiX, basePos.Y - 92 * scale * 0.5f), Size = new Vector2(104, 10), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(barBg);
        var barFg = new ColorRect { Color = HpColor(side.HpBefore, side.MaxHealth), Position = barBg.Position + new Vector2(2, 2), Size = new Vector2(100, 6), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(barFg);
        sn.BarFg = barFg;

        return sn;
    }

    private string TableForTeam(string team)
    {
        var gm = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (gm?.battleTheaterColorTables != null && team != null && gm.battleTheaterColorTables.TryGetValue(team, out string t))
            return t;
        return gm?.battleTheaterFallbackTable ?? "neutral";
    }

    private static Color HpColor(int hp, int maxHp)
    {
        float r = BattleAnimCatalog.HpBars(hp, maxHp) / 10f;
        if (r > 0.5f) return new Color(0.3f, 0.9f, 0.3f);
        if (r > 0.25f) return new Color(0.95f, 0.8f, 0.2f);
        return new Color(0.95f, 0.25f, 0.2f);
    }

    // ---------- 演出节拍 ----------
    private async Task FadeDim(Control root, bool fadeIn)
    {
        var dim = root.GetNode<ColorRect>("Dim");
        float target = fadeIn ? 0.62f : 0f;
        var tw = CreateTween();
        tw.TweenProperty(dim, "color:a", target, _ff ? 0.03 : 0.3 / _speed);
        await ToSignal(tw, Tween.SignalName.Finished);
    }

    private async Task MoveIn(SideNodes a, SideNodes d)
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        a.Root.Position = a.BasePos + new Vector2(-vp.X * 0.35f, 0);
        d.Root.Position = d.BasePos + new Vector2(vp.X * 0.35f, 0);
        PlayMoveAnim(a); PlayMoveAnim(d);
        var tw = CreateTween().SetParallel();
        float dur = _ff ? 0.03f : 0.45f / _speed;
        tw.TweenProperty(a.Root, "position", a.BasePos, dur).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(d.Root, "position", d.BasePos, dur).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        await ToSignal(tw, Tween.SignalName.Finished);
        PlayIdleAnim(a); PlayIdleAnim(d);
    }

    private static void PlayMoveAnim(SideNodes s)
    {
        foreach (var r in s.Alive)
            if (r.SpriteFrames != null && r.SpriteFrames.HasAnimation("move")) r.Play("move");
    }
    private static void PlayIdleAnim(SideNodes s)
    {
        foreach (var r in s.Alive)
            if (r.SpriteFrames != null && r.SpriteFrames.HasAnimation("idle")) r.Play("idle");
    }

    private async Task PlayVolley(SideNodes shooter, SideNodes target, BattleTheaterVolley volley)
    {
        var fx = BattleAnimCatalog.GetFx(volley.FireClass);
        bool attackerShooting = volley.FromAttacker;
        float facing = attackerShooting ? 1f : -1f;

        // 1) 开火动画 + 枪口闪光 + 音效
        float fireDur = 0.45f;
        foreach (var r in shooter.Alive.ToArray())
        {
            if (r.SpriteFrames != null && r.SpriteFrames.HasAnimation("fire"))
            {
                r.Play("fire");
                fireDur = Mathf.Max(fireDur, r.SpriteFrames.GetFrameCount("fire") / 12f + 0.15f);
            }
        }
        SpawnOneShot(BattleAnimCatalog.FxDir + "/" + fx.Shot, shooter.BasePos + new Vector2(facing * 46f * _unitScale, -14f * _unitScale), !attackerShooting);
        PlaySnd(fx.ShotSnd);
        await Wait(fireDur);
        PlayIdleAnim(shooter);

        // 2) 命中特效 + 震屏 + 受击闪白
        SpawnOneShot(BattleAnimCatalog.FxDir + "/" + fx.Hit, target.BasePos + new Vector2(0, -18f * _unitScale), attackerShooting);
        PlaySnd(fx.HitSnd);
        Shake(target);
        Flash(target);

        // 3) 伤害数字
        SpawnDamageLabel(target, volley.Damage);

        // 4) HP 倒计时 + 分身死亡
        int fromHp = target.CurHp;
        int toHp = Math.Max(0, volley.TargetHpAfter);
        int steps = BattleAnimCatalog.HpBars(fromHp, target.Side.MaxHealth) - BattleAnimCatalog.HpBars(toHp, target.Side.MaxHealth);
        target.CurHp = toHp;
        for (int i = 1; i <= Math.Max(steps, 0); i++)
        {
            int hp = fromHp + (toHp - fromHp) * i / steps;
            UpdateHpUi(target, hp);
            TrimReplicas(target, hp);
            await Wait(0.11f);
        }
        UpdateHpUi(target, toHp);
        TrimReplicas(target, toHp);

        // 5) 单位全灭：大爆炸 + 死亡音效
        if (toHp <= 0 && !target.Dead)
        {
            target.Dead = true;
            BattleAnimCatalog.GetDyingFx(target.Side, out string expPng, out string expSnd, out string dyingSnd);
            SpawnOneShot(BattleAnimCatalog.FxDir + "/" + expPng, target.BasePos + new Vector2(0, -16f * _unitScale), attackerShooting, 1.6f);
            PlaySnd(dyingSnd);
            PlaySnd(expSnd);
            foreach (var r in target.Alive.ToArray()) KillReplica(target, r);
            FadeSideUi(target);
            await Wait(0.55f);
        }
        else
        {
            await Wait(0.3f);
        }
    }

    private void UpdateHpUi(SideNodes s, int hp)
    {
        int bars = BattleAnimCatalog.HpBars(hp, s.Side.MaxHealth);
        s.HpLabel.Text = bars.ToString();
        s.BarFg.Size = new Vector2(100f * Mathf.Clamp(bars / 10f, 0f, 1f), 6);
        s.BarFg.Color = HpColor(hp, s.Side.MaxHealth);
    }

    private void TrimReplicas(SideNodes s, int hp)
    {
        if (s.Side.ForceCount >= 0) return; // 强制数量（卫星/狗/自定义大兵）不随血掉
        int want = Mathf.Clamp((BattleAnimCatalog.HpBars(hp, s.Side.MaxHealth) + 1) / 2, hp > 0 ? 1 : 0, s.MaxReps);
        while (s.Alive.Count > want) KillReplica(s, s.Alive[^1]);
    }

    private void KillReplica(SideNodes s, AnimatedSprite2D r)
    {
        if (!s.Alive.Remove(r) || !IsInstanceValid(r)) return;
        var tw = r.CreateTween();
        tw.TweenProperty(r, "modulate", new Color(1.6f, 0.4f, 0.4f), 0.08f / _speed);
        tw.TweenProperty(r, "modulate", new Color(1, 1, 1, 0), 0.22f / _speed);
        tw.Parallel().TweenProperty(r, "scale", r.Scale * 0.75f, 0.3f / _speed);
        tw.TweenCallback(Callable.From(r.QueueFree));
    }

    private void FadeSideUi(SideNodes s)
    {
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(s.NameLabel, "modulate", new Color(1, 1, 1, 0.35f), 0.4f / _speed);
        tw.TweenProperty(s.HpLabel, "modulate", new Color(1, 1, 1, 0.35f), 0.4f / _speed);
        tw.TweenProperty(s.BarFg, "modulate", new Color(1, 1, 1, 0.35f), 0.4f / _speed);
    }

    private void Shake(SideNodes s)
    {
        var tw = s.Root.CreateTween();
        for (int i = 0; i < 3; i++)
        {
            var off = new Vector2((float)GD.RandRange(-5, 5), (float)GD.RandRange(-3, 3));
            tw.TweenProperty(s.Root, "position", s.BasePos + off, 0.045f / _speed);
        }
        tw.TweenProperty(s.Root, "position", s.BasePos, 0.06f / _speed);
    }

    private void Flash(SideNodes s)
    {
        foreach (var r in s.Alive)
        {
            var tw = r.CreateTween();
            tw.TweenProperty(r, "modulate", new Color(4f, 4f, 4f), 0.05f / _speed);
            tw.TweenProperty(r, "modulate", Colors.White, 0.15f / _speed);
        }
    }

    private void SpawnDamageLabel(SideNodes target, int damage)
    {
        var label = new Label { Text = $"-{damage}", Position = target.BasePos + new Vector2(30, -110 * _unitScale * 0.55f), ZIndex = 10 };
        label.AddThemeFontSizeOverride("font_size", 26);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.35f, 0.25f));
        label.AddThemeConstantOverride("outline_size", 5);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        GetNode<Control>("BattleTheaterRoot").AddChild(label);
        var tw = label.CreateTween().SetParallel();
        tw.TweenProperty(label, "position:y", label.Position.Y - 34, 0.7f / _speed).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(label, "modulate:a", 0f, 0.7f / _speed);
        tw.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    private void SpawnOneShot(string resPath, Vector2 pos, bool flip, float scaleMul = 1f)
    {
        string file = resPath[(resPath.LastIndexOf('/') + 1)..];
        string rel = resPath.StartsWith(BattleAnimCatalog.FxDir) ? $"GeneralSprites/{file}" : file;
        int cols = BattleAnimCatalog.GetCols(rel);
        var frames = BattleAnimCatalog.BuildStripFrames(resPath, cols, 14f, false);
        if (frames == null) return;
        var spr = new AnimatedSprite2D
        {
            SpriteFrames = frames,
            Position = pos,
            FlipH = flip,
            Scale = Vector2.One * _unitScale * scaleMul,
            ZIndex = 5,
        };
        GetNode<Control>("BattleTheaterRoot").AddChild(spr);
        spr.Play("play");
        spr.AnimationFinished += spr.QueueFree;
    }

    private void PlaySnd(string file)
    {
        var stream = BattleAnimCatalog.LoadSound(file);
        if (stream == null) return;
        var p = new AudioStreamPlayer { Stream = stream, VolumeDb = _volumeDb };
        AddChild(p);
        p.Play();
        p.Finished += p.QueueFree;
    }

    // 帧驱动等待：点击快进即时生效
    private async Task Wait(float seconds)
    {
        seconds /= _speed;
        if (_ff) seconds = Math.Min(seconds, 0.03f);
        double t = 0;
        while (t < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
        }
    }
}
