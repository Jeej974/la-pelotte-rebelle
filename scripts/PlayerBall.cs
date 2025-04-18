using Godot;
using System;

/**
 * PlayerBall - Classe du joueur principal
 * 
 * Gère le comportement de la balle de laine contrôlée par le joueur,
 * incluant le mouvement physique, l'entrée Arduino (gyroscope),
 * les animations et les effets visuels/sonores.
 */
public partial class PlayerBall : RigidBody3D
{
	[Export]
	private float _moveForce = 30.0f;
	
	[Export]
	private float _jumpForce = 20.0f;
	
	[Export]
	private bool _useArduino = true;
	
	// Paramètres de persistance du mouvement
	private Vector3 _lastSignificantDirection = Vector3.Zero;
	private float _directionPersistence = 0.92f;
	private const float DIRECTION_THRESHOLD = 0.02f;
	
	// Paramètres d'amplification du mouvement
	private const float MIN_AMPLIFICATION = 1.2f;
	private const float MAX_AMPLIFICATION = 3f;
	private float _currentAmplification = MIN_AMPLIFICATION;
	private const float AMPLIFICATION_RATE = 0.6f;
	private const float DECELERATION_RATE = 0.25f;
	
	// Limite de vitesse
	private const float MAX_VELOCITY = 18.0f;
	
	// Lissage du mouvement via historique des accélérations
	private const int HISTORY_SIZE = 5;
	private float[] _accelXHistory = new float[HISTORY_SIZE];
	private float[] _accelYHistory = new float[HISTORY_SIZE];
	private int _historyIndex = 0;
	private bool _historyFilled = false;

	// Gestion du son de roulement
	private bool _isRolling = false;
	private float _rollingSoundThreshold = 0.8f;
	private float _lastSoundUpdateTime = 0f;
	private const float SOUND_UPDATE_INTERVAL = 0.2f;

	// Référence à l'ArduinoManager
	private ArduinoManager _arduinoManager;
	
	// Variables pour le contrôle clavier
	private Vector3 _inputDirection = Vector3.Zero;
	private Vector3 _smoothedInputDirection = Vector3.Zero;
	private bool _jumpRequested = false;
	
	// Composants de caméra
	private Camera3D _camera;
	private Node3D _cameraFollowPivot;
	
	// Configuration de la caméra 
	[Export]
	private Vector3 _cameraOffset = new Vector3(0, 10, 5);
	[Export]
	private Vector3 _cameraRotation = new Vector3(-60, 0, 0);
	
	// États de téléportation
	private bool _isBeingTeleported = false;
	private bool _teleportationCooldown = false;
	private float _teleportCooldownTime = 1.0f;
	private float _teleportCooldownTimer = 0.0f;
	
	// Matériaux pour différents états visuels
	private StandardMaterial3D _normalMaterial;
	private StandardMaterial3D _teleportMaterial;
	
	// État d'activation des contrôles
	private bool _controlsEnabled = true;
	
	// Modèle 3D de la balle
	[Export]
	private string _woolBallModelPath = "res://assets/ball of wool/yarn_ball.glb";
	private Node3D _woolBallModel;
	
	// Variables pour la gestion de l'inclinaison
	private float _tiltDuration = 0.0f;
	private bool _wasTilted = false;

	// Variables de débogage
	private float _lastReportedSpeed = 0;
	private float _speedReportTimer = 0;
	
