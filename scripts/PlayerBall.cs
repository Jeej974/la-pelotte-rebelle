using Godot;
using System;

public partial class PlayerBall : RigidBody3D
{
	[Export]
	private float _moveForce = 70.0f;
	
	[Export]
	private float _jumpForce = 10.0f;
	
	[Export]
	private bool _useArduino = true;
	
	// Référence au gestionnaire Arduino
	private ArduinoManager _arduinoManager;
	
	// Variables pour le contrôle au clavier
	private Vector3 _inputDirection = Vector3.Zero;
	private bool _jumpRequested = false;
	
	// Référence à la caméra et au pivot
	private Camera3D _camera;
	private Node3D _cameraFollowPivot;
	
	// Configuration de la caméra
	[Export]
	private Vector3 _cameraOffset = new Vector3(0, 10, 5);
	[Export]
	private Vector3 _cameraRotation = new Vector3(-60, 0, 0);
	
	// État de téléportation
	private bool _isBeingTeleported = false;
	
	// Matériaux pour différents états
	private StandardMaterial3D _normalMaterial;
	private StandardMaterial3D _teleportMaterial;
	
	// Contrôles activés/désactivés
	private bool _controlsEnabled = true;
	
	public override void _Ready()
	{
		// Configurer la physique
		Mass = 3.0f;
		CanSleep = false;
		CustomIntegrator = false;
		
		// Configurer les collisions
		CollisionLayer = 1;
		CollisionMask = 1;
		
		// Obtenir le pivot de suivi de caméra
		_cameraFollowPivot = GetNode<Node3D>("PlayerFollowPivot");
		
		// Déplacer la caméra du parent direct (PlayerBall) vers le pivot de suivi
		_camera = GetNodeOrNull<Camera3D>("Camera3D");
		if (_camera != null)
		{
			// Déplacer la caméra du PlayerBall au pivot
			RemoveChild(_camera);
			_cameraFollowPivot.AddChild(_camera);
			
			// Configurer la position et rotation de la caméra
			_camera.Position = _cameraOffset;
			_camera.RotationDegrees = _cameraRotation;
			_camera.Current = true;
			
			GD.Print("Caméra déplacée vers le pivot et configurée");
		}
		else
		{
			GD.PrintErr("Caméra non trouvée sur le joueur!");
		}
		
		// S'assurer que le RigidBody commence non-endormi
		Sleeping = false;
		
		// Créer les matériaux
		CreateMaterials();
	}
	
	// Mise à jour pour le suivi de caméra
	public override void _Process(double delta)
	{
		// Mettre à jour le pivot de la caméra pour qu'il suive la position de la balle 
		// mais conserve une orientation fixe
		if (_cameraFollowPivot != null)
		{
			// Le pivot suit la position de la balle, mais garde une rotation indépendante
			_cameraFollowPivot.GlobalPosition = GlobalPosition;
			_cameraFollowPivot.GlobalRotation = Vector3.Zero; // Réinitialiser la rotation pour qu'elle reste fixe
		}
	}
	
	// Nouvelle méthode pour recevoir directement l'ArduinoManager de MainScene
	public void SetArduinoManager(ArduinoManager manager)
	{
		_arduinoManager = manager;
		if (_arduinoManager != null)
		{
			GD.Print("ArduinoManager assigné directement au PlayerBall");
			_useArduino = true;
		}
		else
		{
			GD.Print("ArduinoManager non assigné, utilisation des contrôles clavier");
			_useArduino = false;
		}
	}
	
	private void CreateMaterials()
	{
		// Matériau normal (état par défaut)
		_normalMaterial = new StandardMaterial3D();
		_normalMaterial.AlbedoColor = new Color(0.2f, 0.6f, 1.0f); // Bleu
		_normalMaterial.Metallic = 0.7f;
		_normalMaterial.Roughness = 0.2f;
		
		// Matériau de téléportation (lors des transitions)
		_teleportMaterial = new StandardMaterial3D();
		_teleportMaterial.AlbedoColor = new Color(1.0f, 0.5f, 1.0f); // Rose
		_teleportMaterial.EmissionEnabled = true;
		_teleportMaterial.Emission = new Color(0.5f, 0.2f, 0.5f);

		// Appliquer le matériau normal au départ
		MeshInstance3D ballMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		if (ballMesh != null)
		{
			ballMesh.MaterialOverride = _normalMaterial;
		}
	}
	
	public override void _PhysicsProcess(double delta)
	{
		if (!_controlsEnabled)
			return;
		
		// Tentative de trouver l'ArduinoManager s'il n'est pas déjà référencé
		// et si nous sommes censés l'utiliser
		if (_useArduino && _arduinoManager == null)
		{
			_arduinoManager = GetTree().Root.FindChild("ArduinoManager", true, false) as ArduinoManager;
			if (_arduinoManager == null)
			{
				// Utiliser les contrôles clavier en attendant
				_useArduino = false;
			}
			else
			{
				GD.Print("ArduinoManager trouvé et connecté au PlayerBall");
				_useArduino = true;
			}
		}
			
		// Récupérer les entrées (clavier ou Arduino)
		ProcessInput();
		
		// Appliquer les forces basées sur les entrées
		if (!_isBeingTeleported)
		{
			// Convertir la direction d'entrée en fonction de la rotation de la caméra
			Vector3 cameraRelativeDirection = GetCameraRelativeDirection(_inputDirection);
			
			// Appliquer la force de mouvement
			ApplyCentralForce(cameraRelativeDirection * _moveForce);
			
			// Gestion du saut
			if (_jumpRequested && IsOnFloor())
			{
				ApplyCentralImpulse(Vector3.Up * _jumpForce);
				_jumpRequested = false;
			}
		}
	}
	
