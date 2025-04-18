using Godot;
using System;

/**
 * Énumération des types de chats et leurs effets dans le jeu
 */
public enum CatType
{
	Orange,   // Chat Roux - Commun - +5 secondes
	Black,    // Chat Noir - Commun - -10 secondes
	Tabby,    // Chat Tigré - Moyen - +7/-7 secondes (aléatoire)
	White,    // Chat Blanc - Rare - +15 secondes
	Siamese   // Chat Siamois - Très rare - Révèle le chemin
}

/**
 * Classe Cat - Représente un chat collectable dans le jeu
 * 
 * Chaque chat a un type spécifique qui détermine son apparence et son effet
 * lorsqu'il est collecté par le joueur. Gère également les effets sonores et
 * visuels liés à la présence et à la collection des chats.
 */
public partial class Cat : Area3D
{
	[Export]
	private CatType _catType = CatType.Orange;
	
	// Couleurs associées à chaque type de chat
	private static readonly Color[] _catColors = {
		new Color(0.9f, 0.5f, 0.2f),   // Orange
		new Color(0.2f, 0.2f, 0.2f),   // Noir
		new Color(0.8f, 0.7f, 0.5f),   // Tigré
		new Color(1.0f, 1.0f, 1.0f),   // Blanc
		new Color(0.9f, 0.8f, 0.7f)    // Siamois
	};
	
	// Effets de temps en secondes pour chaque type de chat
	private static readonly float[] _timeEffects = { 5.0f, -10.0f, 7.0f, 15.0f, 0.0f };
	
	// Références aux composants
	private Node3D _catModel;
	private Timer _floatTimer;
	private Vector3 _basePosition;
	private float _floatPhase = 0f;
	
	// État de la collecte
	private bool _collected = false;
	
	// Chemins des ressources
	[Export]
	private string _catModelPath = "res://assets/cat/cat.glb";
	
	[Export]
	private string _catTexturePath = "res://assets/cat/cat_0.png";
	
	// Configuration audio
	private Timer _meowTimer;
	private AudioStreamPlayer3D _audioPlayer;
	private static readonly string _meowSoundPath = "res://assets/audio/bruit_chat.wav";

	/**
	 * Initialisation du chat et configuration de ses composants
	 */
	public override void _Ready()
	{
		// Récupération des composants
		_floatTimer = GetNode<Timer>("FloatTimer");
		
		// Configuration des couches de collision
		SetCollisionLayerValue(1, false);
		SetCollisionLayerValue(2, true);
		SetCollisionMaskValue(1, true);
		
		// Connexion des signaux
		BodyEntered += OnBodyEntered;
		
		if (_floatTimer != null)
		{
			_floatTimer.Timeout += OnFloatTimerTimeout;
		}
		
		// Chargement du modèle 3D
		LoadCatModel();
		
		// Initialisation de la phase d'animation avec une valeur aléatoire
		_floatPhase = (float)GD.RandRange(0, Math.PI * 2);
		
		// Configuration audio
		SetupAudio();
	}

	/**
	 * Configure les composants audio pour les miaulements périodiques
	 */
	private void SetupAudio()
	{
		// Création du lecteur audio
		_audioPlayer = new AudioStreamPlayer3D();
		_audioPlayer.Name = "CatAudioPlayer";
		_audioPlayer.VolumeDb = 0;
		_audioPlayer.MaxDistance = 10.0f;
		_audioPlayer.UnitSize = 1.5f;
		AddChild(_audioPlayer);
		
		// Chargement du son de miaulement
		var meowSound = ResourceLoader.Load<AudioStream>(_meowSoundPath);
		if (meowSound != null)
		{
			_audioPlayer.Stream = meowSound;
		}
		
		// Configuration du timer pour les miaulements aléatoires
		_meowTimer = new Timer();
		_meowTimer.Name = "MeowTimer";
		_meowTimer.WaitTime = GD.RandRange(5.0, 15.0);
		_meowTimer.Autostart = true;
		_meowTimer.OneShot = false;
		_meowTimer.Timeout += PlayRandomMeow;
		AddChild(_meowTimer);
	}

