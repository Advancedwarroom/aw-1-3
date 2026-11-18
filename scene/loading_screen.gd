extends Control

const GAME_SCENE := "res://scene/main.tscn"

# ✅ 方案甲（2026-11-15）：进入战场后的"开盘加载"并入千分制——
#    main.tscn 的格子/单位改为运行时从 campaigns/main_battlefield.txt 逐行分帧生成，
#    本界面常驻顶层直到 BootMapLoader 报完成才淡出（消灭 change_scene 的 700+ 节点原子实例化黑块）。
#    ⚠️ commit1 dormant：BOOT_ENABLED=false 时全文件行为与旧版逐字节一致。
const BOOT_ENABLED := true
const BOOT_MAP := "res://campaigns/main_battlefield.txt"
const PHASE_A_PERMILLE := 500  # 资源加载段权重(0-500‰)，开盘段 BootMapLoader 报 500-1000‰
var _boot_active := false

var label: Label
var enter_button: Button
var _loaded: PackedScene = null
# ✅ 开创人2026-11-15: 主菜单整体可拖动——按住背景拖走,不抢按钮
var _menu_drag := false
var _menu_drag_offset := Vector2.ZERO

# ✅ 2026-11-15 真进度：递归枚举主场景全部依赖，按已缓存比例算千分制。
#    load_threaded_get_status 的 progress 只报顶层资源自身阶段，依赖树不计入——
#    整段加载期恒 0‰ 最后一刻跳满，是假指标，弃用。
var _dep_paths: Array[String] = []
var _load_started := false
# ✅ 开创人2026-11-18: 安卓mono线程加载不可靠→改逐帧手喂依赖表,标签真实时刷新
var _manual_load := false
var _dep_index := 0
var _autocontinue := false   # ✅ 开创人令：点击发起的加载完成后自动续行进战场
# ✅ 卡住检测：IN_PROGRESS 但已缓存数长期不涨 → 亮重试
var _last_cached := -1
var _stall_time := 0.0

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			_menu_drag = true
			_menu_drag_offset = get_global_mouse_position() - global_position
			accept_event()
		else:
			_menu_drag = false
	elif event is InputEventMouseMotion and _menu_drag:
		global_position = get_global_mouse_position() - _menu_drag_offset
		accept_event()

func _ready() -> void:
	label = $Label
	enter_button = $EnterButton
	enter_button.hide()
	enter_button.pressed.connect(_on_enter_pressed)
	_collect_dependencies(GAME_SCENE)
	# ✅ 开创人令（2026-11-18）：进度从按下"进入战场"才开始算——
	#    此前菜单空转就把条跑满，点击后的等待时间完全裸奔
	# ✅ 开创人2026-11-18(补完): 不显示——进度标签隐藏,点击后才弹出
	label.text = ""
	label.hide()
	enter_button.text = "进入战场"
	enter_button.show()
	# ✅ 关键: 菜单空闲期停轮询——_process 默认运行,首帧就把 INVALID_RESOURCE
	#    当失败走同步加载,条被秒满1000‰(11-18前半修复只改文案没刹住车轮)
	set_process(false)

# ✅ 递归收集场景依赖（去重）作分母；脚本(.gd/.cs/.csproj)跳过——has_cached 对脚本不可靠
func _collect_dependencies(path: String) -> void:
	if path in _dep_paths:
		return
	_dep_paths.append(path)
	for dep in ResourceLoader.get_dependencies(path):
		if dep.get_extension() in ["gd", "cs", "csproj"]:
			continue
		_collect_dependencies(dep)

func _start_load() -> void:
	_load_started = true
	_loaded = null
	_last_cached = -1
	_stall_time = 0.0
	set_process(true)
	enter_button.hide()
	enter_button.text = "进入战场"
	label.show()
	label.text = "加载中：0‰"
	if OS.has_feature("android"):
		# ✅ 2026-11-18: 同步加载UI冻结、进度永远画不出来——改逐帧手喂依赖表:
		#    每帧load少量依赖(缓存命中瞬时返回),标签按 已喂/总数 真实刷新,全喂完主场景秒出
		_manual_load = true
		_dep_index = 0
		return
	ResourceLoader.load_threaded_request(GAME_SCENE)

func _manual_load_step() -> void:
	var budget := 2
	while budget > 0 and _dep_index < _dep_paths.size():
		var p := _dep_paths[_dep_index]
		_dep_index += 1
		if ResourceLoader.exists(p) and not ResourceLoader.has_cached(p):
			ResourceLoader.load(p)
		budget -= 1
	label.text = "加载中：%d‰" % int(_dep_index * 1000.0 / max(_dep_paths.size(), 1))
	if _dep_index >= _dep_paths.size():
		_manual_load = false
		var ps: PackedScene = load(GAME_SCENE)
		if ps != null:
			_loaded = ps
			label.text = "加载中：1000‰"
			if _autocontinue or FileAccess.file_exists("user://campaign_pending.map"):
				_on_enter_pressed()
			else:
				enter_button.show()
		else:
			_show_load_failure()

func _show_load_failure() -> void:
	label.show()
	# 诊断探测器：区分"整个加载器坏死"还是"仅主场景坏"
	var probe = load("res://scene/campaign_menu.tscn")
	label.text = "加载失败！点击重试\n诊断: 战役菜单探测 %s" % ("OK" if probe != null else "FAIL")
	enter_button.text = "重试加载"
	enter_button.show()

