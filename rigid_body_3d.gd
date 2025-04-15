extends RigidBody3D

@export var speed := 10.0
var direction := Vector3.ZERO

func _physics_process(delta):
	direction = Vector3.ZERO

	if Input.is_action_pressed("ui_up"):
		direction.x += 1
	if Input.is_action_pressed("ui_down"):
		direction.x -= 1
	if Input.is_action_pressed("ui_right"):
		direction.z += 1
	if Input.is_action_pressed("ui_left"):
		direction.z -= 1

	direction = direction.normalized()

	# Appliquer une force sur X/Z seulement (pas de Y = pas de vol)
	apply_central_force(Vector3(direction.x, 0, direction.z) * speed)


func _on_cat_1_body_entered(body: Node3D):
	print("la balle est touché !!!!!!!")
