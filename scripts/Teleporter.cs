using Godot;
using System;

/**
 * Teleporter - Gère les portails entre différents labyrinthes
 * 
 * Cette classe implémente les zones d'entrée et de sortie des labyrinthes,
 * avec des effets visuels et la logique de téléportation du joueur.
 */
public partial class Teleporter : Area3D
{
	[Export]
	public bool IsExit { get; set; } = false;
	
	[Export]
	public Color TeleporterColor = new Color(0, 1, 0); // Vert par défaut
	
	[Export] 
	public int MazeIndex { get; set; } = 0;
	
	// Signal émis quand un joueur entre dans le téléporteur de sortie
	[Signal]
	public delegate void PlayerEnteredExitTeleporterEventHandler(Node3D body, int mazeIndex);
	
	// État de téléporation pour éviter les déclenchements multiples
	private bool _teleportActive = false;
	private float _cooldownTimer = 0f;
	private const float COOLDOWN_TIME = 1.5f;
	
	// Composants visuels
	private OmniLight3D _light;
	private AnimationPlayer _animationPlayer;
	
	/**
	 * Initialisation du téléporteur
	 */
	public override void _Ready()
	{
		// Configuration de la forme de collision
		var collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
		if (collisionShape != null && collisionShape.Shape == null)
		{
			var capsuleShape = new CapsuleShape3D();
			capsuleShape.Radius = 0.5f;
			capsuleShape.Height = 2.0f;
			collisionShape.Shape = capsuleShape;
		}
		
		// Application de la couleur configurée
		ApplyColor(TeleporterColor);
		
		// Connexion du signal de collision
		BodyEntered += OnBodyEntered;
		
		// Création de l'animation de pulsation
		CreatePulseAnimation();
	}
	
	/**
	 * Mise à jour par frame pour la gestion du cooldown
	 */
	public override void _Process(double delta)
	{
		// Gestion du cooldown anti-spam de téléportation
		if (_teleportActive)
		{
			_cooldownTimer -= (float)delta;
			if (_cooldownTimer <= 0)
			{
				_teleportActive = false;
			}
		}
	}
	
	/**
	 * Applique une couleur personnalisée au téléporteur
	 */
	public void ApplyColor(Color color)
	{
		// Application aux particules
		var particles = GetNodeOrNull<GpuParticles3D>("GPUParticles3D");
		if (particles != null)
		{
			var material = particles.ProcessMaterial as ParticleProcessMaterial;
			if (material != null)
			{
				material.Color = color;
			}
		}
		
		// Application à la lumière
		_light = GetNodeOrNull<OmniLight3D>("OmniLight3D");
		if (_light != null)
		{
			_light.LightColor = color;
		}
		
		// Application au modèle 3D
		var model = GetNodeOrNull<Node3D>("TeleporterModel");
		if (model != null)
		{
			ApplyColorToModel(model, color);
		}
		
		// Sauvegarde de la couleur
		TeleporterColor = color;
	}
	
	/**
	 * Applique récursivement une couleur à tous les meshes du modèle
	 */
	private void ApplyColorToModel(Node node, Color color)
	{
		if (node is MeshInstance3D meshInstance)
		{
			StandardMaterial3D material = new StandardMaterial3D();
			material.AlbedoColor = color;
			material.Metallic = 0.5f;
			material.Roughness = 0.3f;
			material.EmissionEnabled = true;
			material.Emission = new Color(color.R, color.G, color.B, 0.5f);
			material.EmissionEnergyMultiplier = 0.3f;
			
			meshInstance.MaterialOverride = material;
		}
		
		foreach (Node child in node.GetChildren())
		{
			ApplyColorToModel(child, color);
		}
	}
	
	/**
	 * Crée l'animation de pulsation pour la lumière du téléporteur
	 */
	private void CreatePulseAnimation()
	{
		if (_light == null) return;
		
		_animationPlayer = new AnimationPlayer();
		AddChild(_animationPlayer);
		
		var pulseAnimation = new Animation();
		var trackIdx = pulseAnimation.AddTrack(Animation.TrackType.Value);
		
		// Chemin d'accès à la propriété d'intensité lumineuse
		pulseAnimation.TrackSetPath(trackIdx, "OmniLight3D:light_energy");
		
		// Keyframes de l'animation de pulsation
		pulseAnimation.TrackInsertKey(trackIdx, 0.0f, 2.0f);
		pulseAnimation.TrackInsertKey(trackIdx, 1.0f, 4.0f);
		pulseAnimation.TrackInsertKey(trackIdx, 2.0f, 2.0f);
		pulseAnimation.LoopMode = Animation.LoopModeEnum.Linear;
		
		// Création et ajout de la librairie d'animation
		var animLib = new AnimationLibrary();
		animLib.AddAnimation("pulse", pulseAnimation);
		
		_animationPlayer.AddAnimationLibrary("", animLib);
		
		// Démarrage de l'animation
		_animationPlayer.Play("pulse");
	}
	
	/**
	 * Gestion de la collision avec un corps
	 */
	private void OnBodyEntered(Node3D body)
	{
		// Ignorer si ce n'est pas un téléporteur de sortie ou si en cooldown
		if (!IsExit || _teleportActive) return;
		
		// Vérifier si c'est le joueur
		if (body is PlayerBall)
		{
			// Activer le cooldown
			_teleportActive = true;
			_cooldownTimer = COOLDOWN_TIME;
			
			// Émettre le signal pour la téléportation
			EmitSignal(SignalName.PlayerEnteredExitTeleporter, body, MazeIndex);
			
			// Effet visuel
			PlayTeleportEffect();
			
			GD.Print($"Joueur entré dans le téléporteur de sortie du labyrinthe {MazeIndex}");
		}
	}
	
	/**
	 * Joue un effet visuel lors de la téléportation
	 */
	private void PlayTeleportEffect()
	{
		// Effet sur la lumière
		if (_light != null)
		{
			// Arrêter l'animation existante
			if (_animationPlayer != null && _animationPlayer.IsPlaying())
			{
				_animationPlayer.Stop();
			}
			
			// Animation de flash
			var tween = CreateTween();
			tween.SetEase(Tween.EaseType.Out);
			tween.SetTrans(Tween.TransitionType.Cubic);
			
			// Augmentation rapide de l'intensité
			tween.TweenProperty(_light, "light_energy", 8.0f, 0.2f);
			// Retour progressif à la normale
			tween.TweenProperty(_light, "light_energy", 2.0f, 1.0f);
			
			// Redémarrage de l'animation de pulsation après l'effet
			tween.TweenCallback(Callable.From(() => {
				if (_animationPlayer != null)
				{
					_animationPlayer.Play("pulse");
				}
			}));
		}
		
		// Effet sur les particules
		var particles = GetNodeOrNull<GpuParticles3D>("GPUParticles3D");
		if (particles != null)
		{
			// Sauvegarde du nombre initial de particules
			int originalAmount = particles.Amount;
			
			// Augmentation temporaire du nombre de particules
			particles.Amount = originalAmount * 3;
			
			// Timer pour restaurer le nombre original
			var timer = new Timer();
			timer.WaitTime = 1.0f;
			timer.OneShot = true;
			timer.Timeout += () => {
				particles.Amount = originalAmount;
				timer.QueueFree();
			};
			AddChild(timer);
			timer.Start();
		}
	}
}
