// CoEditorUI.cs - ✅ CO 自定义编辑器 + 头像上传
// 功能：新建/编辑 CO（名称/介绍/头像/技能星数/Zone范围/30大模块任意拼装），
//       保存到 user://co/<id>.json，保存后热应用到运行层（已绑定势力即时生效）。
// 入口：编辑模式 → 全局配置面板 → 「🎖️ CO 编辑器」按钮。
using Godot;
using System;
using System.Linq;
using System.Text.RegularExpressions;

public partial class CoEditorUI : CanvasLayer
{
    private Panel panel;
    private OptionButton coSelect;
    private LineEdit nameEdit;
    private TextEdit bioEdit;
    private LineEdit armyEdit;
    private LineEdit copNameEdit;
    private LineEdit scopNameEdit;
    private SpinBox powerStarsSpin;
    private SpinBox superStarsSpin;
    private SpinBox zoneRangeSpin;
    private TextureRect avatarPreview;
    private Label avatarPathLabel;
    private ItemList moduleList;
    private OptionButton addModuleOpt;
    private OptionButton addSlotOpt;
    private OptionButton addKeyOpt;
    private LineEdit addKeyEdit;
    private SpinBox addValueSpin;
    private SpinBox addTurnsSpin;
    private OptionButton addDurationOpt;
    private OptionButton bindTeamOpt;
    private OptionButton bindScreenOpt;   // 开创人2026-11-07：CO绑屏选择（0=不绑定/全屏通用）
    private FileDialog avatarDialog;

    /// <summary>当前编辑中的 CO（工作副本）</summary>
    private CoProfile editing;
    private bool isLoadingUI = false;

    /// <summary>30 大模块显示名（编辑器下拉用）</summary>
    static readonly (int id, string name)[] ModuleNames = new[]
    {
        (1, "伤害加成"), (2, "防御加成"), (3, "射程"), (4, "视野"), (5, "移动"),
        (6, "多次移动"), (7, "生成单位"), (8, "血量变更"), (9, "区域导弹"), (10, "逆转/先制"),
        (11, "经济"), (12, "生产"), (13, "运气"), (14, "隐藏/反隐"), (15, "天气/迷雾"),
        (16, "升级"), (17, "CO等级"), (18, "效率/占领"), (19, "专属单位"), (20, "时限"),
        (21, "操作权"), (22, "势力"), (23, "外交"), (24, "建筑状态"), (25, "状态系统"),
        (26, "AI"), (27, "能量条"), (28, "多屏"), (29, "特殊限定"), (30, "Zone"),
    };

    /// <summary>常用数值键（编辑器下拉用，可自由输入其他键）</summary>
    static readonly string[] CommonKeys = new[]
    {
        "offBonus", "defBonus", "terrainStars", "rangeBonus", "minRangeBonus",
        "visionBonus", "moveBonus", "moveCostReduction", "healHp", "incomePercent",
        "costPercent", "luckMax", "luckMin", "captureBonus", "weatherImmune",
        "setWeather", "weatherTurns", "firstStrike", "antiStealth", "stealthAll",
        "burnHp", "stunTurns", "weakPercent", "missileCount", "missileDamage",
        "blastRadius", "facStatus", "facStatusTurns", "spawnUnitId", "spawnHp",
        "spawnCount", "xpPerKill",
    };