	/**
	 * Joue un son 3D avec un volume personnalisé via l'AudioManager
	 */
	private void PlaySound3DCustomVolume(string soundName, float volumeMultiplier)
	{
		if (AudioManager.Instance == null) return;
		
		AudioManager.Instance.PlaySound3D(soundName, this);
		
		// Personnalisation du volume pour le mode fallback
		foreach (Node child in GetChildren()) {
			if (child is AudioStreamPlayer3D audioPlayer) {
				float volumeDb = Mathf.LinearToDb(volumeMultiplier);
				volumeDb = Mathf.Clamp(volumeDb, -40.0f, 0.0f);
				audioPlayer.VolumeDb = volumeDb;
				break;
			}
		}
	}

	/**
	 * Joue un son de chat via le système audio natif de Godot
	 */
	private void PlayNativeCatSound(float distance, bool wallBetween)
	{
		if (_audioPlayer == null) {
			_audioPlayer = new AudioStreamPlayer3D();
			_audioPlayer.Name = "CatAudioPlayer";
			_audioPlayer.MaxDistance = 10.0f;
			_audioPlayer.UnitSize = 1.5f;
			AddChild(_audioPlayer);
		}
		
		string soundPath = "res://assets/audio/bruit_chat.wav";
		var sound = ResourceLoader.Load<AudioStream>(soundPath);
		
		if (sound != null)
		{
			_audioPlayer.Stream = sound;
			_audioPlayer.VolumeDb = CalculateVolumeDb(distance, wallBetween);
			_audioPlayer.Play();
			
			GD.Print($"Chat {_catType} miaule via Godot AudioPlayer (fallback)");
		}
	}

	/**
	 * Vérifie s'il y a un mur entre le joueur et le chat via raycast
	 */
	private bool CheckWallBetweenPlayerAndCat(PlayerBall player)
	{
		var rayOrigin = GlobalPosition + new Vector3(0, 0.3f, 0);
		var rayEnd = player.GlobalPosition + new Vector3(0, 0.3f, 0);
		var rayParams = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
		
		// Détecter uniquement les murs (couche 1)
		rayParams.CollisionMask = 1;
		
		var space = GetWorld3D().DirectSpaceState;
		var result = space.IntersectRay(rayParams);
		
		bool hasWall = result.Count > 0;
		
		if (hasWall) {
			GD.Print($"Mur détecté entre le chat {_catType} et le joueur");
		}
		
		return hasWall;
	}

	/**
	 * Joue un son de chat avec atténuation basée sur la distance
	 */
	private void PlayFallbackCatSound(float distance, bool wallBetween)
	{
		if (_audioPlayer == null) return;
		
		_audioPlayer.Stream = ResourceLoader.Load<AudioStream>(_meowSoundPath);
		
		_audioPlayer.VolumeDb = CalculateVolumeDb(distance, wallBetween);
		_audioPlayer.MaxDistance = 20.0f;
		_audioPlayer.UnitSize = wallBetween ? 0.5f : 1.0f;
		
		_audioPlayer.Play();
		
		GD.Print($"Chat {_catType} miaule via système Godot! Volume: {_audioPlayer.VolumeDb:F2}dB");
	}

	/**
	 * Vérifie si le joueur est dans le même segment de labyrinthe que le chat
	 */
	private bool IsPlayerInSameMaze()
	{
		Vector3 catGlobalPos = GlobalPosition;
		
		var player = GetTree().Root.FindChild("PlayerBall", true, false) as PlayerBall;
		if (player == null) return false;
		
		float xDistance = Mathf.Abs(player.GlobalPosition.X - catGlobalPos.X);
		float maxDistanceForSameMaze = 20.0f;
		
		return xDistance < maxDistanceForSameMaze;
	}

	/**
	 * Charge le modèle 3D du chat
	 */
	private void LoadCatModel()
	{
		// Suppression de l'ancien sprite si présent
		var oldSprite = GetNodeOrNull<MeshInstance3D>("CatSprite");
		if (oldSprite != null)
		{
			oldSprite.QueueFree();
		}
		
		// Chargement du modèle 3D
		var packedScene = ResourceLoader.Load<PackedScene>(_catModelPath);
		if (packedScene != null)
		{
			_catModel = packedScene.Instantiate<Node3D>();
			_catModel.Name = "CatModel";
			_catModel.Scale = new Vector3(0.1f, 0.1f, 0.1f);
			_catModel.Position = new Vector3(0, 0.2f, 0);
			
			AddChild(_catModel);
			
			// Application de la texture et couleur selon le type
			ApplyTextureToModel();
			
			// Sauvegarde de la position de base pour l'animation
			_basePosition = _catModel.Position;
			
			GD.Print($"Modèle 3D du chat chargé pour le type {_catType}");
		}
		else
		{
			GD.PrintErr($"Erreur lors du chargement du modèle de chat: {_catModelPath}");
			CreateFallbackSprite();
		}
	}

