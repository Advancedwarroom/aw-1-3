extends SceneTree
# 开创人2026-11-11: 设施换色冒烟断言——CI 直接炸，不靠手机截图（ScreenErrorLogger 哲学）
# City 是锚点灾难当事设施：P-1 必须挂上色相材质且 hue_shift 落在紫色带 [200°,340°]
# 入口必须是 _initialize()：_init() 是构造器，此刻 get_root() 还是 null

func _initialize() -> void:
    var packed = load("res://Prefabs/City.tscn")
    if packed == null:
        print("FACILITY_RECOLOR_CHECK_FAIL: City.tscn 加载失败")
        quit(1)
        return
    var fac = packed.instantiate()
    get_root().add_child(fac)
    fac.facilityTeam = "P-1"
    fac.UpdateCityVisual()

    var spr = fac.get_node_or_null("BaseAnimSprite")
    var dual = spr != null and spr.sprite_frames != null
    if not dual:
        spr = fac.get_node_or_null("AnimatedSprite2D")
    if spr == null:
        print("FACILITY_RECOLOR_CHECK_FAIL: 找不到设施精灵节点")
        quit(1)
        return

    var mat = spr.material
    if mat == null:
        print("FACILITY_RECOLOR_CHECK_FAIL: P-1 未挂色相材质（GetHueMaterial 返回 null？看 PushError）")
        quit(1)
        return
    var shift = mat.get_shader_parameter("hue_shift")
    print("P-1 City hue_shift = ", shift, "°  (双图层=", dual, ")")
    if shift == null or shift < 200.0 or shift > 340.0:
        print("FACILITY_RECOLOR_CHECK_FAIL: hue_shift 出紫色带")
        quit(1)
        return
    print("FACILITY_RECOLOR_CHECK_PASS")
    quit(0)
