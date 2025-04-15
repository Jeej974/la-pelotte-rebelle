extends Area3D

#le signal de collision
func _on_body_entered(body):
	if body is RigidBody3D and body.name == "Pelote":
		print("La pelote est entrée dans la zone !")
		#il disparait
		queue_free()	
