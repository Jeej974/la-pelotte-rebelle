using Godot;
using System;

public partial class PlayerBall : RigidBody3D
{
	[Export]
	private float _moveForce = 100.0f; // AUGMENTÉ À 150 pour des mouvements beaucoup plus rapides
	
	[Export]
	private float _jumpForce = 20.0f;
	
	[Export]
	private bool _useArduino = true;
	
	// AJOUT: Variables pour conserver le mouvement même quand les valeurs sont nulles
	private Vector3 _lastSignificantDirection = Vector3.Zero;
	private float _directionPersistence = 0.98f; // Facteur de persistance (diminution très lente)
	private const float DIRECTION_THRESHOLD = 0.05f; // Seuil pour détecter un mouvement significatif
	private const float ARDUINO_AMPLIFICATION = 4.0f; // FORTEMENT AUGMENTÉ pour des mouvements plus marqués


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
	private bool _teleportationCooldown = false;
	private float _teleportCooldownTime = 1.0f; // 1 seconde de cooldown
	private float _teleportCooldownTimer = 0.0f;
	
	// Matériaux pour différents états
	private StandardMaterial3D _normalMaterial;
	private StandardMaterial3D _teleportMaterial;
	
	// Contrôles activés/désactivés
	private bool _controlsEnabled = true;
	
	// Nouveau: Chemin vers le modèle de laine
	[Export]
	private string _woolBallModelPath = "res://assets/ball of wool/yarn_ball.glb";
	
	// Noeud du modèle de laine
	private Node3D _woolBallModel;
	
