using Godot;
using System;

public partial class MainScene : Node3D
{
	[Export]
	private PackedScene _playerBallScene;
	
	// Référence au générateur de labyrinthe
	private VerticalMazeGenerator _mazeGenerator;
	
	// Référence au gestionnaire Arduino
	private ArduinoManager _arduinoManager;
	
	// Référence à la balle du joueur
	private PlayerBall _playerBall;
	
	// Interface utilisateur
	private Label _infoLabel;
	private Label _mazeCountLabel;
	private Panel _uiPanel;
	
	// État du jeu
	private int _currentMazeIndex = 0;
	private bool _gameCompleted = false;
	
	public override void _Ready()
	{
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
	}
	
	private void CreateUI()
	{
		// Créer un canvas layer pour l'UI
		var canvasLayer = new CanvasLayer();
		canvasLayer.Name = "UICanvas";
		AddChild(canvasLayer);
		
		// Créer un panneau pour l'UI
		_uiPanel = new Panel();
		_uiPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_uiPanel.Size = new Vector2(300, 100);
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
		_mazeCountLabel.Text = "Labyrinthe: 1 / 10";
		_mazeCountLabel.Position = new Vector2(10, 60);
		_mazeCountLabel.Size = new Vector2(280, 30);
		_uiPanel.AddChild(_mazeCountLabel);
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
		int size = _mazeGenerator._mazeSizes[0];
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
		
		// Créer l'interface utilisateur
		CreateUI();
	}
	
	private void SetupPlayerCamera()
	{
		// Accéder à la caméra du joueur
		Camera3D playerCamera = _playerBall.GetNodeOrNull<Camera3D>("Camera3D");
		if (playerCamera != null)
		{
			// Configurer la caméra pour voir le labyrinthe de haut
			playerCamera.Position = new Vector3(0, 10, 5);
			playerCamera.RotationDegrees = new Vector3(-60, 0, 0);
			playerCamera.Current = true;
			
			// Ajuster le champ de vision pour mieux voir le labyrinthe
			playerCamera.Fov = 70;
		}
	}
	
	public override void _Process(double delta)
	{
		// Gérer les touches de fonction
		if (Input.IsActionJustPressed("ui_cancel"))
		{
			GetTree().Quit();
		}
		
		if (Input.IsActionJustPressed("ui_select"))
		{
			RestartGame();
		}
		
		// Mise à jour de l'interface utilisateur
		if (_gameCompleted)
		{
			_infoLabel.Text = "Félicitations! Vous avez terminé tous les labyrinthes! Appuyez sur F5 pour recommencer.";
		}
		
		// Vérifier le passage à un nouveau labyrinthe
		CheckMazeTransition();
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
			_currentMazeIndex = newMazeIndex;
			UpdateMazeCounter(newMazeIndex);
			
			// Vérifier si c'est le dernier labyrinthe
			if (_currentMazeIndex == _mazeGenerator._mazeSizes.Length - 1)
			{
				// Surveiller la position dans le dernier labyrinthe pour détecter la fin
				CheckGameCompletion();
			}
		}
	}
	
	private int GetPlayerCurrentMaze()
	{
		// Trouver dans quel labyrinthe se trouve le joueur
		for (int i = 0; i < _mazeGenerator._mazeSizes.Length; i++)
		{
			Node3D maze = GetNodeOrNull<Node3D>($"MazeGenerator/Maze_{i}");
			if (maze != null)
			{
				// Vérifier si le joueur est dans les limites X du labyrinthe
				float mazeMinX = maze.GlobalPosition.X;
				float mazeMaxX = mazeMinX + (_mazeGenerator._mazeSizes[i] * _mazeGenerator._cellSize);
				
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
		int lastMazeIndex = _mazeGenerator._mazeSizes.Length - 1;
		Node3D lastMaze = GetNodeOrNull<Node3D>($"MazeGenerator/Maze_{lastMazeIndex}");
		if (lastMaze == null) return;
		
		// Obtenir les coordonnées de sortie du dernier labyrinthe
		int lastMazeSize = _mazeGenerator._mazeSizes[lastMazeIndex];
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
		
		// Afficher un message de victoire
		_infoLabel.Text = "Félicitations! Vous avez terminé tous les labyrinthes! Appuyez sur F5 pour recommencer.";
		
		// Désactiver les contrôles du joueur (optionnel)
		if (_playerBall != null)
		{
			_playerBall.DisableControls();
		}
		
		// Ajouter des effets de victoire (son, particules, etc.)
		PlayVictoryEffects();
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
			_mazeCountLabel.Text = $"Labyrinthe: {mazeIndex + 1} / {_mazeGenerator._mazeSizes.Length}";
		}
	}
	
	private void RestartGame()
	{
		// Recharger la scène pour recommencer
		GetTree().ReloadCurrentScene();
	}
}