	private void ProcessInput()
	{
		if (_useArduino && _arduinoManager != null)
		{
			// Utiliser les données de l'Arduino si disponibles
			// Pour l'Arduino, nous inversons l'axe Y pour corriger l'inversion avant/arrière
			_inputDirection = new Vector3(
				_arduinoManager.GetAccelX(),
				0,
				-_arduinoManager.GetAccelY() // Inversion pour corriger la direction
			).Normalized();
			
			// Vérifier si un saut est détecté par l'Arduino
			if (_arduinoManager.IsJumpDetected())
			{
				_jumpRequested = true;
			}
		}
		else
		{
			// Entrées clavier par défaut
			_inputDirection = Vector3.Zero;
			
			if (Input.IsActionPressed("ui_right"))
				_inputDirection.X += 1;
			if (Input.IsActionPressed("ui_left"))
				_inputDirection.X -= 1;
			if (Input.IsActionPressed("ui_down"))
				_inputDirection.Z += 1;
			if (Input.IsActionPressed("ui_up"))
				_inputDirection.Z -= 1;
			
			// Normaliser pour éviter une vitesse plus rapide en diagonale
			if (_inputDirection.Length() > 0)
				_inputDirection = _inputDirection.Normalized();
			
			// Détecter une demande de saut
			if (Input.IsActionJustPressed("ui_accept"))
				_jumpRequested = true;
		}
	}
	
	private Vector3 GetCameraRelativeDirection(Vector3 inputDir)
	{
		if (_camera == null || inputDir.LengthSquared() < 0.01f)
			return inputDir;
		
		// Utiliser l'orientation du pivot de la caméra plutôt que celle de la caméra directement
		Basis cameraBasis = _cameraFollowPivot.GlobalTransform.Basis;
		Vector3 forward = -cameraBasis.Z;
		forward.Y = 0; // Ignorer l'axe vertical
		forward = forward.Normalized();
		
		Vector3 right = cameraBasis.X;
		right.Y = 0;
		right = right.Normalized();
		
		// Transformer la direction d'entrée par rapport à la caméra
		// Nous n'avons plus besoin d'inverser l'axe Z ici car nous l'avons déjà fait dans ProcessInput
		return (forward * inputDir.Z + right * inputDir.X);
	}
	
	private bool IsOnFloor()
	{
		// Vérifier si la balle touche le sol
		return GetContactCount() > 0;
	}
	
	// Appelé quand la téléportation commence
	public void StartTeleporting()
	{
		_isBeingTeleported = true;
		
		// Geler le corps physique pendant la téléportation
		Freeze = true;
		
		// Changer l'apparence pour montrer la téléportation
		MeshInstance3D ballMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		if (ballMesh != null)
		{
			ballMesh.MaterialOverride = _teleportMaterial;
		}
		
		// Ajouter un effet de particules pendant la téléportation
		CreateTeleportParticles();
	}
	
	// Appelé quand la téléportation est terminée
	public void FinishTeleporting()
	{
		// Réinitialiser la vélocité
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		
		// Dégeler le corps
		Freeze = false;
		
		// Revenir au matériau normal
		MeshInstance3D ballMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		if (ballMesh != null)
		{
			ballMesh.MaterialOverride = _normalMaterial;
		}
		
		// Réactiver le contrôle
		_isBeingTeleported = false;
	}
	
	// Créer un effet de particules pour la téléportation
	private void CreateTeleportParticles()
	{
		var particles = new GpuParticles3D();
		particles.Name = "TeleportParticles";
		
		// Configurer les particules
		var particlesMaterial = new ParticleProcessMaterial();
		particlesMaterial.Direction = new Vector3(0, 1, 0);
		particlesMaterial.Spread = 180.0f;
		particlesMaterial.InitialVelocityMin = 2.0f;
		particlesMaterial.InitialVelocityMax = 5.0f;
		particlesMaterial.AngularVelocityMin = -90.0f;
		particlesMaterial.AngularVelocityMax = 90.0f;
		particlesMaterial.Color = new Color(0.5f, 0.2f, 1.0f);
		
		particles.ProcessMaterial = particlesMaterial;
		
		// Configurer le mesh pour les particules
		var sphereMesh = new SphereMesh();
		sphereMesh.Radius = 0.1f;
		sphereMesh.Height = 0.2f;
		particles.DrawPass1 = sphereMesh;
		
		// Configurer les paramètres d'émission
		particles.Amount = 50;
		particles.Lifetime = 1.0f;
		particles.OneShot = true;
		particles.Explosiveness = 0.8f;
		
		AddChild(particles);
		particles.Emitting = true;
		
		// Supprimer les particules après la fin de l'émission
		var timer = new Timer();
		timer.WaitTime = 2.0f;
		timer.OneShot = true;
		timer.Timeout += () => {
			particles.QueueFree();
			timer.QueueFree();
		};
		AddChild(timer);
		timer.Start();
	}
	
	// Désactiver les contrôles (utile pour la fin du jeu)
	public void DisableControls()
	{
		_controlsEnabled = false;
	}
	
	// Réactiver les contrôles
	public void EnableControls()
	{
		_controlsEnabled = true;
	}
}
