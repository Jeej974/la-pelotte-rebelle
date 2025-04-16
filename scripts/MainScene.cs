using Godot;
using System;
using System.Collections.Generic;


public partial class MainScene : Node3D
{
	[Export]
	private PackedScene _playerBallScene;
	
	// Référence au générateur de labyrinthe
	private VerticalMazeGenerator _mazeGenerator;
	
	// Référence au gestionnaire Arduino
	private ArduinoManager _arduinoManager;
		// Charger la police
	private Font font = GD.Load<Font>("res://fonts/PoetsenOne-Regular.ttf");
	// Référence à la balle du joueur
	private PlayerBall _playerBall;
	
	// Interface utilisateur
	private Label _infoLabel;
	private Label _mazeCountLabel;
	private Label _timeLabel;     // Label pour afficher le temps
	private Label _catEffectLabel; // Label pour les effets des chats
	private Label _scoreLabel;     // NOUVEAU: Label pour afficher le score
	private Panel _uiPanel;
	
	// Texte central pour instructions
	private Label _centerMessageLabel;
	private Label _centerMessageLabel2;
	private Label _centerMessageLabel3;
	// Etat dans le jeu
	private string etatGame = "LoadingStart" ;


	// État du jeu
	private int _currentMazeIndex = 0;
	private bool _gameCompleted = false;
	
	// Gestion du temps
	[Export]
	private float _initialGameTime = 60.0f; // 60 secondes de base
	private float _remainingTime;
	private bool _timeOver = false;
	
	// États du jeu
	private enum GameState
	{
		WaitingToStart,
		Playing,
		GameOver,
		Victory
	}
	private GameState _gameState = GameState.WaitingToStart;
	
	// Score
	private int _mazesCompleted = 0;
	private Dictionary<CatType, int> _catsCollected = new Dictionary<CatType, int>()
	{
		{ CatType.Orange, 0 },
		{ CatType.Black, 0 },
		{ CatType.Tabby, 0 },
		{ CatType.White, 0 },
		{ CatType.Siamese, 0 }
	};
	private List<ScoreEntry> _highScores = new List<ScoreEntry>();
	
	// Struct pour les entrées de score
	private struct ScoreEntry
	{
		public int mazesCompleted;
		public int totalCats;
		public string date;
		
		public ScoreEntry(int mazes, int cats)
		{
			mazesCompleted = mazes;
			totalCats = cats;
			date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
		}
	}
	
	  // Les objets exportés depuis l'inspecteur
	[Export] public Viewport Viewport3D;
	[Export] public TextureRect BlurredDisplay;
	[Export] public ShaderMaterial BlurShaderMaterial;


	
	public override void _Ready()
	{
		// starting game
		// Crée un ViewportTexture pour capturer la scène 3D
		if (Viewport3D != null && BlurredDisplay != null)
		{
			//RenderingServer.SetDefaultClearColor(new Color(0.2f, 0.6f, 0.9f));
			ViewportTexture viewportTexture = new ViewportTexture();
			viewportTexture.ViewportPath = Viewport3D.GetPath();
			BlurredDisplay.Texture = viewportTexture;
			BlurredDisplay.Material = BlurShaderMaterial;
		}
		
		// Initialiser le temps de jeu
		_remainingTime = _initialGameTime;
		
		// Créer l'Arduino Manager et lui donner un nom précis
		_arduinoManager = new ArduinoManager();
		_arduinoManager.Name = "ArduinoManager";
		
		// Utiliser CallDeferred pour ajouter l'ArduinoManager à la racine
		// Cela évite l'erreur "Parent node is busy setting up children"
		GetTree().Root.CallDeferred(Node.MethodName.AddChild, _arduinoManager);
		
		// Puis continuer avec la création du labyrinthe et du joueur
		var mazeGenerator = new VerticalMazeGenerator();
		mazeGenerator.Name = "MazeGenerator";
		AddChild(mazeGenerator);
		
		// Attendre pour s'assurer que tout est initialisé
		CallDeferred(nameof(SpawnPlayerBall));
		
		// Créer l'interface utilisateur
		CallDeferred(nameof(CreateUI));
		MyThreadFunction();
		// Charger les scores précédents (si disponibles)
		LoadHighScores();
	}
	
