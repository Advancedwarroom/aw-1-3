extends CanvasLayer
# MenuShell.gd - 主菜单十二项接信号+选中高亮+详情面板 (开创人2026-11-14)
# ✅ 开创人2026-11-18修复(手机端三宗罪):
#  ①菜单超高被裁→小屏自动包 ScrollContainer,滚得到每个按钮;
#  ②"魂在排队"弹窗关不掉→面板内✕关闭 + 点面板外任意处关闭;
#  ③只有按钮缝隙能拖→标题区也能拖。
var _buttons: Array[Button] = []
var _panel: PanelContainer
var _panel_vb: VBoxContainer
var _enter_btn: Button = null
var _info: RichTextLabel
var _selected: Button = null
var _scroll: ScrollContainer = null
var _menu_list: Control = null

var _dragging := false
var _drag_off := Vector2.ZERO

func _ready() -> void:
	_menu_list = $MenuList   # 先捕获引用:滚动模式会搬家,$路径不再可靠
	for child in _menu_list.get_children():
		if child is Button:
			_buttons.append(child)
			child.pressed.connect(_on_pressed.bind(child))
			child.focus_mode = Control.FOCUS_NONE
	_build_panel()
	_make_scrollable()   # ✅ 2026-11-18修复①:超高列表包滚动容器
	call_deferred("_auto_layout")
	if not _menu_list.gui_input.is_connected(_on_menu_drag):
		_menu_list.gui_input.connect(_on_menu_drag)
	if has_node("Title") and not $Title.gui_input.is_connected(_on_menu_drag):
		$Title.gui_input.connect(_on_menu_drag)   # ✅ 2026-11-18修复③:标题区也能拖
	# ✅ 开创人2026-11-15: 十三号按钮"进入战场"——隐藏主菜单,直进背后已加载的main战场
	var enter_battle_btn = Button.new()
	enter_battle_btn.text = "⚔ 进入战场"
	enter_battle_btn.pressed.connect(_on_enter_battle)
	_enter_btn = enter_battle_btn
	add_child(enter_battle_btn)   # ✅ 常驻壳上不进 MenuList：壳不藏，按钮才永远够得着

# ✅ 2026-11-18修复①:MenuList 最小高度超出屏幕时,包进 ScrollContainer 全按钮可滚达
func _make_scrollable() -> void:
	var vp := get_viewport().get_visible_rect().size
	var need: float = _menu_list.get_combined_minimum_size().y
	if need <= vp.y - 16:
		return
	var ml: Control = _menu_list
	var col_w: float = ml.get_combined_minimum_size().x + 24
	var scroll := ScrollContainer.new()
	scroll.name = "MenuScroll"
	scroll.position = Vector2(maxi(8, (vp.x - col_w) / 2), 8)
	scroll.size = Vector2(mini(col_w, vp.x - 16), vp.y - 16)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	ml.get_parent().remove_child(ml)
	scroll.add_child(ml)
	ml.position = Vector2.ZERO
	_scroll = scroll
	if ml.gui_input.is_connected(_on_menu_drag):
		ml.gui_input.disconnect(_on_menu_drag)   # 滚动模式下拖动交给滚动,不再与按钮抢输入
	_panel.move_to_front()   # 弹窗必须在滚动层之上,否则输入被滚动毯子吃掉

func _auto_layout() -> void:
	var vp := get_viewport().get_visible_rect().size
	if _scroll != null and is_instance_valid(_scroll):
		var col_w: float = _menu_list.get_combined_minimum_size().x + 24
		_scroll.size = Vector2(mini(col_w, vp.x - 16), vp.y - 16)
		_scroll.position = Vector2(maxi(8, (vp.x - _scroll.size.x) / 2), 8)
		return   # 滚动模式:MenuList 由 ScrollContainer 托管,不再手工居中
	var sz: Vector2 = _menu_list.size
	if sz.x < 100: sz = _menu_list.get_combined_minimum_size()
	_menu_list.position = Vector2(maxi(8, (vp.x - sz.x) / 2), maxi(8, (vp.y - sz.y) / 2))
	_menu_list.visible = true
	if _enter_btn:
		var bsz: Vector2 = _enter_btn.size
		if bsz.x < 10: bsz = _enter_btn.get_combined_minimum_size()
		_enter_btn.position = Vector2(maxi(8, (vp.x - bsz.x) / 2), 8)

