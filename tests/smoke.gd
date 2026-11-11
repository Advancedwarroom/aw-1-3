extends Node

const MAIN_SCENE_PATH := "res://scene/main.tscn"
const FRAMES_TO_RUN := 600

var main_instance: Node = null

func _ready() -> void:
	await _run_smoke()

func _run_smoke() -> void:
	var packed: PackedScene = load(MAIN_SCENE_PATH)
	if packed == null:
		push_error("SMOKE_FAIL: unable to load " + MAIN_SCENE_PATH)
		get_tree().quit(1)
		return

	main_instance = packed.instantiate()
	add_child(main_instance)

	for _frame in range(FRAMES_TO_RUN):
		await get_tree().process_frame

	print("SMOKE_PASS: %d frames" % FRAMES_TO_RUN)
	var tree := get_tree()
	# 只释放 TranslateGroup 静态注册表；节点树交给 quit() 自然拆卸。
	# 手动 free() 在树中节点会导致 C# 静态引用悬空 → 退出 ERROR 洪灾（CI grep 判挂）。
	# 参考 tests/facility_recolor_check.gd：quit(0) 自然拆卸即干净退出。
	if is_instance_valid(TranslateGroup):
		TranslateGroup.ReleaseRuntimeResources()
	game_manager = null
	# 冲刷queue_free+让mono finalizer跑完，消除退出时假泄漏报告（228 CanvasItem/688 ObjectDB）
	for i in 10:
		await tree.process_frame
	tree.quit(0)
