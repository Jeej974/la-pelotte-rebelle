using Godot;
using System;

public enum CatType
{
	Orange,   // Chat Roux - Commun - +5 secondes
	Black,    // Chat Noir - Commun - -10 secondes
	Tabby,    // Chat Tigré - Moyen - +7/-7 secondes (aléatoire)
	White,    // Chat Blanc - Rare - +15 secondes
	Siamese   // Chat Siamois - Très rare - Révèle le chemin
}

public partial class Cat : Area3D
{
	[Export]
	private CatType _catType = CatType.Orange;
	
	// Couleurs de base pour les différents types de chats
	private static readonly Color[] _catColors = {
		new Color(0.9f, 0.5f, 0.2f),   // Orange (roux)
		new Color(0.2f, 0.2f, 0.2f),   // Noir
		new Color(0.8f, 0.7f, 0.5f),   // Tigré
		new Color(1.0f, 1.0f, 1.0f),   // Blanc
		new Color(0.9f, 0.8f, 0.7f)    // Siamois
	};
	
	// Effets des chats en secondes
	private static readonly float[] _timeEffects = { 5.0f, -10.0f, 7.0f, 15.0f, 0.0f };
	
	// Références aux nœuds
	private Node3D _catModel;
	private Timer _floatTimer;
	private Vector3 _basePosition;
	private float _floatPhase = 0f;
	
	// État de la collecte
	private bool _collected = false;
	
	// Nouveau: Chemin vers le modèle du chat
	[Export]
	private string _catModelPath = "res://assets/cat/cat.glb";
	
	// Nouveau: Chemin vers la texture du chat
	[Export]
	private string _catTexturePath = "res://assets/cat/cat_0.png";
	
	// NOUVEAU: Timer pour les sons du chat
	private Timer _meowTimer;
	private AudioStreamPlayer3D _audioPlayer;
	
	// NOUVEAU: Chemins vers les fichiers audio
	private static readonly string _meowSoundPath = "res://assets/audio/bruit_chat.wav";

	
	public override void _Ready()
	{
		// Trouver les nœuds de la scène
		_floatTimer = GetNode<Timer>("FloatTimer");
		
		// Configuration pour éviter les collisions avec la scène
		SetCollisionLayerValue(1, false);
		SetCollisionLayerValue(2, true);
		
		// S'assurer que le chat peut interagir avec le joueur
		SetCollisionMaskValue(1, true);
		
		// Connexion du signal de collision
		BodyEntered += OnBodyEntered;
		
		// Connexion du timer pour l'animation flottante
		if (_floatTimer != null)
		{
			_floatTimer.Timeout += OnFloatTimerTimeout;
		}
		
		// Charger le modèle 3D
		LoadCatModel();
		
		// Démarrer avec une phase aléatoire pour l'animation
		_floatPhase = (float)GD.RandRange(0, Math.PI * 2);
		
		// NOUVEAU: Créer le lecteur audio pour les miaulements périodiques
		SetupAudio();
	}

	// NOUVEAU: Configurer l'audio pour le chat
	private void SetupAudio()
	{
		// Créer le lecteur audio
		_audioPlayer = new AudioStreamPlayer3D();
		_audioPlayer.Name = "CatAudioPlayer";
		_audioPlayer.VolumeDb = 0; // Volume normal
		_audioPlayer.MaxDistance = 10.0f; // Distance maximale d'audition
		_audioPlayer.UnitSize = 1.5f; // Facteur d'atténuation avec la distance
		AddChild(_audioPlayer);
		
		// Charger le son de miaulement
		var meowSound = ResourceLoader.Load<AudioStream>(_meowSoundPath);
		if (meowSound != null)
		{
			_audioPlayer.Stream = meowSound;
		}
		
		// Créer un timer pour les miaulements périodiques
		_meowTimer = new Timer();
		_meowTimer.Name = "MeowTimer";
		
		// Intervalle aléatoire entre 5 et 15 secondes
		_meowTimer.WaitTime = GD.RandRange(5.0, 15.0);
		_meowTimer.Autostart = true;
		_meowTimer.OneShot = false;
		_meowTimer.Timeout += PlayRandomMeow;
		AddChild(_meowTimer);
	}
	