func _on_menu_drag(event: InputEvent) -> void:
	_drag_handle(event, _menu_list)

func _on_panel_drag(event: InputEvent) -> void:
	_drag_handle(event, _panel)

func _drag_handle(event: InputEvent, target: Control) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		_dragging = event.pressed
		if _dragging: _drag_off = target.get_global_mouse_position() - target.global_position
		target.accept_event()
	elif event is InputEventMouseMotion and _dragging:
		target.global_position = target.get_global_mouse_position() - _drag_off

func _on_enter_battle() -> void:
	# ✅ 开创人2026-11-15: 单程票改往返——⚔ 常驻壳上,切换 MenuList 显隐
	var open: bool = not _menu_list.visible   # ✅ 开创人2026-11-15: $节点返回未定型, :=推断失败致ParseError,显式注解
	_menu_list.visible = open
	if _scroll != null and is_instance_valid(_scroll):
		_scroll.visible = open
	if not open:
		_panel.visible = false
		if _selected and is_instance_valid(_selected): _selected.modulate = Color.WHITE
		_selected = null
	if _enter_btn:
		_enter_btn.text = "⚔ 进入战场" if open else "☰ 返回菜单"

func _build_panel() -> void:
	_panel = PanelContainer.new()
	_panel.position = Vector2(400, 60)
	_panel.size = Vector2(520, 560)
	_panel.visible = false
	add_child(_panel)
	_panel.gui_input.connect(_on_panel_drag)   # ✅ 开创人2026-11-15: 详情弹窗可拖动
	_panel_vb = VBoxContainer.new()
	_panel.add_child(_panel_vb)
	# ✅ 2026-11-18修复②:✕关闭键——弹窗不再是"只能拖不能关"的棺材
	var hb := HBoxContainer.new()
	hb.alignment = BoxContainer.ALIGNMENT_END
	_panel_vb.add_child(hb)
	var close_btn := Button.new()
	close_btn.text = "✕ 关闭"
	close_btn.pressed.connect(_close_panel)
	hb.add_child(close_btn)
	_info = RichTextLabel.new()
	_info.bbcode_enabled = true
	_info.fit_content = true
	_info.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_panel_vb.add_child(_info)

func _close_panel() -> void:
	_panel.visible = false
	if _selected and is_instance_valid(_selected): _selected.modulate = Color.WHITE
	_selected = null

# ✅ 2026-11-18修复②:点弹窗外面的任意处,也能关
func _unhandled_input(event: InputEvent) -> void:
	if _panel == null or not _panel.visible:
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if not _panel.get_global_rect().has_point(event.position):
			_close_panel()

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
	_info.text = "[b]肉鸽模式 · 生存战役[/b]\n\n规则：一串关卡一条命,三选一全局预算（资金/回合/时间）,打完主线进无尽池,预算逐层缩减,战败即结算层数。\n\n[i]开局前在自动加载注册 SurvivalSession,\n并用控制台配置战役（stageSlots 指存档槽）。\n示例:\nSurvivalSession.Instance.BeginQuickDemo()[/i]"
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
	b.custom_minimum_size = Vector2(0, 36)
	b.pressed.connect(func():
		s.BeginQuickDemo()
		_panel.visible = false)
	_panel_vb.add_child(b)   # ✅ 2026-11-18:进 VBox 布局,不再手写坐标(原 PanelContainer 单孩布局下坐标不可靠)
	# 面板一藏就焚尸——按钮不许跨面板赖着
	if not _panel.visibility_changed.is_connected(_cleanup_survival_start_btn):
		_panel.visibility_changed.connect(_cleanup_survival_start_btn)

func _cleanup_survival_start_btn() -> void:
	if _panel.visible: return
	var old = _panel.find_child("SurvivalQuickStart", true, false)
	if old: old.queue_free()