	private CanvasLayer AddCanvas() {
				// Créer un canvas layer pour l'UI
		var canvasLayer = new CanvasLayer();
		canvasLayer.Name = "UICanvas";
		AddChild(canvasLayer);
		
		return canvasLayer;
	}
	
	private void CreateUI()
	{

		var canvasLayer = AddCanvas();
		
		// Créer un panneau pour l'UI
		_uiPanel = new Panel();
		_uiPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_uiPanel.Size = new Vector2(300, 220); // Augmenter la taille pour les nouveaux labels
		_uiPanel.Position = new Vector2(-300, 0);
		canvasLayer.AddChild(_uiPanel);
		// Ajouter une étiquette d'information
		_infoLabel = new Label();
		_infoLabel.Text = "Utilisez les flèches ou l'Arduino pour déplacer la balle";
		_infoLabel.Position = new Vector2(10, 10);
		_infoLabel.Size = new Vector2(280, 40);
		_infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_uiPanel.AddChild(_infoLabel);
		
		// Ajouter une étiquette pour le compteur de labyrinthe
		_mazeCountLabel = new Label();
		_mazeCountLabel.Text = "Labyrinthe: 1 / ?";
		_mazeCountLabel.Position = new Vector2(10, 50);
		_mazeCountLabel.Size = new Vector2(280, 30);
		_uiPanel.AddChild(_mazeCountLabel);
		
		// Ajouter une étiquette pour le temps
		_timeLabel = new Label();
		_timeLabel.Text = "Temps: 60s";
		_timeLabel.Position = new Vector2(10, 80);
		_timeLabel.Size = new Vector2(280, 30);
		_timeLabel.Modulate = new Color(1, 1, 1); // Blanc par défaut
		_uiPanel.AddChild(_timeLabel);
		
		// Ajouter une étiquette pour les effets des chats
		_catEffectLabel = new Label();
		_catEffectLabel.Text = "";
		_catEffectLabel.Position = new Vector2(10, 110);
		_catEffectLabel.Size = new Vector2(280, 30);
		_catEffectLabel.Visible = false; // Caché par défaut
		_uiPanel.AddChild(_catEffectLabel);
		
		// NOUVEAU: Ajouter une étiquette pour le score
		_scoreLabel = new Label();
		_scoreLabel.Text = "Chats: 0 | Score: 0";
		_scoreLabel.Position = new Vector2(10, 150);
		_scoreLabel.Size = new Vector2(280, 30);
		_uiPanel.AddChild(_scoreLabel);
		
		
		// 
	if (etatGame == "LoadingStart")
{
	_centerMessageLabel2 = new Label();

	// Texte
	_centerMessageLabel2.Text = "Manette en cours de calibrage, veuillez patienter\n";

		// Ancrer le label en bas (centré horizontalement)
		_centerMessageLabel2.SetAnchorsPreset(Control.LayoutPreset.BottomWide);

		// Taille du label
		_centerMessageLabel2.Size = new Vector2(100, 100);

		// Recentrer horizontalement, et placer un peu au-dessus du bord bas
		_centerMessageLabel2.SetPivotOffset(_centerMessageLabel2.Size / 2);
		_centerMessageLabel2.Position = new Vector2(20, -60);

	// Taille d’écran
	Vector2 screenSize = GetViewport().GetVisibleRect().Size;

	// Taille de police dynamique (par ex. 8% de la hauteur écran)
	int dynamicFontSize = (int)(screenSize.Y * 0.05f); // Ajuste à ton goût

	// Créer les settings avec taille dynamique
	var labelSettings = new LabelSettings
	{
		Font = font,
		FontSize = dynamicFontSize,
		FontColor = new Color(1f, 1f, 1f)
	};

	_centerMessageLabel2.LabelSettings = labelSettings;

	// Ajout au canvas
	canvasLayer.AddChild(_centerMessageLabel2);

	// Lancer les points de chargement
	ShowLoadingDots();
}
		// Mettre à jour les étiquettes
		UpdateUI();
	}

// chargement start screen
public async void ShowLoadingDots()
{
	var canvasLayer = AddCanvas();
	_centerMessageLabel3 = new Label();
	_centerMessageLabel3.Text = "Chargement";
	_centerMessageLabel3.Position = new Vector2(20, 10);

	// Taille d’écran
	Vector2 screenSize = GetViewport().GetVisibleRect().Size;
	
	int dynamicFontSize = (int)(screenSize.Y * 0.03f);
// Créer les settings avec taille dynamique
	var labelSettings = new LabelSettings
	{
		Font = font,
			FontSize = dynamicFontSize,
		FontColor = new Color(1f, 1f, 1f)
	};

	_centerMessageLabel3.LabelSettings = labelSettings;
	for (int i = 0; i < 20; i++)
	{
		_centerMessageLabel3.Text += "...";
		canvasLayer.AddChild(_centerMessageLabel3);
		await ToSignal(GetTree().CreateTimer(0.3), "timeout"); // Attend 0.3 seconde
	}
		
			
}
	
public void ShowLabelStart() {

		var canvasLayer = AddCanvas();
		
		if (etatGame == "StartGame") {
			_centerMessageLabel2.QueueFree();
			_centerMessageLabel3.QueueFree();
			
			// Créer un texte au centre de l'écran pour les instructions/messages importants
		_centerMessageLabel = new Label();
				_centerMessageLabel.Position = new Vector2(20, -60);

	// Taille d’écran
	Vector2 screenSize = GetViewport().GetVisibleRect().Size;

	// Taille de police dynamique (par ex. 8% de la hauteur écran)
	int dynamicFontSize = (int)(screenSize.Y * 0.02f); // Ajuste à ton goût

	// Créer les settings avec taille dynamique
	var labelSettings = new LabelSettings
	{
		Font = font,
		FontSize = dynamicFontSize,
		FontColor = new Color(1f, 1f, 1f)
	};
	   _centerMessageLabel.LabelSettings = labelSettings;
		_centerMessageLabel.Text = "APPUYEZ SUR LE BOUTON ARDUINO POUR COMMENCER";
		_centerMessageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_centerMessageLabel.VerticalAlignment = VerticalAlignment.Center;

		_centerMessageLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
		_centerMessageLabel.Size = new Vector2(800, 100);
		_centerMessageLabel.Position = new Vector2(-400, -50);
		_centerMessageLabel.AddThemeColorOverride("font_color", new Color(1, 1, 0)); // Jaune
		_centerMessageLabel.AddThemeConstantOverride("font_size", 32); // Plus grande taille
		canvasLayer.AddChild(_centerMessageLabel);
		etatGame = "GoGame";
		}
}
public async void MyThreadFunction()
{
	double duration = 6.0;
	double elapsed = 0.0;

	// Créer un Timer dynamiquement
	var timer = new Timer
	{
		OneShot = true,
		WaitTime = duration // Durée en secondes
	};
	AddChild(timer); // Nécessaire pour que le timer fonctionne

	timer.Start(); // Démarre le timer
	GD.Print("Timer démarré. Durée : ", duration, " secondes.");

	// Boucle pendant que le timer tourne
	while (elapsed < duration)
	{
		await ToSignal(GetTree(), "process_frame"); // Attendre une frame
		elapsed += GetProcessDeltaTime(); // Incrémente le temps écoulé
		GD.Print("Temps écoulé : ", Math.Round(elapsed, 2), "s");
	}

	// Attendre le signal Timeout du Timer
	await ToSignal(timer, Timer.SignalName.Timeout);

	// Le temps est écoulé
	etatGame = "StartGame";
	GD.Print("tempsDepasse = TRUE");

	// Nettoyage
	timer.QueueFree();
}
	
