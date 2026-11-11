# tests/selfplay.gd
# 自对弈回归 harness —— 双 AI 托管对局 + 全程黑盒不变量断言
# 设计原则：只接公开接口（组 + 公开字段），不看任何实现细节；
#           属性名探测不到就跳过该断言，绝不误报。
#
# 本地跑：
#   godot --headless --path . res://tests/selfplay.tscn -- --max-phases 60
#
# 命令行参数（-- 之后）：
#   --max-phases N     跑多少个阶段（默认 60）
#   --stall-frames N   多少帧没换阶段算卡死（默认 3000）
#   --max-hp N         HP 上限（默认 100，对应原作 10HP 制）
extends Node

var max_phases: int = 60
var stall_frames: int = 3000
var max_hp: float = 100.0
var timeout_frames: int = 200000
var fail_fast: int = 50
var diag_dumped: bool = false

var game_manager: Node = null
var ai_manager: Node = null
var game_instance: Node = null
var failures: Array = []
var phase_changes: int = 0
var last_phase = null
var frames_since_phase: int = 0
var total_frames: int = 0
var done: bool = false

func _ready() -> void:
	_parse_args()
	var main_scene: PackedScene = load("res://scene/main.tscn")
	if main_scene == null:
		_fail("无法加载 res://scene/main.tscn")
		_finish()
		return
	game_instance = main_scene.instantiate()
	add_child(game_instance)

	game_manager = get_tree().get_first_node_in_group("game_manager")
	ai_manager = get_tree().get_first_node_in_group("ai_manager")
	if game_manager == null or ai_manager == null:
		_fail("场景里找不到 game_manager / ai_manager 组节点")
		_finish()
		return

	# 双 AI 托管 + 全自动 + 无镜头跟随
	ai_manager.set("p1AIEnabled", true)
	ai_manager.set("p2AIEnabled", true)
	ai_manager.set("autoEndTurn", true)
	ai_manager.set("stepDelay", 0.0)
	ai_manager.set("moveStepTime", 0.0)
	ai_manager.set("followCamera", false)
	ai_manager.set("logDecisions", false)

	print("[SELFPLAY] 开局：双AI托管 max_phases=%d" % max_phases)
	# 第一下可能在我们启用 AI 之前就已经错过，补一脚（不是 AI 阶段时它是空操作）
	ai_manager.call("OnPhaseStart")

func _process(_delta: float) -> void:
	if done:
		return
	total_frames += 1
	frames_since_phase += 1

	if game_manager.get("gameEnded") == true:
		print("[SELFPLAY] 游戏自然结束 @阶段#%d" % phase_changes)
		_finish()
		return

	var phase = game_manager.get("turnPhase")
	if phase != last_phase:
		phase_changes += 1
		frames_since_phase = 0
		last_phase = phase
		diag_dumped = false
		print("[SELFPLAY] 阶段 #%d -> %s" % [phase_changes, str(phase)])
		_log_phase_brief()
		if phase_changes >= max_phases:
			print("[SELFPLAY] 达到 max_phases，正常收工")
			_finish()
			return

	_check_invariants()

	if frames_since_phase == int(stall_frames * 0.3) and not diag_dumped:
		diag_dumped = true
		_dump_stall_diag()

	if frames_since_phase > stall_frames:
		_fail("卡死：%d 帧未换阶段（encounterPause=%s，当前阶段=%s）——若是遭遇战在等待玩家裁定，需要给 harness 接自动裁定" % [stall_frames, str(game_manager.get("encounterPause")), str(phase)])
		_finish()
		return

	if total_frames > timeout_frames:
		_fail("超时：%d 帧只完成 %d/%d 阶段" % [total_frames, phase_changes, max_phases])
		_finish()
		return

func _check_invariants() -> void:
	var um = game_manager.get("unitManager")
	if um == null:
		return
	var units = um.get("AllUnits")
	if units == null:
		return
	var seen_grids := {}
	for u in units:
		if u == null or not is_instance_valid(u):
			_fail("AllUnits 里有 null/已释放条目")
			continue
		# HP 越界（属性存在才查）
		var hp = null
		for prop in ["health", "hp"]:
			hp = u.get(prop)
			if hp != null:
				break
		if hp != null and (typeof(hp) == TYPE_INT or typeof(hp) == TYPE_FLOAT):
			if hp <= 0.0 or hp > max_hp:
				_fail("单位 %s HP 越界: %s" % [str(u.name), str(hp)])
		# 格子占用唯一性（运输中的不占格）
		if u.get("isTransported") == true:
			continue
		var g = u.get("grid")
		if g == null or not is_instance_valid(g):
			_fail("单位 %s 没有有效格子" % str(u.name))
			continue
		var key := str(g.get("GridIndex"))
		if seen_grids.has(key):
			_fail("格子 %s 上叠了多个单位" % key)
		else:
			seen_grids[key] = true

	# 资金不为负（常见字段名探测，查到才查）
	for prop in ["p1Funds", "p2Funds", "fundsP1", "fundsP2", "p1Money", "p2Money"]:
		var v = game_manager.get(prop)
		if v != null and (typeof(v) == TYPE_INT or typeof(v) == TYPE_FLOAT) and v < 0:
			_fail("%s = %s（资金为负）" % [prop, str(v)])

