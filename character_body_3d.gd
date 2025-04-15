extends CharacterBody3D
@export var gravity: float = 30.0
@export var speed: float = 10.0
@export var jump_force: float = 12.0

func _physics_process(delta):
	var input_direction = Vector3.ZERO

	if Input.is_action_pressed("move_up"):
		input_direction.x += 1
	if Input.is_action_pressed("move_down"):
		input_direction.x -= 1
	if Input.is_action_pressed("move_right"):
		input_direction.z += 1
	if Input.is_action_pressed("move_left"):
		input_direction.z -= 1

	input_direction = input_direction.normalized()

	# Mouvement horizontal
	var horizontal_velocity = input_direction * speed
	velocity.x = horizontal_velocity.x
	velocity.z = horizontal_velocity.z

	# Gravité
	if not is_on_floor():
		velocity.y -= gravity * delta
	else:
		if Input.is_action_just_pressed("ui_accept"):
			velocity.y = jump_force
		else:
			velocity.y = -0.1  # Légère pression vers le bas pour rester au sol

	# Déplacement
	move_and_slide()