	/**
	 * Crée un sprite de fallback si le modèle 3D n'a pas pu être chargé
	 */
	private void CreateFallbackSprite()
	{
		var sprite = new MeshInstance3D();
		sprite.Name = "CatSprite";
		
		var quadMesh = new QuadMesh();
		quadMesh.Size = new Vector2(0.8f, 0.8f);
		
		var material = new StandardMaterial3D();
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
		
		var texture = ResourceLoader.Load<Texture2D>(_catTexturePath);
		if (texture != null)
		{
			material.AlbedoTexture = texture;
		}
		
		material.AlbedoColor = _catColors[(int)_catType];
		
		quadMesh.Material = material;
		sprite.Mesh = quadMesh;
		
		sprite.Position = new Vector3(0, 0.2f, 0);
		
		AddChild(sprite);
		
		_basePosition = sprite.Position;
		
		GD.Print($"Sprite de chat créé pour le type {_catType} (fallback)");
	}
	
	/**
	 * Applique la texture et la couleur au modèle 3D selon le type de chat
	 */
	private void ApplyTextureToModel()
	{
		if (_catModel == null) return;
		
		foreach (Node child in _catModel.GetChildren())
		{
			if (child is MeshInstance3D meshInstance)
			{
				StandardMaterial3D material = new StandardMaterial3D();
				
				var texture = ResourceLoader.Load<Texture2D>(_catTexturePath);
				if (texture != null)
				{
					material.AlbedoTexture = texture;
				}
				
				material.AlbedoColor = _catColors[(int)_catType];
				
				meshInstance.MaterialOverride = material;
			}
		}
	}

	/**
	 * Gère l'animation flottante du chat
	 */
	private void OnFloatTimerTimeout()
	{
		if (!_collected && _catModel != null)
		{
			_floatPhase += 0.1f;
			float offset = Mathf.Sin(_floatPhase) * 0.1f;
			_catModel.Position = new Vector3(_basePosition.X, _basePosition.Y + offset, _basePosition.Z);
		}
	}
	
	/**
	 * Gère la collision avec le joueur
	 */
	private void OnBodyEntered(Node3D body)
	{
		if (body is PlayerBall playerBall && !_collected)
		{
			_collected = true;
			
			ApplyEffectToMainScene();
			
			PlayCollectionAnimation();
		}
	}
	
	/**
	 * Applique l'effet du chat à la MainScene selon son type
	 */
	private void ApplyEffectToMainScene()
	{
		var mainScene = GetTree().Root.GetNode<Node>("MainScene");
		if (mainScene == null)
		{
			GD.Print("MainScene non disponible, impossible d'appliquer l'effet");
			return;
		}
		
		float effectValue = GetEffectValue();
		string effectText = "";
		Color effectColor = Colors.White;
		
		string soundEvent = "";
		
		switch (_catType)
		{
			case CatType.Orange: // +5 secondes
				mainScene.Call("AddTime", 5.0f);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = "+5 secondes";
				effectColor = Colors.Green;
				soundEvent = "Bonus";
				break;
				
			case CatType.Black: // -10 secondes
				mainScene.Call("AddTime", -10.0f);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = "-10 secondes";
				effectColor = Colors.Red;
				soundEvent = "Malus";
				break;
				
			case CatType.Tabby: // Aléatoire +7/-7 secondes
				bool positive = new Random().NextDouble() > 0.5;
				float effect = positive ? 7.0f : -7.0f;
				mainScene.Call("AddTime", effect);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = $"{effect} secondes";
				effectColor = positive ? Colors.Green : Colors.Red;
				soundEvent = positive ? "Bonus" : "Malus";
				break;
				
			case CatType.White: // +15 secondes
				mainScene.Call("AddTime", 15.0f);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = "+15 secondes";
				effectColor = Colors.Green;
				soundEvent = "Bonus";
				break;
				
			case CatType.Siamese: // Révèle le chemin et +20 secondes
				mainScene.Call("AddTime", 20.0f);
				mainScene.Call("AddCatCollected", (int)_catType);
				effectText = "+20s & Chemin révélé!";
				effectColor = Colors.Cyan;
				soundEvent = "Bonus";
				break;
		}
		
		if (AudioManager.Instance != null) {
			AudioManager.Instance.PlaySound(soundEvent);
		}
		
		CreateFloatingText(effectText, effectColor);
	}

