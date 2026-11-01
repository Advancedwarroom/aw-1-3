// GameManager.WeatherUI.cs - ✅ 天气系统（模块15）UI：左上角天气标签 + 全屏色调
using Godot;

public partial class GameManager
{
    private Label weatherLabel;
    private ColorRect weatherTint;

    /// <summary>由 _Ready 调用：创建天气 UI 并订阅天气变化</summary>
    public void InitWeatherUI()
    {
        var layer = new CanvasLayer { Layer = 95 };
        AddChild(layer);

        // 全屏色调（雨蓝/雪白/沙暴橙/雾灰）
        weatherTint = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        weatherTint.SetAnchorsPreset(Control.Preset.FullRect);
        layer.AddChild(weatherTint);

        // 左上角标签（回合标签下方）
        weatherLabel = new Label();
        weatherLabel.Position = new Vector2(8, 34);
        weatherLabel.AddThemeFontSizeOverride("font_size", 15);
        weatherLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.8f));
        layer.AddChild(weatherLabel);

        WeatherSystem.OnWeatherChanged += UpdateWeatherUI;
        UpdateWeatherUI();
    }

    private void UpdateWeatherUI()
    {
        if (weatherLabel == null) return;
        var w = WeatherSystem.Current;
        weatherLabel.Text = $"天气: {WeatherSystem.WeatherName(w)}"
            + (WeatherSystem.DaysRemaining > 0 ? $" ({WeatherSystem.DaysRemaining}天)" : "");
        weatherTint.Color = w switch
        {
            WeatherType.Rain => new Color(0.35f, 0.45f, 0.75f, 0.10f),
            WeatherType.Snow => new Color(0.85f, 0.90f, 1.00f, 0.10f),
            WeatherType.Sandstorm => new Color(0.80f, 0.60f, 0.25f, 0.10f),
            WeatherType.Mist => new Color(0.55f, 0.55f, 0.60f, 0.12f),
            _ => Colors.Transparent,
        };
        // 天气变化会改视野/雾战 → 刷新
        fogOfWarManager?.OnUnitMoved();
    }
}
