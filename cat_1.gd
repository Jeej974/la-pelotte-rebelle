extends Area3D


func _on_body_entered(body):
	if body is RigidBody3D and body.name == "Pelote":
		print("La pelote est entrée dans la zone !")
	queue_free()	
