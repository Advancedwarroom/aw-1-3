extends Node
# 开创人2026-11-11: 设施换色冒烟断言（诊断版——每环都打印，FAIL信息直接指认断点）

func _ready() -> void:
	var grid_scene = load("res://Prefabs/grid.tscn")
	if grid_scene == null:
		_fail("grid.tscn 加载失败")
		return
	var grids = grid_scene.instantiate()
	add_child(grids)

	var packed = load("res://Prefabs/City.tscn")
	if packed == null:
		_fail("City.tscn 加载失败")
		return
	var fac = packed.instantiate()
	grids.add_child(fac)
	fac.set("facilityTeam", "P-1")

	print("has_method(UpdateCityVisual) = ", fac.has_method("UpdateCityVisual"))
	fac.UpdateCityVisual()

	var base = fac.get_node_or_null("BaseAnimSprite")
	var normal = fac.get_node_or_null("NormalAnimSprite")
	if normal == null:
		normal = fac.get_node_or_null("BaseAnimSprite/NormalAnimSprite")
	var legacy = fac.get_node_or_null("AnimatedSprite2D")
	var base_frames = base.get("sprite_frames") if base != null else "无节点"
	var base_mat = base.get("material") if base != null else "无节点"
	print("BaseAnimSprite: 存在=", base != null, " 帧=", base_frames != null, " 材质=", base_mat != null)
	print("NormalAnimSprite 存在=", normal != null, " AnimatedSprite2D 存在=", legacy != null)

	var mat = null
	var which = ""
	if base_mat != null and base_mat is Material:
		mat = base_mat; which = "BaseAnimSprite"
	elif legacy != null and legacy.get("material") is Material:
		mat = legacy.get("material"); which = "AnimatedSprite2D"
	if mat == null:
		_fail("材质空|base存在:%s base帧:%s base材质:%s legacy存在:%s" % [base != null, base_frames != null, base_mat != null, legacy != null])
		return
	var shift = mat.get_shader_parameter("hue_shift")
	var mode = mat.get_shader_parameter("mode")
	print(which, " hue_shift=", shift, " mode=", mode)
	if mode != null and int(mode) == 2:
		print("mode2模板色表材质——明暗阶路径, 通过")
	else:
		if shift == null or shift < 140.0 or shift > 340.0:
			_fail("hue_shift 出紫色带: " + str(shift))
			return
	print("FACILITY_RECOLOR_CHECK_PASS")
	get_tree().quit(0)

func _fail(msg: String) -> void:
	print("FACILITY_RECOLOR_CHECK_FAIL: ", msg)
	get_tree().quit(1)
