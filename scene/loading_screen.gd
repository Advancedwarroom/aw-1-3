extends Control

const GAME_SCENE := "res://scene/main.tscn"

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
	_start_load()

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
	label.text = "加载中：0‰"
	ResourceLoader.load_threaded_request(GAME_SCENE)

func _notification(what: int) -> void:
	match what:
		NOTIFICATION_APPLICATION_RESUMED:
			# ✅ 2026-11-15 恢复兜底：安卓切后台会冻住/打断加载线程，回来若已失败立刻重开，
			#    不再永驻"加载失败"死标签（此前无任何前后台处理，进不去只能杀进程）
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
		# 不自动跳场景：数字跳完，亮起按钮，玩家自己按进主菜单
		enter_button.show()
	elif status == ResourceLoader.THREAD_LOAD_FAILED or status == ResourceLoader.THREAD_LOAD_INVALID_RESOURCE:
		set_process(false)
		# ✅ 失败不再一死了之：亮重试按钮
		label.text = "加载失败！点击重试"
		enter_button.text = "重试加载"
		enter_button.show()

func _on_enter_pressed() -> void:
	if _loaded != null:
		get_tree().change_scene_to_packed(_loaded)
	else:
		# ✅ 加载未成时按钮=重试（失败后重进那次点击直接救场）
		_start_load()