	// Mise à jour générale de l'interface utilisateur
	private void UpdateUI()
	{
		// Mettre à jour le label de temps
		UpdateTimeLabel();
		
		// Mettre à jour le label de score
		UpdateScoreLabel();
		
		// Mettre à jour le message central selon l'état du jeu
		UpdateCenterMessage();
	}
	
	// Mettre à jour le message central
	private void UpdateCenterMessage()
	{
		if (_centerMessageLabel == null) return;
		
		switch (_gameState)
		{
			case GameState.WaitingToStart:
				_centerMessageLabel.Text = "APPUYEZ SUR LE BOUTON ARDUINO POUR COMMENCER";
				_centerMessageLabel.Visible = true;
				_centerMessageLabel.Modulate = new Color(1, 1, 0); // Jaune
				break;
				
			case GameState.Playing:
				_centerMessageLabel.Visible = false;
				break;
				
			case GameState.GameOver:
				_centerMessageLabel.Text = "VOUS N'AVEZ PLUS DE LAINE... APPUYEZ SUR LE BOUTON POUR RECOMMENCER";
				_centerMessageLabel.Visible = true;
				_centerMessageLabel.Modulate = new Color(1, 0, 0); // Rouge
				break;
				
			case GameState.Victory:
				_centerMessageLabel.Text = "FÉLICITATIONS! VOUS AVEZ TERMINÉ TOUS LES LABYRINTHES!";
				_centerMessageLabel.Visible = true;
				_centerMessageLabel.Modulate = new Color(0, 1, 0); // Vert
				break;
		}
	}
	
