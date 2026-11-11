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

    /// Godot 4 没有 string.EscapeBBCode()，手写："[" → "[lb]"。
    private static string Bb(string s) => (s ?? "").Replace("[", "[lb]");

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        var msg = string.IsNullOrEmpty(rationale) ? code : rationale;
        var isWarning = errorType == (int)ErrorType.Warning;
        var color = isWarning ? "gold" : "tomato";
        var kind = isWarning ? "警告" : "错误";
        ScreenErrorPump.Enqueue($"[color={color}][b]{kind}[/b] {Bb(msg)}\n[i]{file}:{line} @{function}()[/i][/color]\n");
    }

    public override void _LogMessage(string message, bool error)
    {
        if (!error) return;                       // 只上屏 stderr 级，普通 print 不上屏
        if (message.Contains("ScreenError")) return;   // 防自激
        ScreenErrorPump.Enqueue($"[color=orange]{Bb(message)}[/color]\n");
    }

    /// ✅ 开创人2026-11-11：诊断上屏 API——不走错误流，青色 ◆ 前缀，供玩法代码插桩"看为啥"用。
    /// 任何静态方法里直接 ScreenErrorLogger.Diag("...")，错误面板与复制内容同步收录。
    public static void Diag(string msg)
        => ScreenErrorPump.Enqueue($"[color=cyan]◆ {Bb(msg)}[/color]\n");
}

/// 主线程泵：把队列里的错误刷到屏幕控件（Logger 回调可能来自工作线程，不能直接碰 UI）。
public partial class ScreenErrorPump : Node
{
    private static readonly List<string> pending = new();
    private static readonly object padlock = new();
    private static int dropped;
    // ✅ 纯文本镜像（复制用）：随批次同步追加，去 BBCode，封顶 64KB
    private static readonly System.Text.StringBuilder plain = new();

    private static string StripBb(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var noTags = System.Text.RegularExpressions.Regex.Replace(s, @"\[/?(?:color|b|i)(?:=[^\]]*)?\]", "");
        return noTags.Replace("[lb]", "[");
    }

    private CanvasLayer layer;
    private PanelContainer panel;
    private RichTextLabel logLabel;
    private Button reopenButton;
    private static Vector2 savedPos = new Vector2(-472, -312);   // 开创人2026-11-11：默认右下角，避开切换回合按钮；拖动后记住位置
    private static bool posSaved;

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
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        panel.Position = posSaved ? savedPos : savedPos;   // 默认右下角；拖过后沿用上次位置
        panel.CustomMinimumSize = new Vector2(462, 300);
        var vb = new VBoxContainer();

        var header = new HBoxContainer();
        var title = new Label { Text = "⚠ 报错上屏（截图发给开创人）", Modulate = new Color(1f, 0.6f, 0.6f) };
        var closeBtn = new Button { Text = "×关闭" };
        closeBtn.Pressed += () => { panel.Visible = false; if (reopenButton != null) reopenButton.Visible = true; };
        // ✅ 开创人2026-11-11：一键复制全部日志到剪贴板（截图太费劲）——复制的是去 BBCode 的纯文本镜像
        var copyBtn = new Button { Text = "⧉复制" };
        copyBtn.Pressed += () =>
        {
            string text = plain.Length > 0 ? plain.ToString() : (logLabel != null && IsInstanceValid(logLabel) ? logLabel.GetParsedText() : "");
            DisplayServer.ClipboardSet(text);
            copyBtn.Text = "✓已复制";
            var timer = GetTree()?.CreateTimer(1.5);
            if (timer != null) timer.Timeout += () => { if (IsInstanceValid(copyBtn)) copyBtn.Text = "⧉复制"; };
        };
        header.AddChild(title);
        header.AddChild(copyBtn);
        header.AddChild(closeBtn);

        // ✅ 标题栏拖动（开创人2026-11-11）：按住标题栏可拖动整条报错面板，松手记住位置
        bool dragging = false;
        header.GuiInput += (e) =>
        {
            if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                dragging = mb.Pressed;
                if (!dragging) { savedPos = panel.Position; posSaved = true; }
                header.AcceptEvent();
            }
            else if (e is InputEventMouseMotion mm && dragging)
            {
                panel.Position += mm.Relative;
            }
        };

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
        reopenButton.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        reopenButton.Position = new Vector2(-42, -40);
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
        foreach (var line in batch)
        {
            logLabel.AppendText(line);
            plain.Append(StripBb(line));
            if (plain.Length > 65536) plain.Remove(0, plain.Length - 65536);
        }
        if (dropped > 0)
        {
            logLabel.AppendText($"[color=orange]...另有 {dropped} 条被丢弃[/color]\n");
            dropped = 0;
        }
        logLabel.ScrollToLine(logLabel.GetLineCount());
    }
}
