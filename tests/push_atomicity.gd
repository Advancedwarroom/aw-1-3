extends Node
# 开创人2026-11-13: 推挤链原子性+扩展模块回归。
# A(核心回归): 链②第二环撞不可推动设施→整链放弃；B(对照): 无阻挡整链推满；
# C(边缘循环): 撞x=6边界回绕；D(卸力仪式): 削重后推动成功；E(仪式对照): 关闭则原子放弃。

const MAIN_SCENE_PATH := "res://scene/main.tscn"
const GRID_SCENE_PATH := "res://Prefabs/grid.tscn"
const UNIT_SCENE_PATH := "res://Prefabs/infantry(1).tscn"
const FACILITY_SCENE_PATH := "res://Prefabs/City.tscn"

var gm: Node = null
var grid_manager: Node = null
var unit_manager: Node = null
var grids_node: Node = null

func _ready() -> void:
	await _run()

func _fail(msg: String) -> void:
	print("PUSH_ATOMICITY_FAIL: ", msg)
	get_tree().quit(1)

func _run() -> void:
	var packed: PackedScene = load(MAIN_SCENE_PATH)
	if packed == null:
		_fail("main.tscn 加载失败")
		return
	var main_instance = packed.instantiate()
	add_child(main_instance)
	for i in 3:
		await get_tree().process_frame

	gm = get_tree().get_first_node_in_group("game_manager")
	if gm == null:
		_fail("game_manager 不在树里")
		return
	grid_manager = gm.get("gridManager")
	unit_manager = gm.get("unitManager")
	grids_node = grid_manager.get("GridsNode")
	if grid_manager == null or unit_manager == null or grids_node == null:
		_fail("gridManager/unitManager/GridsNode 缺失")
		return

	_clear_field()
	_build_row()
	await get_tree().process_frame

	# ===== 场景B（对照组，第0排）：无阻挡，整链推2格 =====
	var trio_b := _spawn_push_trio(0)
	if trio_b.is_empty():
		return
	trio_b[0].call("TryPushTargetOnAttack", trio_b[1])
	if _idx(trio_b[1]) != Vector2i(3, 0):
		_fail("场景B: 链①应到(3,0)，实际 " + str(_idx(trio_b[1])))
		return
	if _idx(trio_b[2]) != Vector2i(4, 0):
		_fail("场景B: 链②应到(4,0)，实际 " + str(_idx(trio_b[2])))
		return
	print("场景B PASS: 整链推满，链①(3,0) 链②(4,0)")
	for u in trio_b:
		unit_manager.call("RemoveUnit", u)
	await get_tree().process_frame

	# ===== 场景A（核心回归，第0排）：(4,0)放不可推动设施，链②第二步撞死→整链放弃 =====
	var fac: Node2D = load(FACILITY_SCENE_PATH).instantiate()
	grids_node.add_child(fac)
	var tile4: Node = _tile_at(Vector2i(4, 0))
	fac.set("ParentGrid", tile4)
	fac.set("facilityTeam", "Player2")
	tile4.set("city", fac)

	var trio_a := _spawn_push_trio(0)
	if trio_a.is_empty():
		return
	var hp_a: Array = []
	for u in trio_a:
		hp_a.append(int(u.get("health")))
	trio_a[0].call("TryPushTargetOnAttack", trio_a[1])
	var expect_a := [Vector2i(0, 0), Vector2i(1, 0), Vector2i(2, 0)]
	for k in 3:
		if _idx(trio_a[k]) != expect_a[k]:
			_fail("场景A: 单位" + str(k) + " 被污染！应" + str(expect_a[k]) + " 实际 " + str(_idx(trio_a[k])))
			return
		if int(trio_a[k].get("health")) != hp_a[k]:
			_fail("场景A: 单位" + str(k) + " HP被结算！")
			return
	print("场景A PASS: 后续链失败，前面零移动零伤害零消耗")
	for u in trio_a:
		unit_manager.call("RemoveUnit", u)
	await get_tree().process_frame

	# ===== 场景C（边缘循环，第1排）：(4,1)推(5,1)两格，撞x=6回绕→(0,1)→(1,1) =====
	var trio_c := _spawn_push_trio(1)
	if trio_c.is_empty():
		return
	trio_c[0].set("pushWrapAroundEdges", true)
	trio_c[0].call("TryPushTargetOnAttack", trio_c[1])
	if _idx(trio_c[1]) != Vector2i(1, 1):
		_fail("场景C: 循环两格应到(1,1)，实际 " + str(_idx(trio_c[1])))
		return
	print("场景C PASS: 撞边回绕到(1,1)")
	for u in trio_c:
		unit_manager.call("RemoveUnit", u)
	await get_tree().process_frame

	# ===== 场景D（卸力仪式，第1排）：重1推重10本推不动，仪式削9后成功 =====
	var p_d: Node = _spawn_one(Vector2i(0, 1), "Player1", {
		"pushEnabled": true, "pushTriggerMode": 1, "pushDistance": 1, "pushRange": 1,
		"pushMinTargetRange": 1, "pushUsesWeight": true, "weight": 1})
	var t_d: Node = _spawn_one(Vector2i(1, 1), "Player2", {
		"weight": 10, "pushMitigationEnabled": true, "pushMitigationCeremony": true,
		"mitigationWeakensWeight": true, "mitigationWeightWeakenMode": 0, "mitigationWeightWeakenValue": 9})
	if p_d == null or t_d == null:
		return
	p_d.call("TryPushTargetOnAttack", t_d)
	if _idx(t_d) != Vector2i(2, 1):
		_fail("场景D: 仪式削重后应到(2,1)，实际 " + str(_idx(t_d)))
		return
	print("场景D PASS: 卸力仪式削重后推动成功")
	for u in [p_d, t_d]:
		unit_manager.call("RemoveUnit", u)
	await get_tree().process_frame

	# ===== 场景E（仪式对照，第1排）：同配置仪式关闭→原子放弃 =====
	var p_e: Node = _spawn_one(Vector2i(0, 1), "Player1", {
		"pushEnabled": true, "pushTriggerMode": 1, "pushDistance": 1, "pushRange": 1,
		"pushMinTargetRange": 1, "pushUsesWeight": true, "weight": 1})
	var t_e: Node = _spawn_one(Vector2i(1, 1), "Player2", {"weight": 10})
	if p_e == null or t_e == null:
		return
	p_e.call("TryPushTargetOnAttack", t_e)
	if _idx(t_e) != Vector2i(1, 1):
		_fail("场景E: 推不动应留在(1,1)，实际 " + str(_idx(t_e)))
		return
	print("场景E PASS: 仪式关闭时原子放弃")
	for u in [p_e, t_e]:
		unit_manager.call("RemoveUnit", u)

	# ===== 场景F（推己方，第1排）：同队目标+友军可推 → 友军被推动一格 =====
	var p_f: Node = _spawn_one(Vector2i(0, 1), "Player1", {
		"pushEnabled": true, "pushTriggerMode": 1, "pushDistance": 1, "pushRange": 1,
		"pushMinTargetRange": 1, "pushCanTargetFriendlies": true})
	var t_f: Node = _spawn_one(Vector2i(1, 1), "Player1", {})
	if p_f == null or t_f == null:
		return
	p_f.call("TryPushTargetOnAttack", t_f)
	if _idx(t_f) != Vector2i(2, 1):
		_fail("场景F: 友军应被推到(2,1)，实际 " + str(_idx(t_f)))
		return
	print("场景F PASS: 友军推动")
	for u in [p_f, t_f]:
		unit_manager.call("RemoveUnit", u)
	await get_tree().process_frame

	# ===== 场景G（撞地形自毁，第1排）：(3,1)改为SEA，推1格撞水 → 目标销毁、grid=null =====
	var sea_tile: Node = _tile_at(Vector2i(3, 1))
	sea_tile.set("gridType", 3) # SEA
	var p_g: Node = _spawn_one(Vector2i(1, 1), "Player1", {
		"pushEnabled": true, "pushTriggerMode": 1, "pushDistance": 1, "pushRange": 2,
		"pushMinTargetRange": 1, "pushSelfDestructOnInvalidIntermediateTile": true})
	var t_g: Node = _spawn_one(Vector2i(2, 1), "Player2", {})
	if p_g == null or t_g == null:
		return
	p_g.call("TryPushTargetOnAttack", t_g)
	if t_g.get("grid") != null:
		_fail("场景G: 撞水自毁后 grid 应为 null，实际 " + str(_idx(t_g)))
		return
	print("场景G PASS: 撞地形自毁")
	for u in [p_g]:
		unit_manager.call("RemoveUnit", u)
	await get_tree().process_frame
	sea_tile.set("gridType", 0) # 还原 GROUND

	# ===== 场景H（链无上限，第1排）：pushMaxChainStacks=0 时四深链整体推一格 =====
	var row_h: Array = []
	for k in 5:
		var u: Node = _spawn_one(Vector2i(k, 1), "Player2" if k > 0 else "Player1", {})
		if u == null:
			return
		row_h.append(u)
	row_h[0].set("pushEnabled", true)
	row_h[0].set("pushTriggerMode", 1)
	row_h[0].set("pushDistance", 1)
	row_h[0].set("pushRange", 1)
	row_h[0].set("pushMinTargetRange", 1)
	row_h[0].set("pushCanTargetEnemies", true)
	row_h[0].set("pushCanTargetFriendlies", true)
	row_h[0].call("TryPushTargetOnAttack", row_h[1])
	for k in range(1, 5):
		if _idx(row_h[k]) != Vector2i(k + 1, 1):
			_fail("场景H: 第" + str(k) + "环应到(" + str(k + 1) + ",1)，实际 " + str(_idx(row_h[k])))
			return
	print("场景H PASS: 四深混合链无上限")
	for u in row_h:
		unit_manager.call("RemoveUnit", u)

	print("PUSH_ATOMICITY_PASS")
	for i in 5:
		await get_tree().process_frame
	get_tree().quit(0)

