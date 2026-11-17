extends Control

# 战役选关：优先扫描 user://campaigns/（玩家自建/覆盖图），首次打开自动把 APK 内置图播种过去。
# 之后玩家往 user://campaigns 丢 .txt 即可加关卡，不必等重新打包。
const PENDING := "user://campaign_pending.map"
const USER_CAMPAIGNS := "user://campaigns"
const PACK_CAMPAIGNS := "res://campaigns"

func _ready() -> void:
	_seed_pack_campaigns()
	BootMaps.SeedUserCampaigns()   # ✅ 开创人2026-11-18:res://缺文件时,编译内置图兜底写入 user://campaigns
	var box: VBoxContainer = $MapList
	var dir := DirAccess.open(USER_CAMPAIGNS)
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

# ✅ 开创人2026-11-18:内置图播种——user://campaigns 无 .txt 时,从 APK 内置 res://campaigns 复制
func _seed_pack_campaigns() -> void:
	DirAccess.make_dir_recursive_absolute(USER_CAMPAIGNS)
	var user_dir := DirAccess.open(USER_CAMPAIGNS)
	if user_dir != null:
		for f in user_dir.get_files():
			if f.ends_with(".txt"):
				return   # 玩家已有图,不覆盖
	var pack := DirAccess.open(PACK_CAMPAIGNS)
	if pack == null:
		return
	for f in pack.get_files():
		if f.ends_with(".txt"):
			var src := FileAccess.open(PACK_CAMPAIGNS + "/" + f, FileAccess.READ)
			if src == null:
				continue
			var content := src.get_as_text()
			src.close()
			var dst := FileAccess.open(USER_CAMPAIGNS + "/" + f, FileAccess.WRITE)
			if dst == null:
				continue
			dst.store_string(content)
			dst.close()

func _on_pick(file_name: String) -> void:
	var src := FileAccess.open(USER_CAMPAIGNS + "/" + file_name, FileAccess.READ)
	if src == null:
		return
	var content := src.get_as_text()
	src.close()
	var dst := FileAccess.open(PENDING, FileAccess.WRITE)
	if dst == null:
		return
	dst.store_string(content)
	dst.close()
	# ✅ 方案甲：改走 loading_screen 的 boot 路径——选关也享千分制分帧开盘（pending 由 boot 消费）
	get_tree().change_scene_to_file("res://scene/loading_screen.tscn")

func _on_back() -> void:
	# ✅ 方案甲: main.tscn 已掏实体,直落=空战场;返回走 loading_screen 重新 boot 默认图
	get_tree().change_scene_to_file("res://scene/loading_screen.tscn")
