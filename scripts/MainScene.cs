using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Linq; // Ajout pour utiliser ToList()

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
	private Label _scoreLabel;     // Label pour afficher le score
	private Panel _uiPanel;
	
	// Texte central pour instructions
	private Label _centerMessageLabel;
	private Label _loadingDotsLabel;
	
	// Ajout des variables membres pour le suivi des états sonores
	private bool _ambianceSoundPlaying = false;
	private bool _pauseMusicPlaying = false;

	// Panel pour les high scores
	private Panel _highScorePanel;
	
	// État du jeu
	private int _currentMazeIndex = 0;
	private bool _gameCompleted = false;
	
	// Gestion du temps
	[Export]
	private float _initialGameTime = 60.0f; // 60 secondes de base
	private float _remainingTime;
	private float _savedTime; // Pour stocker le temps pendant la pause
	private bool _timeOver = false;
	
	// Flag pour sauter le calibrage lors du redémarrage
	private static bool _isFirstStart = true;
	
	// Flag pour le redémarrage direct
	private static bool _isRestarting = false;
	
	// États du jeu
	private enum GameState
	{
		Calibrating,
		WaitingToStart,
		Playing,
		Paused,
		GameOver,
		ShowingAllMazes,
		GameOverFinal,
		Victory,
		CameraTravel // Nouvel état pour le travelling de caméra
	}
	private GameState _gameState = GameState.Calibrating;
	
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

	private List<ScoreManager.ScoreEntry> _highScores = new List<ScoreManager.ScoreEntry>();
	
	
	// Les objets exportés depuis l'inspecteur
	[Export] public SubViewport Viewport3D;
	[Export] public TextureRect BlurredDisplay;
	[Export] public ShaderMaterial BlurShaderMaterial;
	
	// Canvas pour les éléments UI
	private CanvasLayer _uiCanvas;
	private CanvasLayer _blurCanvas;
	private CanvasLayer _messageCanvas;
	private CanvasLayer _scoreCanvas;
	
	// Nouvel élément pour l'effet de flou bleu
	private ColorRect _blueOverlay;
	
	// Flag pour éviter les pressions répétées du bouton
	private float _buttonCooldownTimer = 0;
	private const float BUTTON_COOLDOWN = 0.2f; // Réduit pour plus de réactivité
	
	// Camera traveling
	private Camera3D _globalCamera;
	private Tween _cameraTween;
	

	public override void _Ready()
	{
		// Initialiser le temps de jeu
		_remainingTime = _initialGameTime;
		
		// Utiliser l'instance existante de l'Arduino Manager ou en créer une nouvelle
		_arduinoManager = ArduinoManager.Instance;
		if (_arduinoManager == null)
		{
			_arduinoManager = new ArduinoManager();
			_arduinoManager.Name = "ArduinoManager";
			GetTree().Root.CallDeferred(Node.MethodName.AddChild, _arduinoManager);
		}
		
		// AJOUT: Jouer le son de début (écran d'intro)
		PlayStartSound();
		
		// Créer le générateur de labyrinthe
		var mazeGenerator = new VerticalMazeGenerator();
		mazeGenerator.Name = "MazeGenerator";
		AddChild(mazeGenerator);
		_mazeGenerator = mazeGenerator;
		
		// Initialiser les éléments d'interface
		InitializeCanvasLayers();
		
		// Attendre pour s'assurer que tout est initialisé
		CallDeferred(nameof(SpawnPlayerBall));
		
		// Créer l'interface utilisateur
		CallDeferred(nameof(CreateUI));
		
		// Configurer l'écran flou et message de calibrage initial
		ConfigureBlurredBackground();
		
		// Charger les scores précédents
		LoadHighScores();
		// Ajouter le panneau de highscores
		CreateHighScorePanel();
		
		// ESSENTIEL: Vérifier la variable _isRestarting pour éviter l'écran de calibration
		if (_isRestarting)
		{
			// NOUVEAU: Démarrer directement le jeu après un game over
			CallDeferred(nameof(SkipCalibrationAndStart));
			
			// Réinitialiser le flag
			_isRestarting = false;
			
			GD.Print("Redémarrage après Game Over - Calibration ignorée");
		}
		else if (!_isFirstStart)
		{
			// Passer directement à l'écran d'attente au redémarrage normal
			_gameState = GameState.WaitingToStart;
			AddCenteredMessage("APPUYEZ SUR LE BOUTON ARDUINO POUR COMMENCER", new Color(1, 1, 0), 36);
		}
		else
		{
			// Simuler le calibrage au premier démarrage du jeu
			SimulateCalibration();
			_isFirstStart = false;
		}
		
		GD.Print("MainScene initialisé");
	}
	
	private void PlayStartSound()
	{
		if (AudioManager.Instance != null) {
			AudioManager.Instance.PlaySound("GameStart");
			GD.Print("Son de début joué");
		}
	}


	// Nouvelle méthode pour démarrer directement le jeu sans calibration
	private void SkipCalibrationAndStart()
	{
		// Attendre un court instant pour que tout soit correctement initialisé
		var timer = GetTree().CreateTimer(0.5f);
		timer.Timeout += () => {
			// Définir l'état de jeu actif
			_gameState = GameState.Playing;
			
			// Masquer les éléments d'interface
			if (_centerMessageLabel != null)
				_centerMessageLabel.Visible = false;
			if (BlurredDisplay != null)
				BlurredDisplay.Visible = false;
			if (_blueOverlay != null)
				_blueOverlay.Visible = false;
			
			// AJOUT: Réinitialiser l'état du bouton Arduino
			if (ArduinoManager.Instance != null)
			{
				ArduinoManager.Instance.ResetButtonState();
			}
			
			// Activer les contrôles du joueur
			if (_playerBall != null)
			{
				// S'assurer que le joueur est à la bonne position
				var firstMaze = GetNodeOrNull<Node3D>("MazeGenerator/Maze_0");
				if (firstMaze != null)
				{
					int size = _mazeGenerator.GetMazeSize(0);
					Vector2I entrancePos = _mazeGenerator.GetEntrancePosition(size, 0);
					
					_playerBall.GlobalPosition = firstMaze.GlobalPosition + new Vector3(
						entrancePos.X * _mazeGenerator._cellSize,
						1.0f,
						entrancePos.Y * _mazeGenerator._cellSize
					);
				}
				
				_playerBall.EnableControls();
				GD.Print("Contrôles activés après redémarrage");
			}
			
			// Mettre à jour l'interface
			UpdateUI();
		};
	}
	private void LoadHighScores()
	{
		try
		{
			// Utiliser GetNode avec sécurité (vérification null)
			var scoreManager = GetNode<ScoreManager>("/root/ScoreManager");
			if (scoreManager != null)
			{
				// Récupérer les scores
				_highScores = scoreManager.GetHighScores();
				GD.Print("Scores chargés avec succès depuis ScoreManager");
			}
			else
			{
				GD.PrintErr("ScoreManager non trouvé dans l'arbre de scène");
				// Utiliser une liste vide en cas d'échec
				_highScores = new List<ScoreManager.ScoreEntry>();
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Erreur lors du chargement des scores: {e.Message}");
			_highScores = new List<ScoreManager.ScoreEntry>();
		}
	}
	
	private void SaveScore()
	{
		// S'assurer que nous avons au moins un labyrinthe terminé
		int mazesCompleted = Math.Max(1, _mazesCompleted);
		int totalCats = CountTotalCats();
		
		GD.Print($"Sauvegarde du score: Labyrinthes={mazesCompleted}, Chats={totalCats}");
		
		try
		{
			// Vérifier d'abord si ScoreManager existe
			var scoreManager = GetNode<ScoreManager>("/root/ScoreManager");
			
			// S'il n'existe pas, vérifier s'il est en tant que singleton
			if (scoreManager == null)
			{
				scoreManager = ScoreManager.Instance;
			}
			
			// S'il est toujours null, essayer de le trouver de manière plus générale
			if (scoreManager == null)
			{
				scoreManager = GetTree().Root.FindChild("ScoreManager", true, false) as ScoreManager;
			}
			
			// Si on a trouvé le ScoreManager, ajouter le score
			if (scoreManager != null)
			{
				scoreManager.AddScore(mazesCompleted, totalCats);
				GD.Print("Score ajouté avec succès");
			}
			else
			{
				GD.PrintErr("ScoreManager non trouvé, création et ajout du ScoreManager");
				
				// Créer le ScoreManager s'il n'existe pas
				scoreManager = new ScoreManager();
				scoreManager.Name = "ScoreManager";
				GetTree().Root.AddChild(scoreManager);
				
				// Attendre un instant pour que le ScoreManager s'initialise
				var timer = GetTree().CreateTimer(0.1f);
				timer.Timeout += () => {
					scoreManager.AddScore(mazesCompleted, totalCats);
					GD.Print("Score ajouté après création du ScoreManager");
				};
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Erreur lors de la sauvegarde du score: {e.Message}");
		}
		
		// Mettre à jour l'affichage des scores
		UpdateHighScoreDisplay();
	}

	private void UpdateHighScoreDisplay()
	{
		// Charger les scores à jour avec plusieurs tentatives
		try
		{
			// Essayer d'obtenir le ScoreManager de différentes façons
			ScoreManager scoreManager = null;
			
			// Essayer d'abord via GetNode
			scoreManager = GetNode<ScoreManager>("/root/ScoreManager");
			
			// Essayer via le singleton si non trouvé
			if (scoreManager == null)
			{
				scoreManager = ScoreManager.Instance;
			}
			
			// Essayer une recherche plus générale
			if (scoreManager == null)
			{
				scoreManager = GetTree().Root.FindChild("ScoreManager", true, false) as ScoreManager;
			}
			
			if (scoreManager != null)
			{
				// Récupérer les scores
				_highScores = scoreManager.GetHighScores();
				GD.Print("Scores chargés avec succès");
			}
			else
			{
				GD.PrintErr("ScoreManager non trouvé dans l'arbre de scène");
				// Utiliser une liste vide en cas d'échec
				_highScores = new List<ScoreManager.ScoreEntry>();
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Erreur lors du chargement des scores: {e.Message}");
			_highScores = new List<ScoreManager.ScoreEntry>();
		}
		
		// Vérifier que le panneau de scores existe
		if (_highScorePanel == null)
		{
			GD.PrintErr("Le panneau de high scores n'existe pas!");
			return;
		}
		
		// Supprimer les anciens labels de score
		foreach (Node child in _highScorePanel.GetChildren())
		{
			if (child.Name.ToString().StartsWith("ScoreLabel"))
			{
				child.QueueFree();
			}
		}
		
		// Créer les nouveaux labels de score - seulement les 5 premiers
		int maxScores = Math.Min(5, _highScores.Count);
		var scoreSettings = new LabelSettings
		{
			Font = font,
			FontSize = 14,
			FontColor = Colors.White
		};
		
		for (int i = 0; i < maxScores; i++)
		{
			Label scoreLabel = new Label();
			scoreLabel.Name = $"ScoreLabel_{i}";
			scoreLabel.Text = $"{i + 1}. {_highScores[i].ToString()}";
			scoreLabel.Position = new Vector2(20, 40 + (i * 25));
			scoreLabel.Size = new Vector2(260, 25);
			scoreLabel.LabelSettings = scoreSettings;
			_highScorePanel.AddChild(scoreLabel);
		}
		
		// S'il n'y a pas de scores, afficher un message
		if (_highScores.Count == 0)
		{
			Label noScoreLabel = new Label();
			noScoreLabel.Name = "ScoreLabel_None";
			noScoreLabel.Text = "Aucun score enregistré";
			noScoreLabel.Position = new Vector2(20, 80);
			noScoreLabel.Size = new Vector2(260, 25);
			noScoreLabel.HorizontalAlignment = HorizontalAlignment.Center;
			noScoreLabel.LabelSettings = scoreSettings;
			_highScorePanel.AddChild(noScoreLabel);
		}
		
		GD.Print($"Affichage des high scores mis à jour avec {_highScores.Count} scores");
	}

	private void InitializeCanvasLayers()
	{
		// Canvas pour l'UI du jeu (score, temps, etc.)
		_uiCanvas = new CanvasLayer();
		_uiCanvas.Name = "UICanvas";
		_uiCanvas.Layer = 1;
		AddChild(_uiCanvas);
		
		// Canvas pour l'effet de flou
		_blurCanvas = new CanvasLayer();
		_blurCanvas.Name = "BlurCanvas";
		_blurCanvas.Layer = 2;
		AddChild(_blurCanvas);
		
		// Canvas pour les messages centraux
		_messageCanvas = new CanvasLayer();
		_messageCanvas.Name = "MessageCanvas";
		_messageCanvas.Layer = 3;
		AddChild(_messageCanvas);
		
		// Canvas pour les highscores
		_scoreCanvas = new CanvasLayer();
		_scoreCanvas.Name = "ScoreCanvas";
		_scoreCanvas.Layer = 4;
		AddChild(_scoreCanvas);
	}
	
	private void ConfigureBlurredBackground()
	{
		// 1. Créer d'abord l'overlay bleu
		_blueOverlay = new ColorRect();
		_blueOverlay.Name = "BlueOverlay";
		_blueOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_blueOverlay.Color = new Color(0.0f, 0.2f, 0.6f, 0.4f); // Bleu semi-transparent
		_blurCanvas.AddChild(_blueOverlay);
		
		if (Viewport3D != null && BlurShaderMaterial != null)
		{
			// 2. Ensuite, configurer le TextureRect avec le shader de flou
			ViewportTexture viewportTexture = null;
			
			// Vérifier si BlurredDisplay existe déjà
			if (BlurredDisplay == null)
			{
				viewportTexture = new ViewportTexture();
				viewportTexture.ViewportPath = Viewport3D.GetPath();
				
				BlurredDisplay = new TextureRect();
				BlurredDisplay.Name = "BlurredDisplay";
				BlurredDisplay.Material = BlurShaderMaterial;
				_blurCanvas.AddChild(BlurredDisplay);
			}
			else
			{
				// Si BlurredDisplay existe, mettre à jour sa texture
				viewportTexture = new ViewportTexture();
				viewportTexture.ViewportPath = Viewport3D.GetPath();
			}
			
			// Configurer le TextureRect
			BlurredDisplay.Texture = viewportTexture;
			BlurredDisplay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			BlurredDisplay.MouseFilter = Control.MouseFilterEnum.Ignore;
			
			// S'assurer que le shader est bien configuré
			if (BlurShaderMaterial != null)
			{
				// Augmenter l'intensité du flou
				BlurShaderMaterial.SetShaderParameter("blur_strength", 6.0f);
			}
		}
	}
	
	private void CreateHighScorePanel()
	{
		// Créer un panneau pour les high scores
		_highScorePanel = new Panel();
		_highScorePanel.Name = "HighScorePanel";
		_highScorePanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_highScorePanel.Size = new Vector2(300, 180);
		_highScorePanel.Position = new Vector2(-300, 240); // Sous le panneau UI principal
		_scoreCanvas.AddChild(_highScorePanel);
		
		// Titre du panneau
		Label titleLabel = new Label();
		titleLabel.Text = "MEILLEURS SCORES";
		titleLabel.Position = new Vector2(10, 10);
		titleLabel.Size = new Vector2(280, 30);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		
		var titleSettings = new LabelSettings
		{
			Font = font,
			FontSize = 18,
			FontColor = new Color(1, 1, 0) // Jaune
		};
		
		titleLabel.LabelSettings = titleSettings;
		_highScorePanel.AddChild(titleLabel);
		
		// Mettre à jour les scores affichés
		UpdateHighScoreDisplay();
	}
	
	
	private async void SimulateCalibration()
	{
		_gameState = GameState.Calibrating;
		GD.Print("État: Calibrating");
		
		// Afficher le message de calibrage
		AddCenteredMessage("Manette en cours de calibrage, veuillez patienter", Colors.White, 32);
		
		// Animation des points de chargement
		_loadingDotsLabel = new Label();
		_loadingDotsLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_loadingDotsLabel.VerticalAlignment = VerticalAlignment.Center;
		
		var labelSettings = new LabelSettings {
			Font = font,
			FontSize = 24,
			FontColor = Colors.White
		};
		
		_loadingDotsLabel.LabelSettings = labelSettings;
		_loadingDotsLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
		_loadingDotsLabel.Position = new Vector2(0, 50);
		_messageCanvas.AddChild(_loadingDotsLabel);
		
		// Animation des points
		for (int i = 0; i < 5; i++)
		{
			_loadingDotsLabel.Text = ".";
			await ToSignal(GetTree().CreateTimer(0.3), "timeout");
			_loadingDotsLabel.Text = "..";
			await ToSignal(GetTree().CreateTimer(0.3), "timeout");
			_loadingDotsLabel.Text = "...";
			await ToSignal(GetTree().CreateTimer(0.3), "timeout");
		}
		
		// Passer à l'écran d'attente
		_gameState = GameState.WaitingToStart;
		GD.Print("État: WaitingToStart");
		
		// Nettoyer l'écran et afficher le nouveau message
		if (_loadingDotsLabel != null)
			_loadingDotsLabel.QueueFree();
		
		// Afficher le message de démarrage
		AddCenteredMessage("APPUYEZ SUR LE BOUTON ARDUINO POUR COMMENCER", new Color(1, 1, 0), 36);
	}
	
	private void AddCenteredMessage(string text, Color color, int fontSize = 32)
	{
		// Nettoyer l'ancien message s'il existe
		if (_centerMessageLabel != null)
			_centerMessageLabel.QueueFree();
		
		// Créer un nouveau label
		_centerMessageLabel = new Label();
		_centerMessageLabel.Text = text;
		
		// Configurer l'apparence
		var labelSettings = new LabelSettings {
			Font = font,
			FontSize = fontSize,
			FontColor = color
		};
		
		_centerMessageLabel.LabelSettings = labelSettings;
		
		// Centrer le texte
		_centerMessageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_centerMessageLabel.VerticalAlignment = VerticalAlignment.Center;
		
		// Configurer l'ancrage et la taille
		_centerMessageLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
		_centerMessageLabel.Size = new Vector2(800, 100);
		_centerMessageLabel.Position = new Vector2(-400, 0);
		
		// Ajouter au canvas des messages
		_messageCanvas.AddChild(_centerMessageLabel);
	}
	
	private void CreateUI()
	{
		// Créer un panneau pour l'UI
		_uiPanel = new Panel();
		_uiPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_uiPanel.Size = new Vector2(300, 220);
		_uiPanel.Position = new Vector2(-300, 0);
		_uiCanvas.AddChild(_uiPanel);
		
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
		
		// Ajouter une étiquette pour le score
		_scoreLabel = new Label();
		_scoreLabel.Text = "Chats: 0 | Score: 0";
		_scoreLabel.Position = new Vector2(10, 150);
		_scoreLabel.Size = new Vector2(280, 30);
		_uiPanel.AddChild(_scoreLabel);
		
		// Mettre à jour les étiquettes
		UpdateUI();
	}

	
	public override void _Process(double delta)
	{
		// Mettre à jour le timer de cooldown du bouton
		if (_buttonCooldownTimer > 0)
		{
			_buttonCooldownTimer -= (float)delta;
		}
		
		// CRUCIAL: Vérification directe du bouton Arduino avec amélioration de la logique
		if (ArduinoManager.Instance != null && _buttonCooldownTimer <= 0)
		{
			// Stocker l'état actuel pour ne réagir qu'aux changements
			bool buttonPressed = ArduinoManager.Instance.IsButtonJustPressed();
			
			if (buttonPressed)
			{
				GD.Print("DÉTECTION BOUTON CONFIRMÉE DANS PROCESS: " + _gameState);
				HandleButtonPress();
				_buttonCooldownTimer = BUTTON_COOLDOWN;
				
				// Forcer la réinitialisation complète de l'état du bouton après traitement
				ArduinoManager.Instance.ForceResetButtonState();
			}
		}
		
		// AJOUT: Touche Espace pour simuler le bouton (pour les tests)
		if (Input.IsKeyPressed(Key.Space) && _buttonCooldownTimer <= 0)
		{
			GD.Print("ESPACE PRESSÉ - SIMULATION BOUTON");
			HandleButtonPress();
			_buttonCooldownTimer = BUTTON_COOLDOWN;
		}
		
		// AJOUT: Touche F1 pour démarrer le jeu en cas d'urgence
		if (Input.IsKeyPressed(Key.F1) && _gameState == GameState.WaitingToStart)
		{
			GD.Print("TOUCHE F1 DÉTECTÉE - DÉMARRAGE FORCÉ DU JEU");
			StartGame();
		}
		
		// Ajouter un reset d'urgence avec la touche Echap ou Retour Arrière
		if (Input.IsActionJustPressed("ui_cancel") || Input.IsKeyPressed(Key.Backspace))
		{
			EmergencyReset();
			return;
		}
		
		// OPTION DE SECOURS: Touche P pour la pause/reprise
		if (Input.IsKeyPressed(Key.P) && _buttonCooldownTimer <= 0)
		{
			if (_gameState == GameState.Playing)
			{
				PauseGame();
				_buttonCooldownTimer = BUTTON_COOLDOWN;
			}
			else if (_gameState == GameState.Paused)
			{
				ResumeGame();
				_buttonCooldownTimer = BUTTON_COOLDOWN;
			}
		}
		
		// OPTION DE SECOURS: Touche R pour le redémarrage
		if (Input.IsKeyPressed(Key.R) && (_gameState == GameState.GameOver || 
										_gameState == GameState.GameOverFinal ||
										_gameState == GameState.CameraTravel) && 
			_buttonCooldownTimer <= 0)
		{
			_isRestarting = true;
			RestartGame();
			_buttonCooldownTimer = BUTTON_COOLDOWN;
		}
		
		// Si le jeu n'est pas en cours, ne pas mettre à jour le temps
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
	
	
	// Démarrer le jeu
	private void StartGame()
	{
		_gameState = GameState.Playing;
		
		// AJOUT: Démarrer le son d'ambiance du jeu
		StartAmbianceSound();
		
		// Masquer le fond flou et l'overlay bleu
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = false;
		if (_blueOverlay != null)
			_blueOverlay.Visible = false;
		
		// Masquer le message central
		if (_centerMessageLabel != null)
			_centerMessageLabel.Visible = false;
		
		// Activer les contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.EnableControls();
		}
		
		// Mettre à jour l'interface
		UpdateUI();
		
		GD.Print("Jeu démarré - État: Playing");
	}
	// Nouvelle méthode pour démarrer le son d'ambiance
	private void StartAmbianceSound()
	{
		if (_ambianceSoundPlaying) return;
		
		if (AudioManager.Instance != null) {
			AudioManager.Instance.PlaySound("Ambiance");
			_ambianceSoundPlaying = true;
			GD.Print("Son d'ambiance démarré");
		}
		
		// S'assurer que la musique de pause est arrêtée
		StopPauseMusic();
	}


	// Mettre le jeu en pause
	private void PauseGame()
	{
		_gameState = GameState.Paused;
		
		// Sauvegarder le temps actuel
		_savedTime = _remainingTime;
		
		// Désactiver les contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		// AJOUT: Démarrer la musique de pause
		StartPauseMusic();

		// Afficher l'overlay bleu et le fond flou
		if (_blueOverlay != null)
		{
			_blueOverlay.Visible = true;
			_blueOverlay.Color = new Color(0.0f, 0.2f, 0.6f, 0.4f); // Bleu pour la pause
		}
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = true;
		
		// Afficher le message de pause
		AddCenteredMessage("Pause en cours...", new Color(0, 1, 1), 36); // Cyan
		
		GD.Print("Jeu en pause - État: Paused");
	}
	
	private void StartPauseMusic()
	{
		if (_pauseMusicPlaying) return;
		
		if (AudioManager.Instance != null) {
			AudioManager.Instance.PlaySound("PauseMusic");
			_pauseMusicPlaying = true;
			GD.Print("Musique de pause démarrée");
		}
	}


	// Reprendre le jeu
	private void ResumeGame()
	{
		_gameState = GameState.Playing;
		
		// Restaurer le temps
		_remainingTime = _savedTime;
		StopPauseMusic();
		StartAmbianceSound();
		// Masquer le fond flou et l'overlay bleu
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = false;
		if (_blueOverlay != null)
			_blueOverlay.Visible = false;
		
		// Masquer le message central
		if (_centerMessageLabel != null)
			_centerMessageLabel.Visible = false;
		
		// Activer les contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.EnableControls();
		}
		
		GD.Print("Jeu repris - État: Playing");
	}
	
	// Nouvelle méthode pour arrêter la musique de pause
	private void StopPauseMusic()
	{
		if (!_pauseMusicPlaying) return;
		
		// Arrêter la musique de pause (dépend de l'implémentation FMOD)
		_pauseMusicPlaying = false;
		GD.Print("Musique de pause arrêtée");
		
		// Reprendre le son d'ambiance si nécessaire
	}

	// Mise à jour générale de l'interface utilisateur
	private void UpdateUI()
	{
		// Mettre à jour le label de temps
		UpdateTimeLabel();
		
		// Mettre à jour le label de score
		UpdateScoreLabel();
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
		var timer = new Godot.Timer();
		AddChild(timer);
		timer.WaitTime = 2.0f; // 2 secondes
		timer.OneShot = true;
		timer.Timeout += () => {
			_catEffectLabel.Visible = false;
			timer.QueueFree();
		};
		timer.Start();
	}
	
	// Démarrer le travelling de caméra pour montrer tous les labyrinthes
	private void StartCameraTraveling()
	{
		_gameState = GameState.CameraTravel;
		GD.Print("État: CameraTravel - Démarrage du travelling de caméra");
		
		// Masquer temporairement le message
		if (_centerMessageLabel != null)
			_centerMessageLabel.Visible = false;
		
		// Masquer temporairement le fond flou et l'overlay
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = false;
		if (_blueOverlay != null)
			_blueOverlay.Visible = false;
		
		// Créer un texte pour afficher les statistiques
		var statsLabel = new Label();
		statsLabel.Text = $"Labyrinthes complétés: {_mazesCompleted}\nChats collectés: {CountTotalCats()}";
		
		var labelSettings = new LabelSettings {
			Font = font,
			FontSize = 32,
			FontColor = Colors.White
		};
		
		statsLabel.LabelSettings = labelSettings;
		statsLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statsLabel.VerticalAlignment = VerticalAlignment.Top;
		statsLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		statsLabel.Position = new Vector2(0, 50);
		_messageCanvas.AddChild(statsLabel);
		
		// CORRECTION: Créer ou récupérer la caméra globale si elle n'existe pas déjà
		if (_globalCamera == null)
		{
			// Vérifier d'abord si elle existe déjà dans l'arbre
			_globalCamera = GetNodeOrNull<Camera3D>("GlobalCamera");
			
			// Sinon, la créer
			if (_globalCamera == null)
			{
				_globalCamera = new Camera3D();
				_globalCamera.Name = "GlobalCamera";
				AddChild(_globalCamera);
				GD.Print("Caméra globale créée pour le travelling");
			}
		}
		
		// Lancer le mouvement de caméra
		if (_globalCamera != null)
		{
			// Activer la caméra globale
			_globalCamera.Current = true;
			
			// Démarrer le travelling
			StartCameraTravelingAnimation(_globalCamera, statsLabel);
		}
		else
		{
			// Si la caméra n'est pas disponible, passer directement à l'écran de game over
			ShowFinalGameOver(statsLabel);
		}
	}

	// Animation du travelling de caméra
	private void StartCameraTravelingAnimation(Camera3D camera, Label statsLabel)
	{
		if (_mazeGenerator == null) return;
		
		// Annuler tout tween existant
		if (_cameraTween != null && _cameraTween.IsValid())
		{
			_cameraTween.Kill();
		}
		
		// CORRECTION: Utiliser le dernier labyrinthe réellement complété, pas juste l'index courant
		int lastMazeIndex = Math.Max(_currentMazeIndex, _mazesCompleted - 1);
		if (lastMazeIndex < 0) lastMazeIndex = 0;
		
		GD.Print($"Démarrage du travelling de la caméra pour montrer les labyrinthes 0 à {lastMazeIndex}");
		
		// Trouver les nœuds des labyrinthes
		Node3D lastMaze = GetNodeOrNull<Node3D>($"MazeGenerator/Maze_{lastMazeIndex}");
		Node3D firstMaze = GetNodeOrNull<Node3D>("MazeGenerator/Maze_0");
		
		if (lastMaze == null || firstMaze == null)
		{
			GD.PrintErr($"Impossible de trouver les labyrinthes pour le travelling (premier: {firstMaze != null}, dernier: {lastMaze != null})");
			ShowFinalGameOver(statsLabel);
			return;
		}
		
		// Obtenir les tailles des labyrinthes
		int lastMazeSize = _mazeGenerator.GetMazeSize(lastMazeIndex);
		int firstMazeSize = _mazeGenerator.GetMazeSize(0);
		
		// Calculer les centres des labyrinthes
		Vector3 lastMazeCenter = lastMaze.GlobalPosition + new Vector3(
			(lastMazeSize * _mazeGenerator._cellSize) / 2,
			0,
			(lastMazeSize * _mazeGenerator._cellSize) / 2
		);
		
		Vector3 firstMazeCenter = firstMaze.GlobalPosition + new Vector3(
			(firstMazeSize * _mazeGenerator._cellSize) / 2,
			0,
			(firstMazeSize * _mazeGenerator._cellSize) / 2
		);
		
		// Calculer une hauteur suffisante pour voir tous les labyrinthes
		float totalSpan = (lastMaze.GlobalPosition.X + lastMazeSize * _mazeGenerator._cellSize) - firstMaze.GlobalPosition.X;
		float maxSize = Math.Max(lastMazeSize, firstMazeSize);
		float cameraHeight = Math.Max(totalSpan * 0.5f, maxSize * _mazeGenerator._cellSize * 0.8f);
		
		GD.Print($"Distance totale des labyrinthes: {totalSpan}, hauteur de caméra: {cameraHeight}");
		
		// Position de départ de la caméra (au-dessus du dernier labyrinthe)
		Vector3 startPosition = lastMazeCenter + new Vector3(0, cameraHeight * 0.5f, 0);
		camera.GlobalPosition = startPosition;
		
		// S'assurer que la caméra regarde vers le bas avec un angle pour éviter les vecteurs colinéaires
		camera.LookAt(lastMazeCenter + new Vector3(0.01f, 0, 0.01f), Vector3.Up);
		
		// Position finale (vue d'ensemble)
		Vector3 midPoint = new Vector3(
			(firstMazeCenter.X + lastMazeCenter.X) / 2,
			0,
			(firstMazeCenter.Z + lastMazeCenter.Z) / 2
		);
		
		Vector3 endPosition = midPoint + new Vector3(0, cameraHeight, totalSpan * 0.15f);
		
		// CORRECTION: Ajuster la position finale pour garantir que tous les labyrinthes sont visibles
		float adjustedHeight = cameraHeight;
		if (_mazesCompleted > 2)
		{
			// Augmenter la hauteur proportionnellement au nombre de labyrinthes
			adjustedHeight = cameraHeight * (1.0f + (_mazesCompleted * 0.1f));
			endPosition.Y = adjustedHeight;
			
			// Ajuster aussi le recul de la caméra
			endPosition.Z = totalSpan * (0.15f + (_mazesCompleted * 0.02f));
		}
		
		GD.Print($"Position de départ: {startPosition}, Position finale: {endPosition}");
		
		// Créer un nouveau tween
		_cameraTween = CreateTween();
		_cameraTween.SetEase(Tween.EaseType.InOut);
		_cameraTween.SetTrans(Tween.TransitionType.Cubic);
		
		// Animation de la position (prolongée à 7 secondes pour mieux apprécier)
		_cameraTween.TweenProperty(camera, "global_position", endPosition, 7.0f);
		
		// Faire pivoter la caméra pendant le mouvement pour garder la vue sur les labyrinthes
		_cameraTween.Parallel().TweenMethod(
			Callable.From((float t) => {
				// Ajouter un petit décalage pour éviter les vecteurs colinéaires
				Vector3 lookAtPos = lastMazeCenter.Lerp(midPoint, t) + new Vector3(0.01f, 0, 0.01f);
				camera.LookAt(lookAtPos, Vector3.Up);
			}),
			0.0f, 1.0f, 7.0f
		);
		
		// À la fin de l'animation, montrer le message final
		_cameraTween.TweenCallback(Callable.From(() => {
			ShowFinalGameOver(statsLabel);
		}));
	}
	
	// Afficher le message final après le travelling de caméra
	private void ShowFinalGameOver(Label statsLabel)
	{
		_gameState = GameState.GameOverFinal;
		GD.Print("État: GameOverFinal");
		
		// Supprimer le label de statistiques
		if (statsLabel != null)
			statsLabel.QueueFree();
		
		// Désactiver la caméra globale
		if (_globalCamera != null)
		{
			_globalCamera.Current = false;
		}
		
		// Activer la caméra du joueur
		if (_playerBall != null && _playerBall.FindChild("Camera3D") is Camera3D playerCamera)
		{
			playerCamera.Current = true;
		}
		
		// Réafficher le fond flou et l'overlay rouge
		if (_blueOverlay != null)
		{
			_blueOverlay.Visible = true;
			_blueOverlay.Color = new Color(0.6f, 0.0f, 0.0f, 0.4f); // Rouge pour game over
		}
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = true;
		
		// Afficher le message final avec les statistiques
		AddCenteredMessage(
			$"VOUS N'AVEZ PLUS DE LAINE...\n\nLabyrinthes: {_mazesCompleted} | Chats: {CountTotalCats()}\n\nAPPUYEZ SUR LE BOUTON POUR RECOMMENCER", 
			new Color(1, 1, 1), // Blanc pour une meilleure visibilité
			24
		);
	}
	
	// Compter le total des chats collectés
	private int CountTotalCats()
	{
		int total = 0;
		foreach (var count in _catsCollected.Values)
		{
			total += count;
		}
		return total;
	}
	
	// Jouer un son de game over
	private void PlayGameOverSound()
	{
		if (AudioManager.Instance != null) {
			AudioManager.Instance.PlaySound("GameOver");
			GD.Print("Son GameOver joué");
		}
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
				// Auparavant : _mazesCompleted = newMazeIndex;
				_mazesCompleted += 1; // Incrémente de 1 à chaque nouveau labyrinthe traversé
				
				UpdateScoreLabel();
				GD.Print($"Transition vers le labyrinthe {newMazeIndex}, total complétés: {_mazesCompleted}");
			}
			
			_currentMazeIndex = newMazeIndex;
			UpdateMazeCounter(newMazeIndex);
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
	
	private void UpdateMazeCounter(int mazeIndex)
	{
		if (_mazeCountLabel != null)
		{
			// Afficher "infini" pour le nombre total de labyrinthes
			_mazeCountLabel.Text = $"Labyrinthe: {mazeIndex + 1} / ∞";
		}
	}
	
	// Modifier la méthode HandleButtonPress pour être plus robuste
	private void HandleButtonPress()
	{
		// Afficher un message de débogage TRÈS VISIBLE
		GD.Print("!!! BOUTON DÉTECTÉ !!! État du jeu: " + _gameState);
		
		switch (_gameState)
		{
			case GameState.Calibrating:
				// Ignorer pendant le calibrage
				GD.Print("Bouton ignoré pendant le calibrage");
				break;
				
			case GameState.WaitingToStart:
				// Démarrer le jeu
				GD.Print("!!! DÉMARRAGE DU JEU !!!");
				StartGame();
				break;
				
			case GameState.Playing:
				// Mettre le jeu en pause
				GD.Print("Mise en pause du jeu...");
				PauseGame();
				break;
				
			case GameState.Paused:
				// Reprendre le jeu
				GD.Print("Reprise du jeu...");
				ResumeGame();
				break;
				
			case GameState.GameOver:
			case GameState.ShowingAllMazes:
			case GameState.GameOverFinal:
			case GameState.Victory:
			case GameState.CameraTravel:
				// Redémarrer le jeu
				GD.Print("Redémarrage du jeu...");
				// Définir _isRestarting à true pour sauter l'écran de calibrage
				_isRestarting = true;
				// NOUVEAU: Réinitialiser l'état du bouton avant de redémarrer
				if (ArduinoManager.Instance != null)
				{
					ArduinoManager.Instance.ResetButtonState();
				}
				RestartGame();
				break;
		}
		
		// Réinitialiser l'état du bouton explicitement
		if (ArduinoManager.Instance != null)
		{
			ArduinoManager.Instance.ForceResetButtonState();
		}
	}
	
	private void EmergencyReset()
	{
		GD.Print("RESET D'URGENCE ACTIVÉ!");
		
		// Forcer une réinitialisation complète
		if (ArduinoManager.Instance != null)
		{
			ArduinoManager.Instance.ResetButtonState();
		}
		
		// Définir _isRestarting à true
		_isRestarting = true;
		
		// Recharger la scène immédiatement
		GetTree().ReloadCurrentScene();
	}
	
	
	private void RestartGame()
	{
		GD.Print("Démarrage du processus de redémarrage du jeu...");
		
		// Désactiver les contrôles du joueur immédiatement
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		
		// Définir le flag de redémarrage pour éviter l'écran de calibration au prochain chargement
		_isRestarting = true;
		
		// NOUVEAU: Réinitialiser l'état du bouton avant de redémarrer
		if (ArduinoManager.Instance != null)
		{
			ArduinoManager.Instance.ResetButtonState();
		}
		
		// IMPORTANT: NE PAS FERMER LE PORT ARDUINO
		// Supprimer ces lignes:
		// if (ArduinoManager.Instance != null)
		// {
		//     ArduinoManager.Instance.ForceClosePort();
		//     ArduinoManager.ResetPortState();
		//     GD.Print("Port Arduino correctement fermé avant rechargement");
		// }
		
		// Au lieu de tous les bricolages compliqués, on recharge simplement la scène complète
		// Mais avec un délai plus court pour être plus réactif
		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += () => 
		{
			// NOUVEAU: Ceci est très important - on force la libération du bouton
			if (ArduinoManager.Instance != null)
			{
				ArduinoManager.Instance._buttonPressed = false;
				ArduinoManager.Instance._buttonJustPressed = false;
				ArduinoManager.Instance._buttonJustReleased = false;
				ArduinoManager.Instance._buttonEventProcessed = true;
			}
			
			// Recharger la scène entière
			GetTree().ReloadCurrentScene();
		};
	}

	// Modifier ReinitializeGame pour s'assurer que le nouvel ArduinoManager est utilisé
	private void ReinitializeGame()
	{
		GD.Print("Réinitialisation du jeu sans rechargement de scène...");
		
		// Recréer le générateur de labyrinthe
		var mazeGenerator = new VerticalMazeGenerator();
		mazeGenerator.Name = "MazeGenerator";
		AddChild(mazeGenerator);
		_mazeGenerator = mazeGenerator;
		
		// Attendre que les labyrinthes soient générés
		var timer = GetTree().CreateTimer(1.0f);
		timer.Timeout += () => {
			// Instancier le joueur
			SpawnPlayerBall();
			
			// Passer directement à l'état Playing si on est en mode redémarrage
			if (_isRestarting)
			{
				_gameState = GameState.Playing;
				
				// Masquer l'interface
				if (_centerMessageLabel != null)
					_centerMessageLabel.Visible = false;
				if (BlurredDisplay != null)
					BlurredDisplay.Visible = false;
				if (_blueOverlay != null)
					_blueOverlay.Visible = false;
					
				// CRITIQUE: Forcer explicitement le passage de l'ArduinoManager au PlayerBall
				// après un délai pour s'assurer que tout est initialisé
				var controlsTimer = GetTree().CreateTimer(0.5f);
				controlsTimer.Timeout += () => {
					if (_playerBall != null && _arduinoManager != null)
					{
						// Forcer la connexion
						_playerBall.SetArduinoManager(_arduinoManager);
						_playerBall.EnableControls();
						GD.Print("CRITIQUE: Connexion forcée au nouvel ArduinoManager!");
					}
				};
			}
			else
			{
				_gameState = GameState.WaitingToStart;
				AddCenteredMessage("APPUYEZ SUR LE BOUTON ARDUINO POUR COMMENCER", new Color(1, 1, 0), 36);
			}
			
			GD.Print($"Jeu réinitialisé et prêt à démarrer, état: {_gameState}");
		};
	}

	// Dans SpawnPlayerBall() - Mise à jour pour garantir la connexion avec l'ArduinoManager
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
		
		// IMPORTANT: S'assurer que _arduinoManager est valide
		if (_arduinoManager == null)
		{
			_arduinoManager = ArduinoManager.Instance;
			if (_arduinoManager == null)
			{
				_arduinoManager = GetTree().Root.FindChild("ArduinoManager", true, false) as ArduinoManager;
				if (_arduinoManager == null)
				{
					GD.PrintErr("CRITIQUE: Aucun ArduinoManager trouvé! Création d'une nouvelle instance...");
					_arduinoManager = new ArduinoManager();
					_arduinoManager.Name = "ArduinoManager";
					GetTree().Root.AddChild(_arduinoManager);
				}
			}
		}
		
		// Assigner la référence de l'ArduinoManager au PlayerBall avant de l'ajouter
		_playerBall.SetArduinoManager(_arduinoManager);
		GD.Print($"ArduinoManager assigné au PlayerBall: {_arduinoManager != null}");
		
		AddChild(_playerBall);
		GD.Print("PlayerBall instancié avec succès");
		
		// Trouver le premier labyrinthe
		var firstMaze = GetNodeOrNull<Node3D>("MazeGenerator/Maze_0");
		if (firstMaze == null && _mazeGenerator != null)
		{
			firstMaze = _mazeGenerator.FindChild("Maze_0", true, false) as Node3D;
		}
		
		if (firstMaze == null)
		{
			GD.PrintErr("Premier labyrinthe non trouvé! Utilisation d'une position par défaut");
			_playerBall.GlobalPosition = new Vector3(2, 1.0f, 0);
		}
		else
		{
			// Obtenir la position d'entrée du premier labyrinthe
			int size = _mazeGenerator.GetMazeSize(0);
			Vector2I entrancePos = _mazeGenerator.GetEntrancePosition(size, 0);
			
			// Positionner le joueur à l'entrée du premier labyrinthe
			_playerBall.GlobalPosition = firstMaze.GlobalPosition + new Vector3(
				entrancePos.X * _mazeGenerator._cellSize,
				1.0f, // Un peu au-dessus du sol
				entrancePos.Y * _mazeGenerator._cellSize
			);
			
			GD.Print("Joueur placé à la position: " + _playerBall.GlobalPosition);
		}
		
		// Au début, désactiver les contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
	}


	// Remplacer la méthode GameOver pour appeler SaveScore avant le travelling de caméra
	private void GameOver()
	{
		if (_gameState == GameState.GameOver || 
			_gameState == GameState.ShowingAllMazes || 
			_gameState == GameState.GameOverFinal ||
			_gameState == GameState.CameraTravel) return;
		
		_gameState = GameState.GameOver;
		_timeOver = true;
		
		// Désactiver les contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		// AJOUT: Arrêter le son d'ambiance
		StopAllSounds();

		// Sauvegarder le score
		SaveScore();
		
		// Afficher le fond flou et le message de fin avec un overlay rouge
		if (_blueOverlay != null)
		{
			_blueOverlay.Visible = true;
			_blueOverlay.Color = new Color(0.6f, 0.0f, 0.0f, 0.4f); // Rouge pour game over
		}
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = true;
		
		AddCenteredMessage("VOUS N'AVEZ PLUS DE LAINE...", new Color(1, 1, 1), 36); // Blanc pour meilleure visibilité
		
		// Jouer un son de game over
		PlayGameOverSound();
		
		// Démarrer le traveling de caméra après un court délai
		var timer = new Godot.Timer();
		timer.WaitTime = 1.0f;
		timer.OneShot = true;
		timer.Timeout += () => StartCameraTraveling();
		AddChild(timer);
		timer.Start();
		
		GD.Print("Game Over - Plus de temps! État: GameOver");
	}
	
	private void StopAllSounds()
	{
		// Arrêter le son d'ambiance
		_ambianceSoundPlaying = false;
		
		// Arrêter la musique de pause
		_pauseMusicPlaying = false;
		
		// Arrêter tous les sons
		if (AudioManager.Instance != null) {
			AudioManager.Instance.StopAllSounds();
		}
		
		GD.Print("Tous les sons arrêtés");
	}

	private void StartGameAfterRestart()
	{
		// Définir l'état du jeu
		_gameState = GameState.Playing;
		
		// Masquer le fond flou et l'overlay
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = false;
		if (_blueOverlay != null)
			_blueOverlay.Visible = false;
		
		// Masquer tout message central
		if (_centerMessageLabel != null)
			_centerMessageLabel.Visible = false;
		
		// Ajouter un petit délai pour s'assurer que tout est prêt
		var timer = GetTree().CreateTimer(0.2f);
		timer.Timeout += () => {
			// Appel de EnablePlayerControls qui contient déjà un délai
			EnablePlayerControls();
			GD.Print("StartGameAfterRestart terminé");
		};
	}

	// Modifier les chemins audio dans toutes les méthodes nécessaires
	private void PlayTeleportSound(Node3D target)
	{
		var audioPlayer = new AudioStreamPlayer3D();
		target.AddChild(audioPlayer);
		
		// Charger le son de téléportation (CORRIGÉ)
		audioPlayer.Stream = ResourceLoader.Load<AudioStream>("res://assets/audio/bruit_teleporteur.wav");
		
		// Configuration du son
		audioPlayer.VolumeDb = 5.0f; // Volume
		audioPlayer.MaxDistance = 100.0f;
		audioPlayer.Autoplay = true;
		
		// Supprimer le lecteur une fois le son terminé
		audioPlayer.Finished += () => audioPlayer.QueueFree();
	}

	// Méthode modifiée pour activer les contrôles du joueur
	private void EnablePlayerControls()
	{
		// Rechercher le joueur si la référence est perdue
		if (_playerBall == null)
		{
			_playerBall = GetTree().Root.FindChild("PlayerBall", true, false) as PlayerBall;
			GD.Print("Recherche du PlayerBall après redémarrage...");
		}
		
		// Rechercher l'ArduinoManager si nécessaire
		if (_arduinoManager == null)
		{
			_arduinoManager = ArduinoManager.Instance;
			if (_arduinoManager == null)
			{
				_arduinoManager = GetTree().Root.FindChild("ArduinoManager", true, false) as ArduinoManager;
			}
			GD.Print("Recherche de l'ArduinoManager après redémarrage...");
		}
		
		// S'assurer que l'Arduino est bien configuré
		if (_arduinoManager != null && _playerBall != null)
		{
			_playerBall.SetArduinoManager(_arduinoManager);
			GD.Print("ArduinoManager reconnecté au PlayerBall");
		}
		else
		{
			GD.PrintErr("ERREUR: ArduinoManager ou PlayerBall null dans EnablePlayerControls!");
		}
		
		// Activer les contrôles du joueur après un court délai pour s'assurer que tout est prêt
		if (_playerBall != null)
		{
			var timer = GetTree().CreateTimer(0.5f);
			timer.Timeout += () => {
				_playerBall.ResetState();
				_playerBall.EnableControls();
				_playerBall.ForceReconnectArduino();
				GD.Print("Contrôles activés après redémarrage avec délai");
			};
		}
		else
		{
			GD.PrintErr("ERREUR CRITIQUE: PlayerBall non trouvé lors de l'activation des contrôles!");
		}
	}
}
