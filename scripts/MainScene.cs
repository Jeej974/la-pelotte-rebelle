using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;

/**
 * MainScene - Contrôleur principal du jeu
 * 
 * Gère le flux du jeu, l'interface utilisateur, et coordonne les interactions
 * entre les différents systèmes (labyrinthe, joueur, audio, scores, etc.).
 * Contrôle les transitions d'états et la logique globale de progression.
 */
public partial class MainScene : Node3D
{
	[Export]
	private PackedScene _playerBallScene;
	
	// Références aux différents composants du jeu
	private VerticalMazeGenerator _mazeGenerator;
	private ArduinoManager _arduinoManager;
	private PlayerBall _playerBall;
	
	// Ressources partagées
	private Font font = GD.Load<Font>("res://fonts/PoetsenOne-Regular.ttf");
	
	// Éléments d'interface utilisateur
	private Label _infoLabel;
	private Label _mazeCountLabel;
	private Label _timeLabel;
	private Label _catEffectLabel;
	private Label _scoreLabel;
	private Panel _uiPanel;
	private Label _centerMessageLabel;
	private Label _loadingDotsLabel;
	private Panel _highScorePanel;
	
	// États sonores pour le suivi des transitions
	private bool _ambianceSoundPlaying = false;
	private bool _pauseMusicPlaying = false;

	// État du jeu
	private int _currentMazeIndex = 0;
	private bool _gameCompleted = false;
	
	// Gestion du temps
	[Export]
	private float _initialGameTime = 60.0f;
	private float _remainingTime;
	private float _savedTime;
	private bool _timeOver = false;
	
	// Flags de session
	private static bool _isFirstStart = true;
	private static bool _isRestarting = false;
	
	// Énumération des états possibles du jeu
	private enum GameState
	{
		Calibrating,        // Calibrage initial de l'Arduino
		WaitingToStart,     // Écran d'attente, prêt à démarrer
		Playing,            // Jeu en cours
		Paused,             // Jeu en pause
		GameOver,           // Fin de partie initiale
		ShowingAllMazes,    // Affichage des labyrinthes complétés
		GameOverFinal,      // Écran de fin final
		Victory,            // Victoire (non utilisé actuellement)
		CameraTravel        // Déplacement de caméra cinématique
	}
	
	private GameState _gameState = GameState.Calibrating;
	
	// Suivi du score
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
	
	// Références aux éléments de l'interface
	[Export] public SubViewport Viewport3D;
	[Export] public TextureRect BlurredDisplay;
	[Export] public ShaderMaterial BlurShaderMaterial;
	
	// Canvas layers pour organisation de l'UI
	private CanvasLayer _uiCanvas;
	private CanvasLayer _blurCanvas;
	private CanvasLayer _messageCanvas;
	private CanvasLayer _scoreCanvas;
	private ColorRect _blueOverlay;
	
	// Contrôle des interactions
	private float _buttonCooldownTimer = 0;
	private const float BUTTON_COOLDOWN = 0.2f;
	
	// Composants pour le travelling de caméra
	private Camera3D _globalCamera;
	private Tween _cameraTween;
	
	/**
	 * Initialisation de la scène principale
	 */
	public override void _Ready()
	{
		// Initialisation du temps de jeu
		_remainingTime = _initialGameTime;
		
		// Récupération ou création de l'ArduinoManager
		_arduinoManager = ArduinoManager.Instance;
		if (_arduinoManager == null)
		{
			_arduinoManager = new ArduinoManager();
			_arduinoManager.Name = "ArduinoManager";
			GetTree().Root.CallDeferred(Node.MethodName.AddChild, _arduinoManager);
		}
		
		// Démarrer le son d'introduction
		PlayStartSound();
		
		// Création du générateur de labyrinthe
		var mazeGenerator = new VerticalMazeGenerator();
		mazeGenerator.Name = "MazeGenerator";
		AddChild(mazeGenerator);
		_mazeGenerator = mazeGenerator;
		
		// Initialisation des canvas pour l'interface
		InitializeCanvasLayers();
		
		// Instanciation différée du joueur
		CallDeferred(nameof(SpawnPlayerBall));
		
		// Création de l'interface utilisateur
		CallDeferred(nameof(CreateUI));
		
		// Configuration de l'effet de flou
		ConfigureBlurredBackground();
		
		// Chargement des scores précédents
		LoadHighScores();
		
		// Création du panneau de high scores
		CreateHighScorePanel();
		
		// Gestion du démarrage selon le contexte
		if (_isRestarting)
		{
			// Démarrage direct après un game over
			CallDeferred(nameof(SkipCalibrationAndStart));
			_isRestarting = false;
			GD.Print("Redémarrage après Game Over - Calibration ignorée");
		}
		else if (!_isFirstStart)
		{
			// Écran d'attente au redémarrage normal
			_gameState = GameState.WaitingToStart;
			AddCenteredMessage("APPUYEZ SUR LE BOUTON POUR COMMENCER", new Color(1, 1, 0), 36);
		}
		else
		{
			// Premier démarrage avec calibration
			SimulateCalibration();
			_isFirstStart = false;
		}
		
		GD.Print("MainScene initialisé");
	}
	
	/**
	 * Joue le son d'introduction/démarrage du jeu
	 */
	private void PlayStartSound()
	{
		if (AudioManager.Instance != null) {
			// Arrêt de tous les sons en cours
			AudioManager.Instance.StopAllSounds();
			
			// Démarrage de la musique d'intro
			AudioManager.Instance.PlayLoopingSound("GameStart");
			GD.Print("Son de début joué en boucle");
		}
	}

