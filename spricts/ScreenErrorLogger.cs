// ScreenErrorLogger.cs - 全局屏幕错误显示（开创人2026-11-11）：引擎所有报错/警告直接上屏，可关闭。
// 手机看不了 Godot 控制台——这个文件把全引擎错误流变成游戏画面里的滚动红字条。
using Godot;
using System.Collections.Generic;

public partial class ScreenErrorLogger : Godot.Logger
{
    private static ScreenErrorPump pump;

    /// 在 _Ready 里调一次即可（GameManager / TerrainEditor 都调，内部有静态防重）。
    public static void EnsureInstalled(Node host)
    {
        if (pump != null || host == null) return;
        pump = new ScreenErrorPump();
        OS.AddLogger(new ScreenErrorLogger());
        host.CallDeferred(Node.MethodName.AddChild, pump);
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        var msg = string.IsNullOrEmpty(rationale) ? code : rationale;
        var color = errorType == (int)ErrorType.Warning ? "gold" : "tomato";
        var kind = errorType == (int)ErrorType.Warning ? "警告" : "错误";
        ScreenErrorPump.Enqueue($"[color={color}][b]{kind}[/b] {msg.EscapeBBCode()}\n[i]{file}:{line} @{function}()[/i][/color]\n");
    }

    public override void _LogMessage(string message, bool error)
    {
        if (!error) return;                       // 只上屏 stderr 级，普通 print 不上屏
        if (message.Contains("ScreenError")) return;   // 防自激
        ScreenErrorPump.Enqueue($"[color=orange]{message.EscapeBBCode()}[/color]\n");
    }
}

/// 主线程泵：把队列里的错误刷到屏幕控件（Logger 回调可能来自工作线程，不能直接碰 UI）。
public partial class ScreenErrorPump : Node
{
    private static readonly List<string> pending = new();
    private static readonly object padlock = new();
    private static int dropped;

    private CanvasLayer layer;
    private PanelContainer panel;
    private RichTextLabel logLabel;
    private Button reopenButton;

    public static void Enqueue(string line)
    {
        lock (padlock)
        {
            if (pending.Count > 200) { dropped++; return; }
            pending.Add(line);
        }
    }

    public override void _Ready()
    {
        var root = GetTree()?.Root;
        if (root == null) return;
        layer = new CanvasLayer { Layer = 2500 };
        root.AddChild(layer);

        panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.TopRight);
        panel.Position = new Vector2(-472, 8);
        panel.CustomMinimumSize = new Vector2(462, 300);
        var vb = new VBoxContainer();

        var header = new HBoxContainer();
        var title = new Label { Text = "⚠ 报错上屏（截图发给开创人）", Modulate = new Color(1f, 0.6f, 0.6f) };
        var closeBtn = new Button { Text = "×关闭" };
        closeBtn.Pressed += () => { panel.Visible = false; if (reopenButton != null) reopenButton.Visible = true; };
        header.AddChild(title);
        header.AddChild(closeBtn);

        logLabel = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            CustomMinimumSize = new Vector2(452, 250),
            SelectionEnabled = true
        };
        vb.AddChild(header);
        vb.AddChild(logLabel);
        panel.AddChild(vb);
        layer.AddChild(panel);

        reopenButton = new Button { Text = "⚠", Modulate = new Color(1f, 0.4f, 0.4f) };
        reopenButton.SetAnchorsPreset(LayoutPreset.TopRight);
        reopenButton.Position = new Vector2(-42, 8);
        reopenButton.Visible = false;
        reopenButton.Pressed += () => { panel.Visible = true; reopenButton.Visible = false; };
        layer.AddChild(reopenButton);
    }

    public override void _Process(double delta)
    {
        List<string> batch = null;
        lock (padlock)
        {
            if (pending.Count > 0) { batch = new List<string>(pending); pending.Clear(); }
        }
        if (batch == null || logLabel == null || !IsInstanceValid(logLabel)) return;
        foreach (var line in batch) logLabel.AppendText(line);
        if (dropped > 0)
        {
            logLabel.AppendText($"[color=orange]…另有 {dropped} 条被丢弃[/color]\n");
            dropped = 0;
        }
        logLabel.ScrollToLine(logLabel.GetLineCount());
    }
}
