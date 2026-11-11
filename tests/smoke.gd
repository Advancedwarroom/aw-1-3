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
	main_instance.queue_free()
	main_instance = null
	await get_tree().process_frame
	await get_tree().process_frame
	get_tree().quit(0)