	// Mettre à jour le label de score
	private void UpdateScoreLabel()
	{
		if (_scoreLabel == null) return;
		
		int totalCats = 0;
		foreach (var count in _catsCollected.Values)
		{
			totalCats += count;
		}
		
		_scoreLabel.Text = $"Labyrinthes: {_mazesCompleted} | Chats: {totalCats}";
	}
	
	private void SpawnPlayerBall()
	{
		GD.Print("Tentative de spawn du joueur...");
		
		// Vérifier que la scène du joueur est correctement référencée
		if (_playerBallScene == null)
		{
			GD.PrintErr("Erreur: PlayerBallScene n'est pas définie dans l'inspecteur!");
			return;
		}
		
		// Instancier le joueur
		_playerBall = _playerBallScene.Instantiate<PlayerBall>();
		
		// Assigner la référence de l'ArduinoManager au PlayerBall avant de l'ajouter
		_playerBall.SetArduinoManager(_arduinoManager);
		
		AddChild(_playerBall);
		GD.Print("PlayerBall instancié avec succès");
		
		// Rechercher le premier labyrinthe
		_mazeGenerator = GetNode<VerticalMazeGenerator>("MazeGenerator");
		if (_mazeGenerator == null)
		{
			GD.PrintErr("Erreur: MazeGenerator non trouvé!");
			return;
		}
		
		var firstMaze = GetNode<Node3D>("MazeGenerator/Maze_0");
		if (firstMaze == null)
		{
			GD.PrintErr("Erreur: Premier labyrinthe non trouvé!");
			return;
		}
		
		// Obtenir la position d'entrée du premier labyrinthe
		int size = _mazeGenerator.GetMazeSize(0); // Utiliser la méthode d'accès
		Vector2I entrancePos = _mazeGenerator.GetEntrancePosition(size, 0);
		
		// Positionner le joueur à l'entrée du premier labyrinthe
		_playerBall.GlobalPosition = firstMaze.GlobalPosition + new Vector3(
			entrancePos.X * _mazeGenerator._cellSize,
			1.0f, // Un peu au-dessus du sol
			entrancePos.Y * _mazeGenerator._cellSize
		);
		
		GD.Print("Joueur placé à la position: " + _playerBall.GlobalPosition);
		
		// IMPORTANT: Désactiver la caméra globale du labyrinthe
		var globalCamera = GetNode<Camera3D>("MazeGenerator/GlobalCamera");
		if (globalCamera != null)
		{
			globalCamera.Current = false;
			GD.Print("Caméra globale désactivée");
		}
		
		// Au début, désactiver les contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
	}
	
	public override void _Process(double delta)
	{
		ShowLabelStart();
		// Vérifier si le bouton Arduino est pressé
		if (_arduinoManager != null && _arduinoManager.IsJumpDetected())
		{
			HandleButtonPress();
		}
		
		// Également vérifier les touches du clavier pour le débogage
		if (Input.IsActionJustPressed("ui_accept"))
		{
			HandleButtonPress();
		}
		
		// Si le jeu n'est pas encore démarré ou est terminé, ne pas mettre à jour le temps
		if (_gameState != GameState.Playing)
		{
			return;
		}
		
		// Mise à jour du temps restant
		_remainingTime -= (float)delta;
		UpdateTimeLabel();
		
		// Vérifier si le temps est écoulé
		if (_remainingTime <= 0)
		{
			_remainingTime = 0;
			GameOver();
		}
		
		// Vérifier le passage à un nouveau labyrinthe
		CheckMazeTransition();
	}
	