	/**
	 * Initialisation de la balle du joueur
	 */
	public override void _Ready()
	{
		// Configuration physique
		Mass = 3.0f;
		CanSleep = false;
		CustomIntegrator = false;
		
		// Configuration des collisions
		CollisionLayer = 1;
		CollisionMask = 1;
		
		// Chargement du modèle 3D
		LoadWoolBallModel();
		
		// Configuration de la caméra
		_cameraFollowPivot = GetNode<Node3D>("PlayerFollowPivot");
		
		_camera = GetNodeOrNull<Camera3D>("Camera3D");
		if (_camera != null)
		{
			// Déplacer la caméra au pivot pour un suivi fluide
			RemoveChild(_camera);
			_cameraFollowPivot.AddChild(_camera);
			
			_camera.Position = _cameraOffset;
			_camera.RotationDegrees = _cameraRotation;
			_camera.Current = true;
			
			GD.Print("Caméra déplacée vers le pivot et configurée");
		}
		else
		{
			GD.PrintErr("Caméra non trouvée sur le joueur!");
		}
		
		// Activer le corps rigide
		Sleeping = false;
		
		// Créer les matériaux pour les différents états
		CreateMaterials();
		
		// Initialiser l'historique des accélérations
		for (int i = 0; i < HISTORY_SIZE; i++)
		{
			_accelXHistory[i] = 0;
			_accelYHistory[i] = 0;
		}
	}
	
	/**
	 * Charge le modèle 3D de la balle de laine
	 */
	private void LoadWoolBallModel()
	{
		// Supprimer l'ancien mesh s'il existe
		var oldMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		if (oldMesh != null)
		{
			oldMesh.QueueFree();
		}
		
		// Charger le modèle 3D
		var packedScene = GD.Load<PackedScene>(_woolBallModelPath);
		if (packedScene != null)
		{
			_woolBallModel = packedScene.Instantiate<Node3D>();
			_woolBallModel.Scale = new Vector3(0.5f, 0.5f, 0.5f);
			_woolBallModel.Name = "WoolBallModel";
			AddChild(_woolBallModel);
			
			GD.Print("Modèle de balle de laine chargé avec succès");
		}
		else
		{
			GD.PrintErr($"Erreur lors du chargement du modèle: {_woolBallModelPath}");
			
			// Créer un mesh de fallback
			var meshInstance = new MeshInstance3D();
			meshInstance.Name = "MeshInstance3D";
			meshInstance.Mesh = new SphereMesh();
			meshInstance.Scale = new Vector3(0.5f, 0.5f, 0.5f);
			AddChild(meshInstance);
			_woolBallModel = meshInstance;
		}
	}
	
	/**
	 * Mise à jour visuelle et son par frame
	 */
	public override void _Process(double delta)
	{
		// Mise à jour des timers
		_lastSoundUpdateTime += (float)delta;
		_speedReportTimer += (float)delta;
		
		// Mise à jour du pivot de la caméra pour suivi fluide
		if (_cameraFollowPivot != null)
		{
			_cameraFollowPivot.GlobalPosition = GlobalPosition;
			_cameraFollowPivot.GlobalRotation = Vector3.Zero; // Maintient une orientation fixe
		}
		
		// Animation de rotation de la balle selon le mouvement
		RotateWoolBall(delta);
		
		// Gestion du son de roulement
		if (_lastSoundUpdateTime >= SOUND_UPDATE_INTERVAL)
		{
			UpdateRollingSound();
			_lastSoundUpdateTime = 0f;
		}

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
		
		// Débogage de la vitesse (périodique)
		if (_speedReportTimer >= 1.0f)
		{
			float currentSpeed = LinearVelocity.Length();
			if (Math.Abs(currentSpeed - _lastReportedSpeed) > 0.5f)
			{
				GD.Print($"Vitesse actuelle: {currentSpeed:F2}, Direction: {_inputDirection.Length():F2}, Amplification: {_currentAmplification:F1}");
				_lastReportedSpeed = currentSpeed;
			}
			_speedReportTimer = 0;
		}
	}
	
	/**
	 * Gère le son de roulement de la balle selon sa vitesse
	 */
	private void UpdateRollingSound()
	{
		// Désactiver le son si contrôles inactifs ou en téléportation
		if (!_controlsEnabled || _isBeingTeleported) 
		{
			if (_isRolling)
			{
				StopRollingSound();
			}
			return;
		}
		
		// Vérification de la vitesse pour l'émission du son
		float speed = LinearVelocity.Length();
		
		if (speed > _rollingSoundThreshold && !_isRolling)
		{
			StartRollingSound(speed);
		}
		else if (speed <= _rollingSoundThreshold * 0.5f && _isRolling)
		{
			StopRollingSound();
		}
		else if (_isRolling && speed > _rollingSoundThreshold)
		{
			UpdateRollingSoundParameters(speed);
		}
	}
	
