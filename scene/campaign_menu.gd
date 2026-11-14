extends Control

# 战役选关：扫描 res://campaigns/ 下的地图导出文件（.txt），
# 选定后写入 user://campaign_pending.map，主场景 TerrainEditor 启动时接管导入。
const PENDING := "user://campaign_pending.map"
const CAMPAIGNS_DIR := "res://campaigns"

func _ready() -> void:
	var box: VBoxContainer = $MapList
	var dir := DirAccess.open(CAMPAIGNS_DIR)
	var found := false
	if dir != null:
		for f in dir.get_files():
			if f.ends_with(".txt"):
				found = true
				var b := Button.new()
				b.text = f.trim_suffix(".txt")
				b.custom_minimum_size = Vector2(400, 56)
				b.pressed.connect(_on_pick.bind(f))
				box.add_child(b)
	if not found:
		var l := Label.new()
		l.text = "暂无战役地图\n把地图导出文件（.txt）放进 campaigns/ 目录"
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		box.add_child(l)
	$BackButton.pressed.connect(_on_back)

func _on_pick(file_name: String) -> void:
	var src := FileAccess.open(CAMPAIGNS_DIR + "/" + file_name, FileAccess.READ)
	if src == null:
		return
	var content := src.get_as_text()
	src.close()
	var dst := FileAccess.open(PENDING, FileAccess.WRITE)
	if dst == null:
		return
	dst.store_string(content)
	dst.close()
	get_tree().change_scene_to_file("res://scene/main.tscn")

func _on_back() -> void:
	get_tree().change_scene_to_file("res://scene/main.tscn")   # ✅ 开创人2026-11-14: 僵尸标题页已火化,返回直落main
