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
	if main_instance != null and is_instance_valid(main_instance):
		main_instance.free()
	main_instance = null
	print("[TEST_CLEANUP] found TranslateGroup: ", is_instance_valid(TranslateGroup))
	if is_instance_valid(TranslateGroup):
		TranslateGroup.ReleaseRuntimeResources()
		print("[TEST_CLEANUP] called ReleaseRuntimeResources")
	# Some effects deliberately reparent themselves to the SceneTree root. They are
	# transient in this test and must be freed before checking shutdown leaks.
	for child in tree.root.get_children(true):
		if child == self:
			continue
		child.free()
	await tree.process_frame
	await tree.process_frame
	tree.quit(0)