	// Gérer l'appui sur le bouton (Arduino ou clavier)
	private void HandleButtonPress()
	{
		// Afficher un message de débogage
		GD.Print("Bouton détecté! État du jeu: " + _gameState);
		if (etatGame == "GoGame") {
		switch (_gameState)
		{
			case GameState.WaitingToStart:
				// Démarrer le jeu
				StartGame();
				BlurredDisplay.QueueFree();
				Viewport3D.QueueFree();
				break;
				
			case GameState.Playing:
				// Optionnel: Redémarrer le jeu ou ignorer
				// RestartGame();
				break;
				
			case GameState.GameOver:
			case GameState.Victory:
				// Redémarrer le jeu
				RestartGame();
				break;
			}
		}
	}
	
	// Démarrer le jeu
	private void StartGame()
	{
		_gameState = GameState.Playing;
		
		// Activer les contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.EnableControls();
		}
		
		// Mettre à jour l'interface
		UpdateUI();
		
		GD.Print("Jeu démarré");
	}
	
	// Mise à jour de l'affichage du temps
	private void UpdateTimeLabel()
	{
		if (_timeLabel == null) return;
		
		int seconds = (int)_remainingTime;
		_timeLabel.Text = $"Temps: {seconds}s";
		
		// Changer la couleur si le temps est presque écoulé
		if (_remainingTime < 10)
		{
			_timeLabel.Modulate = new Color(1, 0, 0); // Rouge
		}
		else if (_remainingTime < 20)
		{
			_timeLabel.Modulate = new Color(1, 0.5f, 0); // Orange
		}
		else
		{
			_timeLabel.Modulate = new Color(1, 1, 1); // Blanc
		}
	}
	
	// Méthode appelée par les chats ou la téléportation pour ajouter du temps
	public void AddTime(float seconds)
	{
		if (_gameState != GameState.Playing) return;
		
		_remainingTime += seconds;
		
		// Afficher un message d'effet temporaire
		ShowCatEffect(seconds);
		
		GD.Print($"Temps ajouté: {seconds} secondes. Temps restant: {_remainingTime}");
	}
	
	// Méthode appelée quand un chat est collecté
	public void AddCatCollected(int catType)
	{
		if (_gameState != GameState.Playing) return;
		
		// S'assurer que le type est valide
		if (catType >= 0 && catType < 5)
		{
			CatType type = (CatType)catType;
			if (_catsCollected.ContainsKey(type))
			{
				_catsCollected[type]++;
			}
			else
			{
				_catsCollected[type] = 1;
			}
			
			// Mettre à jour l'affichage du score
			UpdateScoreLabel();
		}
	}
	
	// Afficher un effet temporaire pour l'effet d'un chat
	private void ShowCatEffect(float seconds)
	{
		if (_catEffectLabel == null) return;
		
		string effectText;
		Color effectColor;
		
		if (seconds > 0)
		{
			effectText = $"+{seconds:0.0} secondes";
			effectColor = new Color(0, 1, 0); // Vert pour positif
		}
		else
		{
			effectText = $"{seconds:0.0} secondes";
			effectColor = new Color(1, 0, 0); // Rouge pour négatif
		}
		
		_catEffectLabel.Text = effectText;
		_catEffectLabel.Modulate = effectColor;
		_catEffectLabel.Visible = true;
		
		// Créer un timer pour masquer le message après un délai
		var timer = new Timer();
		AddChild(timer);
		timer.WaitTime = 2.0f; // 2 secondes
		timer.OneShot = true;
		timer.Timeout += () => {
			_catEffectLabel.Visible = false;
			timer.QueueFree();
		};
		timer.Start();
	}
	
	// Appelé quand le temps est écoulé
	private void GameOver()
	{
		if (_gameState == GameState.GameOver) return;
		
		_gameState = GameState.GameOver;
		_timeOver = true;
		
		// Désactiver les contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		
		// Sauvegarder le score
		SaveScore();
		
		// Mettre à jour l'interface
		UpdateUI();
		
		// Jouer un son de game over
		PlayGameOverSound();
		
		GD.Print("Game Over - Plus de temps!");
	}
	
	// Jouer un son de game over
	private void PlayGameOverSound()
	{
		var audioPlayer = new AudioStreamPlayer();
		AddChild(audioPlayer);
		
		// Configurer le son (à implémenter avec FMOD)
		// audioPlayer.Stream = ResourceLoader.Load<AudioStream>("res://assets/sounds/game_over.wav");
		// audioPlayer.Play();
		
		// Supprimer le lecteur une fois le son terminé
		audioPlayer.Finished += () => audioPlayer.QueueFree();
	}
	
	private void CheckMazeTransition()
	{
		// Si le joueur n'existe pas encore, ne rien faire
		if (_playerBall == null) return;
		
		// Détecter dans quel labyrinthe se trouve le joueur
		int newMazeIndex = GetPlayerCurrentMaze();
		
		// Si le joueur a changé de labyrinthe
		if (newMazeIndex != _currentMazeIndex && newMazeIndex >= 0)
		{
			// Si le joueur avance (et ne recule pas)
			if (newMazeIndex > _currentMazeIndex)
			{
				// Incrémenter le nombre de labyrinthes complétés
				_mazesCompleted++;
				UpdateScoreLabel();
			}
			
			_currentMazeIndex = newMazeIndex;
			UpdateMazeCounter(newMazeIndex);
			
			// Vérifier si c'est le dernier labyrinthe
			if (_currentMazeIndex == _mazeGenerator.GetTotalMazeCount() - 1)
			{
				// Surveiller la position dans le dernier labyrinthe pour détecter la fin
				CheckGameCompletion();
			}
		}
	}
	
	private int GetPlayerCurrentMaze()
	{
		// Trouver dans quel labyrinthe se trouve le joueur
		for (int i = 0; i < _mazeGenerator.GetTotalMazeCount(); i++)
		{
			Node3D maze = GetNodeOrNull<Node3D>($"MazeGenerator/Maze_{i}");
			if (maze != null)
			{
				// Vérifier si le joueur est dans les limites X du labyrinthe
				float mazeMinX = maze.GlobalPosition.X;
				int size = _mazeGenerator.GetMazeSize(i);
				float mazeMaxX = mazeMinX + (size * _mazeGenerator._cellSize);
				
				if (_playerBall.GlobalPosition.X >= mazeMinX && _playerBall.GlobalPosition.X <= mazeMaxX)
				{
					return i;
				}
			}
		}
		
		return -1; // Joueur hors des labyrinthes
	}
	
	private void CheckGameCompletion()
	{
		// Si déjà complété, ne pas vérifier à nouveau
		if (_gameCompleted) return;
		
		// Obtenir le dernier labyrinthe
		int lastMazeIndex = _mazeGenerator.GetTotalMazeCount() - 1;
		Node3D lastMaze = GetNodeOrNull<Node3D>($"MazeGenerator/Maze_{lastMazeIndex}");
		if (lastMaze == null) return;
		
		// Obtenir les coordonnées de sortie du dernier labyrinthe
		int lastMazeSize = _mazeGenerator.GetMazeSize(lastMazeIndex);
		Vector2I exitPos = _mazeGenerator.GetExitPosition(lastMazeSize, lastMazeIndex);
		
		// Calculer la position globale de la sortie
		Vector3 exitGlobalPos = lastMaze.GlobalPosition + new Vector3(
			exitPos.X * _mazeGenerator._cellSize,
			0,
			exitPos.Y * _mazeGenerator._cellSize
		);
		
		// Vérifier si le joueur est à la sortie
		float distanceToExit = (_playerBall.GlobalPosition - exitGlobalPos).Length();
		if (distanceToExit < _mazeGenerator._cellSize * 0.5f)
		{
			// Le joueur a atteint la sortie du dernier labyrinthe
			GameCompleted();
		}
	}
	
	private void GameCompleted()
	{
		_gameCompleted = true;
		_gameState = GameState.Victory;
		
		// Désactiver les contrôles du joueur (optionnel)
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		
		// Sauvegarder le score
		SaveScore();
		
		// Mettre à jour l'interface
		UpdateUI();
		
		// Ajouter des effets de victoire (son, particules, etc.)
		PlayVictoryEffects();
		
		GD.Print("Jeu terminé avec succès!");
	}
	
	private void PlayVictoryEffects()
	{
		// Créer un système de particules pour célébrer la victoire
		var particles = new GpuParticles3D();
		particles.Name = "VictoryParticles";
		
		// Configurer les particules (à personnaliser selon vos préférences)
		var particlesMaterial = new ParticleProcessMaterial();
		particlesMaterial.InitialVelocityMin = 5.0f;
		particlesMaterial.InitialVelocityMax = 10.0f;
		particlesMaterial.ScaleMin = 0.5f;
		particlesMaterial.ScaleMax = 1.0f;
		particlesMaterial.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
		particlesMaterial.EmissionSphereRadius = 1.0f;
		particlesMaterial.Direction = new Vector3(0, 1, 0);
		particlesMaterial.Spread = 45.0f;
		particlesMaterial.Gravity = new Vector3(0, -9.8f, 0);
		particlesMaterial.Color = new Color(1, 1, 0);
		
		particles.ProcessMaterial = particlesMaterial;
		
		// Ajouter un mesh pour les particules
		particles.Amount = 100;
		particles.Lifetime = 2.0f;
		particles.OneShot = false;
		particles.Explosiveness = 0.1f;
		
		// Ajouter les particules au joueur
		if (_playerBall != null)
		{
			_playerBall.AddChild(particles);
			particles.Emitting = true;
		}
		
		// Jouer un son de victoire (si disponible)
		var audioPlayer = new AudioStreamPlayer3D();
		// audioPlayer.Stream = ResourceLoader.Load<AudioStream>("res://assets/sounds/victory.wav");
		audioPlayer.Autoplay = true;
		
		if (_playerBall != null)
		{
			_playerBall.AddChild(audioPlayer);
		}
	}
	
	private void UpdateMazeCounter(int mazeIndex)
	{
		if (_mazeCountLabel != null)
		{
			// Afficher "infini" pour le nombre total de labyrinthes
			_mazeCountLabel.Text = $"Labyrinthe: {mazeIndex + 1} / ∞";
		}
	}
	
	// Sauvegarder le score actuel
	private void SaveScore()
	{
		int totalCats = 0;
		foreach (var count in _catsCollected.Values)
		{
			totalCats += count;
		}
		
		// Créer une nouvelle entrée de score
		var newScore = new ScoreEntry(_mazesCompleted, totalCats);
		
		// Ajouter à la liste des scores
		_highScores.Add(newScore);
		
		// Trier les scores (du plus élevé au plus bas)
		_highScores.Sort((a, b) => {
			// D'abord trier par nombre de labyrinthes
			int mazeCompare = b.mazesCompleted.CompareTo(a.mazesCompleted);
			if (mazeCompare != 0) return mazeCompare;
			
			// Ensuite par nombre de chats
			return b.totalCats.CompareTo(a.totalCats);
		});
		
		// Limiter la liste à 10 scores maximum
		if (_highScores.Count > 10)
		{
			_highScores.RemoveAt(_highScores.Count - 1);
		}
		
		// Dans une implémentation plus complète, sauvegarder dans un fichier
		SaveHighScoresToFile();
	}
	
	// Sauvegarder les scores dans un fichier
	private void SaveHighScoresToFile()
	{
		// Pour une implémentation simple, on peut utiliser JSON
		// Mais pour l'instant, affichons juste les scores
		GD.Print("=== SCORES SAUVEGARDÉS ===");
		for (int i = 0; i < _highScores.Count; i++)
		{
			GD.Print($"{i+1}. Labyrinthes: {_highScores[i].mazesCompleted}, Chats: {_highScores[i].totalCats}, Date: {_highScores[i].date}");
		}
	}
	
	// Charger les scores précédents
	private void LoadHighScores()
	{
		// Dans une implémentation complète, charger depuis un fichier
		// Pour l'instant, juste initialiser avec des données vides
		_highScores = new List<ScoreEntry>();
	}
	
	private void RestartGame()
	{
		// Recharger la scène pour recommencer
		GetTree().ReloadCurrentScene();
	}
}
