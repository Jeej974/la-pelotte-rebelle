using Godot;
using System;

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
	
	private OmniLight3D _light;
	private AnimationPlayer _animationPlayer;
	
	public override void _Ready()
	{
		// Configurer la forme de collision
		var collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
		if (collisionShape != null && collisionShape.Shape == null)
		{
			var capsuleShape = new CapsuleShape3D();
			capsuleShape.Radius = 0.5f;
			capsuleShape.Height = 2.0f;
			collisionShape.Shape = capsuleShape;
		}
		
		// Appliquer la couleur
		ApplyColor(TeleporterColor);
		
		// Connecter le signal de collision
		BodyEntered += OnBodyEntered;
		
		// Créer et démarrer l'animation de pulsation
		CreatePulseAnimation();
	}
	
	// Méthode pour appliquer la couleur au téléporteur
	public void ApplyColor(Color color)
	{
		// Mettre à jour la couleur des particules
		var particles = GetNodeOrNull<GpuParticles3D>("GPUParticles3D");
		if (particles != null)
		{
			var material = particles.ProcessMaterial as ParticleProcessMaterial;
			if (material != null)
			{
				material.Color = color;
			}
		}
		
		// Mettre à jour la couleur de la lumière
		_light = GetNodeOrNull<OmniLight3D>("OmniLight3D");
		if (_light != null)
		{
			_light.LightColor = color;
		}
		
		// Appliquer la couleur au modèle 3D
		var model = GetNodeOrNull<Node3D>("TeleporterModel");
		if (model != null)
		{
			ApplyColorToModel(model, color);
		}
		
		// Sauvegarder la couleur
		TeleporterColor = color;
	}
	
	// Méthode récursive pour appliquer la couleur au modèle
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
	
	// Créer une animation de pulsation pour la lumière
	private void CreatePulseAnimation()
	{
		if (_light == null) return;
		
		_animationPlayer = new AnimationPlayer();
		AddChild(_animationPlayer);
		
		var pulseAnimation = new Animation();
		var trackIdx = pulseAnimation.AddTrack(Animation.TrackType.Value);
		
		// Utiliser le chemin correct pour accéder à la propriété light_energy de OmniLight3D
		// Enlever le préfixe % qui cause le problème
		pulseAnimation.TrackSetPath(trackIdx, "OmniLight3D:light_energy");
		
		// Animation de pulsation de la lumière (sur 2 secondes)
		pulseAnimation.TrackInsertKey(trackIdx, 0.0f, 2.0f);
		pulseAnimation.TrackInsertKey(trackIdx, 1.0f, 4.0f);
		pulseAnimation.TrackInsertKey(trackIdx, 2.0f, 2.0f);
		pulseAnimation.LoopMode = Animation.LoopModeEnum.Linear;
		
		// Créer une librairie d'animation
		var animLib = new AnimationLibrary();
		animLib.AddAnimation("pulse", pulseAnimation);
		
		// Ajouter la librairie au player d'animation
		_animationPlayer.AddAnimationLibrary("", animLib);
		
		// Démarrer l'animation
		_animationPlayer.Play("pulse");
	}
	
	// Méthode appelée quand un corps entre dans le téléporteur
	private void OnBodyEntered(Node3D body)
	{
		if (IsExit && body is PlayerBall)
		{
			// Émettre le signal si c'est un téléporteur de sortie
			EmitSignal(SignalName.PlayerEnteredExitTeleporter, body, MazeIndex);
			GD.Print($"Joueur entré dans le téléporteur de sortie du labyrinthe {MazeIndex}");
		}
	}
}