	/**
	 * Démarre le jeu immédiatement sans calibration
	 * Utilisé lors des redémarrages
	 */
	private void SkipCalibrationAndStart()
	{
		// Court délai pour assurer l'initialisation complète
		var timer = GetTree().CreateTimer(0.5f);
		timer.Timeout += () => {
			// Activation de l'état de jeu
			_gameState = GameState.Playing;
			
			// Masquage des éléments d'interface d'attente
			if (_centerMessageLabel != null)
				_centerMessageLabel.Visible = false;
			if (BlurredDisplay != null)
				BlurredDisplay.Visible = false;
			if (_blueOverlay != null)
				_blueOverlay.Visible = false;
			
			// Réinitialisation de l'état du bouton
			if (ArduinoManager.Instance != null)
			{
				ArduinoManager.Instance.ResetButtonState();
			}
			
			// Activation des contrôles du joueur
			if (_playerBall != null)
			{
				// Positionnement au début du premier labyrinthe
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
			
			// Mise à jour de l'interface
			UpdateUI();
		};
	}

	/**
	 * Charge les meilleurs scores depuis le ScoreManager
	 */
	private void LoadHighScores()
	{
		try
		{
			// Récupération du ScoreManager
			var scoreManager = GetNode<ScoreManager>("/root/ScoreManager");
			if (scoreManager != null)
			{
				_highScores = scoreManager.GetHighScores();
				GD.Print("Scores chargés avec succès depuis ScoreManager");
			}
			else
			{
				GD.PrintErr("ScoreManager non trouvé dans l'arbre de scène");
				_highScores = new List<ScoreManager.ScoreEntry>();
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Erreur lors du chargement des scores: {e.Message}");
			_highScores = new List<ScoreManager.ScoreEntry>();
		}
	}
	
	/**
	 * Enregistre le score de la partie actuelle
	 */
	private void SaveScore()
	{
		// Garantit au moins un labyrinthe complété
		int mazesCompleted = Math.Max(1, _mazesCompleted);
		int totalCats = CountTotalCats();
		
		GD.Print($"Sauvegarde du score: Labyrinthes={mazesCompleted}, Chats={totalCats}");
		
		try
		{
			// Recherche du ScoreManager de différentes façons
			var scoreManager = GetNode<ScoreManager>("/root/ScoreManager");
			
			if (scoreManager == null)
			{
				scoreManager = ScoreManager.Instance;
			}
			
			if (scoreManager == null)
			{
				scoreManager = GetTree().Root.FindChild("ScoreManager", true, false) as ScoreManager;
			}
			
			// Ajout du score si le ScoreManager est trouvé
			if (scoreManager != null)
			{
				scoreManager.AddScore(mazesCompleted, totalCats);
				GD.Print("Score ajouté avec succès");
			}
			else
			{
				GD.PrintErr("ScoreManager non trouvé, création et ajout du ScoreManager");
				
				// Création du ScoreManager si nécessaire
				scoreManager = new ScoreManager();
				scoreManager.Name = "ScoreManager";
				GetTree().Root.AddChild(scoreManager);
				
				// Ajout différé pour initialisation
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
		
		// Mise à jour de l'affichage des scores
		UpdateHighScoreDisplay();
	}

	/**
	 * Met à jour l'affichage du panneau des meilleurs scores
	 */
	private void UpdateHighScoreDisplay()
	{
		// Chargement des scores à jour
		try
		{
			// Tentatives multiples de récupération du ScoreManager
			ScoreManager scoreManager = null;
			
			scoreManager = GetNode<ScoreManager>("/root/ScoreManager");
			
			if (scoreManager == null)
			{
				scoreManager = ScoreManager.Instance;
			}
			
			if (scoreManager == null)
			{
				scoreManager = GetTree().Root.FindChild("ScoreManager", true, false) as ScoreManager;
			}
			
			if (scoreManager != null)
			{
				_highScores = scoreManager.GetHighScores();
				GD.Print("Scores chargés avec succès");
			}
			else
			{
				GD.PrintErr("ScoreManager non trouvé dans l'arbre de scène");
				_highScores = new List<ScoreManager.ScoreEntry>();
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Erreur lors du chargement des scores: {e.Message}");
			_highScores = new List<ScoreManager.ScoreEntry>();
		}
		
		// Vérification de l'existence du panneau
		if (_highScorePanel == null)
		{
			GD.PrintErr("Le panneau de high scores n'existe pas!");
			return;
		}
		
		// Nettoyage des scores existants
		foreach (Node child in _highScorePanel.GetChildren())
		{
			if (child.Name.ToString().StartsWith("ScoreLabel"))
			{
				child.QueueFree();
			}
		}
		
		// Affichage des 5 meilleurs scores
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
		
		// Message si aucun score n'est enregistré
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

	/**
	 * Initialise les couches de canvas pour l'organisation de l'interface
	 */
	private void InitializeCanvasLayers()
	{
		// Canvas pour l'UI principale
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
	
	/**
	 * Configure l'effet de flou pour les transitions et pauses
	 */
	private void ConfigureBlurredBackground()
	{
		// Création de l'overlay coloré
		_blueOverlay = new ColorRect();
		_blueOverlay.Name = "BlueOverlay";
		_blueOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_blueOverlay.Color = new Color(0.0f, 0.2f, 0.6f, 0.4f); // Bleu semi-transparent
		_blurCanvas.AddChild(_blueOverlay);
		
		if (Viewport3D != null && BlurShaderMaterial != null)
		{
			// Configuration du TextureRect avec le shader de flou
			ViewportTexture viewportTexture = null;
			
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
				viewportTexture = new ViewportTexture();
				viewportTexture.ViewportPath = Viewport3D.GetPath();
			}
			
			// Application de la texture et configuration
			BlurredDisplay.Texture = viewportTexture;
			BlurredDisplay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			BlurredDisplay.MouseFilter = Control.MouseFilterEnum.Ignore;
			
			// Configuration de l'intensité du flou
			if (BlurShaderMaterial != null)
			{
				BlurShaderMaterial.SetShaderParameter("blur_strength", 6.0f);
			}
		}
	}
	
	/**
	 * Crée et configure le panneau des meilleurs scores
	 */
	private void CreateHighScorePanel()
	{
		_highScorePanel = new Panel();
		_highScorePanel.Name = "HighScorePanel";
		_highScorePanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_highScorePanel.Size = new Vector2(300, 180);
		_highScorePanel.Position = new Vector2(-1150, 0); // Sous le panneau UI principal
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
		
		// Mise à jour des scores
		UpdateHighScoreDisplay();
	}
	
	/**
	 * Simule le processus de calibration de l'Arduino
	 */
	private async void SimulateCalibration()
	{
		_gameState = GameState.Calibrating;
		GD.Print("État: Calibrating");
		
		// Message de calibration
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
		
		// Animation des points successifs
		for (int i = 0; i < 5; i++)
		{
			_loadingDotsLabel.Text = ".";
			await ToSignal(GetTree().CreateTimer(0.3), "timeout");
			_loadingDotsLabel.Text = "..";
			await ToSignal(GetTree().CreateTimer(0.3), "timeout");
			_loadingDotsLabel.Text = "...";
			await ToSignal(GetTree().CreateTimer(0.3), "timeout");
		}
		
		// Transition vers l'écran d'attente
		_gameState = GameState.WaitingToStart;
		GD.Print("État: WaitingToStart");
		
		// Nettoyage et nouveau message
		if (_loadingDotsLabel != null)
			_loadingDotsLabel.QueueFree();
		
		AddCenteredMessage("APPUYEZ SUR LE BOUTON POUR COMMENCER", new Color(1, 1, 0), 36);
	}
	
	/**
	 * Ajoute un message centré avec style personnalisé
	 */
	private void AddCenteredMessage(string text, Color color, int fontSize = 32)
	{
		// Nettoyage du message précédent
		if (_centerMessageLabel != null)
			_centerMessageLabel.QueueFree();
		
		// Création du nouveau message
		_centerMessageLabel = new Label();
		_centerMessageLabel.Text = text;
		
		// Configuration du style
		var labelSettings = new LabelSettings {
			Font = font,
			FontSize = fontSize,
			FontColor = color
		};
		
		_centerMessageLabel.LabelSettings = labelSettings;
		
		// Centrage et positionnement
		_centerMessageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_centerMessageLabel.VerticalAlignment = VerticalAlignment.Center;
		_centerMessageLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
		_centerMessageLabel.Size = new Vector2(800, 100);
		_centerMessageLabel.Position = new Vector2(-400, 0);
		
		// Ajout au canvas des messages
		_messageCanvas.AddChild(_centerMessageLabel);
	}
	
	/**
	 * Crée les éléments d'interface utilisateur principaux
	 */
	private void CreateUI()
	{
		// Panneau principal d'UI
		_uiPanel = new Panel();
		_uiPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_uiPanel.Size = new Vector2(310, 190);
		_uiPanel.Position = new Vector2(-310, 0);
		_uiCanvas.AddChild(_uiPanel);
		
		// Label d'informations générales
		_infoLabel = new Label();
		_infoLabel.Text = "Bouge la manette pour te déplacer ";
		_infoLabel.Position = new Vector2(10, 10);
		_infoLabel.Size = new Vector2(280, 40);
		_infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_uiPanel.AddChild(_infoLabel);
		
		// Compteur de labyrinthes
		_mazeCountLabel = new Label();
		_mazeCountLabel.Text = "Labyrinthe: 1 / ?";
		_mazeCountLabel.Position = new Vector2(10, 50);
		_mazeCountLabel.Size = new Vector2(280, 30);
		_uiPanel.AddChild(_mazeCountLabel);
		
		// Affichage du temps
		_timeLabel = new Label();
		_timeLabel.Text = "Temps: 60s";
		_timeLabel.Position = new Vector2(10, 80);
		_timeLabel.Size = new Vector2(280, 30);
		_timeLabel.Modulate = new Color(1, 1, 1);
		_uiPanel.AddChild(_timeLabel);
		
		// Affichage des effets de chats
		_catEffectLabel = new Label();
		_catEffectLabel.Text = "";
		_catEffectLabel.Position = new Vector2(10, 110);
		_catEffectLabel.Size = new Vector2(280, 30);
		_catEffectLabel.Visible = false;
		_uiPanel.AddChild(_catEffectLabel);
		
		// Affichage du score
		_scoreLabel = new Label();
		_scoreLabel.Text = "Chats: 0 | Score: 0";
		_scoreLabel.Position = new Vector2(10, 150);
		_scoreLabel.Size = new Vector2(280, 30);
		_uiPanel.AddChild(_scoreLabel);
		
		// Initialisation des valeurs
		UpdateUI();
	}

	/**
	 * Mise à jour par frame - gestion des entrées et états
	 */
	public override void _Process(double delta)
	{
		// Mise à jour du timer de cooldown pour le bouton
		if (_buttonCooldownTimer > 0)
		{
			_buttonCooldownTimer -= (float)delta;
		}
		
		// Détection d'appui sur le bouton Arduino
		if (ArduinoManager.Instance != null && _buttonCooldownTimer <= 0)
		{
			bool buttonPressed = ArduinoManager.Instance.IsButtonJustPressed();
			
			if (buttonPressed)
			{
				GD.Print("DÉTECTION BOUTON CONFIRMÉE DANS PROCESS: " + _gameState);
				HandleButtonPress();
				_buttonCooldownTimer = BUTTON_COOLDOWN;
				
				// Réinitialisation forcée de l'état du bouton
				ArduinoManager.Instance.ForceResetButtonState();
			}
		}
		
		// Touche Espace comme alternative au bouton Arduino
		if (Input.IsKeyPressed(Key.Space) && _buttonCooldownTimer <= 0)
		{
			GD.Print("ESPACE PRESSÉ - SIMULATION BOUTON");
			HandleButtonPress();
			_buttonCooldownTimer = BUTTON_COOLDOWN;
		}
		
		// Touche F1 pour démarrage d'urgence
		if (Input.IsKeyPressed(Key.F1) && _gameState == GameState.WaitingToStart)
		{
			GD.Print("TOUCHE F1 DÉTECTÉE - DÉMARRAGE FORCÉ DU JEU");
			StartGame();
		}
		
		// Reset d'urgence avec Echap ou Retour Arrière
		if (Input.IsActionJustPressed("ui_cancel") || Input.IsKeyPressed(Key.Backspace))
		{
			EmergencyReset();
			return;
		}
		
		// Touche P pour pause/reprise
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
		
		// Touche R pour redémarrage dans les états de fin
		if (Input.IsKeyPressed(Key.R) && (_gameState == GameState.GameOver || 
										_gameState == GameState.GameOverFinal ||
										_gameState == GameState.CameraTravel) && 
			_buttonCooldownTimer <= 0)
		{
			_isRestarting = true;
			RestartGame();
			_buttonCooldownTimer = BUTTON_COOLDOWN;
		}
		
		// Ne pas mettre à jour le temps en dehors de l'état de jeu
		if (_gameState != GameState.Playing)
		{
			return;
		}
		
		// Mise à jour du temps restant
		_remainingTime -= (float)delta;
		UpdateTimeLabel();
		
		// Vérification de fin de temps
		if (_remainingTime <= 0)
		{
			_remainingTime = 0;
			GameOver();
		}
		
		// Vérification de changement de labyrinthe
		CheckMazeTransition();
	}
	
	/**
	 * Démarre le jeu depuis l'écran d'attente
	 */
	private void StartGame()
	{
		_gameState = GameState.Playing;
		
		// Démarrage du son d'ambiance
		StartAmbianceSound();
		
		// Masquage des éléments d'interface d'attente
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = false;
		if (_blueOverlay != null)
			_blueOverlay.Visible = false;
		
		if (_centerMessageLabel != null)
			_centerMessageLabel.Visible = false;
		
		// Activation des contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.EnableControls();
		}
		
		// Mise à jour de l'interface
		UpdateUI();
		
		GD.Print("Jeu démarré - État: Playing");
	}

	/**
	 * Active le son d'ambiance du jeu
	 */
	private void StartAmbianceSound()
	{
		if (AudioManager.Instance == null) return;
		
		// Transition entre la musique précédente et l'ambiance
		if (AudioManager.Instance.IsLoopingSoundPlaying("GameStart"))
		{
			AudioManager.Instance.TransitionToSound("Ambiance", "GameStart");
		}
		else if (AudioManager.Instance.IsLoopingSoundPlaying("PauseMusic"))
		{
			AudioManager.Instance.TransitionToSound("Ambiance", "PauseMusic");
		}
		else if (!AudioManager.Instance.IsLoopingSoundPlaying("Ambiance"))
		{
			AudioManager.Instance.PlayLoopingSound("Ambiance");
		}
		
		_ambianceSoundPlaying = true;
		GD.Print("Son d'ambiance démarré");
	}

	/**
	 * Met le jeu en pause
	 */
	private void PauseGame()
	{
		_gameState = GameState.Paused;
		
		// Sauvegarde du temps actuel
		_savedTime = _remainingTime;
		
		// Désactivation des contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		
		// Démarrage de la musique de pause
		StartPauseMusic();
		
		// Affichage de l'interface de pause
		if (_blueOverlay != null)
		{
			_blueOverlay.Visible = true;
			_blueOverlay.Color = new Color(0.0f, 0.2f, 0.6f, 0.4f); // Bleu pour la pause
		}
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = true;
		
		// Message de pause
		AddCenteredMessage("Pause en cours...", new Color(0, 1, 1), 36); // Cyan
		
		GD.Print("Jeu en pause - État: Paused");
	}

	/**
	 * Active la musique de pause
	 */
	private void StartPauseMusic()
	{
		if (AudioManager.Instance == null) return;
		
		// Transition entre l'ambiance et la musique de pause
		if (AudioManager.Instance.IsLoopingSoundPlaying("Ambiance"))
		{
			AudioManager.Instance.TransitionToSound("PauseMusic", "Ambiance");
		}
		else if (!AudioManager.Instance.IsLoopingSoundPlaying("PauseMusic"))
		{
			AudioManager.Instance.PlayLoopingSound("PauseMusic");
		}
		
		_pauseMusicPlaying = true;
		GD.Print("Musique de pause démarrée");
	}

	/**
	 * Reprend le jeu après une pause
	 */
	private void ResumeGame()
	{
		_gameState = GameState.Playing;
		
		// Restauration du temps
		_remainingTime = _savedTime;
		
		// Transition audio
		StopPauseMusic();
		StartAmbianceSound();
		
		// Masquage des éléments d'interface de pause
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = false;
		if (_blueOverlay != null)
			_blueOverlay.Visible = false;
		
		if (_centerMessageLabel != null)
			_centerMessageLabel.Visible = false;
		
		// Réactivation des contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.EnableControls();
		}
		
		GD.Print("Jeu repris - État: Playing");
	}

	/**
	 * Arrête la musique de pause
	 */
	private void StopPauseMusic()
	{
		if (AudioManager.Instance == null) return;
		
		if (AudioManager.Instance.IsLoopingSoundPlaying("PauseMusic"))
		{
			AudioManager.Instance.StopLoopingSound("PauseMusic");
			_pauseMusicPlaying = false;
			GD.Print("Musique de pause arrêtée");
		}
	}

	/**
	 * Met à jour les éléments d'interface
	 */
	private void UpdateUI()
	{
		UpdateTimeLabel();
		UpdateScoreLabel();
	}
	
	/**
	 * Met à jour l'affichage du score
	 */
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
	
	/**
	 * Met à jour l'affichage du temps avec indicateur coloré
	 */
	private void UpdateTimeLabel()
	{
		if (_timeLabel == null) return;
		
		int seconds = (int)_remainingTime;
		_timeLabel.Text = $"Temps: {seconds}s";
		
		// Couleur selon le temps restant
		if (_remainingTime < 10)
		{
			_timeLabel.Modulate = new Color(1, 0, 0); // Rouge pour très peu de temps
		}
		else if (_remainingTime < 20)
		{
			_timeLabel.Modulate = new Color(1, 0.5f, 0); // Orange pour temps critique
		}
		else
		{
			_timeLabel.Modulate = new Color(1, 1, 1); // Blanc pour temps normal
		}
	}
	
	/**
	 * Ajoute du temps au compteur (bonus/malus des chats)
	 */
	public void AddTime(float seconds)
	{
		if (_gameState != GameState.Playing) return;
		
		_remainingTime += seconds;
		
		// Affichage de l'effet
		ShowCatEffect(seconds);
		
		GD.Print($"Temps ajouté: {seconds} secondes. Temps restant: {_remainingTime}");
	}
	
	/**
	 * Incrémente le compteur de chats collectés par type
	 */
	public void AddCatCollected(int catType)
	{
		if (_gameState != GameState.Playing) return;
		
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
			
			UpdateScoreLabel();
		}
	}
	
	/**
	 * Affiche temporairement l'effet d'un chat
	 */
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
		
		// Timer pour masquer le message après un délai
		var timer = new Timer();
		AddChild(timer);
		timer.WaitTime = 2.0f;
		timer.OneShot = true;
		timer.Timeout += () => {
			_catEffectLabel.Visible = false;
			timer.QueueFree();
		};
		timer.Start();
	}
	
	/**
	 * Lance le travelling de caméra pour montrer tous les labyrinthes complétés
	 */
	private void StartCameraTraveling()
	{
		_gameState = GameState.CameraTravel;
		GD.Print("État: CameraTravel - Démarrage du travelling de caméra");
		
		// Masquage temporaire des interfaces
		if (_centerMessageLabel != null)
			_centerMessageLabel.Visible = false;
		
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = false;
		if (_blueOverlay != null)
			_blueOverlay.Visible = false;
		
		// Affichage des statistiques
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
		
		// Création ou récupération de la caméra globale
		if (_globalCamera == null)
		{
			_globalCamera = GetNodeOrNull<Camera3D>("GlobalCamera");
			
			if (_globalCamera == null)
			{
				_globalCamera = new Camera3D();
				_globalCamera.Name = "GlobalCamera";
				AddChild(_globalCamera);
				GD.Print("Caméra globale créée pour le travelling");
			}
		}
		
		// Lancement du travelling
		if (_globalCamera != null)
		{
			_globalCamera.Current = true;
			StartCameraTravelingAnimation(_globalCamera, statsLabel);
		}
		else
		{
			// Si la caméra n'est pas disponible, passer directement à l'écran final
			ShowFinalGameOver(statsLabel);
		}
	}

	/**
	 * Anime le déplacement de la caméra pour le travelling
	 */
	private void StartCameraTravelingAnimation(Camera3D camera, Label statsLabel)
	{
		if (_mazeGenerator == null) return;
		
		// Annulation des tweens existants
		if (_cameraTween != null && _cameraTween.IsValid())
		{
			_cameraTween.Kill();
		}
		
		// Calcul du dernier labyrinthe complété
		int lastMazeIndex = Math.Max(_currentMazeIndex, _mazesCompleted - 1);
		if (lastMazeIndex < 0) lastMazeIndex = 0;
		
		GD.Print($"Démarrage du travelling de la caméra pour montrer les labyrinthes 0 à {lastMazeIndex}");
		
		// Récupération des noeuds des labyrinthes
		Node3D lastMaze = GetNodeOrNull<Node3D>($"MazeGenerator/Maze_{lastMazeIndex}");
		Node3D firstMaze = GetNodeOrNull<Node3D>("MazeGenerator/Maze_0");
		
		if (lastMaze == null || firstMaze == null)
		{
			GD.PrintErr($"Impossible de trouver les labyrinthes pour le travelling (premier: {firstMaze != null}, dernier: {lastMaze != null})");
			ShowFinalGameOver(statsLabel);
			return;
		}
		
		// Récupération des tailles
		int lastMazeSize = _mazeGenerator.GetMazeSize(lastMazeIndex);
		int firstMazeSize = _mazeGenerator.GetMazeSize(0);
		
		// Calcul des centres
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
		
		// Calcul de la hauteur optimale pour voir tous les labyrinthes
		float totalSpan = (lastMaze.GlobalPosition.X + lastMazeSize * _mazeGenerator._cellSize) - firstMaze.GlobalPosition.X;
		float maxSize = Math.Max(lastMazeSize, firstMazeSize);
		float cameraHeight = Math.Max(totalSpan * 0.5f, maxSize * _mazeGenerator._cellSize * 0.8f);
		
		GD.Print($"Distance totale des labyrinthes: {totalSpan}, hauteur de caméra: {cameraHeight}");
		
		// Position initiale de la caméra
		Vector3 startPosition = lastMazeCenter + new Vector3(0, cameraHeight * 0.5f, 0);
		camera.GlobalPosition = startPosition;
		
		// Orientation vers le dernier labyrinthe
		camera.LookAt(lastMazeCenter + new Vector3(0.01f, 0, 0.01f), Vector3.Up);
		
		// Position finale avec vue d'ensemble
		Vector3 midPoint = new Vector3(
			(firstMazeCenter.X + lastMazeCenter.X) / 2,
			0,
			(firstMazeCenter.Z + lastMazeCenter.Z) / 2
		);
		
		Vector3 endPosition = midPoint + new Vector3(0, cameraHeight, totalSpan * 0.15f);
		
		// Ajustement selon le nombre de labyrinthes
		float adjustedHeight = cameraHeight;
		if (_mazesCompleted > 2)
		{
			adjustedHeight = cameraHeight * (1.0f + (_mazesCompleted * 0.1f));
			endPosition.Y = adjustedHeight;
			endPosition.Z = totalSpan * (0.15f + (_mazesCompleted * 0.02f));
		}
		
		GD.Print($"Position de départ: {startPosition}, Position finale: {endPosition}");
		
		// Configuration du tween d'animation
		_cameraTween = CreateTween();
		_cameraTween.SetEase(Tween.EaseType.InOut);
		_cameraTween.SetTrans(Tween.TransitionType.Cubic);
		
		// Animation de la position
		_cameraTween.TweenProperty(camera, "global_position", endPosition, 7.0f);
		
		// Animation de l'orientation durant le mouvement
		_cameraTween.Parallel().TweenMethod(
			Callable.From((float t) => {
				Vector3 lookAtPos = lastMazeCenter.Lerp(midPoint, t) + new Vector3(0.01f, 0, 0.01f);
				camera.LookAt(lookAtPos, Vector3.Up);
			}),
			0.0f, 1.0f, 7.0f
		);
		
		// Callback de fin d'animation
		_cameraTween.TweenCallback(Callable.From(() => {
			ShowFinalGameOver(statsLabel);
		}));
	}
	
	/**
	 * Affiche l'écran de game over final après le travelling
	 */
	private void ShowFinalGameOver(Label statsLabel)
	{
		_gameState = GameState.GameOverFinal;
		GD.Print("État: GameOverFinal");
		
		// Suppression du label de statistiques
		if (statsLabel != null)
			statsLabel.QueueFree();
		
		// Désactivation de la caméra globale
		if (_globalCamera != null)
		{
			_globalCamera.Current = false;
		}
		
		// Réactivation de la caméra du joueur
		if (_playerBall != null && _playerBall.FindChild("Camera3D") is Camera3D playerCamera)
		{
			playerCamera.Current = true;
		}
		
		// Affichage de l'interface de fin
		if (_blueOverlay != null)
		{
			_blueOverlay.Visible = true;
			_blueOverlay.Color = new Color(0.6f, 0.0f, 0.0f, 0.4f); // Rouge pour game over
		}
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = true;
		
		// Message de fin avec statistiques
		AddCenteredMessage(
			$"VOUS N'AVEZ PLUS DE LAINE...\n\nLabyrinthes: {_mazesCompleted} | Chats: {CountTotalCats()}\n\nAPPUYEZ SUR LE BOUTON POUR RECOMMENCER", 
			new Color(1, 1, 1),
			24
		);
	}
	
	/**
	 * Retourne le nombre total de chats collectés
	 */
	private int CountTotalCats()
	{
		int total = 0;
		foreach (var count in _catsCollected.Values)
		{
			total += count;
		}
		return total;
	}
	
	/**
	 * Joue le son de game over
	 */
	private void PlayGameOverSound()
	{
		if (AudioManager.Instance != null) {
			// Arrêt de l'ambiance
			if (_ambianceSoundPlaying)
			{
				AudioManager.Instance.StopLoopingSound("Ambiance");
				_ambianceSoundPlaying = false;
			}
			
			// Démarrage du son de game over
			AudioManager.Instance.PlayLoopingSound("GameOver");
			GD.Print("Son GameOver joué en boucle");
		}
	}

	/**
	 * Vérifie si le joueur a changé de labyrinthe
	 */
	private void CheckMazeTransition()
	{
		if (_playerBall == null) return;
		
		// Détection du labyrinthe actuel
		int newMazeIndex = GetPlayerCurrentMaze();
		
		// Si changement de labyrinthe
		if (newMazeIndex != _currentMazeIndex && newMazeIndex >= 0)
		{
			// Incrémentation uniquement si avance (pas recul)
			if (newMazeIndex > _currentMazeIndex)
			{
				_mazesCompleted += 1;
				UpdateScoreLabel();
				GD.Print($"Transition vers le labyrinthe {newMazeIndex}, total complétés: {_mazesCompleted}");
			}
			
			_currentMazeIndex = newMazeIndex;
			UpdateMazeCounter(newMazeIndex);
		}
	}
	
	/**
	 * Détermine dans quel labyrinthe se trouve le joueur
	 */
	private int GetPlayerCurrentMaze()
	{
		for (int i = 0; i < _mazeGenerator.GetTotalMazeCount(); i++)
		{
			Node3D maze = GetNodeOrNull<Node3D>($"MazeGenerator/Maze_{i}");
			if (maze != null)
			{
				// Vérification des limites X du labyrinthe
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
	
	/**
	 * Met à jour l'affichage du compteur de labyrinthes
	 */
	private void UpdateMazeCounter(int mazeIndex)
	{
		if (_mazeCountLabel != null)
		{
			// Affichage avec symbole infini pour le total
			_mazeCountLabel.Text = $"Labyrinthe: {mazeIndex + 1} / ∞";
		}
	}
	
	/**
	 * Gère l'appui sur le bouton Arduino
	 */
	private void HandleButtonPress()
	{
		GD.Print("!!! BOUTON DÉTECTÉ !!! État du jeu: " + _gameState);
		
		switch (_gameState)
		{
			case GameState.Calibrating:
				// Ignorer pendant la calibration
				GD.Print("Bouton ignoré pendant le calibrage");
				break;
				
			case GameState.WaitingToStart:
				// Démarrer le jeu
				GD.Print("!!! DÉMARRAGE DU JEU !!!");
				StartGame();
				break;
				
			case GameState.Playing:
				// Mettre en pause
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
				_isRestarting = true;
				
				// Réinitialisation de l'état du bouton
				if (ArduinoManager.Instance != null)
				{
					ArduinoManager.Instance.ResetButtonState();
				}
				RestartGame();
				break;
		}
		
		// Réinitialisation explicite de l'état du bouton
		if (ArduinoManager.Instance != null)
		{
			ArduinoManager.Instance.ForceResetButtonState();
		}
	}
	
	/**
	 * Réinitialisation d'urgence du jeu
	 */
	private void EmergencyReset()
	{
		GD.Print("RESET D'URGENCE ACTIVÉ!");
		
		// Réinitialisation de l'état du bouton
		if (ArduinoManager.Instance != null)
		{
			ArduinoManager.Instance.ResetButtonState();
		}
		
		// Flag pour éviter la calibration au redémarrage
		_isRestarting = true;
		
		// Rechargement immédiat de la scène
		GetTree().ReloadCurrentScene();
	}
	
	/**
	 * Redémarre le jeu proprement
	 */
	private void RestartGame()
	{
		GD.Print("Démarrage du processus de redémarrage du jeu...");
		
		// Désactivation des contrôles joueur
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		
		// Flag pour éviter la calibration
		_isRestarting = true;
		
		// Arrêt de tous les sons
		StopAllSounds();
		
		// Réinitialisation de l'état du bouton
		if (ArduinoManager.Instance != null)
		{
			ArduinoManager.Instance.ResetButtonState();
		}
		
		// Rechargement différé de la scène
		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += () => 
		{
			// Réinitialisation forcée de l'état du bouton
			if (ArduinoManager.Instance != null)
			{
				ArduinoManager.Instance._buttonPressed = false;
				ArduinoManager.Instance._buttonJustPressed = false;
				ArduinoManager.Instance._buttonJustReleased = false;
				ArduinoManager.Instance._buttonEventProcessed = true;
			}
			
			// Rechargement de la scène
			GetTree().ReloadCurrentScene();
		};
	}

	/**
	 * Réinitialise le jeu sans recharger la scène
	 */
	private void ReinitializeGame()
	{
		GD.Print("Réinitialisation du jeu sans rechargement de scène...");
		
		// Recréation du générateur de labyrinthe
		var mazeGenerator = new VerticalMazeGenerator();
		mazeGenerator.Name = "MazeGenerator";
		AddChild(mazeGenerator);
		_mazeGenerator = mazeGenerator;
		
		// Attente pour la génération des labyrinthes
		var timer = GetTree().CreateTimer(1.0f);
		timer.Timeout += () => {
			// Réinstanciation du joueur
			SpawnPlayerBall();
			
			// État de jeu selon le contexte
			if (_isRestarting)
			{
				_gameState = GameState.Playing;
				
				// Masquage des interfaces
				if (_centerMessageLabel != null)
					_centerMessageLabel.Visible = false;
				if (BlurredDisplay != null)
					BlurredDisplay.Visible = false;
				if (_blueOverlay != null)
					_blueOverlay.Visible = false;
					
				// Connexion forcée à l'ArduinoManager
				var controlsTimer = GetTree().CreateTimer(0.5f);
				controlsTimer.Timeout += () => {
					if (_playerBall != null && _arduinoManager != null)
					{
						_playerBall.SetArduinoManager(_arduinoManager);
						_playerBall.EnableControls();
						GD.Print("CRITIQUE: Connexion forcée au nouvel ArduinoManager!");
					}
				};
			}
			else
			{
				_gameState = GameState.WaitingToStart;
				AddCenteredMessage("APPUYEZ SUR LE BOUTON POUR COMMENCER", new Color(1, 1, 0), 36);
			}
			
			GD.Print($"Jeu réinitialisé et prêt à démarrer, état: {_gameState}");
		};
	}

	/**
	 * Crée le joueur et le place dans le labyrinthe
	 */
	private void SpawnPlayerBall()
	{
		GD.Print("Tentative de spawn du joueur...");
		
		// Vérification de la référence à la scène
		if (_playerBallScene == null)
		{
			GD.PrintErr("Erreur: PlayerBallScene n'est pas définie dans l'inspecteur!");
			return;
		}
		
		// Instanciation du joueur
		_playerBall = _playerBallScene.Instantiate<PlayerBall>();
		
		// Récupération ou création de l'ArduinoManager
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
		
		// Assignation de l'ArduinoManager au joueur
		_playerBall.SetArduinoManager(_arduinoManager);
		GD.Print($"ArduinoManager assigné au PlayerBall: {_arduinoManager != null}");
		
		AddChild(_playerBall);
		GD.Print("PlayerBall instancié avec succès");
		
		// Recherche du premier labyrinthe
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
			// Positionnement à l'entrée du premier labyrinthe
			int size = _mazeGenerator.GetMazeSize(0);
			Vector2I entrancePos = _mazeGenerator.GetEntrancePosition(size, 0);
			
			_playerBall.GlobalPosition = firstMaze.GlobalPosition + new Vector3(
				entrancePos.X * _mazeGenerator._cellSize,
				1.0f,
				entrancePos.Y * _mazeGenerator._cellSize
			);
			
			GD.Print("Joueur placé à la position: " + _playerBall.GlobalPosition);
		}
		
		// Désactivation initiale des contrôles
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
	}

	/**
	 * Gère la fin de partie (temps écoulé)
	 */
	private void GameOver()
	{
		// Éviter les appels multiples
		if (_gameState == GameState.GameOver || 
			_gameState == GameState.ShowingAllMazes || 
			_gameState == GameState.GameOverFinal ||
			_gameState == GameState.CameraTravel) return;
		
		_gameState = GameState.GameOver;
		_timeOver = true;
		
		// Désactivation des contrôles du joueur
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		
		// Son de game over
		StopAllSounds();
		PlayGameOverSound();
		
		// Sauvegarde du score
		SaveScore();
		
		// Affichage de l'interface de fin
		if (_blueOverlay != null)
		{
			_blueOverlay.Visible = true;
			_blueOverlay.Color = new Color(0.6f, 0.0f, 0.0f, 0.4f); // Rouge pour game over
		}
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = true;
		
		AddCenteredMessage("VOUS N'AVEZ PLUS DE LAINE...", new Color(1, 1, 1), 36);
		
		// Lancement différé du travelling de caméra
		var timer = new Timer();
		timer.WaitTime = 1.0f;
		timer.OneShot = true;
		timer.Timeout += () => StartCameraTraveling();
		AddChild(timer);
		timer.Start();
		
		GD.Print("Game Over - Plus de temps! État: GameOver");
	}

	/**
	 * Arrête tous les sons du jeu
	 */
	private void StopAllSounds()
	{
		// Mise à jour des flags d'état
		_ambianceSoundPlaying = false;
		_pauseMusicPlaying = false;
		
		// Arrêt global des sons
		if (AudioManager.Instance != null) {
			AudioManager.Instance.StopAllSounds();
		}
		
		GD.Print("Tous les sons arrêtés");
	}

	/**
	 * Démarre le jeu après un redémarrage
	 */
	private void StartGameAfterRestart()
	{
		// Configuration de l'état
		_gameState = GameState.Playing;
		
		// Masquage des interfaces
		if (BlurredDisplay != null)
			BlurredDisplay.Visible = false;
		if (_blueOverlay != null)
			_blueOverlay.Visible = false;
		
		if (_centerMessageLabel != null)
			_centerMessageLabel.Visible = false;
		
		// Activation différée des contrôles
		var timer = GetTree().CreateTimer(0.2f);
		timer.Timeout += () => {
			EnablePlayerControls();
			GD.Print("StartGameAfterRestart terminé");
		};
	}

	/**
	 * Joue le son de téléportation sur la cible
	 */
	private void PlayTeleportSound(Node3D target)
	{
		var audioPlayer = new AudioStreamPlayer3D();
		target.AddChild(audioPlayer);
		
		// Chargement du son
		audioPlayer.Stream = ResourceLoader.Load<AudioStream>("res://assets/audio/bruit_teleporteur.wav");
		
		// Configuration
		audioPlayer.VolumeDb = 5.0f;
		audioPlayer.MaxDistance = 100.0f;
		audioPlayer.Autoplay = true;
		
		// Nettoyage automatique
		audioPlayer.Finished += () => audioPlayer.QueueFree();
	}

	/**
	 * Active les contrôles du joueur avec connexion Arduino
	 */
	private void EnablePlayerControls()
	{
		// Recherche du joueur si nécessaire
		if (_playerBall == null)
		{
			_playerBall = GetTree().Root.FindChild("PlayerBall", true, false) as PlayerBall;
			GD.Print("Recherche du PlayerBall après redémarrage...");
		}
		
		// Recherche de l'ArduinoManager si nécessaire
		if (_arduinoManager == null)
		{
			_arduinoManager = ArduinoManager.Instance;
			if (_arduinoManager == null)
			{
				_arduinoManager = GetTree().Root.FindChild("ArduinoManager", true, false) as ArduinoManager;
			}
			GD.Print("Recherche de l'ArduinoManager après redémarrage...");
		}
		
		// Reconnexion de l'Arduino au joueur
		if (_arduinoManager != null && _playerBall != null)
		{
			_playerBall.SetArduinoManager(_arduinoManager);
			GD.Print("ArduinoManager reconnecté au PlayerBall");
		}
		else
		{
			GD.PrintErr("ERREUR: ArduinoManager ou PlayerBall null dans EnablePlayerControls!");
		}
		
		// Activation différée des contrôles
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
