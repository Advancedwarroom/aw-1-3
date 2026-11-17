using Godot;
using System;
using System.Threading.Tasks;

// ==================== 方案甲 · 千分制开盘加载（BootMapLoader） ====================
// 2026-11-15 立项：开局资源加载(0-500‰) + 战场实例化/开盘初始化(500-1000‰) 全程真进度。
// main.tscn 的 719 格子/单位场景子节点将由 campaigns/main_battlefield.txt（极简格式）
// 在运行时逐行分帧生成——消灭 change_scene_to_packed 的 700+ 节点原子实例化黑块。
//
// ⚠️ 两阶段启用：
//   commit 1（本文）：全部代码 dormant——BOOT_ENABLED=false，任何人拉 main 行为零变化；
//   commit 2：导出首份 campaigns/main_battlefield.txt 并验收后，翻 BOOT_ENABLED=true + 掏空 main.tscn。
public partial class BootMapLoader : Node
{
    // ===== 阶段开关（commit 2 翻转）=====
    public const bool BOOT_ENABLED = true;

    // ===== GDScript 可读状态（loading_screen.gd 轮询）=====
    /// <summary>phase B 子进度 0..500（与 phase A 的 0..500 拼接成全程千分制）。</summary>
    [Export] public int Permille { get; private set; }
    [Export] public string PhaseLabel { get; private set; } = "";
    [Export] public bool IsCompleted { get; private set; }
    [Export] public bool IsFailed { get; private set; }
    [Export] public string FailMessage { get; private set; } = "";

    public static BootMapLoader Instance { get; private set; }

    /// <summary>C# 侧静态可读：loading_screen 请求了 boot 模式（GameManager._Ready 据此跳过自带初始化）。</summary>
    public static bool BootRequested { get; private set; }

    private string _mapPath;
    private int _total;
    private int _done;

    public override void _Ready()
    {
        Instance = this;
    }

    // ===== loading_screen.gd 调用：进入 boot 模式（实例化 main 场景之前）=====
    public void RequestBoot(string mapPath)
    {
        _mapPath = mapPath;
        BootRequested = true;
        PhaseLabel = "部署战场";
        GD.Print($"[BootMapLoader] boot 模式已请求：{mapPath}");
    }

    // ===== GameManager._Ready 末尾调用：接管开盘初始化 =====
    public void StartFromGameManager(GameManager gm)
    {
        _ = BootAsync(gm);
    }

    private async Task BootAsync(GameManager gm)
    {
        try
        {
            string content = null;
            using (var f = FileAccess.Open(_mapPath, FileAccess.ModeFlags.Read))
                if (f != null) content = f.GetAsText();
            if (string.IsNullOrEmpty(content))
            {
                // ✅ 开创人2026-11-18:打包缺文件时回退编译内置图,战场永不空屏
                GD.PushWarning($"[BootMapLoader] 启动地图 {_mapPath} 不可读(err={FileAccess.GetOpenError()})→回退内置应急图");
                content = BootMaps.MainBattlefield;
            }

            // 地图本体：逐行/逐实体分帧生成（TerrainEditor 内需私有 spawn 方法，故由其托管）
            SetPhase("部署战场");
            var te = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
            if (te == null) throw new Exception("TerrainEditor 未找到（terrain_editor 组缺失）");
            await te.BootParseCompactMap(content, this);

            // 游戏级收尾：AI 兜底创建（与 GameManager._Ready 同款逻辑）
            var aiManager = GetTree().GetFirstNodeInGroup("ai_manager") as AI_Manager;
            if (aiManager == null)
            {
                aiManager = new AI_Manager();
                aiManager.Name = "AI_Manager";
                aiManager.gameManager = gm;
                gm.AddChild(aiManager);
            }

            Permille = 500;
            IsCompleted = true;
            GD.Print("[BootMapLoader] ✅ 战场开盘完成");
        }
        catch (Exception ex)
        {
            IsFailed = true;
            FailMessage = ex.Message;
            GD.PushError($"[BootMapLoader] ❌ {ex}");
        }
    }

    // ===== 进度上报（TerrainEditor.BootParse / GridManager.Boot 调用）=====
    public void Begin(int total)
    {
        _total = Math.Max(1, total);
        _done = 0;
        Permille = 0;
    }

    public void Tick(int n = 1)
    {
        _done += n;
        Permille = Math.Min(500, _done * 500 / _total);
    }

    public void SetPhase(string name)
    {
        PhaseLabel = name;
        GD.Print($"[BootMapLoader] {name} {_done}/{_total}");
    }
}
