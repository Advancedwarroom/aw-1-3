// SurvivalUIDesign.cs - 生存模式UI美学配置 (开创人2026-11-14)
// 万物可控延伸到界面层:位置/字号/时长/配色/稀有度色值全部Export\n// ✅ 全修复批次完成(开创人2026-11-14):i18n全量+美学Export+漏网数值——CI触发验证
using Godot;

[GlobalClass]
public partial class SurvivalUIDesign : Resource
{
    [ExportGroup("HUD")]
    [Export] public Vector2 hudPos = new Vector2(8, 8);
    [Export] public Vector2 hudSize = new Vector2(360, 46);
    [Export] public Vector2 saveBtnPos = new Vector2(8, 58);
    [Export] public Vector2 continueBtnPos = new Vector2(114, 58);
    [Export] public Vector2 btnSize = new Vector2(100, 30);

    [ExportGroup("闪屏")]
    [Export] public int splashFontSize = 72;
    [Export] public float splashFadeIn = 0.35f;
    [Export] public float splashFadeOut = 0.5f;
    [Export] public Color splashBg = new Color(0f, 0f, 0f, 0.6f);

    [ExportGroup("结算配色")]
    [Export] public Color victoryColor = new Color("#7CFC98");
    [Export] public Color defeatColor = new Color("#FF6B6B");

    [ExportGroup("稀有度配色(钢铁/宝石/反物质/铀核)")]
    [Export] public Color[] tierColors = { new Color("#C8D2DC"), new Color("#6FB7FF"), new Color("#FFD24A"), new Color("#C678FF") };
    [Export] public string[] tierNames = { "钢铁", "宝石", "反物质", "铀核" };

    [ExportGroup("情报面板")]
    [Export] public Color intelBg = new Color(0.05f, 0.08f, 0.15f, 0.92f);
    [Export] public int intelTitleSize = 44;
    [Export] public int intelBodySize = 26;

    [ExportGroup("选关面板")]
    [Export] public Color branchBg = new Color(0.04f, 0.06f, 0.12f, 0.94f);

    [ExportGroup("选秀面板")]
    [Export] public Color draftBg = new Color(0.03f, 0.05f, 0.1f, 0.95f);
}
