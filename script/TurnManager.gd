extends Node

signal turn_changed(new_number: int)
var turn_number: int = 1

func _input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT:
		turn_number += 1
func next_turn() -> void:
	for u in get_tree().get_nodes_in_group("units"):
		u.reset_turn_state()
	print("进入第", turn_number, "回合")
	turn_changed.emit(turn_number)
