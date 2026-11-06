using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public partial class CustomConfigUI : Control
{
    [ExportGroup("")]
    [Export] public ExtraSave extraSave;
    [Export] public TerrainEditor terrainEditor;
    [Export] public GameManager gameManager;
    [Export] public EditorSaveSlots editorSaveSlots;

    private Panel mainPanel;
    private TabContainer tabContainer;

    // ✅ 拖拽字段
    private bool isDraggingMenu = false;
    private Vector2 dragOffset = Vector2.Zero;

    // ✅ 贴图应用模式
    private string pendingApplyTexturePath = "";
    private string pendingApplyTargetType = "";

    // 子面板
    private VBoxContainer moveTypePanel;
    private VBoxContainer terrainTypePanel;
    private VBoxContainer textureUploadPanel;
    private VBoxContainer saveSlotPanel;
    private FactionConfigPanel factionConfigPanel;

    // ✅ 贴图Tab的控件引用（避免运行时查找失败）
    private OptionButton applyTextureSelect;
    private OptionButton targetTypeSelect;

    public override void _Ready()
    {
        AddToGroup("custom_config_ui");

        if (extraSave == null)
            extraSave = GetTree().GetFirstNodeInGroup("extra_save") as ExtraSave;

        CreateMainPanel();
        CreateTabs();
        CreateOpenButton(); // ✅ 创建编辑模式入口按钮
        
        // ✅ 势力配置Tab切换时自动刷新
        tabContainer.TabChanged += (long idx) => {
            if (idx == 4) factionConfigPanel?.RefreshList();
        };
        
        Hide();

        // 延迟获取其他引用（确保目标节点已 _Ready）
        CallDeferred(nameof(DeferredInit));
    }

    private void DeferredInit()
    {
        if (terrainEditor == null)
            terrainEditor = GetTree().GetFirstNodeInGroup("terrain_editor") as TerrainEditor;
        if (gameManager == null)
            gameManager = GetTree().GetFirstNodeInGroup("game_manager") as GameManager;
        if (editorSaveSlots == null)
            editorSaveSlots = GetTree().GetFirstNodeInGroup("editor_save_slots") as EditorSaveSlots;

        // 订阅EditorSaveSlots数据变化信号，自动刷新槽位显示
        if (editorSaveSlots != null)
            editorSaveSlots.SlotDataChanged += OnSlotDataChanged;
    }

    // ✅ 新增：编辑模式下的 Extra配置 打开按钮
    private Button openConfigBtn;

    private void CreateOpenButton()
    {
        openConfigBtn = new Button();
        openConfigBtn.Name = "OpenCustomConfigBtn";
        TranslateGroup.SetText(openConfigBtn, "📦 Extra配置");
        openConfigBtn.CustomMinimumSize = new Vector2(120, 50);
        openConfigBtn.AddThemeFontSizeOverride("font_size", 14);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.3f, 0.5f, 0.7f, 0.9f);
        style.SetCornerRadiusAll(8);
        openConfigBtn.AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.4f, 0.6f, 0.8f, 0.95f);
        hoverStyle.SetCornerRadiusAll(8);
        openConfigBtn.AddThemeStyleboxOverride("hover", hoverStyle);

        openConfigBtn.AddThemeColorOverride("font_color", Colors.White);
        openConfigBtn.Pressed += () => {
            if (mainPanel?.Visible == true)
                Hide();
            else
                Show();
        };

        AddChild(openConfigBtn);

        var viewportSize = GetViewport().GetVisibleRect().Size;
        openConfigBtn.SetAnchorsPreset(LayoutPreset.TopLeft);
        openConfigBtn.Position = new Vector2(viewportSize.X - 290, viewportSize.Y - 130);
        openConfigBtn.Visible = false; // 初始隐藏，编辑模式时显示
    }

    /// <summary>控制Extra配置按钮的显示/隐藏（由TerrainEditor调用）</summary>
    public void SetOpenButtonVisible(bool visible)
    {
        if (openConfigBtn != null)
            openConfigBtn.Visible = visible;
    }

    private string GodotPathCombine(params string[] parts)
    {
        return string.Join("/", parts).Replace("\\", "/");
    }

    private void CreateMainPanel()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        mainPanel = new Panel();
        mainPanel.Name = "CustomConfigPanel";

        // ✅ 自适应屏幕：手机端缩小面板，确保不超出屏幕
        float panelWidth = Mathf.Min(500, viewportSize.X * 0.95f);
        float panelHeight = Mathf.Min(700, viewportSize.Y * 0.85f);
        mainPanel.CustomMinimumSize = new Vector2(panelWidth, panelHeight);
        mainPanel.SetAnchorsPreset(LayoutPreset.Center);
        mainPanel.ZIndex = 1500;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.1f, 0.15f, 0.98f);
        style.SetCornerRadiusAll(16);
        style.SetBorderWidthAll(3);
        style.BorderColor = new Color(0.5f, 0.7f, 0.9f);
        mainPanel.AddThemeStyleboxOverride("panel", style);

        AddChild(mainPanel);

        // ✅ 拖拽手柄
        var dragHandle = new Panel();
        dragHandle.Name = "DragHandle";
        dragHandle.CustomMinimumSize = new Vector2(panelWidth, 35);
        dragHandle.Position = new Vector2(0, 0);
        var handleStyle = new StyleBoxFlat();
        handleStyle.BgColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
        handleStyle.SetCornerRadiusAll(16);
        dragHandle.AddThemeStyleboxOverride("panel", handleStyle);

        var dragHint = new Label();
        TranslateGroup.SetText(dragHint, "⋮⋮ 拖拽移动");
        dragHint.AddThemeFontSizeOverride("font_size", 10);
        dragHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.7f, 0.8f));
        dragHint.Position = new Vector2(8, 2);
        dragHandle.AddChild(dragHint);

        mainPanel.AddChild(dragHandle);
        MakeMenuDraggable(mainPanel, dragHandle);

        // 标题栏
        var titleBar = new HBoxContainer();
        titleBar.Position = new Vector2(10, 40);
        titleBar.Size = new Vector2(panelWidth - 20, 40);

        var title = new Label();
        TranslateGroup.SetText(title, "⚙️ 自定义配置中心");
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 1f));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleBar.AddChild(title);

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(36, 36);
        closeBtn.Pressed += Hide;
        titleBar.AddChild(closeBtn);

        mainPanel.AddChild(titleBar);

        // Tab容器
        tabContainer = new TabContainer();
        tabContainer.Position = new Vector2(10, 85);
        tabContainer.Size = new Vector2(panelWidth - 20, panelHeight - 95);
        mainPanel.AddChild(tabContainer);
    }

    private void CreateTabs()
    {
        // 1. 自定义移动方式
        moveTypePanel = CreateMoveTypeTab();
        tabContainer.AddChild(moveTypePanel);
        tabContainer.SetTabTitle(0, TranslateGroup.T("🚶 移动方式"));

        // 2. 自定义地形
        terrainTypePanel = CreateTerrainTypeTab();
        tabContainer.AddChild(terrainTypePanel);
        tabContainer.SetTabTitle(1, TranslateGroup.T("🗺️ 地形种类"));

        // 3. 贴图上传
        textureUploadPanel = CreateTextureUploadTab();
        tabContainer.AddChild(textureUploadPanel);
        tabContainer.SetTabTitle(2, TranslateGroup.T("🎨 贴图上传"));

        // 4. 保存槽位
        saveSlotPanel = CreateSaveSlotTab();
        tabContainer.AddChild(saveSlotPanel);
        tabContainer.SetTabTitle(3, TranslateGroup.T("💾 保存/加载"));
        
        // 5. 势力配置
        factionConfigPanel = new FactionConfigPanel();
        factionConfigPanel.Name = "FactionConfig";
        tabContainer.AddChild(factionConfigPanel);
        tabContainer.SetTabTitle(4, TranslateGroup.T("🏴 势力配置"));

        // 6. 分屏调整（每屏宽高加减+锚点）
        var screenResizePanel = BuildScreenResizeTab();
        tabContainer.AddChild(screenResizePanel);
        tabContainer.SetTabTitle(5, TranslateGroup.T("🖥️ 分屏调整"));
    }

    // ==================== 通用方法 ====================

    public new void Show()
    {
        if (mainPanel == null) return;
        mainPanel.Show();
        // ✅ 确保 ExtraSave 已初始化
        if (extraSave == null)
        {
            extraSave = GetTree()?.GetFirstNodeInGroup("extra_save") as ExtraSave;
        }
        RefreshCustomMoveTypeList();
        RefreshCustomTerrainTypeList();
        RefreshUploadedTextureList();
        RefreshApplyTextureList();
        RefreshSaveSlotTab(); // 保存槽位界面
        GD.Print(TranslateGroup.T("[CustomConfigUI] 配置面板已打开，所有列表已刷新"));
    }

    public new void Hide()
    {
        mainPanel?.Hide();
    }



    public override void _Process(double delta)
    {
        if (isDraggingMenu && mainPanel != null)
        {
            Vector2 newPosition = GetGlobalMousePosition() + dragOffset;
            var viewportSize = GetViewport().GetVisibleRect().Size;
            newPosition.X = Mathf.Clamp(newPosition.X, 0, viewportSize.X - mainPanel.Size.X);
            newPosition.Y = Mathf.Clamp(newPosition.Y, 0, viewportSize.Y - mainPanel.Size.Y);
            mainPanel.GlobalPosition = newPosition;
        }
    }

    // ✅ 拖拽增强：使任意对话框可拖拽并管理ZIndex
    private void MakeMenuDraggable(Control menuPanel, Control dragHandle = null)
    {
        Control handle = dragHandle ?? menuPanel;
        handle.MouseFilter = MouseFilterEnum.Stop;

        handle.GuiInput += (InputEvent @event) => {
            if (@event is InputEventMouseButton mouseEvent)
            {
                if (mouseEvent.ButtonIndex == MouseButton.Left)
                {
                    if (mouseEvent.Pressed)
                    {
                        isDraggingMenu = true;
                        dragOffset = menuPanel.GlobalPosition - handle.GetGlobalMousePosition();
                        menuPanel.ZIndex = 2000; // 拖拽时置顶
                    }
                    else
                    {
                        isDraggingMenu = false;
                        menuPanel.ZIndex = menuPanel.Name == "CustomConfigPanel" ? 1500 : 1600;
                    }
                }
            }
        };
    }

    private void ShowToast(string message)
    {
        var toast = new Label();
        toast.Text = message;
        toast.HorizontalAlignment = HorizontalAlignment.Center;
        toast.AddThemeFontSizeOverride("font_size", 14);
        toast.AddThemeColorOverride("font_color", Colors.Yellow);
        toast.SetAnchorsPreset(LayoutPreset.CenterTop);
        toast.Position = new Vector2(0, 100);
        toast.ZIndex = 2000;

        var bg = new ColorRect();
        bg.Color = new Color(0, 0, 0, 0.8f);
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        toast.AddChild(bg);

        AddChild(toast);

        var timer = new Timer();
        timer.WaitTime = 2.0f;
        timer.OneShot = true;
        timer.Timeout += () => { toast.QueueFree(); timer.QueueFree(); };
        AddChild(timer);
        timer.Start();
    }
}
