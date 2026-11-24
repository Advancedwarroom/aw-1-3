using Godot;
using System;

// TerrainEditor.PropertyEditorV3d.cs - ✅ 开创人2026-11-25: 检查器V3d（UX三件套+脏状态）
// 触屏≥44×44 / 视听反馈闭环(行闪烁+程序化提示音) / 破坏性操作二次确认 / 已改未存脏标记(独立显示)
public partial class TerrainEditor
{
    // ===== ① 触屏目标：全面板控件≥44×44（HIG标准，小了就是误触之源）=====
    public void EnsureV3TouchTargets()
    {
        ApplyTouchRec(propertyContainer);
    }
    private static void ApplyTouchRec(Node node)
    {
        foreach (var ch in node.GetChildren())
        {
            if (ch is Button or CheckButton or OptionButton or LineEdit or SpinBox or CheckBox)
            {
                var c = (Control)ch;
                c.CustomMinimumSize = new Vector2(Mathf.Max(c.CustomMinimumSize.X, 44), Mathf.Max(c.CustomMinimumSize.Y, 44));
            }
            else if (ch is HSlider s)
            {
                s.CustomMinimumSize = new Vector2(Mathf.Max(s.CustomMinimumSize.X, 44), Mathf.Max(s.CustomMinimumSize.Y, 44));
            }
            ApplyTouchRec(ch);
        }
    }

    // ===== ② 反馈闭环：视觉(行闪烁) + 听觉(程序化提示音,零素材) =====
    private static AudioStreamWav _v3Blip;
    private AudioStreamPlayer _v3Player;
    private static AudioStreamWav V3Blip()
    {
        if (_v3Blip != null) return _v3Blip;
        const int sr = 22050, n = (int)(sr * 0.07);
        var bytes = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            double env = 1.0 - (double)i / n;
            short v = (short)(Math.Sin(2 * Math.PI * 880 * i / sr) * env * 9000);
            bytes[i * 2] = (byte)(v & 0xff); bytes[i * 2 + 1] = (byte)(v >> 8);
        }
        _v3Blip = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = sr, Stereo = false, Data = bytes };
        return _v3Blip;
    }
    private void PlayV3Blip()
    {
        try
        {
            _v3Player ??= new AudioStreamPlayer { Stream = V3Blip(), VolumeDb = -14f };
            if (_v3Player.GetParent() == null) AddChild(_v3Player);
            _v3Player.Play();
        }
        catch { }
    }
    private void FlashV3Row(string field)
    {
        if (!_rowBoxes.TryGetValue(field, out var row)) return;
        var tw = CreateTween();
        row.Modulate = new Color(0.6f, 1f, 0.6f);
        tw.TweenProperty(row, "modulate", Colors.White, 0.3);
    }

    // ===== ③ 破坏性操作：先问"确定吗，是否误触？" =====
    private ConfirmationDialog _v3ConfirmDlg;
    public void ConfirmDestructive(string message, Action onYes)
    {
        _v3ConfirmDlg ??= new ConfirmationDialog { Title = "⚠ 确认操作" };
        if (_v3ConfirmDlg.GetParent() == null) AddChild(_v3ConfirmDlg);
        _v3ConfirmDlg.DialogText = $"{message}\n\n确定吗，是否误触？";
        _v3ConfirmDlg.Confirmed -= _v3ConfirmYes;   // 防重复挂接
        _v3ConfirmYes = onYes;
        _v3ConfirmDlg.Confirmed += _v3ConfirmYes;
        _v3ConfirmDlg.PopupCentered();
    }
    private event Action _v3ConfirmYes;

    // ===== ④ 脏状态：已改未存（独立显示，与撤销/收藏并列）=====
    private int _v3Dirty;
    private Label _v3DirtyLabel;
    public void MarkV3Dirty()
    {
        _v3Dirty++;
        RefreshV3Dirty();
    }
    public void ClearV3Dirty()
    {
        _v3Dirty = 0;
        RefreshV3Dirty();
    }
    private void RefreshV3Dirty()
    {
        if (_v3DirtyLabel == null) return;
        _v3DirtyLabel.Visible = _v3Dirty > 0;
        _v3DirtyLabel.Text = $"● 已改未存 ({_v3Dirty})";
        _v3DirtyLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.3f));
    }
    public void BindV3DirtyLabel(Label l) { _v3DirtyLabel = l; RefreshV3Dirty(); }
}
