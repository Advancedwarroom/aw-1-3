extends Control

# 主菜单路由表：各模式当前全部先进入主场景 main.tscn（占位）。
# 模式专属场景就绪后只改这张表，按钮逻辑不用动。
const ROUTES := {
	"campaign": "res://scene/campaign_menu.tscn",   # 战役（选关页）
	"skirmish": "res://scene/main.tscn",   # 遭遇战（自由对战）
	"warroom": "res://scene/main.tscn",    # 作战室（挑战残局）
	"daily": "res://scene/main.tscn",      # 每日挑战
	"editor": "res://scene/main.tscn",     # 地图编辑器
	"codex": "res://scene/main.tscn",      # 单位图鉴
	"records": "res://scene/main.tscn",    # 战绩
	"settings": "res://scene/main.tscn",   # 设置
}

func _ready() -> void:
	$Buttons/Campaign.pressed.connect(_go.bind("campaign"))
	$Buttons/Skirmish.pressed.connect(_go.bind("skirmish"))
	$Buttons/WarRoom.pressed.connect(_go.bind("warroom"))
	$Buttons/Daily.pressed.connect(_go.bind("daily"))
	$Buttons/Editor.pressed.connect(_go.bind("editor"))
	$Buttons/Codex.pressed.connect(_go.bind("codex"))
	$Buttons/Records.pressed.connect(_go.bind("records"))
	$Buttons/Settings.pressed.connect(_go.bind("settings"))

func _go(route: String) -> void:
	get_tree().change_scene_to_file(ROUTES[route])
