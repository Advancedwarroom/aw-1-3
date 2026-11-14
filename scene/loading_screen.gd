extends Control

const GAME_SCENE := "res://scene/main.tscn"

var label: Label
var enter_button: Button
var _loaded: PackedScene = null

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
		# ✅ 开创人2026-11-14: 加载完成直接自动进场——零按钮,菜单自己会居中显示在战场上
		call_deferred("_auto_enter")
	elif status == ResourceLoader.THREAD_LOAD_FAILED:
		set_process(false)
		label.text = "加载失败！"

func _on_enter_pressed() -> void:
	if _loaded != null:
		get_tree().change_scene_to_packed(_loaded)

func _auto_enter() -> void:
	await get_tree().create_timer(0.4).timeout
	_on_enter_pressed()