	/**
	 * Démarre le son de roulement de la balle
	 */
	private void StartRollingSound(float speed)
	{
		if (AudioManager.Instance != null) {
			AudioManager.Instance.PlayLoopingSound3D("RollingBall", this);
			
			// Ajustement des paramètres selon la vitesse
			float speedFactor = Mathf.Clamp(speed / 10f, 0.5f, 2.0f);
			AudioManager.Instance.SetGlobalParameter("Speed", speedFactor);
			
			_isRolling = true;
			GD.Print($"Son de roulement démarré - Vitesse: {speed:F2}");
		}
	}
	
	/**
	 * Met à jour les paramètres du son de roulement
	 */
	private void UpdateRollingSoundParameters(float speed)
	{
		if (AudioManager.Instance != null) {
			float speedFactor = Mathf.Clamp(speed / 10f, 0.5f, 2.0f);
			AudioManager.Instance.SetGlobalParameter("Speed", speedFactor);
		}
	}
	
	/**
	 * Arrête le son de roulement
	 */
	private void StopRollingSound()
	{
		if (AudioManager.Instance != null && _isRolling) {
			AudioManager.Instance.StopLoopingSound("RollingBall");
			_isRolling = false;
			GD.Print("Son de roulement arrêté");
		}
	}
	
	/**
	 * Anime la rotation de la balle de laine selon son mouvement
	 */
	private void RotateWoolBall(double delta)
	{
		if (_woolBallModel != null && LinearVelocity.Length() > 0.1f)
		{
			// Calcul de l'axe de rotation perpendiculaire au mouvement
			Vector3 rotationAxis = Vector3.Up.Cross(LinearVelocity.Normalized());
			
			if (rotationAxis.LengthSquared() > 0.001f)
			{
				float rotationSpeed = LinearVelocity.Length() * 0.5f;
				
				rotationAxis = rotationAxis.Normalized();
				_woolBallModel.Rotate(rotationAxis, rotationSpeed * (float)delta);
			}
			else
			{
				// Rotation autour de l'axe Z si mouvement vertical
				_woolBallModel.Rotate(Vector3.Forward, LinearVelocity.Length() * 0.2f * (float)delta);
			}
		}
	}
	
	/**
	 * Crée les matériaux pour les différents états visuels
	 */
	private void CreateMaterials()
	{
		// Matériau état normal
		_normalMaterial = new StandardMaterial3D();
		_normalMaterial.AlbedoColor = new Color(0.2f, 0.6f, 1.0f);
		_normalMaterial.Metallic = 0.7f;
		_normalMaterial.Roughness = 0.2f;
		
		// Matériau téléportation
		_teleportMaterial = new StandardMaterial3D();
		_teleportMaterial.AlbedoColor = new Color(1.0f, 0.5f, 1.0f);
		_teleportMaterial.EmissionEnabled = true;
		_teleportMaterial.Emission = new Color(0.5f, 0.2f, 0.5f);
	}
	
	/**
	 * Convertit une direction d'entrée en direction relative à la caméra
	 */
	private Vector3 GetCameraRelativeDirection(Vector3 inputDir)
	{
		if (_camera == null || inputDir.LengthSquared() < 0.01f)
			return inputDir;
		
		// Utiliser l'orientation du pivot de caméra
		Basis cameraBasis = _cameraFollowPivot.GlobalTransform.Basis;
		Vector3 forward = -cameraBasis.Z;
		forward.Y = 0; // Ignorer la composante verticale
		forward = forward.Normalized();
		
		Vector3 right = cameraBasis.X;
		right.Y = 0;
		right = right.Normalized();
		
		// Transformer la direction selon l'orientation de la caméra
		return (forward * inputDir.Z + right * inputDir.X);
	}
	
