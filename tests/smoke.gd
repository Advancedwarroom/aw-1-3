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
	# 最简退出：对照组 facility_recolor_check.gd 同款——零拆卸操作，57秒干净退出
	get_tree().quit(0)
