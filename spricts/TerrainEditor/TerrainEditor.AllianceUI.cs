// TerrainEditor.AllianceUI.cs - ✅ 盟友机制：配置面板里的同盟列表维护
using Godot;
using System.Linq;

public partial class TerrainEditor
{
    /// <summary>刷新同盟列表（每对同盟一行，可单独解盟）</summary>
    private void RefreshAlliancePanelList(VBoxContainer container)
    {
        if (container == null) return;
        foreach (var child in container.GetChildren()) child.QueueFree();

        var allied = TeamHelper.GetAlliancePairs();
        if (allied.Count == 0)
        {
            var empty = new Label();
            TranslateGroup.SetText(empty, "（暂无同盟：上方选两个势力后按结盟/解盟按钮）");
            empty.AddThemeFontSizeOverride("font_size", 12);
            container.AddChild(empty);
            return;
        }

        foreach (var (a, b) in allied)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var label = new Label();
            label.Text = $"🤝 {a} × {b}";
            label.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(label);

            var delBtn = new Button();
            TranslateGroup.SetText(delBtn, "解盟");
            delBtn.AddThemeFontSizeOverride("font_size", 11);
            string ca = a, cb = b;
            delBtn.Pressed += () => {
                TeamHelper.SetAlliance(ca, cb, false);
                RefreshAlliancePanelList(container);
            };
            row.AddChild(delBtn);

            container.AddChild(row);
        }
    }
}
