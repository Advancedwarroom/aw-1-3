extends Node
# 开创人2026-11-11: 设施换色冒烟断言——CI 直接炸，不靠手机截图（ScreenErrorLogger 哲学）
# 结构对齐 selfplay.gd 已验证模式：extends Node + _ready + .tscn 场景进入
# City 是锚点灾难当事设施：P-1 必须挂上色相材质且 hue_shift 落在紫色带 [200°,340°]

func _ready() -> void:
	var packed = load("res://Prefabs/City.tscn")
	if packed == null:
		_fail("City.tscn 加载失败")
		return
	var fac = packed.instantiate()
	add_child(fac)
	fac.set("facilityTeam", "P-1")
	if fac.has_method("UpdateCityVisual"):
		fac.UpdateCityVisual()

	var spr = fac.get_node_or_null("BaseAnimSprite")
	var dual = spr != null and spr.get("sprite_frames") != null
	if not dual:
		spr = fac.get_node_or_null("AnimatedSprite2D")
	if spr == null:
		_fail("找不到设施精灵节点")
		return

	var mat = spr.get("material")
	if mat == null:
		_fail("P-1 未挂色相材质（GetHueMaterial 返回 null？看 PushError）")
		return
	var shift = mat.get_shader_parameter("hue_shift")
	print("P-1 City hue_shift = ", shift, "  (双图层=", dual, ")")
	if shift == null or shift < 200.0 or shift > 340.0:
		_fail("hue_shift 出紫色带: " + str(shift))
		return
	print("FACILITY_RECOLOR_CHECK_PASS")
	get_tree().quit(0)

func _fail(msg: String) -> void:
	print("FACILITY_RECOLOR_CHECK_FAIL: ", msg)
	get_tree().quit(1)