func _notification(what: int) -> void:
	match what:
		NOTIFICATION_APPLICATION_RESUMED:
			# ✅ 2026-11-15 恢复兜底：安卓切后台会冻住/打断加载线程，回来若已失败立刻重开，
			#    不再永驻"加载失败"死标签（此前无任何前后台处理，进不去只能杀进程）
			# ✅ 2026-11-18：GL 上下文重建后不自动重绘，菜单侧同样全树刷一遍防黑屏
			_redraw_all(self)
			if _load_started and _loaded == null:
				var st := ResourceLoader.load_threaded_get_status(GAME_SCENE)
				if st == ResourceLoader.THREAD_LOAD_FAILED or st == ResourceLoader.THREAD_LOAD_INVALID_RESOURCE:
					_start_load()

func _cached_count() -> int:
	var n := 0
	for p in _dep_paths:
		if ResourceLoader.has_cached(p):
			n += 1
	return n

func _process(delta: float) -> void:
	if not _load_started:
		return
	if _manual_load:
		_manual_load_step()
		return
	if _boot_active:
		_poll_boot()
		return
	var status := ResourceLoader.load_threaded_get_status(GAME_SCENE)
	# 千分制真进度：依赖树已缓存比例
	label.text = "加载中：%d‰" % int(float(_cached_count()) / float(max(1, _dep_paths.size())) * 1000.0)
	if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
		var cur := _cached_count()
		if cur != _last_cached:
			_last_cached = cur
			_stall_time = 0.0
		else:
			_stall_time += delta
			if _stall_time > 5.0 and not enter_button.visible:
				label.text = "加载卡住？点击重试"
				enter_button.text = "重试加载"
				enter_button.show()
	elif status == ResourceLoader.THREAD_LOAD_LOADED:
		set_process(false)
		_loaded = ResourceLoader.load_threaded_get(GAME_SCENE) as PackedScene
		label.text = "加载中：1000‰"
		# ✅ 战役选关接力 / 开创人令的点击续行：两类都自动进，无需再点一次"进入战场"
		if _autocontinue or FileAccess.file_exists("user://campaign_pending.map"):
			_on_enter_pressed()
			return
		enter_button.show()
	elif status == ResourceLoader.THREAD_LOAD_FAILED or status == ResourceLoader.THREAD_LOAD_INVALID_RESOURCE:
		set_process(false)
		# mono 安卓线程加载偶发失败：先回退同步加载，仍失败才亮重试
		var fallback := load(GAME_SCENE)
		if fallback != null:
			_loaded = fallback as PackedScene
			label.text = "加载中：1000‰"
			if _autocontinue or FileAccess.file_exists("user://campaign_pending.map"):
				_on_enter_pressed()
				return
			enter_button.show()
		else:
			_show_load_failure()

func _on_enter_pressed() -> void:
	if _loaded != null:
		if BOOT_ENABLED and FileAccess.file_exists(BOOT_MAP):
			_start_boot()
			return
		get_tree().change_scene_to_packed(_loaded)
	elif not _load_started:
		# ✅ 开创人令（2026-11-18）：点击这一刻才开始加载，完成后 _autocontinue 自动续行进战场
		_autocontinue = true
		_start_load()
	else:
		# ✅ 加载未成时按钮=重试（失败后重进那次点击直接救场）
		_start_load()

# ✅ 方案甲：boot 模式——实例化轻量 main 场景，常驻顶层报千分制，BootMapLoader 分帧生成地图
func _start_boot() -> void:
	_boot_active = true
	enter_button.hide()
	var boot_map := BOOT_MAP
	if FileAccess.file_exists("user://campaign_pending.map"):
		boot_map = "user://campaign_pending.map"
	BootMapLoader.RequestBoot(boot_map)
	var scene := _loaded.instantiate()
	get_tree().root.add_child(scene)
	get_tree().current_scene = scene
	# 盖住战场画面，进度走完才淡出
	z_index = 4096
	top_level = true
	label.text = "加载中：%d‰" % PHASE_A_PERMILLE

func _poll_boot() -> void:
	if BootMapLoader.IsFailed:
		label.text = "战场加载失败：" + BootMapLoader.FailMessage
		enter_button.text = "请重启游戏"
		enter_button.show()
		set_process(false)
	elif BootMapLoader.IsCompleted:
		label.text = "加载中：1000‰"
		set_process(false)
		# ✅ 战役选关经 boot 消费 pending：成功后清掉，防止下次"进入战场"重放旧选关图
		if FileAccess.file_exists("user://campaign_pending.map"):
			DirAccess.remove_absolute("user://campaign_pending.map")
		var tw := create_tween()
		tw.tween_interval(0.15)
		tw.tween_property(self, "modulate:a", 0.0, 0.4)
		tw.tween_callback(queue_free)
	else:
		label.text = "加载中：%d‰  %s" % [PHASE_A_PERMILLE + BootMapLoader.Permille, BootMapLoader.PhaseLabel]

func _redraw_all(n: Node) -> void:
	if n is CanvasItem:
		n.queue_redraw()
	for c in n.get_children():
		_redraw_all(c)