func _log_phase_brief() -> void:
	var parts := []
	for prop in ["turnPhase", "currentTeam", "activeTeam", "CurrentWeather", "currentWeather"]:
		var v = game_manager.get(prop)
		if v != null:
			parts.append("%s=%s" % [prop, str(v)])
	var um = game_manager.get("unitManager")
	if um != null:
		var units = um.get("AllUnits")
		if units != null:
			parts.append("units=%d" % units.size())
	for prop in ["p1Funds", "p2Funds"]:
		var v = game_manager.get(prop)
		if v != null:
			parts.append("%s=%s" % [prop, str(v)])
	print("[SELFPLAY-STATE] " + " | ".join(parts))

func _dump_stall_diag() -> void:
	print("[SELFPLAY-DIAG] ===== 卡死现场 dump =====")
	print("[DIAG] encounterPause=%s gameEnded=%s" % [str(game_manager.get("encounterPause")), str(game_manager.get("gameEnded"))])
	print("[DIAG] ai: p1=%s p2=%s autoEndTurn=%s" % [str(ai_manager.get("p1AIEnabled")), str(ai_manager.get("p2AIEnabled")), str(ai_manager.get("autoEndTurn"))])
	for prop in ["currentTeam", "currentTeamIndex", "activeTeam", "teamIndex", "currentStep", "aiState", "state", "thinking", "isThinking", "busy"]:
		var v1 = game_manager.get(prop)
		if v1 != null:
			print("[DIAG] gm.%s = %s" % [prop, str(v1)])
		var v2 = ai_manager.get(prop)
		if v2 != null:
			print("[DIAG] ai.%s = %s" % [prop, str(v2)])
	var popups := get_tree().root.find_children("*", "ConfirmationDialog", true, false)
	var accepts := get_tree().root.find_children("*", "AcceptDialog", true, false)
	print("[DIAG] 弹窗 ConfirmationDialog=%d AcceptDialog=%d" % [popups.size(), accepts.size()])
	for p in popups:
		print("[DIAG]   弹窗 visible=%s title=%s" % [str(p.visible), str(p.get("title"))])
	var menus := get_tree().root.find_children("*ActionMenu*", "Control", true, false)
	print("[DIAG] ActionMenu节点=%d" % menus.size())
	for m in menus:
		print("[DIAG]   菜单 visible=%s pos=%s" % [str(m.visible), str(m.get("global_position"))])
	var um = game_manager.get("unitManager")
	if um != null:
		var units = um.get("AllUnits")
		if units != null:
			print("[DIAG] 单位总数=%d" % units.size())
	print("[SELFPLAY-DIAG] ===== dump 结束 =====")

func _fail(msg: String) -> void:
	if done or failures.size() >= fail_fast:
		return
	failures.append(msg)
	push_error("[SELFPLAY-FAIL] " + msg)
	print("[SELFPLAY-FAIL] " + msg)

func _finish() -> void:
	if done:
		return
	done = true
	var report := {
		"result": ("FAIL" if failures.size() > 0 else "PASS"),
		"phases_completed": phase_changes,
		"frames": total_frames,
		"failures": failures
	}
	var f := FileAccess.open("user://selfplay_report.json", FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(report, "  "))
	print("[SELFPLAY] 结果: %s | 阶段: %d | 失败: %d" % [report["result"], phase_changes, failures.size()])
	var exit_code := 0 if failures.is_empty() else 1
	var tree := get_tree()
	# 只释放 TranslateGroup 静态注册表（防跨测试污染）；节点树交给 quit() 自然拆卸。
	# 手动 free() 在树中节点会导致 C# 静态引用悬空 → "Cannot get path of node"+退出泄漏 ERROR 洪灾（CI grep 判挂）。
	# 参考 tests/facility_recolor_check.gd：quit(0) 自然拆卸即干净退出（该断言 45 秒绿）。
	if is_instance_valid(TranslateGroup):
		TranslateGroup.ReleaseRuntimeResources()
	game_manager = null
	ai_manager = null
	# 冲刷queue_free+让mono finalizer跑完，消除退出时假泄漏报告（228 CanvasItem/688 ObjectDB）
	for i in 10:
		await tree.process_frame
	tree.quit(exit_code)

func _parse_args() -> void:
	var args := OS.get_cmdline_user_args()
	for i in range(args.size()):
		var a := str(args[i])
		if a == "--max-phases" and i + 1 < args.size():
			max_phases = int(args[i + 1])
		elif a.begins_with("--max-phases="):
			max_phases = int(a.split("=")[1])
		elif a == "--stall-frames" and i + 1 < args.size():
			stall_frames = int(args[i + 1])
		elif a.begins_with("--stall-frames="):
			stall_frames = int(a.split("=")[1])
		elif a.begins_with("--max-hp="):
			max_hp = float(a.split("=")[1])