	private void PlayRandomMeow()
	{
		// Ne pas jouer de son si le chat a été collecté
		if (_collected) return;
		
		// OPTIMISATION CLÉ: Vérifier si le joueur est dans le même labyrinthe
		bool isPlayerInSameMaze = IsPlayerInSameMaze();
		
		// SILENCE SI HORS ZONE: Si le chat n'est pas dans le même labyrinthe, ne pas jouer de son
		if (!isPlayerInSameMaze) {
			// Mettre à jour le timer pour le prochain essai
			if (_meowTimer != null) {
				_meowTimer.WaitTime = GD.RandRange(5.0, 15.0);
			}
			return;
		}
		
		// Trouver le joueur
		var player = GetTree().Root.FindChild("PlayerBall", true, false) as PlayerBall;
		if (player == null) return;
		
		// Calculer la distance entre le chat et le joueur
		float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
		
		// Si le joueur est très loin, ne pas jouer le son
		float maxHearingDistance = 25.0f;
		if (distance > maxHearingDistance) {
			if (_meowTimer != null) {
				_meowTimer.WaitTime = GD.RandRange(5.0, 15.0);
			}
			return;
		}
		
		// Déterminer s'il y a un mur entre le chat et le joueur par raycast
		bool wallBetween = CheckWallBetweenPlayerAndCat(player);
		
		// Réduction du volume en fonction de la distance et des obstacles
		float volumeMultiplier = CalculateVolumeMultiplier(distance, wallBetween);
		
		if (AudioManager.Instance != null) {
			// Définir l'occlusion en fonction de la présence d'un mur
			AudioManager.Instance.SetGlobalParameter("Occlusion", wallBetween ? 0.8f : 0.0f);
			
			if (distance < 3.0f) {
				// Son très proche: jouer à volume max
				AudioManager.Instance.PlaySound3D("Cat" + _catType.ToString(), this);
			}
			else {
				// Adapter le volume en fonction de la distance
				// Note: Si nous sommes en mode fallback, cette adaptation se fera automatiquement
				// dans la méthode PlaySound3DCustomVolume ci-dessous
				PlaySound3DCustomVolume("Cat" + _catType.ToString(), volumeMultiplier);
			}
			
			GD.Print($"Chat {_catType} miaule! Distance: {distance:F2}, Mur entre: {wallBetween}");
		}
		
		// Définir un nouvel intervalle aléatoire pour le prochain miaulement
		if (_meowTimer != null) {
			_meowTimer.WaitTime = GD.RandRange(5.0, 15.0);
		}
	}

	// Nouvelle méthode pour jouer un son 3D avec volume personnalisé
	private void PlaySound3DCustomVolume(string soundName, float volumeMultiplier)
	{
		if (AudioManager.Instance == null) return;
		
		// Si nous utilisons FMOD, il prend déjà en charge l'atténuation de distance
		// mais nous pouvons essayer de personnaliser davantage via AudioManager
		AudioManager.Instance.PlaySound3D(soundName, this);
		
		// Personnaliser le volume pour le son (pour le mode fallback)
		// Chercher le lecteur audio associé au chat et ajuster son volume
		foreach (Node child in GetChildren()) {
			if (child is AudioStreamPlayer3D audioPlayer) {
				// Calculer le facteur de volume en dB (logarithmique)
				float volumeDb = Mathf.LinearToDb(volumeMultiplier);
				
				// Limiter à une plage raisonnable
				volumeDb = Mathf.Clamp(volumeDb, -40.0f, 0.0f);
				
				// Appliquer le volume
				audioPlayer.VolumeDb = volumeDb;
				break;
			}
		}
	}