	/**
	 * Vérifie si la balle est au sol
	 */
	private bool IsOnFloor()
	{
		return GetContactCount() > 0;
	}
	
	/**
	 * Démarrage de la téléportation
	 */
	public void StartTeleporting()
	{
		if (_isBeingTeleported) return;
		
		_isBeingTeleported = true;
		
		if (_isRolling)
		{
			StopRollingSound();
		}

		// Gèle le mouvement pendant la téléportation
		Freeze = true;
		
		// Réinitialisation des vélocités
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		
		// Animation visuelle de la téléportation
		var modelNode = GetNodeOrNull<Node3D>("WoolBallModel");
		if (modelNode != null)
		{
			var tween = CreateTween();
			tween.SetEase(Tween.EaseType.InOut);
			tween.TweenProperty(modelNode, "scale", new Vector3(0.6f, 0.6f, 0.6f), 0.3f);
			tween.TweenProperty(modelNode, "scale", new Vector3(0.4f, 0.4f, 0.4f), 0.3f);
			tween.SetLoops(3);
		}
		
		// Effet de particules
		CreateTeleportParticles();
	}
	
	/**
	 * Termine la téléportation
	 */
	public void FinishTeleporting()
	{
		// Réinitialisation physique
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		
		// Dégel du corps
		Freeze = false;
		
		// Restauration de l'apparence normale
		var modelNode = GetNodeOrNull<Node3D>("WoolBallModel");
		if (modelNode != null)
		{
			modelNode.Scale = new Vector3(0.5f, 0.5f, 0.5f);
		}
		
		// Réactivation des contrôles
		_controlsEnabled = true;
		
		// Réinitialisation des états de téléportation
		_isBeingTeleported = false;
		_teleportationCooldown = false;
		_teleportCooldownTimer = 0.0f;
		
		// Vérification de la connexion Arduino
		if (_useArduino && _arduinoManager == null)
		{
			_arduinoManager = GetTree().Root.FindChild("ArduinoManager", true, false) as ArduinoManager;
			if (_arduinoManager != null)
			{
				GD.Print("ArduinoManager retrouvé après téléportation");
			}
		}
		
		GD.Print("Téléportation terminée, contrôles réactivés");
	}
	
