extends Node2D

var camera: Camera2D = null
var is_dragging = false
var drag_start_pos = Vector2.ZERO
var camera_start_pos = Vector2.ZERO
var min_zoom = 0.5
var max_zoom = 3.0
var zoom_speed = 0.1

# 严格参数
var hold_threshold = 0.25
var drag_threshold = 16

var battlefield_size = Vector2(1920, 1080)

var is_holding = false
var hold_timer = 0.0
var mouse_pressed_pos = Vector2.ZERO

func _ready():
	camera = get_viewport().get_camera_2d()
	if camera:
		camera.position = battlefield_size / 2
		camera.position_smoothing_enabled = false

# ✅ 核心：用物理查询检查鼠标下是否有可点击对象
func _has_clickable_under_mouse() -> bool:
	var mouse_pos = get_global_mouse_position()
	
	# 创建物理查询参数
	var query = PhysicsPointQueryParameters2D.new()
	query.position = mouse_pos
	query.collide_with_areas = true
	query.collide_with_bodies = false
	
	# 执行查询
	var space_state = get_world_2d().direct_space_state
	var results = space_state.intersect_point(query)
	
	for result in results:
		var collider = result.get("collider")
		if collider == null:
			continue
		
		var parent = collider.get_parent()
		if parent == null:
			continue
		
		# 检查是否是 Infantry 或 Weapon（通过组或类型）
		if parent.is_in_group("infantry") or parent.is_in_group("weapons"):
			return true
		
		# 检查是否是 Grid（格子）
		if parent.is_in_group("grids") or parent.name == "Area2D":
			# 如果是格子且处于强制操作模式（显示范围中），也算可点击
			if parent.has_method("ClickGrid") or parent.get_parent().has_method("ClickGrid"):
				return true
	
	return false

func _is_any_menu_open() -> bool:
	var terrain_editor = get_tree().get_first_node_in_group("terrain_editor")
	if terrain_editor:
		if terrain_editor.call("IsAnyMenuOpen") == true:
			return true
	var action_menu = get_tree().get_first_node_in_group("action_menu")
	if action_menu is Control and action_menu.visible:
		return true
	var gm = get_tree().get_first_node_in_group("game_manager")
	if gm and gm.call("IsProductionMenuOpen") == true:
		return true
	return false

func _input(event):
	if not camera:
		return
	
	if _is_any_menu_open():
		_stop_drag()
		return
	
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP and event.pressed:
			zoom_in()
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN and event.pressed:
			zoom_out()
		elif event.button_index == MOUSE_BUTTON_LEFT:
			if event.pressed:
				# ✅ 关键：检查是否点到了单位/兵器/格子
				if _has_clickable_under_mouse():
					# 有点击目标，不启动拖拽！
					return
				
				# 空地 → 启动拖拽
				is_holding = true
				hold_timer = 0.0
				mouse_pressed_pos = get_global_mouse_position()
				drag_start_pos = mouse_pressed_pos
				camera_start_pos = camera.position
			else:
				_stop_drag()

func _stop_drag():
	is_holding = false
	is_dragging = false
	hold_timer = 0.0

func _process(delta):
	if _is_any_menu_open():
		_stop_drag()
		return
	
	if is_holding and not is_dragging:
		hold_timer += delta
		var current_pos = get_global_mouse_position()
		var move_dist = current_pos.distance_to(mouse_pressed_pos)
		
		# 严格判定
		if move_dist > drag_threshold and hold_timer >= 0.05:  # 至少50ms
			is_dragging = true
			drag_start_pos = current_pos
			camera_start_pos = camera.position
	
	if is_dragging and camera:
		var current_mouse_pos = get_global_mouse_position()
		var delta_pos = current_mouse_pos - drag_start_pos
		camera.position = camera_start_pos - delta_pos

func zoom_in():
	if camera:
		var new_zoom = clamp(camera.zoom.x + zoom_speed, min_zoom, max_zoom)
		camera.zoom = Vector2(new_zoom, new_zoom)

func zoom_out():
	if camera:
		var new_zoom = clamp(camera.zoom.x - zoom_speed, min_zoom, max_zoom)
		camera.zoom = Vector2(new_zoom, new_zoom)