	// Nouvelle méthode pour calculer le facteur de volume basé sur la distance et les obstacles
	private float CalculateVolumeMultiplier(float distance, bool wallBetween)
	{
		// Distance de référence (volume plein)
		float referenceDistance = 3.0f;
		
		// Distance maximale d'audition
		float maxDistance = 25.0f;
		
		// Si au-delà de la distance maximale, son inaudible
		if (distance > maxDistance) {
			return 0.0f;
		}
		
		// Facteur d'atténuation basé sur la distance (plus réaliste que linéaire)
		// Cette formule donne une atténuation plus douce au début, puis plus rapide à distance
		float distanceFactor = 1.0f - (distance - referenceDistance) / (maxDistance - referenceDistance);
		distanceFactor = Mathf.Clamp(distanceFactor, 0.0f, 1.0f);
		
		// Courbe d'atténuation quadratique (plus réaliste que linéaire)
		distanceFactor = distanceFactor * distanceFactor;
		
		// Facteur d'atténuation supplémentaire si un mur est présent
		float wallFactor = wallBetween ? 0.3f : 1.0f; // 70% de réduction avec un mur
		
		// Combiner les facteurs
		return distanceFactor * wallFactor;
	}



	
	private void PlayNativeCatSound(float distance, bool wallBetween)
	{
		if (_audioPlayer == null) {
			// Créer le lecteur audio s'il n'existe pas
			_audioPlayer = new AudioStreamPlayer3D();
			_audioPlayer.Name = "CatAudioPlayer";
			_audioPlayer.MaxDistance = 10.0f;
			_audioPlayer.UnitSize = 1.5f;
			AddChild(_audioPlayer);
		}
		
		// Charger le son approprié en fonction du type de chat
		string soundPath = "res://assets/audio/bruit_chat.wav";
		var sound = ResourceLoader.Load<AudioStream>(soundPath);
		
		if (sound != null)
		{
			_audioPlayer.Stream = sound;
			
			// Ajuster le volume en fonction de la distance et des obstacles
			_audioPlayer.VolumeDb = CalculateVolumeDb(distance, wallBetween);
			
			// Jouer le son
			_audioPlayer.Play();
			
			GD.Print($"Chat {_catType} miaule via Godot AudioPlayer (fallback)");
		}
	}



	// Ajouter cette nouvelle méthode pour vérifier s'il y a un mur entre le chat et le joueur
	private bool CheckWallBetweenPlayerAndCat(PlayerBall player)
	{
		// Créer un objet PhysicsRayQueryParameters3D pour configurer le raycast
		var rayOrigin = GlobalPosition;
		var rayEnd = player.GlobalPosition;
		var rayParams = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
		
		// Ne détecter que les murs (layer 1 - collision layer standard)
		rayParams.CollisionMask = 1;
		
		// Effectuer le raycast
		var space = GetWorld3D().DirectSpaceState;
		var result = space.IntersectRay(rayParams);
		
		// Si le résultat contient une collision, il y a un mur entre les deux
		return result.Count > 0;
	}

	// Ajouter cette méthode pour jouer le son via AudioStreamPlayer standard avec atténuation
	private void PlayFallbackCatSound(float distance, bool wallBetween)
	{
		if (_audioPlayer == null) return;
		
		// Charger le son
		_audioPlayer.Stream = ResourceLoader.Load<AudioStream>(_meowSoundPath);
		
		// Configurer l'atténuation du son
		_audioPlayer.VolumeDb = CalculateVolumeDb(distance, wallBetween);
		_audioPlayer.MaxDistance = 20.0f; // Distance maximale d'audition
		_audioPlayer.UnitSize = 1.0f; // Facteur d'atténuation avec la distance
		
		// Atténuation supplémentaire s'il y a un mur
		if (wallBetween)
		{
			_audioPlayer.UnitSize = 0.5f; // Réduire davantage la portée
		}
		
		_audioPlayer.Play();
		
		GD.Print($"Chat {_catType} miaule via système Godot! Volume: {_audioPlayer.VolumeDb:F2}dB");
	}

	// Calcule le volume en fonction de la distance et des obstacles
	private float CalculateVolumeDb(float distance, bool wallBetween)
	{
		// Volume de base
		float baseVolume = 0.0f; // 0dB = volume normal
		
		// Atténuation basée sur la distance (logarithmique)
		float distanceAttenuation = -6.0f * Mathf.Log(Mathf.Max(1.0f, distance / 5.0f));
		
		// Atténuation supplémentaire s'il y a un mur
		float wallAttenuation = wallBetween ? -12.0f : 0.0f; // -12dB supplémentaires si un mur est présent
		
		// Calculer le volume final (limité entre -80dB et 0dB)
		return Mathf.Clamp(baseVolume + distanceAttenuation + wallAttenuation, -80.0f, 0.0f);
	}
	
