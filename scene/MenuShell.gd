extends CanvasLayer
# MenuShell.gd - 主菜单十二项接信号+选中高亮+详情面板 (开创人2026-11-14)
var _buttons: Array[Button] = []
var _panel: PanelContainer
var _info: RichTextLabel
var _selected: Button = null

var _dragging := false
var _drag_off := Vector2.ZERO

func _ready() -> void:
    for child in $MenuList.get_children():
        if child is Button:
            _buttons.append(child)
            child.pressed.connect(_on_pressed.bind(child))
            child.focus_mode = Control.FOCUS_NONE
    _build_panel()
    # ✅ 开创人2026-11-14: 开局自动居中显示+可拖动——手机不点按钮,拖开即玩
    call_deferred("_auto_layout")
    if not $MenuList.gui_input.is_connected(_on_menu_drag):
        $MenuList.gui_input.connect(_on_menu_drag)

func _auto_layout() -> void:
    var vp: Vector2 = get_viewport().get_visible_rect().size
    var sz: Vector2 = $MenuList.size
    if sz.x < 100.0:
        sz = $MenuList.get_combined_minimum_size()
    $MenuList.position = Vector2(maxf(8.0, (vp.x - sz.x) / 2.0), maxf(8.0, (vp.y - sz.y) / 2.0))
    $MenuList.visible = true

func _on_menu_drag(event: InputEvent) -> void:
    if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
        _dragging = event.pressed
        if _dragging: _drag_off = $MenuList.get_global_mouse_position() - $MenuList.global_position
        $MenuList.accept_event()
    elif event is InputEventMouseMotion and _dragging:
        $MenuList.global_position = $MenuList.get_global_mouse_position() - _drag_off

func _build_panel() -> void:
    _panel = PanelContainer.new()
    _panel.position = Vector2(400, 60)
    _panel.size = Vector2(520, 560)
    _panel.visible = false
    add_child(_panel)
    _info = RichTextLabel.new()
    _info.bbcode_enabled = true
    _info.fit_content = true
    _panel.add_child(_info)

func _on_pressed(btn: Button) -> void:
    if _selected and is_instance_valid(_selected): _selected.modulate = Color.WHITE
    _selected = btn
    btn.modulate = Color(1.0, 0.82, 0.25)   # ✅ 选中高亮
    match btn.text:
        "纪念馆": _show_memorial()
        "正规战役", "自定义战役":
            get_tree().change_scene_to_file("res://scene/campaign_menu.tscn")
        "肉鸽模式": _show_survival()
        _:
            _show_placeholder(btn.text)

func _show_memorial() -> void:
    var p = Engine.get_singleton("CampaignProfile")
    var node = get_tree().root.find_child("CampaignProfile", true, false)
    var body := "[b]纪念馆 · 战绩总账[/b]\n\n"
    if node:
        body += "总  分：%s\n宝  钞：%s\n战役进度：%s\n已通关卡：%s\n\n[i]商店与成就兑换处建设中——账先记着,店马上开。[/i]" % [
            node.TotalScore, node.TotalBlackCrystals, node.CurrentProgress, node.ClearedSlots.size()]
    else:
        body += "[i]CampaignProfile 未注册自动加载——\n项目设置→自动加载→添加 CampaignProfile + CampaignSettlement + SurvivalSession[/i]"
    _info.text = body
    _panel.visible = true

func _show_survival() -> void:
    _ensure_survival_start_btn()
    _info.text = "[b]肉鸽模式 · 生存战役[/b]\n\n规则：一串关卡一条命,三选一全局预算（资金/回合/时间）,打完主线进无尽池,预算逐层缩减,战败即结算层数。\n\n[i]开局前在自动加载注册 SurvivalSession,\n并用控制台配置战役（stageSlots 指存档槽）。\n示例:\nSurvivalSession.Instance.BeginCampaign(c)[/i]"
    _panel.visible = true

func _show_placeholder(title: String) -> void:
    _info.text = "[b]%s[/b]\n\n[i]建设中。骨架已立,魂在排队。[/i]" % title
    _panel.visible = true

# ✅ 开创人2026-11-14 接缝④: 肉鸽模式"直接开局"按钮(双槽演示战役)
func _ensure_survival_start_btn() -> void:
    var old = _panel.find_child("SurvivalQuickStart", true, false)
    if old: old.queue_free()
    var s = get_tree().root.find_child("SurvivalSession", true, false)
    if s == null or not s.has_method("BeginQuickDemo"):
        return
    var b := Button.new()
    b.name = "SurvivalQuickStart"
    b.text = "⚔ 直接开局（双槽演示 · 30回合命）"
    b.position = Vector2(20, 216)
    b.size = Vector2(280, 36)
    b.pressed.connect(func():
        s.BeginQuickDemo()
        _panel.visible = false)
    _panel.add_child(b)
    # 面板一藏就焚尸——按钮不许跨面板赖着
    if not _panel.visibility_changed.is_connected(_cleanup_survival_start_btn):
        _panel.visibility_changed.connect(_cleanup_survival_start_btn)

func _cleanup_survival_start_btn() -> void:
    if _panel.visible: return
    var old = _panel.find_child("SurvivalQuickStart", true, false)
    if old: old.queue_free()
