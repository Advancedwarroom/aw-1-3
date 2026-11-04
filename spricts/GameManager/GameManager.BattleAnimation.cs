using Godot;

// GameManager · 剧场式战斗动画配置（复刻 CoW；[Export] 全自由化，可在编辑器/检查器直接调）
public partial class GameManager
{
    [ExportGroup("剧场式战斗动画")]
    [Export] public bool enableBattleTheater = true;                 // ✅ 全局总开关：关闭则退回旧即时战斗表现
    [Export] public bool battleTheaterForAI = false;                 // AI 回合是否也播放剧场动画（默认关，避免拖慢 AI 回合）
    [Export] public bool battleTheaterClickSkip = true;              // 点击/按键快进动画
    [Export(PropertyHint.Range, "0.25,4,0.05")] public float battleTheaterSpeed = 1.0f;   // 播放速度倍率
    [Export(PropertyHint.Range, "1,4,0.05")] public float battleTheaterUnitScale = 2.0f;  // 单位大图整体缩放
    [Export(PropertyHint.Range, "-40,6,1")] public float battleTheaterVolumeDb = -8f;     // 音效音量
    [Export] public Godot.Collections.Dictionary<string, string> battleTheaterColorTables = new()
    {
        // 队伍 → CoW 染色 LUT 表（asscets/BattleAnim/colortables/*.png，共34张可选）
        { "Player1", "red" },
        { "Player2", "blue_moon" },
        { "Player3", "yellow_comet" },
        { "Player4", "green_earth" },
        { "Player5", "purple" },
        { "Player", "neutral" },
    };
    [Export] public string battleTheaterFallbackTable = "neutral";   // 未配置队伍时的默认染色表
}
