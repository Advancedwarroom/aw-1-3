extends Control

const GAME_SCENE := "res://scene/main.tscn"

var label: Label

func _ready() -> void:
	label = $Label
	# 后台线程加载真正的主场景，不卡首帧
	ResourceLoader.load_threaded_request(GAME_SCENE)

func _process(_delta: float) -> void:
	var progress: Array = []
	var status := ResourceLoader.load_threaded_get_status(GAME_SCENE, progress)
	# 千分制：别人 * 100，我们 * 1000
	label.text = "加载中：%d‰" % int(progress[0] * 1000)
	match status:
		ResourceLoader.THREAD_LOAD_LOADED:
			set_process(false)
			var packed := ResourceLoader.load_threaded_get(GAME_SCENE) as PackedScene
			get_tree().change_scene_to_packed(packed)
		ResourceLoader.THREAD_LOAD_FAILED:
			set_process(false)
			label.text = "加载失败！"