	// NOUVEAU: Vérifier si le joueur est dans le même labyrinthe que ce chat
	private bool IsPlayerInSameMaze()
	{
		// Obtenir la position globale du chat
		Vector3 catGlobalPos = GlobalPosition;
		
		// Trouver le joueur
		var player = GetTree().Root.FindChild("PlayerBall", true, false) as PlayerBall;
		if (player == null) return false;
		
		// Vérifier si le joueur et le chat sont dans le même segment X (même labyrinthe)
		// Nous utilisons une approximation basée sur la distance en X, en supposant que chaque labyrinthe 
		// est séparé horizontalement
		float xDistance = Mathf.Abs(player.GlobalPosition.X - catGlobalPos.X);
		
		// Si la distance en X est inférieure à la largeur typique d'un labyrinthe, ils sont probablement dans le même
		float maxDistanceForSameMaze = 20.0f; // Ajuster cette valeur en fonction de la taille des labyrinthes
		
		return xDistance < maxDistanceForSameMaze;
	}

	// Nouvelle méthode pour charger le modèle 3D
	private void LoadCatModel()
	{
		// Supprimer l'ancien sprite s'il existe
		var oldSprite = GetNodeOrNull<MeshInstance3D>("CatSprite");
		if (oldSprite != null)
		{
			oldSprite.QueueFree();
		}
		
		// Charger le modèle 3D
		var packedScene = ResourceLoader.Load<PackedScene>(_catModelPath);
		if (packedScene != null)
		{
			_catModel = packedScene.Instantiate<Node3D>();
			_catModel.Name = "CatModel";
			
			// Ajuster l'échelle uniformément
			_catModel.Scale = new Vector3(0.1f, 0.1f, 0.1f);
			
			// Corriger la position
			_catModel.Position = new Vector3(0, 0.2f, 0);
			
			
			// Ajouter le modèle comme enfant
			AddChild(_catModel);
			
			// Appliquer la texture et la couleur en fonction du type
			ApplyTextureToModel();
			
			// Enregistrer la position de base pour l'animation
			_basePosition = _catModel.Position;
			
			GD.Print($"Modèle 3D du chat chargé pour le type {_catType}");
		}
		else
		{
			GD.PrintErr($"Erreur lors du chargement du modèle de chat: {_catModelPath}");
			
			// Fallback vers l'approche originale avec sprite si nécessaire
			CreateFallbackSprite();
		}
	}	
	// Méthode de fallback pour créer un sprite si le modèle 3D échoue
	private void CreateFallbackSprite()
	{
		var sprite = new MeshInstance3D();
		sprite.Name = "CatSprite";
		
		// Créer un quad mesh
		var quadMesh = new QuadMesh();
		quadMesh.Size = new Vector2(0.8f, 0.8f);
		
		// Créer un matériau standard
		var material = new StandardMaterial3D();
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
		
		// Charger la texture
		var texture = ResourceLoader.Load<Texture2D>(_catTexturePath);
		if (texture != null)
		{
			material.AlbedoTexture = texture;
		}
		
		// Appliquer la couleur selon le type de chat
		material.AlbedoColor = _catColors[(int)_catType];
		
		// Appliquer le matériau au mesh
		quadMesh.Material = material;
		sprite.Mesh = quadMesh;
		
		// Positionner le sprite
		sprite.Position = new Vector3(0, 0.2f, 0);
		
		// Ajouter le sprite comme enfant
		AddChild(sprite);
		
		// Enregistrer la position de base pour l'animation
		_basePosition = sprite.Position;
		
		GD.Print($"Sprite de chat créé pour le type {_catType} (fallback)");
	}
	
	// Appliquer la texture et la couleur au modèle 3D
	private void ApplyTextureToModel()
	{
		if (_catModel == null) return;
		
		// Trouver tous les MeshInstance3D dans le modèle
		foreach (Node child in _catModel.GetChildren())
		{
			if (child is MeshInstance3D meshInstance)
			{
				// Duplicating material to avoid affecting other instances
				StandardMaterial3D material = new StandardMaterial3D();
				
				// Charger la texture
				var texture = ResourceLoader.Load<Texture2D>(_catTexturePath);
				if (texture != null)
				{
					material.AlbedoTexture = texture;
				}
				
				// Appliquer la couleur selon le type de chat
				material.AlbedoColor = _catColors[(int)_catType];
				
				// Appliquer le matériau au mesh
				meshInstance.MaterialOverride = material;
			}
		}
	}

	private void OnFloatTimerTimeout()
	{
		// Animation de flottement simple
		if (!_collected && _catModel != null)
		{
			_floatPhase += 0.1f;
			float offset = Mathf.Sin(_floatPhase) * 0.1f;
			_catModel.Position = new Vector3(_basePosition.X, _basePosition.Y + offset, _basePosition.Z);
		}
	}
	