	public override void _Ready()
	{
		// Configurer la physique
		Mass = 3.0f;
		CanSleep = false;
		CustomIntegrator = false;
		
		// Configurer les collisions
		CollisionLayer = 1;
		CollisionMask = 1;
		
		// Nouveau: Charger le modèle de balle de laine
		LoadWoolBallModel();
		
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
	
	// Nouvelle méthode pour charger le modèle de balle de laine
	private void LoadWoolBallModel()
	{
		// Supprimer l'ancien MeshInstance3D s'il existe
		var oldMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		if (oldMesh != null)
		{
			oldMesh.QueueFree();
		}
		
		// Charger le modèle 3D depuis le fichier .glb
		var packedScene = GD.Load<PackedScene>(_woolBallModelPath);
		if (packedScene != null)
		{
			// Instancier la scène
			_woolBallModel = packedScene.Instantiate<Node3D>();
			
			// Ajuster l'échelle si nécessaire
			_woolBallModel.Scale = new Vector3(0.5f, 0.5f, 0.5f);
			
			// Ajouter le modèle comme enfant
			AddChild(_woolBallModel);
			
			// Renommer pour faciliter les références
			_woolBallModel.Name = "WoolBallModel";
			
			GD.Print("Modèle de balle de laine chargé avec succès");
		}
		else
		{
			GD.PrintErr($"Erreur lors du chargement du modèle: {_woolBallModelPath}");
			
			// En cas d'échec, créer un MeshInstance3D avec un SphereMesh comme fallback
			var meshInstance = new MeshInstance3D();
			meshInstance.Name = "MeshInstance3D";
			meshInstance.Mesh = new SphereMesh();
			meshInstance.Scale = new Vector3(0.5f, 0.5f, 0.5f);
			AddChild(meshInstance);
			_woolBallModel = meshInstance;
		}
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
		
		// Faire tourner la balle de laine quand elle se déplace
		RotateWoolBall(delta);
		
		// Gestion du cooldown de téléportation
		if (_teleportationCooldown)
		{
			_teleportCooldownTimer += (float)delta;
			if (_teleportCooldownTimer >= _teleportCooldownTime)
			{
				_teleportationCooldown = false;
				_teleportCooldownTimer = 0.0f;
			}
		}
	}
	
	// Nouvelle méthode pour faire tourner la balle de laine en fonction du mouvement
	private void RotateWoolBall(double delta)
	{
		if (_woolBallModel != null && LinearVelocity.Length() > 0.1f)
		{
			// Calculer la rotation en fonction de la vitesse et de la direction
			Vector3 rotationAxis = Vector3.Up.Cross(LinearVelocity.Normalized());
			
			// Vérifier que l'axe de rotation n'est pas un vecteur nul (évite l'erreur de normalisation)
			if (rotationAxis.LengthSquared() > 0.001f)
			{
				float rotationSpeed = LinearVelocity.Length() * 0.5f;
				
				// Normaliser l'axe de rotation et appliquer la rotation
				rotationAxis = rotationAxis.Normalized();
				_woolBallModel.Rotate(rotationAxis, rotationSpeed * (float)delta);
			}
			else
			{
				// Si l'axe est presque nul (mouvement vertical), faire une rotation simple autour de l'axe Z
				_woolBallModel.Rotate(Vector3.Forward, LinearVelocity.Length() * 0.2f * (float)delta);
			}
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

		// Appliquer le matériau normal au départ - Note: nous n'appliquons plus directement le matériau
		// car nous utilisons maintenant un modèle GLB qui a son propre matériau
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
		if (_isBeingTeleported) return;
		
		_isBeingTeleported = true;
		
		// Geler le corps physique pendant la téléportation
		Freeze = true;
		
		// Réinitialiser les vélocités pour éviter tout mouvement pendant la téléportation
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		
		// Changer l'apparence pour montrer la téléportation
		var modelNode = GetNodeOrNull<Node3D>("WoolBallModel");
		if (modelNode != null)
		{
			// Appliquer un effet visuel pour la téléportation (par exemple, faire scintiller)
			var tween = CreateTween();
			tween.SetEase(Tween.EaseType.InOut);
			tween.TweenProperty(modelNode, "scale", new Vector3(0.6f, 0.6f, 0.6f), 0.3f);
			tween.TweenProperty(modelNode, "scale", new Vector3(0.4f, 0.4f, 0.4f), 0.3f);
			tween.SetLoops(3);
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
		
		// Revenir à l'apparence normale
		var modelNode = GetNodeOrNull<Node3D>("WoolBallModel");
		if (modelNode != null)
		{
			modelNode.Scale = new Vector3(0.5f, 0.5f, 0.5f);
		}
		
		// Réactiver explicitement les contrôles
		_controlsEnabled = true;
		
		// Réactiver le contrôle après un court délai
		_isBeingTeleported = false;
		_teleportationCooldown = false; // Désactiver le cooldown pour pouvoir contrôler immédiatement
		_teleportCooldownTimer = 0.0f;
		
		// S'assurer qu'Arduino est correctement configuré
		if (_useArduino && _arduinoManager == null)
		{
			// Essayer de retrouver l'ArduinoManager
			_arduinoManager = GetTree().Root.FindChild("ArduinoManager", true, false) as ArduinoManager;
			if (_arduinoManager != null)
			{
				GD.Print("ArduinoManager retrouvé après téléportation");
			}
		}
		
		GD.Print("Téléportation terminée, contrôles réactivés");
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

	private void ProcessInput()
	{
		// Toujours essayer d'utiliser le singleton ArduinoManager
		_arduinoManager = ArduinoManager.Instance;
		
		if (_useArduino && _arduinoManager != null)
		{
			try {
				// Utiliser les données de l'Arduino
				float accelX = _arduinoManager.GetAccelX() * 3.0f;  // Amplification simple
				float accelY = _arduinoManager.GetAccelY() * 3.0f;  // Amplification simple
				
				// Appliquer un seuil minimum pour éviter les micro-mouvements
				if (Math.Abs(accelX) < 0.08f) accelX = 0;
				if (Math.Abs(accelY) < 0.08f) accelY = 0;
				
				// Utiliser les données du gyroscope
				_inputDirection = new Vector3(
					accelX,
					0,
					-accelY  // Inversion pour corriger la direction
				);
				
				// Log diagnostic pour voir les valeurs
				if (Engine.GetProcessFrames() % 60 == 0)
				{
					GD.Print($"Valeurs Arduino: X={accelX:F2}, Y={accelY:F2}");
				}
				
				// Vérifier si un saut est détecté par l'Arduino
				if (_arduinoManager.IsJumpDetected())
				{
					_jumpRequested = true;
					GD.Print("Saut via Arduino détecté");
				}
			}
			catch {
				// Si une erreur se produit, utiliser les contrôles clavier
				// Suppression de la variable 'e' non utilisée
				ProcessKeyboardInput();
			}
		}
		else
		{
			// Utiliser les contrôles clavier par défaut
			ProcessKeyboardInput();
		}
	}
	// Override de _PhysicsProcess pour rendre le mouvement plus dynamique
	public override void _PhysicsProcess(double delta)
	{
		// Si les contrôles sont désactivés, sortir
		if (!_controlsEnabled || _isBeingTeleported || _teleportationCooldown)
		{
			return;
		}
		
		// Récupérer les entrées (incluant la persistance)
		ProcessInput();
		
		// Convertir la direction en fonction de la caméra
		Vector3 cameraRelativeDirection = GetCameraRelativeDirection(_inputDirection);
		
		// NOUVEAU: Boost d'accélération dynamique
		float accelerationBoost = 1.0f;
		
		// Si on a une direction significative
		if (cameraRelativeDirection.LengthSquared() > 0.01f)
		{
			// Boost d'accélération pour démarrer plus rapidement
			float speed = LinearVelocity.Length();
			if (speed < 5.0f)
			{
				accelerationBoost = 2.0f; // Boost de démarrage
			}
			
			// NOUVEAU: Force proportionnelle à la magnitude de la direction
			float magnitude = cameraRelativeDirection.Length();
			float forceMagnitude = _moveForce * magnitude * accelerationBoost;
			
			// Appliquer la force de mouvement
			ApplyCentralForce(cameraRelativeDirection.Normalized() * forceMagnitude);
		}
		else if (LinearVelocity.LengthSquared() > 0.1f)
		{
			// Freinage progressif (beaucoup plus lent)
			Vector3 brakeForce = -LinearVelocity.Normalized() * _moveForce * 0.2f;
			ApplyCentralForce(brakeForce);
		}
		
		// Gestion du saut
		if (_jumpRequested && IsOnFloor())
		{
			ApplyCentralImpulse(Vector3.Up * _jumpForce);
			_jumpRequested = false;
		}
	}
	
	// Pour réinitialiser complètement l'état y compris le mouvement persistant
	public void ResetState()
	{
		// Réinitialiser les états
		_lastSignificantDirection = Vector3.Zero;
		_inputDirection = Vector3.Zero;
		_jumpRequested = false;
		
		// Réinitialiser la physique
		Freeze = false;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		
		// Réinitialiser les états de téléportation
		_isBeingTeleported = false;
		_teleportationCooldown = false;
		
		// S'assurer que les contrôles sont activés
		_controlsEnabled = true;
		
		GD.Print("État du PlayerBall complètement réinitialisé");
	}
	
	
	// Méthode SetArduinoManager modifiée pour garantir la connexion
	public void SetArduinoManager(ArduinoManager manager)
	{
		if (manager == null)
		{
			GD.PrintErr("ERREUR: Tentative de définir un ArduinoManager null!");
			// Essayer de retrouver le singleton
			manager = ArduinoManager.Instance;
			if (manager == null)
			{
				GD.PrintErr("ERREUR GRAVE: Impossible de trouver ArduinoManager.Instance!");
				
				// Dernier recours - créer une nouvelle instance
				manager = new ArduinoManager();
				manager.Name = "ArduinoManagerEmergency";
				GetTree().Root.AddChild(manager);
				GD.Print("ArduinoManager de secours créé!");
			}
		}
		
		// Stocker la référence explicite
		_arduinoManager = manager;
		_useArduino = true;
		
		// Diagnostiquer l'état
		GD.Print($"PlayerBall configuré pour utiliser ArduinoManager (référence directe: {_arduinoManager != null}, singleton: {ArduinoManager.Instance != null})");
		
		// NOUVEAU: Diagnostic pour vérifier la communication
		try {
			float testX = _arduinoManager.GetAccelX();
			float testY = _arduinoManager.GetAccelY();
			GD.Print($"Test de communication réussi: X={testX:F2}, Y={testY:F2}");
		} catch (Exception e) {
			GD.PrintErr($"ALERTE: Communication Arduino impossible: {e.Message}");
		}
	}
	// NOUVELLE MÉTHODE: Séparation de la gestion des entrées clavier
	private void ProcessKeyboardInput()
	{
		_inputDirection = Vector3.Zero;
		
		if (Input.IsActionPressed("ui_right"))
		{
			_inputDirection.X += 1;
		}
		if (Input.IsActionPressed("ui_left"))
		{
			_inputDirection.X -= 1;
		}
		if (Input.IsActionPressed("ui_down"))
		{
			_inputDirection.Z += 1;
		}
		if (Input.IsActionPressed("ui_up"))
		{
			_inputDirection.Z -= 1;
		}
		
		// Normaliser pour éviter une vitesse plus rapide en diagonale
		if (_inputDirection.LengthSquared() > 0.01f)
		{
			_inputDirection = _inputDirection.Normalized();
		}
		
		// Détecter une demande de saut
		if (Input.IsActionJustPressed("ui_accept"))
		{
			_jumpRequested = true;
		}
	}

	// Ajouter cette méthode pour forcer la reconnexion avec l'Arduino
	public void ForceReconnectArduino()
	{
		// Priorité à l'instance singleton
		_arduinoManager = ArduinoManager.Instance;
		
		// Si toujours null, essayer de le trouver dans l'arbre
		if (_arduinoManager == null)
		{
			_arduinoManager = GetTree().Root.FindChild("ArduinoManager", true, false) as ArduinoManager;
		}
		
		// Dernier recours: vérification dans les enfants directs de Root
		if (_arduinoManager == null)
		{
			foreach (Node child in GetTree().Root.GetChildren())
			{
				if (child is ArduinoManager arduino)
				{
					_arduinoManager = arduino;
					break;
				}
			}
		}
		
		if (_arduinoManager != null)
		{
			_useArduino = true;
			GD.Print("Force reconnexion à ArduinoManager réussie");
			
			// Réinitialiser les états de mouvement
			_inputDirection = Vector3.Zero;
			_jumpRequested = false;
		}
		else
		{
			_useArduino = false;
			GD.PrintErr("Force reconnexion à ArduinoManager échouée - contrôles clavier activés");
		}
		
		// Réinitialiser les états importants
		_controlsEnabled = true;
		_isBeingTeleported = false;
		_teleportationCooldown = false;
		
		// Réinitialiser la physique
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
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
