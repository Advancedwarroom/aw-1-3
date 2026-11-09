using Godot;
using System.Linq;

// TerrainEditor · 区域天气格子画笔
public partial class TerrainEditor
{
    private Panel regionalWeatherPanel;
    private OptionButton regionalWeatherTypeOption;
    private OptionButton regionalWeatherModeOption;
    private SpinBox regionalWeatherTurnsSpin;
    private CheckBox regionalWeatherReplaceSunnyCheck;
    private Label regionalWeatherStatusLabel;
    private bool isRegionalWeatherEditMode;

    private void CreateRegionalWeatherPanel()
    {
        regionalWeatherPanel = new Panel
        {
            Name = "RegionalWeatherPanel",
            CustomMinimumSize = new Vector2(390, 330),
            Visible = false,
            ZIndex = 1400
        };
        regionalWeatherPanel.SetAnchorsPreset(LayoutPreset.Center);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.11f, 0.16f, 0.97f),
            BorderColor = new Color(0.35f, 0.75f, 0.95f)
        };
        style.SetCornerRadiusAll(12);
        style.SetBorderWidthAll(2);
        regionalWeatherPanel.AddThemeStyleboxOverride("panel", style);

        var title = new Label { Position = new Vector2(16, 10), Size = new Vector2(340, 32) };
        TranslateGroup.SetText(title, "🌦️ 区域天气画笔");
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.55f, 0.9f, 1f));
        regionalWeatherPanel.AddChild(title);

        var close = new Button { Text = "✕", Position = new Vector2(346, 8), Size = new Vector2(32, 32) };
        close.Pressed += CloseRegionalWeatherEditor;
        regionalWeatherPanel.AddChild(close);

        var content = new VBoxContainer
        {
            Position = new Vector2(18, 52),
            Size = new Vector2(354, 265)
        };
        content.AddThemeConstantOverride("separation", 9);
        regionalWeatherPanel.AddChild(content);

        var typeRow = new HBoxContainer();
        var typeLabel = new Label { CustomMinimumSize = new Vector2(92, 30) };
        TranslateGroup.SetText(typeLabel, "天气类型");
        typeRow.AddChild(typeLabel);
        regionalWeatherTypeOption = new OptionButton { CustomMinimumSize = new Vector2(245, 32) };
        typeRow.AddChild(regionalWeatherTypeOption);
        content.AddChild(typeRow);

        var turnsRow = new HBoxContainer();
        var turnsLabel = new Label { CustomMinimumSize = new Vector2(92, 30) };
        TranslateGroup.SetText(turnsLabel, "持续回合");
        turnsRow.AddChild(turnsLabel);
        regionalWeatherTurnsSpin = new SpinBox
        {
            MinValue = -1,
            MaxValue = 9999,
            Step = 1,
            Value = -1,
            CustomMinimumSize = new Vector2(120, 32),
            TooltipText = "-1 = 永久；正数 = 剩余回合"
        };
        turnsRow.AddChild(regionalWeatherTurnsSpin);
        var permanentHint = new Label();
        TranslateGroup.SetText(permanentHint, "-1 = 永久");
        turnsRow.AddChild(permanentHint);
        content.AddChild(turnsRow);

        regionalWeatherReplaceSunnyCheck = new CheckBox();
        TranslateGroup.SetText(regionalWeatherReplaceSunnyCheck, "替代全球天气为晴（replaceSunny）");
        content.AddChild(regionalWeatherReplaceSunnyCheck);

        var modeRow = new HBoxContainer();
        var modeLabel = new Label { CustomMinimumSize = new Vector2(92, 30) };
        TranslateGroup.SetText(modeLabel, "点格操作");
        modeRow.AddChild(modeLabel);
        regionalWeatherModeOption = new OptionButton { CustomMinimumSize = new Vector2(245, 32) };
        regionalWeatherModeOption.AddItem(TranslateGroup.T("涂抹 / 续当前天气"), 0);
        regionalWeatherModeOption.AddItem(TranslateGroup.T("删除当前类型"), 1);
        regionalWeatherModeOption.AddItem(TranslateGroup.T("清空整格天气"), 2);
        modeRow.AddChild(regionalWeatherModeOption);
        content.AddChild(modeRow);

        var help = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(350, 42) };
        TranslateGroup.SetText(help, "直接点击地图格子应用；区域层覆盖全球天气，且不受 CO 天气免疫影响。");
        help.AddThemeFontSizeOverride("font_size", 12);
        help.AddThemeColorOverride("font_color", new Color(0.7f, 0.82f, 0.9f));
        content.AddChild(help);

        regionalWeatherStatusLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(350, 48)
        };
        TranslateGroup.SetText(regionalWeatherStatusLabel, "尚未选择格子");
        regionalWeatherStatusLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        content.AddChild(regionalWeatherStatusLabel);

        AddChild(regionalWeatherPanel);
        MakeMenuDraggable(regionalWeatherPanel, title);
        RefreshRegionalWeatherTypes();
    }

    private WeatherManager RegionalWeatherManager
        => GetTree()?.GetFirstNodeInGroup("weather_manager") as WeatherManager;

    private void RefreshRegionalWeatherTypes()
    {
        if (regionalWeatherTypeOption == null) return;
        int previous = regionalWeatherTypeOption.ItemCount > 0
            ? regionalWeatherTypeOption.GetSelectedId()
            : 1;
        regionalWeatherTypeOption.Clear();
        var wm = RegionalWeatherManager;
        if (wm != null)
        {
            foreach (var type in wm.GetRegisteredTypes().Where(t => t.id != 0))
                regionalWeatherTypeOption.AddItem($"{type.name}  [ID {type.id}]", type.id);
        }
        if (regionalWeatherTypeOption.ItemCount == 0)
        {
            regionalWeatherTypeOption.AddItem(TranslateGroup.T("无可用天气管理器"), 0);
            regionalWeatherTypeOption.Disabled = true;
            return;
        }
        regionalWeatherTypeOption.Disabled = false;
        int index = regionalWeatherTypeOption.GetItemIndex(previous);
        regionalWeatherTypeOption.Select(index >= 0 ? index : 0);
    }

    private void OpenRegionalWeatherEditor()
    {
        if (!IsEditMode)
        {
            ShowImportHint(TranslateGroup.T("请先进入编辑模式，再使用区域天气画笔"));
            return;
        }
        var wm = RegionalWeatherManager;
        if (wm == null)
        {
            ShowImportHint(TranslateGroup.T("当前场景没有 WeatherManager，无法编辑区域天气"));
            return;
        }
        wm.localWeatherEnabled = true;
        if (regionalWeatherPanel == null) CreateRegionalWeatherPanel();
        CloseAllMenus();
        RefreshRegionalWeatherTypes();
        isRegionalWeatherEditMode = true;
        regionalWeatherPanel.Visible = true;
        isMenuOpen = false; // 面板保持打开，但地图格子可以继续接收画笔点击。
        TranslateGroup.SetText(regionalWeatherStatusLabel, "画笔已开启：点击地图格子应用");
    }

    private void CloseRegionalWeatherEditor()
    {
        isRegionalWeatherEditMode = false;
        if (regionalWeatherPanel != null) regionalWeatherPanel.Visible = false;
        isMenuOpen = false;
    }

    private void ApplyRegionalWeatherBrush(Grids grid)
    {
        if (grid == null) return;
        var wm = RegionalWeatherManager;
        if (wm == null) return;
        wm.localWeatherEnabled = true;
        selectedGrid = grid;
        Vector2I cell = grid.GridIndex;
        int typeId = regionalWeatherTypeOption.GetSelectedId();
        int mode = regionalWeatherModeOption.GetSelectedId();
        bool changed = false;

        switch (mode)
        {
            case 0:
                int turns = Mathf.RoundToInt((float)regionalWeatherTurnsSpin.Value);
                changed = wm.AddWeather(cell, typeId, turns, regionalWeatherReplaceSunnyCheck.ButtonPressed);
                if (!changed)
                    TranslateGroup.SetText(regionalWeatherStatusLabel, "涂抹失败：类型无效，或本格已达到四层上限");
                break;
            case 1:
                changed = wm.GetLayersAt(cell).Any(layer => layer.typeId == typeId);
                if (changed) wm.RemoveWeather(cell, typeId);
                break;
            case 2:
                changed = wm.GetLayersAt(cell).Count > 0;
                if (changed) wm.ClearWeather(cell);
                break;
        }

        string weather = wm.GetCellWeatherText(cell);
        string state = string.IsNullOrEmpty(weather)
            ? $"{TranslateGroup.T("无区域层（跟随全球天气")}：{WeatherSystem.WeatherName(WeatherSystem.Current)}）"
            : weather;
        string prefix = mode == 0 && !changed ? "" : changed ? TranslateGroup.T("已应用") + "｜" : TranslateGroup.T("无变化") + "｜";
        regionalWeatherStatusLabel.Text = $"{prefix}{cell}：{state}";
    }
}