	private void OnBodyEntered(Node3D body)
	{
		// Vérifier si c'est le joueur
		if (body is PlayerBall playerBall && !_collected)
		{
			_collected = true;
			
			// Appliquer l'effet selon le type
			ApplyEffectToMainScene();
			
			// Animation de collecte
			PlayCollectionAnimation();
		}
	}
	

	private void ApplyEffectToMainScene()
	{
		// Chercher le MainScene
		var mainScene = GetTree().Root.GetNode<Node>("MainScene");
		if (mainScene == null)
		{
			GD.Print("MainScene non disponible, impossible d'appliquer l'effet");
			return;
		}
		
		float effectValue = GetEffectValue();
		string effectText = "";
		Color effectColor = Colors.White;
		
		// Déterminer l'événement sonore en fonction du type de chat
		string soundEvent = "";
		
		switch (_catType)
		{
			case CatType.Orange: // Chat Roux: +5 secondes
				mainScene.Call("AddTime", 5.0f);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = "+5 secondes";
				effectColor = Colors.Green;
				soundEvent = "Bonus";
				break;
				
			case CatType.Black: // Chat Noir: -10 secondes
				mainScene.Call("AddTime", -10.0f);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = "-10 secondes";
				effectColor = Colors.Red;
				soundEvent = "Malus";
				break;
				
			case CatType.Tabby: // Chat Tigré: Aléatoire +7/-7 secondes
				bool positive = new Random().NextDouble() > 0.5;
				float effect = positive ? 7.0f : -7.0f;
				mainScene.Call("AddTime", effect);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = $"{effect} secondes";
				effectColor = positive ? Colors.Green : Colors.Red;
				soundEvent = positive ? "Bonus" : "Malus";
				break;
				
			case CatType.White: // Chat Blanc: +15 secondes
				mainScene.Call("AddTime", 15.0f);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = "+15 secondes";
				effectColor = Colors.Green;
				soundEvent = "Bonus";
				break;
				
			case CatType.Siamese: // Chat Siamois: Révèle le chemin
				mainScene.Call("AddTime", 20.0f);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = "+20s & Chemin révélé!";
				effectColor = Colors.Cyan;
				soundEvent = "Bonus";
				break;
		}
		
		// Jouer le son approprié via le système disponible
		if (AudioManager.Instance != null) {
			AudioManager.Instance.PlaySound(soundEvent);
		}

		
		// Afficher un texte flottant
		CreateFloatingText(effectText, effectColor);
	}


	private float GetEffectValue()
	{
		if (_catType == CatType.Tabby)
		{
			// Pour le chat tigré, l'effet est aléatoire
			return new Random().NextDouble() > 0.5 ? 7.0f : -7.0f;
		}
		
		// Pour les autres types, utiliser la valeur prédéfinie
		return _timeEffects[(int)_catType];
	}
	
	private void PlayCollectionAnimation()
	{
		// Animation de collecte simple avec Tween
		if (_catModel != null)
		{
			Tween tween = CreateTween();
			tween.TweenProperty(_catModel, "scale", _catModel.Scale * 1.5f, 0.2f);
			tween.TweenProperty(_catModel, "scale", Vector3.Zero, 0.3f);
		}
		
		// Ajouter quelques particules si possible
		CreateCollectionParticles();
		
		// Désactiver le timer de miaulement
		if (_meowTimer != null)
		{
			_meowTimer.Stop();
			_meowTimer.QueueFree();
		}
		
		// Supprimer le chat après un court délai
		var timer = new Timer();
		timer.WaitTime = 1.0f;
		timer.OneShot = true;
		timer.Timeout += () => QueueFree();
		AddChild(timer);
		timer.Start();
		
		// Désactiver les collisions
		SetCollisionLayerValue(2, false);
		SetCollisionMaskValue(1, false);
	}
	