# ---- helpers ----

func _clear_field() -> void:
	var existing: Array = unit_manager.get("AllUnits")
	for u in existing:
		unit_manager.call("RemoveUnit", u)
	for c in grids_node.get_children():
		grids_node.remove_child(c)
		c.queue_free()

func _build_row() -> void:
	grid_manager.set("searchRange", Vector2i(6, 2))
	grid_manager.set("startPos", Vector2i(0, 0))
	grid_manager.set("gridSize", Vector2i(32, 32))
	var grid_scene: PackedScene = load(GRID_SCENE_PATH)
	for x in 6:
		for y in 2:
			var t: Node2D = grid_scene.instantiate()
			t.position = Vector2(x * 32, y * 32)
			grids_node.add_child(t)
	grid_manager.call("Init")

func _tile_at(idx: Vector2i) -> Node:
	return grid_manager.call("GetGridAt", idx)

func _idx(unit: Node) -> Vector2i:
	var g: Node = unit.get("grid")
	return g.get("GridIndex") if g != null else Vector2i(-99, -99)

func _spawn_one(pos: Vector2i, team: String, cfg: Dictionary) -> Node:
	var unit_scene: PackedScene = load(UNIT_SCENE_PATH)
	var u: Node = unit_manager.call("SpawnUnit", unit_scene, pos, team)
	if u == null:
		_fail("SpawnUnit 失败 @ " + str(pos))
		return null
	u.set("health", 10)
	for k in cfg:
		u.set(k, cfg[k])
	return u

func _spawn_push_trio(row: int) -> Array:
	# 推动者(0,row)+链①(1,row)+链②(2,row)，推2格，重量关
	var us: Array = []
	var unit_scene: PackedScene = load(UNIT_SCENE_PATH)
	var teams := ["Player1", "Player2", "Player2"]
	for k in 3:
		var u: Node = unit_manager.call("SpawnUnit", unit_scene, Vector2i(k, row), teams[k])
		if u == null:
			_fail("SpawnUnit 失败")
			return []
		u.set("health", 10)
		us.append(u)
	us[0].set("pushEnabled", true)
	us[0].set("pushTriggerMode", 1)
	us[0].set("pushDistance", 2)
	us[0].set("pushRange", 1)
	us[0].set("pushMinTargetRange", 1)
	us[0].set("pushUsesWeight", false)
	return us
