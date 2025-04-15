extends Node3D

#Avec Lerp
#@export var target_path: NodePath
#@export var offset := Vector3(0, 5, -10) # Position relative à Pelote
#@export var follow_speed := 2.0
#
#var target: Node3D
#
#func _ready():
	#target = get_node(target_path)
#
#func _physics_process(delta):
	#if not target:
		#return
#
	## Obtenons la position cible de la Pelote sans affecter la hauteur
	#var target_position = target.global_transform.origin
#
	## Nous ignorons la hauteur de la pelote pour garder la caméra à une hauteur fixe
	#target_position.y = offset.y
#
	## Calculer la position de la caméra, et la rendre fluide avec lerp
	#var camera_position = target_position + offset
#
	## Lerp entre la position actuelle et la nouvelle position
	#global_transform.origin = global_transform.origin.lerp(camera_position, follow_speed * delta)
	
	
#Sans Lerp 
@export var target_path: NodePath
@export var offset := Vector3(0, 20, 0)  # Caméra vue de dessus, à 20 unités de haut

var target: Node3D

func _ready():
	target = get_node(target_path)

func _physics_process(delta):
	if not target:
		return

	# On suit X et Z, on garde Y fixe
	var target_position = target.global_transform.origin
	target_position.y = offset.y

	global_position = target_position + offset