	private void CreateCollectionParticles()
	{
		// Version simplifiée des particules pour éviter les erreurs de matrice
		try {
			var particles = new GpuParticles3D();
			particles.Name = "CollectionParticles";
			
			var particlesMaterial = new ParticleProcessMaterial();
			particlesMaterial.Direction = new Vector3(0, 1, 0);
			particlesMaterial.Spread = 180.0f;
			particlesMaterial.Color = _catColors[(int)_catType];
			
			particles.ProcessMaterial = particlesMaterial;
			
			var sphereMesh = new SphereMesh();
			sphereMesh.Radius = 0.05f;
			sphereMesh.Height = 0.1f;
			particles.DrawPass1 = sphereMesh;
			
			particles.Amount = 20;
			particles.Lifetime = 0.5f;
			particles.OneShot = true;
			particles.Explosiveness = 0.8f;
			
			AddChild(particles);
			particles.Emitting = true;
		}
		catch (Exception e) {
			// En cas d'erreur, ne pas insister sur les particules
			GD.PrintErr("Erreur lors de la création des particules: " + e.Message);
		}
	}
	
	private void CreateFloatingText(string text, Color color)
	{
		var label3D = new Label3D();
		label3D.Text = text;
		label3D.FontSize = 16;
		label3D.Modulate = color;
		label3D.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		label3D.NoDepthTest = true;
		label3D.Position = new Vector3(0, 0.5f, 0);
		
		AddChild(label3D);
		
		// Animation simple du texte
		Tween tween = CreateTween();
		tween.TweenProperty(label3D, "position:y", 1.5f, 1.0f);
		tween.Parallel().TweenProperty(label3D, "modulate:a", 0.0f, 1.0f);
		tween.TweenCallback(Callable.From(() => {
			label3D.QueueFree();
		}));
	}
	
	// Méthodes statiques utilisées par le générateur de labyrinthe
	
	// Obtenir un type de chat pondéré par la rareté
	public static CatType GetRandomType(int mazeLevel)
	{
		// Probabilités de base pour chaque type
		int[] rarityValues = { 100, 100, 50, 25, 10 }; // Orange, Black, Tabby, White, Siamese
		
		// Ajuster les probabilités en fonction du niveau
		int[] adjustedRarities = new int[rarityValues.Length];
		
		for (int i = 0; i < rarityValues.Length; i++)
		{
			adjustedRarities[i] = rarityValues[i];
			
			// Augmenter la probabilité des chats malus avec le niveau
			if (i == (int)CatType.Black)
			{
				adjustedRarities[i] += mazeLevel * 15; // Le chat noir devient plus fréquent
			}
			else if (i == (int)CatType.Tabby)
			{
				adjustedRarities[i] += mazeLevel * 5; // Le chat tigré devient plus fréquent 
			}
			else if (i == (int)CatType.White || i == (int)CatType.Siamese)
			{
				// Les chats rares deviennent légèrement plus rares avec le niveau
				adjustedRarities[i] = Math.Max(5, adjustedRarities[i] - mazeLevel * 2);
			}
		}
		
		// Calculer la somme totale des raretés ajustées
		int totalRarity = 0;
		foreach (int rarity in adjustedRarities)
		{
			totalRarity += rarity;
		}
		
		// Sélectionner un type aléatoirement en fonction des raretés
		Random random = new Random();
		int randomValue = random.Next(totalRarity);
		
		int cumulativeRarity = 0;
		for (int i = 0; i < adjustedRarities.Length; i++)
		{
			cumulativeRarity += adjustedRarities[i];
			if (randomValue < cumulativeRarity)
			{
				return (CatType)i;
			}
		}
		
		// Par défaut, retourner le chat orange (le plus commun)
		return CatType.Orange;
	}
	
	private string GetCatSoundEvent()
	{
		switch (_catType)
		{
			case CatType.Orange:
				return "CatOrange"; // Nouveau format
			case CatType.Black:
				return "CatBlack";
			case CatType.Tabby:
				return "CatTabby";
			case CatType.White:
				return "CatWhite";
			case CatType.Siamese:
				return "CatSiamese";
			default:
				return "CatOrange"; // Fallback
		}
	}


	// Déterminer le nombre de chats pour un labyrinthe en fonction du niveau
	public static int GetCatCountForMaze(int mazeLevel)
	{
		// Augmenter progressivement le nombre de chats avec le niveau
		int baseCount = Math.Min(mazeLevel, 5); // Maximum 5 chats de base
		
		// Ajouter un élément aléatoire
		Random random = new Random();
		int randomOffset = random.Next(2); // 0 ou 1 chat supplémentaire
		
		return baseCount + randomOffset;
	}
}
