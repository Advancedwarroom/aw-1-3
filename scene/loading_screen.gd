extends Control

const GAME_SCENE := "res://scene/main.tscn"

var label: Label
var enter_button: Button
var _loaded: PackedScene = null
# ✅ 开创人2026-11-15: 主菜单整体可拖动——按住背景拖走,不抢按钮
var _menu_drag := false
var _menu_drag_offset := Vector2.ZERO

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			_menu_drag = true
			_menu_drag_offset = get_global_mouse_position() - global_position
			accept_event()
		else:
			_menu_drag = false
	elif event is InputEventMouseMotion and _menu_drag:
		global_position = get_global_mouse_position() - _menu_drag_offset
		accept_event()

func _ready() -> void:
	label = $Label
	enter_button = $EnterButton
	enter_button.hide()
	enter_button.pressed.connect(_on_enter_pressed)
	# 后台线程加载主场景，不卡首帧
	ResourceLoader.load_threaded_request(GAME_SCENE)

func _process(_delta: float) -> void:
	var progress: Array = []
	var status := ResourceLoader.load_threaded_get_status(GAME_SCENE, progress)
	# 千分制：别人 * 100，我们 * 1000
	label.text = "加载中：%d‰" % int(progress[0] * 1000)
	if status == ResourceLoader.THREAD_LOAD_LOADED:
		set_process(false)
		_loaded = ResourceLoader.load_threaded_get(GAME_SCENE) as PackedScene
		label.text = "加载中：1000‰"
		# 不自动跳场景：数字跳完，亮起按钮，玩家自己按进主菜单
		enter_button.show()
	elif status == ResourceLoader.THREAD_LOAD_FAILED:
		set_process(false)
		label.text = "加载失败！"

func _on_enter_pressed() -> void:
	if _loaded != null:
		get_tree().change_scene_to_packed(_loaded)