    private bool _dragging;
    private void OnTitleBarGuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            _dragging = mb.Pressed;
        else if (e is InputEventMouseMotion mm && _dragging && panel != null)
            panel.Position += mm.Relative;
    }

    public override void _Ready()
    {
        // ✅ 下拉修复：PopupMenu继承Window（Layer/ZIndex均不存在），唯一正解=把弹窗移到更高CanvasLayer
        var popupTopLayer = new CanvasLayer { Layer = 1300, Name = "COEditorPopupTop" };
        AddChild(popupTopLayer);
        foreach (var opt in new OptionButton[] { coSelect, addModuleOpt, addSlotOpt, addKeyOpt, addDurationOpt, bindTeamOpt })
        {
            if (opt == null) continue;
            var popup = opt.GetPopup();
            if (popup.GetParent() != popupTopLayer) popup.Reparent(popupTopLayer);   // 弹窗置顶：不再被面板盖住
            popup.AllowSearch = true;
        }
        Layer = 1200;
        AddToGroup("co_editor_ui");
        BuildUI();
        Visible = false;
    }

    /// <summary>打开编辑器（刷新列表）</summary>
    public void Open()
    {
        RefreshCoList();
        if (coSelect.ItemCount > 0 && coSelect.Selected < 0) coSelect.Selected = 0;
        if (editing == null) LoadSelectedCo();
        Visible = true;
    }

    // ================= UI 构建 =================
    private void BuildUI()
    {
        panel = new Panel();
        panel.CustomMinimumSize = new Vector2(560, 660);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.ZIndex = 100;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        panelStyle.SetCornerRadiusAll(16);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.BorderColor = new Color(0.9f, 0.7f, 0.3f);
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var title = new Label();
        TranslateGroup.SetText(title, "🎖️ CO 编辑器（自定义 CO + 头像上传）");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 19);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        title.CustomMinimumSize = new Vector2(0, 38);
        panel.AddChild(title);
        title.Position = new Vector2(0, 12);
        title.Size = new Vector2(560, 38);

        // ✅ 标题栏拖动（按住标题移动窗口）
        title.MouseFilter = Control.MouseFilterEnum.Stop;
        title.MouseDefaultCursorShape = Control.CursorShape.Move;
        title.GuiInput += OnTitleBarGuiInput;

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(32, 32);
        closeBtn.Position = new Vector2(515, 14);
        closeBtn.Pressed += () => Visible = false;
        panel.AddChild(closeBtn);

        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(16, 58);
        scroll.Size = new Vector2(528, 590);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        panel.AddChild(scroll);

        var box = new VBoxContainer();
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(box);

        // ---------- CO 选择 ----------
        var selHBox = new HBoxContainer();
        selHBox.AddThemeConstantOverride("separation", 8);
        coSelect = new OptionButton();
        coSelect.CustomMinimumSize = new Vector2(360, 30);
        coSelect.ItemSelected += (idx) =>
        {
            if (isLoadingUI) return;
            LoadSelectedCo();
        };
        selHBox.AddChild(coSelect);
        var newBtn = new Button();
        TranslateGroup.SetText(newBtn, "➕ 新建");
        newBtn.CustomMinimumSize = new Vector2(80, 30);
        newBtn.Pressed += NewCo;
        selHBox.AddChild(newBtn);
        box.AddChild(selHBox);

        // ---------- 基本信息 ----------
        box.AddChild(MakeSection("📋 基本信息"));
        nameEdit = MakeLineEdit(box, "名称", "自定义CO");
        armyEdit = MakeLineEdit(box, "阵营 (OS/BM/GE/YC...，可空)", "");
        bioEdit = new TextEdit();
        bioEdit.CustomMinimumSize = new Vector2(0, 52);
        bioEdit.PlaceholderText = TranslateGroup.T("介绍（自定义介绍，可空）");
        var bioH = new HBoxContainer();
        bioH.AddThemeConstantOverride("separation", 8);
        bioH.AddChild(MakeLabel("介绍:"));
        bioH.AddChild(bioEdit);
        box.AddChild(bioH);

        var skillRow = new HBoxContainer();
        skillRow.AddThemeConstantOverride("separation", 6);
        skillRow.AddChild(MakeLabel("技能名:"));
        copNameEdit = new LineEdit();
        copNameEdit.PlaceholderText = TranslateGroup.T("COP名");
        copNameEdit.CustomMinimumSize = new Vector2(110, 28);
        skillRow.AddChild(copNameEdit);
        scopNameEdit = new LineEdit();
        scopNameEdit.PlaceholderText = TranslateGroup.T("SCOP名");
        scopNameEdit.CustomMinimumSize = new Vector2(110, 28);
        skillRow.AddChild(scopNameEdit);
        skillRow.AddChild(MakeHint("槽位: d2d常态/COP/SCOP/Zone"));
        box.AddChild(skillRow);

        var starsRow = new HBoxContainer();
        starsRow.AddThemeConstantOverride("separation", 10);
        starsRow.AddChild(MakeLabel("COP★:"));
        powerStarsSpin = MakeSpin(1, 14, 3);
        starsRow.AddChild(powerStarsSpin);
        starsRow.AddChild(MakeLabel("SCOP★:"));
        superStarsSpin = MakeSpin(1, 14, 6);
        starsRow.AddChild(superStarsSpin);
        starsRow.AddChild(MakeLabel("Zone:"));
        zoneRangeSpin = MakeSpin(-1, 10, -1);
        starsRow.AddChild(zoneRangeSpin);
        starsRow.AddChild(MakeHint("(-1=全局CO)"));
        box.AddChild(starsRow);

        // ---------- 头像 ----------
        box.AddChild(MakeSection("🖼️ 头像"));
        var avatarH = new HBoxContainer();
        avatarH.AddThemeConstantOverride("separation", 10);
        avatarPreview = new TextureRect();
        avatarPreview.CustomMinimumSize = new Vector2(72, 72);
        avatarPreview.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        avatarPreview.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        var previewStyle = new StyleBoxFlat();
        previewStyle.BgColor = new Color(0.15f, 0.17f, 0.22f);
        previewStyle.SetCornerRadiusAll(8);
        avatarPreview.AddThemeStyleboxOverride("panel", previewStyle);
        avatarH.AddChild(avatarPreview);
        var avatarBtnCol = new VBoxContainer();
        avatarBtnCol.AddThemeConstantOverride("separation", 6);
        var uploadBtn = new Button();
        TranslateGroup.SetText(uploadBtn, "📤 上传头像 (png/jpg)");
        uploadBtn.CustomMinimumSize = new Vector2(180, 32);
        uploadBtn.Pressed += OpenAvatarDialog;
        avatarBtnCol.AddChild(uploadBtn);
        var clearAvatarBtn = new Button();
        TranslateGroup.SetText(clearAvatarBtn, "🧹 清除头像");
        clearAvatarBtn.CustomMinimumSize = new Vector2(180, 28);
        clearAvatarBtn.Pressed += () =>
        {
            if (editing != null) { editing.avatar = ""; RefreshAvatar(); }
        };
        avatarBtnCol.AddChild(clearAvatarBtn);
        avatarPathLabel = new Label();
        avatarPathLabel.AddThemeFontSizeOverride("font_size", 10);
        avatarPathLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        avatarBtnCol.AddChild(avatarPathLabel);
        avatarH.AddChild(avatarBtnCol);
        box.AddChild(avatarH);

        // ---------- 模块编辑 ----------
        box.AddChild(MakeSection("🧩 模块（30 大模块任意拼装）"));
        moduleList = new ItemList();
        moduleList.CustomMinimumSize = new Vector2(0, 140);
        moduleList.SelectMode = ItemList.SelectModeEnum.Single;
        box.AddChild(moduleList);
        var delModBtn = new Button();
        TranslateGroup.SetText(delModBtn, "🗑 删除选中模块");
        delModBtn.CustomMinimumSize = new Vector2(160, 28);
        delModBtn.Pressed += DeleteSelectedModule;
        box.AddChild(delModBtn);

        // 添加模块行
        var addRow1 = new HBoxContainer();
        addRow1.AddThemeConstantOverride("separation", 6);
        addRow1.AddChild(MakeLabel("模块:"));
        addModuleOpt = new OptionButton();
        foreach (var (mid, mname) in ModuleNames)
            addModuleOpt.AddItem($"{mid}.{mname}");
        addModuleOpt.CustomMinimumSize = new Vector2(140, 28);
        addRow1.AddChild(addModuleOpt);
        addRow1.AddChild(MakeLabel("槽位:"));
        addSlotOpt = new OptionButton();
        foreach (string s in new[] { "d2d", "cop", "scop", "zone" }) addSlotOpt.AddItem(s);
        addSlotOpt.CustomMinimumSize = new Vector2(80, 28);
        addRow1.AddChild(addSlotOpt);
        addRow1.AddChild(MakeLabel("时限:"));
        addDurationOpt = new OptionButton();
        foreach (string s in new[] { "turns", "permanent", "instant" }) addDurationOpt.AddItem(s);
        addDurationOpt.CustomMinimumSize = new Vector2(110, 28);
        addRow1.AddChild(addDurationOpt);
        box.AddChild(addRow1);

        var addRow2 = new HBoxContainer();
        addRow2.AddThemeConstantOverride("separation", 6);
        addRow2.AddChild(MakeLabel("数值键:"));
        addKeyOpt = new OptionButton();
        foreach (string k in CommonKeys) addKeyOpt.AddItem(k);
        addKeyOpt.CustomMinimumSize = new Vector2(150, 28);
        addKeyOpt.ItemSelected += (idx) => { addKeyEdit.Text = CommonKeys[(int)idx]; };
        addRow2.AddChild(addKeyOpt);
        addKeyEdit = new LineEdit();
        addKeyEdit.Text = "offBonus";
        addKeyEdit.CustomMinimumSize = new Vector2(150, 28);
        addKeyEdit.PlaceholderText = TranslateGroup.T("数值键(可自由输入)");
        addRow2.AddChild(addKeyEdit);
        addRow2.AddChild(MakeLabel("值:"));
        addValueSpin = MakeSpin(-999, 9999, 10);
        addRow2.AddChild(addValueSpin);
        addRow2.AddChild(MakeLabel("回合:"));
        addTurnsSpin = MakeSpin(1, 99, 1);
        addRow2.AddChild(addTurnsSpin);
        var addBtn = new Button();
        TranslateGroup.SetText(addBtn, "➕ 添加");
        addBtn.CustomMinimumSize = new Vector2(80, 28);
        addBtn.Pressed += AddModuleEntry;
        addRow2.AddChild(addBtn);
        box.AddChild(addRow2);
        box.AddChild(MakeHint("数值键参考文档：offBonus/defBonus/moveBonus/healHp/spawnUnitId/missileCount 等"));

        // ---------- 保存 / 绑定 ----------
        box.AddChild(MakeSection("💾 保存与应用"));
        var saveBtn = new Button();
        TranslateGroup.SetText(saveBtn, "💾 保存 CO（user://co/<id>.json，立即热应用）");
        saveBtn.CustomMinimumSize = new Vector2(480, 40);
        saveBtn.AddThemeFontSizeOverride("font_size", 14);
        var saveStyle = new StyleBoxFlat();
        saveStyle.BgColor = new Color(0.2f, 0.5f, 0.3f, 0.9f);
        saveStyle.SetCornerRadiusAll(8);
        saveBtn.AddThemeStyleboxOverride("normal", saveStyle);
        saveBtn.Pressed += SaveCurrent;
        box.AddChild(saveBtn);

        var bindRow = new HBoxContainer();
        bindRow.AddThemeConstantOverride("separation", 8);
        bindRow.AddChild(MakeLabel("绑定势力:"));
        bindTeamOpt = new OptionButton();
        foreach (string t in new[] { "Player1", "Player2", "Player3", "Player4", "Player5", "Player6", "Player7", "Player8" })
            bindTeamOpt.AddItem(t);
        bindTeamOpt.CustomMinimumSize = new Vector2(120, 30);
        bindRow.AddChild(bindTeamOpt);
        var bindScreenLabel = new Label();
        bindScreenLabel.Text = $"{TranslateGroup.T("绑定屏幕")}（{TranslateGroup.T("不绑定")}={TranslateGroup.T("全屏通用")}）";
        bindScreenLabel.AddThemeFontSizeOverride("font_size", 12);
        bindRow.AddChild(bindScreenLabel);
        bindScreenOpt = new OptionButton();
        bindScreenOpt.CustomMinimumSize = new Vector2(120, 30);
        bindScreenOpt.AddThemeFontSizeOverride("font_size", 12);
        bindScreenOpt.AddItem(TranslateGroup.T("不绑定（全屏通用）"));
        {
            var gmScr = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
            int scrN = gmScr?.gridManager != null ? Mathf.Max(1, gmScr.gridManager.screenCount) : 4;
            for (int s = 0; s < scrN; s++)
                bindScreenOpt.AddItem($"{TranslateGroup.T("屏")}{s + 1}");
        }
        bindRow.AddChild(bindScreenOpt);
        var bindBtn = new Button();
        TranslateGroup.SetText(bindBtn, "🔗 绑定（本局该势力立即使用此 CO）");
        bindBtn.CustomMinimumSize = new Vector2(280, 30);
        bindBtn.Pressed += BindCurrent;
        bindRow.AddChild(bindBtn);
        box.AddChild(bindRow);

        // ---------- 头像文件对话框 ----------
        avatarDialog = new FileDialog();
        avatarDialog.Access = FileDialog.AccessEnum.Filesystem;
        avatarDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
        avatarDialog.Filters = new[] { "*.png ; PNG 图片", "*.jpg, *.jpeg ; JPG 图片", "*.webp ; WebP 图片" };
        avatarDialog.FileSelected += OnAvatarPicked;
        AddChild(avatarDialog);
    }

    // ================= 交互逻辑 =================

    void RefreshCoList()
    {
        isLoadingUI = true;
        coSelect.Clear();
        foreach (var p in CoModuleSystem.AllProfiles().OrderBy(p => p.id))
            coSelect.AddItem($"{p.name}（{p.id}）");
        isLoadingUI = false;
    }

    void LoadSelectedCo()
    {
        int idx = coSelect.Selected;
        if (idx < 0) return;
        string coId = CoModuleSystem.AllProfiles().OrderBy(p => p.id).ToList()[idx].id;
        var p = CoModuleSystem.AllProfiles().FirstOrDefault(x => x.id == coId);
        if (p == null) return;
        // 工作副本（深拷贝模块列表，避免直接改内置档案）
        editing = new CoProfile
        {
            id = p.id, name = p.name, avatar = p.avatar, bio = p.bio, army = p.army,
            copName = p.copName, scopName = p.scopName,
            powerStars = p.powerStars, superStars = p.superStars, zoneRange = p.zoneRange,
            coUnits = new System.Collections.Generic.List<string>(p.coUnits),
        };
        foreach (var m in p.modules)
        {
            var copy = new CoModuleEntry
            {
                module = m.module, name = m.name, duration = m.duration,
                durationTurns = m.durationTurns, maxStacks = m.maxStacks,
                minCoLevel = m.minCoLevel, maxCoLevel = m.maxCoLevel,
                directOnly = m.directOnly, indirectOnly = m.indirectOnly,
                unitTypes = new System.Collections.Generic.List<string>(m.unitTypes),
                terrainTypes = new System.Collections.Generic.List<string>(m.terrainTypes),
            };
            foreach (var kv in m.d2d) copy.d2d[kv.Key] = kv.Value;
            foreach (var kv in m.cop) copy.cop[kv.Key] = kv.Value;
            foreach (var kv in m.scop) copy.scop[kv.Key] = kv.Value;
            foreach (var kv in m.zone) copy.zone[kv.Key] = kv.Value;
            editing.modules.Add(copy);
        }
        LoadIntoUI();
    }

    void NewCo()
    {
        editing = new CoProfile
        {
            id = "custom_" + DateTime.Now.ToString("HHmmss"),
            name = "自定义CO",
            copName = "技能",
            scopName = "大招",
        };
        LoadIntoUI();
    }

    void LoadIntoUI()
    {
        if (editing == null) return;
        isLoadingUI = true;
        nameEdit.Text = editing.name;
        bioEdit.Text = editing.bio;
        armyEdit.Text = editing.army;
        copNameEdit.Text = editing.copName;
        scopNameEdit.Text = editing.scopName;
        powerStarsSpin.Value = editing.powerStars;
        superStarsSpin.Value = editing.superStars;
        zoneRangeSpin.Value = editing.zoneRange;
        RefreshAvatar();
        RefreshModuleList();
        isLoadingUI = false;
    }

    void RefreshAvatar()
    {
        avatarPathLabel.Text = string.IsNullOrEmpty(editing?.avatar) ? TranslateGroup.T("（未设置）") : editing.avatar;
        var tex = editing?.GetAvatarTexture();
        avatarPreview.Texture = tex;
    }

    void RefreshModuleList()
    {
        moduleList.Clear();
        if (editing == null) return;
        foreach (var m in editing.modules)
        {
            string vals = string.Join(" ",
                m.d2d.Count > 0 ? $"d2d:{DictSummary(m.d2d)}" : "",
                m.cop.Count > 0 ? $"COP:{DictSummary(m.cop)}" : "",
                m.scop.Count > 0 ? $"SCOP:{DictSummary(m.scop)}" : "",
                m.zone.Count > 0 ? $"Zone:{DictSummary(m.zone)}" : "");
            string label = $"#{m.module} {ModuleName(m.module)} [{m.duration}×{m.durationTurns}] {vals.Trim()}";
            moduleList.AddItem(label);
        }
    }

    static string DictSummary(Godot.Collections.Dictionary<string, float> d)
        => string.Join(",", d.Select(kv => $"{kv.Key}={kv.Value}"));

    static string ModuleName(int id)
    {
        var found = ModuleNames.FirstOrDefault(x => x.id == id);
        return found.name ?? $"模块{id}";
    }

    void AddModuleEntry()
    {
        if (editing == null) { NewCo(); }
        int modId = addModuleOpt.Selected + 1;
        string slot = addSlotOpt.Selected == 1 ? "cop" : addSlotOpt.Selected == 2 ? "scop" : addSlotOpt.Selected == 3 ? "zone" : "d2d";
        string key = string.IsNullOrWhiteSpace(addKeyEdit.Text) ? CommonKeys[Mathf.Max(0, addKeyOpt.Selected)] : addKeyEdit.Text.Trim();
        float value = (float)addValueSpin.Value;
        int turns = (int)addTurnsSpin.Value;
        string duration = addDurationOpt.Selected == 1 ? "permanent" : addDurationOpt.Selected == 2 ? "instant" : "turns";

        var entry = editing.modules.FirstOrDefault(m => m.module == modId && m.duration == duration && m.durationTurns == turns)
            ?? new CoModuleEntry
            {
                module = modId,
                name = ModuleName(modId),
                duration = duration,
                durationTurns = turns,
            };
        var target = slot == "cop" ? entry.cop : slot == "scop" ? entry.scop : slot == "zone" ? entry.zone : entry.d2d;
        target[key] = value;
        if (!editing.modules.Contains(entry)) editing.modules.Add(entry);
        RefreshModuleList();
    }

    void DeleteSelectedModule()
    {
        int[] selected = moduleList.GetSelectedItems();
        if (selected == null || selected.Length == 0 || editing == null) return;
        int idx = selected[0];
        if (idx >= 0 && idx < editing.modules.Count)
        {
            editing.modules.RemoveAt(idx);
            RefreshModuleList();
        }
    }

    void OpenAvatarDialog() => avatarDialog.PopupCenteredRatio(0.7f);

    void OnAvatarPicked(string path)
    {
        if (editing == null) NewCo();
        // 复制到 user://co/avatars/<id>.<ext>
        DirAccess.MakeDirRecursiveAbsolute(CoProfile.CustomAvatarDir);
        string ext = path.GetExtension().ToLower();
        if (string.IsNullOrEmpty(ext)) ext = "png";
        string dest = $"{CoProfile.CustomAvatarDir}{editing.id}.{ext}";
        var bytes = FileAccess.GetFileAsBytes(path);
        if (bytes == null || bytes.Length == 0)
        {
            GD.PrintErr($"[CO编辑器] 读取头像失败：{path}");
            return;
        }
        using var f = FileAccess.Open(dest, FileAccess.ModeFlags.Write);
        if (f == null)
        {
            GD.PrintErr($"[CO编辑器] 写入头像失败：{dest}");
            return;
        }
        f.StoreBuffer(bytes);
        editing.avatar = dest;
        RefreshAvatar();
        GD.Print($"[CO编辑器] 头像已上传：{dest}（{bytes.Length / 1024}KB）");
    }

    void SaveCurrent()
    {
        if (editing == null) { NewCo(); }
        // 从 UI 收集
        editing.name = string.IsNullOrWhiteSpace(nameEdit.Text) ? editing.id : nameEdit.Text.Trim();
        editing.bio = bioEdit.Text;
        editing.army = armyEdit.Text.Trim();
        editing.copName = copNameEdit.Text.Trim();
        editing.scopName = scopNameEdit.Text.Trim();
        editing.powerStars = (int)powerStarsSpin.Value;
        editing.superStars = (int)superStarsSpin.Value;
        editing.zoneRange = (int)zoneRangeSpin.Value;

        var err = editing.SaveCustom();
        if (err != Error.Ok)
        {
            GD.PrintErr($"[CO编辑器] 保存失败：{err}");
            return;
        }
        // 热应用：更新档案缓存 + 已绑定该 CO 的势力换新对象
        CoModuleSystem.ApplyProfile(editing);
        RefreshCoList();
        GD.Print($"[CO编辑器] 已保存并热应用：{editing.id}（{editing.name}，{editing.modules.Count} 条模块）");
    }

    void BindCurrent()
    {
        if (editing == null) { SaveCurrent(); }
        string team = bindTeamOpt.Selected >= 0
            ? new[] { "Player1", "Player2", "Player3", "Player4", "Player5", "Player6", "Player7", "Player8" }[bindTeamOpt.Selected]
            : "Player1";
        int bs = bindScreenOpt != null ? bindScreenOpt.Selected - 1 : -1;
        if (bs < 0) bs = -1;
        CoModuleSystem.Register(team, editing.id, bs);   // 开创人2026-11-07：绑屏CO（-1=不绑定=全屏通用，绑屏失守即死）
        GD.Print($"[CO] {team} 绑定 {editing.name}，屏幕: {(bs < 0 ? "全屏通用" : $"屏{bs + 1}")}");
        GD.Print($"[CO编辑器] {team} 已绑定 CO：{editing.name}（{editing.id}）");
    }

    // ================= 小控件辅助 =================
    static Label MakeSection(string text)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", 15);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
        label.CustomMinimumSize = new Vector2(0, 26);
        return label;
    }

    static Label MakeLabel(string text)
    {
        var l = new Label();
        l.Text = text;
        l.AddThemeFontSizeOverride("font_size", 12);
        return l;
    }

    static Label MakeHint(string text)
    {
        var l = new Label();
        l.Text = text;
        l.AddThemeFontSizeOverride("font_size", 10);
        l.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        return l;
    }

    static SpinBox MakeSpin(float min, float max, float val)
    {
        var s = new SpinBox();
        s.MinValue = min;
        s.MaxValue = max;
        s.Value = val;
        s.CustomMinimumSize = new Vector2(72, 28);
        s.AddThemeFontSizeOverride("font_size", 12);
        return s;
    }

    static LineEdit MakeLineEdit(VBoxContainer parent, string placeholder, string initial)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MakeLabel(placeholder + ":"));
        var edit = new LineEdit();
        edit.Text = initial;
        edit.CustomMinimumSize = new Vector2(300, 28);
        edit.PlaceholderText = TranslateGroup.T(placeholder);
        row.AddChild(edit);
        parent.AddChild(row);
        return edit;
    }

}