	/**
	 * Crée un effet de particules pour la téléportation
	 */
	private void CreateTeleportParticles()
	{
		var particles = new GpuParticles3D();
		particles.Name = "TeleportParticles";
		
		// Configuration du matériau des particules
		var particlesMaterial = new ParticleProcessMaterial();
		particlesMaterial.Direction = new Vector3(0, 1, 0);
		particlesMaterial.Spread = 180.0f;
		particlesMaterial.InitialVelocityMin = 2.0f;
		particlesMaterial.InitialVelocityMax = 5.0f;
		particlesMaterial.AngularVelocityMin = -90.0f;
		particlesMaterial.AngularVelocityMax = 90.0f;
		particlesMaterial.Color = new Color(0.5f, 0.2f, 1.0f);
		
		particles.ProcessMaterial = particlesMaterial;
		
		// Mesh pour les particules
		var sphereMesh = new SphereMesh();
		sphereMesh.Radius = 0.1f;
		sphereMesh.Height = 0.2f;
		particles.DrawPass1 = sphereMesh;
		
		// Paramètres d'émission
		particles.Amount = 50;
		particles.Lifetime = 1.0f;
		particles.OneShot = true;
		particles.Explosiveness = 0.8f;
		
		AddChild(particles);
		particles.Emitting = true;
		
		// Nettoyage automatique des particules
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

	/**
	 * Traitement des entrées utilisateur (Arduino ou clavier)
	 */
	private void ProcessInput()
	{
		// Utiliser l'instance singleton d'ArduinoManager
		_arduinoManager = ArduinoManager.Instance;
		
		if (_useArduino && _arduinoManager != null)
		{
			try {
				// Lecture des données du gyroscope Arduino
				float accelX = _arduinoManager.GetAccelX();
				float accelY = _arduinoManager.GetAccelY();
				
				// Mise à jour de l'historique pour lissage
				_accelXHistory[_historyIndex] = accelX;
				_accelYHistory[_historyIndex] = accelY;
				_historyIndex = (_historyIndex + 1) % HISTORY_SIZE;
				
				if (_historyIndex == 0) {
					_historyFilled = true;
				}
				
				// Lissage des valeurs d'accélération
				float smoothedX = 0;
				float smoothedY = 0;
				int samplesCount = _historyFilled ? HISTORY_SIZE : _historyIndex;
				
				if (samplesCount > 0) {
					for (int i = 0; i < samplesCount; i++) {
						smoothedX += _accelXHistory[i];
						smoothedY += _accelYHistory[i];
					}
					smoothedX /= samplesCount;
					smoothedY /= samplesCount;
				}
				
				// Amplification initiale pour meilleure sensibilité
				smoothedX *= 1.2f;
				smoothedY *= 1.2f;
				
				// Détection d'inclinaison significative
				bool isTilted = (Mathf.Abs(smoothedX) > DIRECTION_THRESHOLD || Mathf.Abs(smoothedY) > DIRECTION_THRESHOLD);
				
				// Gestion de l'amplification progressive du mouvement
				if (isTilted) {
					if (!_wasTilted) {
						// Démarrage d'une nouvelle inclinaison
						_wasTilted = true;
						_tiltDuration = 0.0f;
					} else {
						// Inclinaison maintenue, augmentation progressive de l'amplification
						_tiltDuration += 0.016f;
						_currentAmplification = Mathf.Min(MAX_AMPLIFICATION, 
														 MIN_AMPLIFICATION + (_tiltDuration * AMPLIFICATION_RATE));
					}
					
					// Calcul de la direction 
					Vector3 currentDirection = new Vector3(smoothedX, 0, -smoothedY);
					
					if (currentDirection.LengthSquared() > 0.0001f) {
						_lastSignificantDirection = currentDirection.Normalized() * currentDirection.Length() * _currentAmplification;
					}
					
					_inputDirection = _lastSignificantDirection;
				} else {
					// Fin d'inclinaison, persistance de direction
					if (_wasTilted) {
						_wasTilted = false;
						_inputDirection = _lastSignificantDirection * _directionPersistence;
					} else {
						// Décélération progressive
						_inputDirection = _inputDirection * (_directionPersistence - DECELERATION_RATE);
						
						// Réinitialisation si la direction devient négligeable
						if (_inputDirection.LengthSquared() < DIRECTION_THRESHOLD * DIRECTION_THRESHOLD) {
							_inputDirection = Vector3.Zero;
							_lastSignificantDirection = Vector3.Zero;
							_currentAmplification = MIN_AMPLIFICATION;
						}
					}
				}
				
				// Lissage de la transition entre directions
				_smoothedInputDirection = _smoothedInputDirection.Lerp(_inputDirection, 0.2f);
				_inputDirection = _smoothedInputDirection;
				
				// Diagnostic périodique
				if (Engine.GetProcessFrames() % 60 == 0) {
					GD.Print($"Gyro: X={smoothedX:F2}, Y={smoothedY:F2}, Amplification={_currentAmplification:F1}, Tilt={_tiltDuration:F1}s");
				}
				
				// Détection de saut depuis Arduino
				if (_arduinoManager.IsJumpDetected()) {
					_jumpRequested = true;
					GD.Print("Saut via Arduino détecté");
				}
			}
			catch {
				// En cas d'erreur, fallback sur les contrôles clavier
				ProcessKeyboardInput();
			}
		}
		else {
			// Utilisation des contrôles clavier
			ProcessKeyboardInput();
		}
	}
	
	/**
	 * Traitement des entrées clavier comme fallback
	 */
	private void ProcessKeyboardInput() {
		Vector3 rawDirection = Vector3.Zero;
		
		// Lecture des touches directionnelles
		if (Input.IsActionPressed("ui_right")) {
			rawDirection.X += 1;
		}
		if (Input.IsActionPressed("ui_left")) {
			rawDirection.X -= 1;
		}
		if (Input.IsActionPressed("ui_down")) {
			rawDirection.Z += 1;
		}
		if (Input.IsActionPressed("ui_up")) {
			rawDirection.Z -= 1;
		}
		
		// Normalisation et traitement similaire à l'accéléromètre
		if (rawDirection.LengthSquared() > 0.01f) {
			rawDirection = rawDirection.Normalized();
			
			// Simulation d'amplification progressive
			if (!_wasTilted) {
				_wasTilted = true;
				_tiltDuration = 0.0f;
			} else {
				_tiltDuration += 0.016f;
				_currentAmplification = Mathf.Min(MAX_AMPLIFICATION, 
												MIN_AMPLIFICATION + (_tiltDuration * AMPLIFICATION_RATE));
			}
			
			_lastSignificantDirection = rawDirection * _currentAmplification;
			_inputDirection = _lastSignificantDirection;
		} else {
			// Gestion de la persistance de direction
			if (_wasTilted) {
				_wasTilted = false;
				_inputDirection = _lastSignificantDirection * _directionPersistence;
			} else {
				_inputDirection = _inputDirection * (_directionPersistence - DECELERATION_RATE);
				
				if (_inputDirection.LengthSquared() < DIRECTION_THRESHOLD * DIRECTION_THRESHOLD) {
					_inputDirection = Vector3.Zero;
					_lastSignificantDirection = Vector3.Zero;
					_currentAmplification = MIN_AMPLIFICATION;
				}
			}
		}
		
		// Lissage de la transition entre directions
		_smoothedInputDirection = _smoothedInputDirection.Lerp(_inputDirection, 0.2f);
		_inputDirection = _smoothedInputDirection;
		
		// Détection de saut via touche espace
		if (Input.IsActionJustPressed("ui_accept")) {
			_jumpRequested = true;
		}
	}
	
	/**
	 * Mise à jour physique du mouvement de la balle
	 */
	public override void _PhysicsProcess(double delta) {
		// Sortir si les contrôles sont désactivés
		if (!_controlsEnabled || _isBeingTeleported || _teleportationCooldown) {
			return;
		}
		
		// Traitement des entrées
		ProcessInput();
		
		// Conversion de la direction selon l'orientation de la caméra
		Vector3 cameraRelativeDirection = GetCameraRelativeDirection(_inputDirection);
		
		// Application des forces de mouvement
		if (cameraRelativeDirection.LengthSquared() > 0.01f) {
			// Force proportionnelle à l'intensité de la direction
			Vector3 forceVector = cameraRelativeDirection.Normalized() * _moveForce * cameraRelativeDirection.Length();
			
			ApplyCentralForce(forceVector);
			
			// Limitation de la vitesse maximum
			if (LinearVelocity.Length() > MAX_VELOCITY) {
				LinearVelocity = LinearVelocity.Normalized() * MAX_VELOCITY;
			}
		} else if (LinearVelocity.LengthSquared() > 0.1f) {
			// Force de freinage naturel
			Vector3 brakeForce = -LinearVelocity.Normalized() * _moveForce * 0.1f;
			ApplyCentralForce(brakeForce);
		}
		
		// Gestion du saut
		if (_jumpRequested && IsOnFloor()) {
			ApplyCentralImpulse(Vector3.Up * _jumpForce);
			_jumpRequested = false;
		}
	}
	
	/**
	 * Réinitialise complètement l'état de la balle
	 */
	public void ResetState() {
		// Réinitialisation du mouvement
		_lastSignificantDirection = Vector3.Zero;
		_inputDirection = Vector3.Zero;
		_smoothedInputDirection = Vector3.Zero;
		_jumpRequested = false;
		_tiltDuration = 0.0f;
		_wasTilted = false;
		_currentAmplification = MIN_AMPLIFICATION;
		
		// Réinitialisation de la physique
		Freeze = false;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		
		// Réinitialisation des états de téléportation
		_isBeingTeleported = false;
		_teleportationCooldown = false;
		if (_isRolling) {
			StopRollingSound();
		}

		// Activation des contrôles
		_controlsEnabled = true;
		
		// Réinitialisation de l'historique des accélérations
		for (int i = 0; i < HISTORY_SIZE; i++) {
			_accelXHistory[i] = 0;
			_accelYHistory[i] = 0;
		}
		_historyIndex = 0;
		_historyFilled = false;
		
		GD.Print("État du PlayerBall complètement réinitialisé");
	}
	
	/**
	 * Définit l'instance d'ArduinoManager à utiliser
	 */
	public void SetArduinoManager(ArduinoManager manager) {
		if (manager == null) {
			GD.PrintErr("ERREUR: Tentative de définir un ArduinoManager null!");
			// Tentative de récupération du singleton
			manager = ArduinoManager.Instance;
			if (manager == null) {
				GD.PrintErr("ERREUR GRAVE: Impossible de trouver ArduinoManager.Instance!");
				
				// Création d'une instance d'urgence
				manager = new ArduinoManager();
				manager.Name = "ArduinoManagerEmergency";
				GetTree().Root.AddChild(manager);
				GD.Print("ArduinoManager de secours créé!");
			}
		}
		
		// Stockage de la référence
		_arduinoManager = manager;
		_useArduino = true;
		
		// Diagnostic de la connexion
		GD.Print($"PlayerBall configuré pour utiliser ArduinoManager (référence directe: {_arduinoManager != null}, singleton: {ArduinoManager.Instance != null})");
		
		// Test de communication
		try {
			float testX = _arduinoManager.GetAccelX();
			float testY = _arduinoManager.GetAccelY();
			GD.Print($"Test de communication réussi: X={testX:F2}, Y={testY:F2}");
		} catch (Exception e) {
			GD.PrintErr($"ALERTE: Communication Arduino impossible: {e.Message}");
		}
	}
	
	/**
	 * Force la reconnexion avec l'Arduino
	 * Utilisé en cas de problème de connexion
	 */
	public void ForceReconnectArduino() {
		// Recherche de l'instance d'ArduinoManager
		_arduinoManager = ArduinoManager.Instance;
		
		if (_arduinoManager == null) {
			_arduinoManager = GetTree().Root.FindChild("ArduinoManager", true, false) as ArduinoManager;
		}
		
		if (_arduinoManager == null) {
			foreach (Node child in GetTree().Root.GetChildren()) {
				if (child is ArduinoManager arduino) {
					_arduinoManager = arduino;
					break;
				}
			}
		}
		
		if (_arduinoManager != null) {
			_useArduino = true;
			GD.Print("Force reconnexion à ArduinoManager réussie");
			
			// Réinitialisation des états de mouvement
			_inputDirection = Vector3.Zero;
			_smoothedInputDirection = Vector3.Zero;
			_jumpRequested = false;
		} else {
			_useArduino = false;
			GD.PrintErr("Force reconnexion à ArduinoManager échouée - contrôles clavier activés");
		}
		
		// Réinitialisation des états importants
		_controlsEnabled = true;
		_isBeingTeleported = false;
		_teleportationCooldown = false;
		
		// Réinitialisation de la physique
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
	}
	
	/**
	 * Désactive les contrôles du joueur
	 */
	public void DisableControls() {
		_controlsEnabled = false;
		
		if (_isRolling) {
			StopRollingSound();
		}
	}
	
	/**
	 * Réactive les contrôles du joueur
	 */
	public void EnableControls() {
		_controlsEnabled = true;
	}
}