	/**
	 * Obtient la valeur de l'effet de temps selon le type de chat
	 */
	private float GetEffectValue()
	{
		if (_catType == CatType.Tabby)
		{
			// Pour le chat tigré, effet aléatoire
			return new Random().NextDouble() > 0.5 ? 7.0f : -7.0f;
		}
		
		return _timeEffects[(int)_catType];
	}
	
	/**
	 * Anime la collection du chat (grossissement puis disparition)
	 */
	private void PlayCollectionAnimation()
	{
		if (_catModel != null)
		{
			Tween tween = CreateTween();
			tween.TweenProperty(_catModel, "scale", _catModel.Scale * 1.5f, 0.2f);
			tween.TweenProperty(_catModel, "scale", Vector3.Zero, 0.3f);
		}
		
		CreateCollectionParticles();
		
		if (_meowTimer != null)
		{
			_meowTimer.Stop();
			_meowTimer.QueueFree();
		}
		
		var timer = new Timer();
		timer.WaitTime = 1.0f;
		timer.OneShot = true;
		timer.Timeout += () => QueueFree();
		AddChild(timer);
		timer.Start();
		
		SetCollisionLayerValue(2, false);
		SetCollisionMaskValue(1, false);
	}
	
	/**
	 * Crée un effet de particules lors de la collection
	 */
	private void CreateCollectionParticles()
	{
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
			GD.PrintErr("Erreur lors de la création des particules: " + e.Message);
		}
	}
	
	/**
	 * Crée un texte flottant pour montrer l'effet appliqué
	 */
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
		
		Tween tween = CreateTween();
		tween.TweenProperty(label3D, "position:y", 1.5f, 1.0f);
		tween.Parallel().TweenProperty(label3D, "modulate:a", 0.0f, 1.0f);
		tween.TweenCallback(Callable.From(() => {
			label3D.QueueFree();
		}));
	}
	
	/**
	 * Obtient un type de chat aléatoire selon sa rareté et le niveau du labyrinthe
	 */
	public static CatType GetRandomType(int mazeLevel)
	{
		// Valeurs de base de rareté
		int[] rarityValues = { 100, 100, 50, 25, 10 }; // Orange, Black, Tabby, White, Siamese
		
		// Ajustement des probabilités selon le niveau
		int[] adjustedRarities = new int[rarityValues.Length];
		
		for (int i = 0; i < rarityValues.Length; i++)
		{
			adjustedRarities[i] = rarityValues[i];
			
			// Augmentation de la probabilité des chats malus avec le niveau
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
		
		// Calcul de la somme totale des raretés
		int totalRarity = 0;
		foreach (int rarity in adjustedRarities)
		{
			totalRarity += rarity;
		}
		
		// Sélection aléatoire selon les raretés
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
		
		return CatType.Orange; // Type par défaut
	}
	
	/**
	 * Déclenche un miaulement aléatoire selon la position du joueur
	 */
	private void PlayRandomMeow()
	{
		if (_collected) return;
		
		// Vérifier si le joueur est dans le même labyrinthe
		bool isPlayerInSameMaze = IsPlayerInSameMaze();
		
		if (!isPlayerInSameMaze) {
			if (_meowTimer != null) {
				_meowTimer.WaitTime = GD.RandRange(5.0, 15.0);
			}
			return;
		}
		
		// Trouver le joueur
		var player = GetTree().Root.FindChild("PlayerBall", true, false) as PlayerBall;
		if (player == null) return;
		
		// Calculer la distance
		float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
		
		// Ne pas jouer le son si trop loin
		float maxHearingDistance = 25.0f;
		if (distance > maxHearingDistance) {
			if (_meowTimer != null) {
				_meowTimer.WaitTime = GD.RandRange(5.0, 15.0);
			}
			return;
		}
		
		// Vérifier s'il y a un mur entre le chat et le joueur
		bool wallBetween = CheckWallBetweenPlayerAndCat(player);
		
		// Calcul du volume selon la distance et les obstacles
		float volumeMultiplier = CalculateVolumeMultiplier(distance, wallBetween);
		
		if (AudioManager.Instance != null) {
			PlayCatMeowSound(distance, wallBetween, player);
			
			GD.Print($"Chat {_catType} miaule! Distance: {distance:F2}, Mur entre: {wallBetween}, Volume: {volumeMultiplier:F2}");
		}
		
		// Reprogrammer le prochain miaulement
		if (_meowTimer != null) {
			_meowTimer.WaitTime = GD.RandRange(5.0, 15.0);
		}
	}
	
	/**
	 * Joue un son de miaulement avec la gestion de distance et d'obstacle
	 */
	private void PlayCatMeowSound(float distance, bool wallBetween, PlayerBall player)
	{
		string soundName = GetCatSoundEvent();
		
		// Son direct pour les distances très courtes sans obstacle
		if (distance < 2.0f && !wallBetween) {
			AudioManager.Instance.PlaySound3D(soundName, this);
			return;
		}
		
		// Création temporaire d'un AudioStreamPlayer3D
		var audioPlayer = new AudioStreamPlayer3D();
		AddChild(audioPlayer);
		
		audioPlayer.Name = "TempMeowPlayer";
		
		// Sélection du fichier audio selon le type de chat
		string path = "res://assets/audio/";
		switch (_catType)
		{
			case CatType.Orange:
				path += "cat_orange.wav";
				break;
			case CatType.Black:
				path += "chat_noir.wav";
				break;
			case CatType.Tabby:
				path += "bruit_chat.wav";
				break;
			case CatType.White:
				path += "chat_blanc.wav";
				break;
			case CatType.Siamese:
				path += "bruit_chat_siamois.wav";
				break;
			default:
				path += "bruit_chat.wav";
				break;
		}
		
		var sound = ResourceLoader.Load<AudioStream>(path);
		if (sound == null) {
			sound = ResourceLoader.Load<AudioStream>("res://assets/audio/bruit_chat.wav");
		}
		
		if (sound != null) {
			audioPlayer.Stream = sound;
			
			// Ajustement du pitch selon le type de chat
			float basePitch = 1.0f;
			switch (_catType) {
				case CatType.Black:
					basePitch = 0.8f;
					break;
				case CatType.Tabby:
					basePitch = 1.1f;
					break;
				case CatType.White:
					basePitch = 1.2f;
					break;
				case CatType.Siamese:
					basePitch = 1.3f;
					break;
			}
			
			// Réduction légère du pitch avec un mur
			audioPlayer.PitchScale = wallBetween ? basePitch * 0.9f : basePitch;
			
			// Calcul du volume selon la distance et les obstacles
			float volumeDb = CalculateVolumeDb(distance, wallBetween);
			audioPlayer.VolumeDb = volumeDb;
			
			// Configuration de l'atténuation avec la distance
			if (wallBetween) {
				audioPlayer.MaxDistance = 25.0f;
				audioPlayer.UnitSize = 0.8f;
			} else {
				audioPlayer.MaxDistance = 25.0f;
				audioPlayer.UnitSize = 1.0f;
			}
			
			audioPlayer.Play();
			
			// Nettoyage automatique
			audioPlayer.Finished += () => {
				audioPlayer.QueueFree();
			};
			
			GD.Print($"Son de chat joué avec volume {volumeDb:F2}dB, distance: {distance:F2}, mur: {wallBetween}");
		} else {
			audioPlayer.QueueFree();
			GD.PrintErr("Impossible de charger le son du chat");
		}
	}

	/**
	 * Calcule le volume en dB selon la distance et les obstacles
	 */
	private float CalculateVolumeDb(float distance, bool wallBetween)
	{
		// Volume de base plus élevé à travers les murs pour compenser
		float baseVolume = wallBetween ? 6.0f : 0.0f;
		
		// Atténuation logarithmique basée sur la distance
		float distanceAttenuation;
		
		if (wallBetween) {
			// Atténuation plus douce avec un mur
			distanceAttenuation = -5.0f * Mathf.Log(Mathf.Max(1.0f, distance / 3.0f)) / Mathf.Log(10.0f);
		} else {
			// Atténuation normale sans mur
			distanceAttenuation = -10.0f * Mathf.Log(Mathf.Max(1.0f, distance / 2.0f)) / Mathf.Log(10.0f);
		}
		
		// Atténuation supplémentaire pour les murs
		float wallAttenuation = wallBetween ? -4.0f : 0.0f;
		
		// Bonus de volume pour les chats importants
		float catTypeBonus = 0.0f;
		if (_catType == CatType.White || _catType == CatType.Siamese) {
			catTypeBonus = 6.0f;  // Bonus pour les chats rares
		} else if (_catType == CatType.Tabby) {
			catTypeBonus = 3.0f;  // Bonus pour les chats moyens
		} else {
			catTypeBonus = 1.0f;  // Petit bonus pour les chats communs
		}
		
		// Bonus de proximité pour les sons à travers les murs
		float proximityBonus = 0.0f;
		if (wallBetween) {
			if (distance < 4.0f) {
				proximityBonus = 10.0f * (1.0f - distance / 4.0f);
			}
		}
		
		// Calcul du volume final avec limites
		float finalVolume = Mathf.Clamp(baseVolume + distanceAttenuation + wallAttenuation + catTypeBonus + proximityBonus, -80.0f, 10.0f);
		
		return finalVolume;
	}

	/**
	 * Calcule le multiplicateur de volume linéaire selon la distance et les obstacles
	 */
	private float CalculateVolumeMultiplier(float distance, bool wallBetween)
	{
		float referenceDistance = 2.0f;
		float maxDistance = 25.0f;
		
		if (distance > maxDistance) {
			return 0.0f;
		}
		
		// Facteur d'atténuation basé sur la distance
		float distanceFactor;
		
		if (wallBetween) {
			// Atténuation plus faible avec mur
			distanceFactor = 1.0f - 0.6f * (distance / maxDistance);
		} else {
			// Atténuation standard
			distanceFactor = (referenceDistance * referenceDistance) / 
							  (distance * distance + referenceDistance * referenceDistance);
		}
		
		distanceFactor = Mathf.Clamp(distanceFactor, 0.0f, 1.0f);
		
		// Facteur d'atténuation pour les murs
		float wallFactor = wallBetween ? 0.8f : 1.0f;
		
		// Bonus pour les chats rares
		float rareCatBonus = 1.0f;
		if (_catType == CatType.White || _catType == CatType.Siamese) {
			rareCatBonus = 1.8f;
		} else if (_catType == CatType.Tabby) {
			rareCatBonus = 1.4f;
		} else {
			rareCatBonus = 1.2f;
		}
		
		// Bonus de proximité pour murs
		float proximityBonus = 1.0f;
		if (wallBetween && distance < 5.0f) {
			proximityBonus = Mathf.Lerp(3.0f, 1.0f, distance / 5.0f);
		}
		
		// Combinaison des facteurs avec limite maximum
		return Mathf.Min(distanceFactor * wallFactor * rareCatBonus * proximityBonus, 1.0f);
	}

	/**
	 * Compte le nombre de murs entre le chat et le joueur
	 */
	private int CountWallsBetweenPlayerAndCat(PlayerBall player)
	{
		var rayOrigin = GlobalPosition + new Vector3(0, 0.3f, 0);
		var rayEnd = player.GlobalPosition + new Vector3(0, 0.3f, 0);
		var rayParams = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
		
		rayParams.CollisionMask = 1;
		
		var space = GetWorld3D().DirectSpaceState;
		var result = space.IntersectRay(rayParams);
		
		return result.Count > 0 ? 1 : 0;
	}

	/**
	 * Obtient le nom du son associé au type de chat
	 */
	private string GetCatSoundEvent()
	{
		switch (_catType)
		{
			case CatType.Orange:
				return "CatOrange"; 
			case CatType.Black:
				return "CatBlack";
			case CatType.Tabby:
				return "CatTabby";
			case CatType.White:
				return "CatWhite";
			case CatType.Siamese:
				return "CatSiamese";
			default:
				return "CatOrange";
		}
	}

	/**
	 * Détermine le nombre de chats à placer dans un labyrinthe selon son niveau
	 */
	public static int GetCatCountForMaze(int mazeLevel)
	{
		// Augmente progressivement le nombre de chats avec le niveau
		int baseCount = Math.Min(mazeLevel, 5);
		
		// Ajoute une variation aléatoire
		Random random = new Random();
		int randomOffset = random.Next(2);
		
		return baseCount + randomOffset;
	}
}
