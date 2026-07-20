# Unit.gd  所有单位的父类
extends CharacterBody2D
class_name Unit

const GRID_SIZE := 32

@export var team: String = "player1"
@export var unit_name: String = ""
@export var move_range: int = 2          # 走几格
@export var max_hp: int = 10
@onready var sprite: Sprite2D = $Sprite2D   # 默认子节点叫 Sprite2D

var hp: int:
	set(v): hp = v; hp_changed.emit(hp)
signal hp_changed(new_hp)

func move_to_mouse_tile(mouse_tile: Vector2i, grid_size: int) -> void:
	var map = get_tree().current_scene.get_node("TileMapLayer")
	var world_center: Vector2 = map.map_to_local(mouse_tile) + Vector2(grid_size*0.5, grid_size*0.5)
	var unit_tile: Vector2i = logical_tile()

	print("单位格子=", unit_tile, " 目标格子=", mouse_tile, " 世界坐标=", world_center)

	sprite.global_position = world_center
	$CollisionShape2D.global_position = world_center

# 返回当前逻辑格子（按视觉中心对齐）
func logical_tile() -> Vector2i:
	# 1. 先拿到主场景
	var main = get_tree().current_scene
	# 2. 用绝对路径抓 TileMapLayer（按你的实际路径写）
	var tile_map = main.get_node("TileMapLayer")   # 如果你的 TileMapLayer 直接挂在 Main 下
	# 若 TileMapLayer 在别的节点下面，写成如 main.get_node("World/TileMapLayer")
	if not is_instance_valid(tile_map):
		push_error("TileMapLayer 未找到，请检查路径")
		return Vector2i(-1, -1)

	# 3. 用 Sprite 视觉中心转格子
	var sprite_center := sprite.global_position
	return tile_map.local_to_map(tile_map.to_local(sprite_center))

# 共用的被击、死亡等空函数，子类可覆写
func take_damage(dmg: int) -> void:
	hp = max(hp - dmg, 0)
	if hp == 0: queue_free()
