extends Node
# 开创人2026-11-18: 地图序列化 lint 硬门——内置战役图每次构建必检
# 抓三类病: 节段缺失/行格式漂移/类型引用不可解析(沉默丢数据的上游)
# 阶段一(本门): 语法+引用 lint; 阶段二(待纯API拆分): 导出→导入→再导出字节比对

const KNOWN_UNITS = ["AA","APC","AT","Art","FBo","Flr","Inf","LTk","Mch","Ooz","RBt","Rck","Rec"]
# 扩展新兵种时在此登记其图内短名
const KNOWN_WEAPONS = ["BCn","LargeCannon","Lsr"]
const KNOWN_FACILITIES = ["Apt","Bse","Cty"]

func _ready() -> void:
	var path := "res://campaigns/main_battlefield.txt"
	if not FileAccess.file_exists(path):
		_fail("战役图不存在: " + path)
		return
	var text := FileAccess.open(path, FileAccess.READ).get_as_text()
	if text.length() < 100:
		_fail("战役图过小(%d字符),疑似空图" % text.length())
		return
	var errors := []
	var unit_cnt := 0
	var weapon_cnt := 0
	var fac_cnt := 0
	var in_units := false
	var in_weapons := false
	var in_facs := false
	for ln in text.split("\n"):
		var line := ln.strip_edges()
		if line.is_empty(): continue
		if line.begins_with("=== "):
			# ✅ 开创人2026-11-18: 节段是短码(U/W/F),不是全词——首跑此处误判全空
			var sec := line.trim_prefix("=== ").trim_suffix(" ===").strip_edges()
			in_units = sec == "U"
			in_weapons = sec == "W"
			in_facs = sec == "F"
			continue
		if line.begins_with("["):
			var m := RegEx.create_from_string("^\\[(\\d+),(\\d+)\\]\\s+(\\S+)").search(line)
			if m == null:
				errors.append("行格式坏: " + line.substr(0, 60))
				continue
			var tname := m.get_string(3)
			if in_units:
				unit_cnt += 1
				if tname not in KNOWN_UNITS: errors.append("未知单位类型: " + tname)
			elif in_weapons:
				weapon_cnt += 1
				if tname not in KNOWN_WEAPONS: errors.append("未知兵器类型: " + tname)
			elif in_facs:
				fac_cnt += 1
				if tname not in KNOWN_FACILITIES: errors.append("未知设施类型: " + tname)
	if unit_cnt == 0: errors.append("单位节段为空")
	if weapon_cnt == 0: errors.append("兵器节段为空(大炮/幽灵炮丢了?)")
	if fac_cnt == 0: errors.append("设施节段为空")
	if errors.size() > 0:
		_fail("；".join(errors))
		return
	print("MAP_LINT: units=%d weapons=%d facilities=%d" % [unit_cnt, weapon_cnt, fac_cnt])
	print("MAP_LINT_PASS")
	get_tree().quit(0)

func _fail(msg: String) -> void:
	print("MAP_LINT_FAIL: ", msg)
	get_tree().quit(1)
