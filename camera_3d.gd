extends Camera3D
@export var target: NodePath
@export var follow_speed := 5.0
@export var offset := Vector3(0, 5, -10)

var target_node: 

func _ready():
	target_node = get_node(target)

func _process(delta):
	if not target_node:
		return

	var desired_position = target_node.global_transform.origin + offset
	global_transform.origin = global_transform.origin.lerp(desired_position, follow_speed * delta)
