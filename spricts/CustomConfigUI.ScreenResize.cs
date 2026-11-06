// CustomConfigUI.ScreenResize.cs —— Extra配置·分屏调整：每屏独立宽高加减（带锚点）
using Godot;
using System.Text;

public partial class CustomConfigUI
{
    private VBoxContainer screenRowsBox;
    private Label screenResizeMsg;

    private Control BuildScreenResizeTab()
    {
        var panel = new VBoxContainer { Name = "ScreenResizePanel" };
        panel.AddChild(new Label { Text = TranslateGroup.T("对每屏分别加减宽高；锚点 = 伸缩发生在哪一侧\n宽：左=右边动 / 右=左边动 / 中=两边各半\n高：上=下边动 / 下=内容随顶边平移 / 中=各半") });
        var refreshBtn = new Button { Text = TranslateGroup.T("刷新屏列表") };
        refreshBtn.Pressed += RebuildScreenRows;
        panel.AddChild(refreshBtn);
        screenRowsBox = new VBoxContainer();
        panel.AddChild(screenRowsBox);
        var applyBtn = new Button { Text = TranslateGroup.T("应用全部调整") };
        applyBtn.Pressed += ApplyScreenResizes;
        panel.AddChild(applyBtn);
        screenResizeMsg = new Label();
        panel.AddChild(screenResizeMsg);
        RebuildScreenRows();
        return panel;
    }

    private void RebuildScreenRows()
    {
        if (screenRowsBox == null) return;
        foreach (var c in screenRowsBox.GetChildren()) c.QueueFree();
        var gmgr = gameManager?.gridManager;
        int n = gmgr?.GetScreenCount() ?? 1;
        for (int k = 0; k < n; k++)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = $"屏{k + 1}" });
            row.AddChild(new Label { Text = TranslateGroup.T("宽") });
            var dw = new SpinBox { MinValue = -300, MaxValue = 300, Value = 0, CustomMinimumSize = new Vector2(80, 26) };
            dw.Name = $"Dw{k}";
            row.AddChild(dw);
            var wa = new OptionButton { CustomMinimumSize = new Vector2(56, 26) };
            wa.AddItem(TranslateGroup.T("左")); wa.AddItem(TranslateGroup.T("中")); wa.AddItem(TranslateGroup.T("右"));
            wa.Name = $"Wa{k}";
            row.AddChild(wa);
            row.AddChild(new Label { Text = TranslateGroup.T("高") });
            var dh = new SpinBox { MinValue = -300, MaxValue = 300, Value = 0, CustomMinimumSize = new Vector2(80, 26) };
            dh.Name = $"Dh{k}";
            row.AddChild(dh);
            var ha = new OptionButton { CustomMinimumSize = new Vector2(56, 26) };
            ha.AddItem(TranslateGroup.T("上")); ha.AddItem(TranslateGroup.T("中")); ha.AddItem(TranslateGroup.T("下"));
            ha.Name = $"Ha{k}";
            row.AddChild(ha);
            screenRowsBox.AddChild(row);
        }
    }

    private void ApplyScreenResizes()
    {
        var gmgr = gameManager?.gridManager;
        if (gmgr == null) { if (screenResizeMsg != null) screenResizeMsg.Text = TranslateGroup.T("GridManager 未就绪"); return; }
        int n = gmgr.GetScreenCount();
        var msgs = new StringBuilder();
        for (int k = 0; k < n; k++)
        {
            var dw = screenRowsBox.GetNodeOrNull<SpinBox>($"Dw{k}");
            var wa = screenRowsBox.GetNodeOrNull<OptionButton>($"Wa{k}");
            var dh = screenRowsBox.GetNodeOrNull<SpinBox>($"Dh{k}");
            var ha = screenRowsBox.GetNodeOrNull<OptionButton>($"Ha{k}");
            if (dw == null || dh == null) continue;
            int ddw = (int)dw.Value, ddh = (int)dh.Value;
            if (ddw == 0 && ddh == 0) continue;
            var err = gmgr.ResizeScreen(k, ddw, ddh, wa?.Selected ?? 0, ha?.Selected ?? 0);
            if (err != null) msgs.AppendLine($"屏{k + 1}: {err}");
            else { dw.Value = 0; dh.Value = 0; }
        }
        if (screenResizeMsg != null)
            screenResizeMsg.Text = msgs.Length > 0 ? msgs.ToString() : TranslateGroup.T("✅ 全部应用完成");
    }
}
