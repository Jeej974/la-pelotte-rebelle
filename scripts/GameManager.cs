using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;

/**
 * GameManager - Gestionnaire global du jeu
 * 
 * Contrôle l'état du jeu, le timer, les scores, et les événements centraux.
 * Gère également les effets visuels comme le chemin révélé par le chat Siamois.
 */
public partial class GameManager : Node
{
	[Export]
	private float _initialGameTime = 120.0f; // 2 minutes par défaut
	
	[Export]
	private Label _timeLabel;
	
	[Export]
	private Label _catEffectLabel;
	
	[Export]
	private bool _enableTimeLimit = true;
	
	// Temps de jeu et état
	private float _remainingTime;
	private bool _gameOver = false;
	
	// Signals pour communiquer avec d'autres systèmes
	[Signal]
	public delegate void GameOverEventHandler();
	
	[Signal]
	public delegate void CatEffectAppliedEventHandler(CatType type, float value);
	
	// Références aux composants du jeu
	private VerticalMazeGenerator _mazeGenerator;
	private List<Node3D> _pathMarkers = new List<Node3D>();
	private MainScene _mainScene;
	
	// Structure pour le stockage des scores
	[Serializable]
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
	
	// Liste des meilleurs scores
	private List<ScoreEntry> _highScores = new List<ScoreEntry>();
	
	/**
	 * Initialisation du gestionnaire de jeu
	 */
	public override void _Ready()
	{
		// Initialisation du temps de jeu
		_remainingTime = _initialGameTime;
		
		// Mise à jour initiale de l'UI
		UpdateTimeLabel();
		
		// Recherche différée des composants du jeu
		CallDeferred(nameof(FindMazeGenerator));
		
		// Recherche de la scène principale
		_mainScene = GetTree().Root.GetNode<MainScene>("MainScene");
		if (_mainScene == null)
		{
			GD.PrintErr("MainScene non trouvée!");
		}
		
		// Chargement des scores existants
		LoadHighScores();
	}
	
	/**
	 * Recherche le générateur de labyrinthe dans la scène
	 */
	private void FindMazeGenerator()
	{
		_mazeGenerator = GetTree().Root.FindChild("MazeGenerator", true, false) as VerticalMazeGenerator;
		if (_mazeGenerator == null)
		{
			GD.PrintErr("Générateur de labyrinthe non trouvé!");
		}
	}
	
	/**
	 * Mise à jour du temps et vérification de fin de jeu
	 */
	public override void _Process(double delta)
	{
		if (_gameOver || !_enableTimeLimit) return;
		
		// Décrémentation du temps restant
		_remainingTime -= (float)delta;
		
		// Mise à jour de l'affichage
		UpdateTimeLabel();
		
		// Vérification de fin de jeu
		if (_remainingTime <= 0)
		{
			_remainingTime = 0;
			_gameOver = true;
			EmitSignal(SignalName.GameOver);
			GD.Print("Temps écoulé! Game Over!");
			
			// Notification à la scène principale
			if (_mainScene != null)
			{
				_mainScene.Call("GameOver");
			}
		}
	}
	
	/**
	 * Met à jour l'affichage du temps avec code couleur
	 */
	private void UpdateTimeLabel()
	{
		if (_timeLabel != null)
		{
			int minutes = (int)(_remainingTime / 60);
			int seconds = (int)(_remainingTime % 60);
			_timeLabel.Text = $"Temps: {minutes:00}:{seconds:00}";
			
			// Code couleur selon le temps restant
			if (_remainingTime < 30)
			{
				_timeLabel.Modulate = new Color(1, 0, 0); // Rouge pour critique
			}
			else if (_remainingTime < 60)
			{
				_timeLabel.Modulate = new Color(1, 0.5f, 0); // Orange pour attention
			}
			else
			{
				_timeLabel.Modulate = new Color(1, 1, 1); // Blanc pour normal
			}
		}
	}
	
	/**
	 * Ajoute ou retire du temps (utilisé par les chats)
	 */
	public void AddTime(float seconds)
	{
		if (_gameOver) return;
		
		_remainingTime += seconds;
		
		// Empêcher le temps négatif
		if (_remainingTime < 0)
		{
			_remainingTime = 0;
		}
		
		// Afficher l'effet
		ShowCatEffect(seconds);
		
		// Notification aux autres systèmes
		EmitSignal(SignalName.CatEffectApplied, (int)CatType.Orange, seconds);
	}
	
	/**
	 * Affiche un effet temporaire pour les bonus/malus de temps
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
	 * Révèle le chemin du labyrinthe (effet du chat Siamois)
	 */
	public void RevealPath()
	{
		if (_mazeGenerator == null)
		{
			GD.PrintErr("Impossible de révéler le chemin: MazeGenerator non trouvé");
			return;
		}
		
		// Nettoyer les marqueurs existants
		ClearPathMarkers();
		
		// Identifier le labyrinthe actuel
		int currentMazeIndex = GetCurrentMazeIndex();
		if (currentMazeIndex < 0) return;
		
		// Créer les marqueurs pour ce labyrinthe
		CreatePathMarkers(currentMazeIndex);
		
		// Timer pour supprimer les marqueurs après un délai
		var timer = new Timer();
		AddChild(timer);
		timer.WaitTime = 7.0f;
		timer.OneShot = true;
		timer.Timeout += ClearPathMarkers;
		timer.Start();
		
		// Afficher un message d'effet
		if (_catEffectLabel != null)
		{
			_catEffectLabel.Text = "Chemin révélé!";
			_catEffectLabel.Modulate = new Color(0.5f, 0.5f, 1.0f); // Bleu clair
			_catEffectLabel.Visible = true;
			
			// Timer pour masquer le message
			var labelTimer = new Timer();
			AddChild(labelTimer);
			labelTimer.WaitTime = 2.0f;
			labelTimer.OneShot = true;
			labelTimer.Timeout += () => {
				_catEffectLabel.Visible = false;
				labelTimer.QueueFree();
			};
			labelTimer.Start();
		}
	}
	
	/**
	 * Détermine l'index du labyrinthe où se trouve le joueur
	 */
	private int GetCurrentMazeIndex()
	{
		// Recherche du joueur
		var player = GetTree().Root.FindChild("PlayerBall", true, false) as PlayerBall;
		if (player == null)
		{
			GD.PrintErr("Joueur non trouvé!");
			return -1;
		}
		
		// Vérification de la position du joueur par rapport aux labyrinthes
		for (int i = 0; i < _mazeGenerator._mazeSizes.Count; i++)
		{
			Node3D maze = GetNode<Node3D>($"/root/MainScene/MazeGenerator/Maze_{i}");
			if (maze != null)
			{
				float mazeMinX = maze.GlobalPosition.X;
				float mazeMaxX = mazeMinX + (_mazeGenerator._mazeSizes[i] * _mazeGenerator._cellSize);
				
				if (player.GlobalPosition.X >= mazeMinX && player.GlobalPosition.X <= mazeMaxX)
				{
					return i;
				}
			}
		}
		
		return -1;
	}
	
	/**
	 * Crée des marqueurs visuels pour indiquer le chemin
	 */
	private void CreatePathMarkers(int mazeIndex)
	{
		// Récupération des informations du labyrinthe
		int mazeSize = _mazeGenerator._mazeSizes[mazeIndex];
		Vector2I entrancePos = _mazeGenerator.GetEntrancePosition(mazeSize, mazeIndex);
		Vector2I exitPos = _mazeGenerator.GetExitPosition(mazeSize, mazeIndex);
		
		// Récupération du nœud du labyrinthe
		Node3D mazeNode = GetNode<Node3D>($"/root/MainScene/MazeGenerator/Maze_{mazeIndex}");
		if (mazeNode == null) return;
		
		// Création des marqueurs horizontaux (de l'entrée à la sortie en X)
		for (int x = entrancePos.X; x <= exitPos.X; x++)
		{
			CreatePathMarker(mazeNode, x, entrancePos.Y, mazeIndex);
		}
		
		// Création des marqueurs verticaux (jusqu'à la sortie en Y)
		for (int y = entrancePos.Y; y <= exitPos.Y; y++)
		{
			CreatePathMarker(mazeNode, exitPos.X, y, mazeIndex);
		}
	}
	
	/**
	 * Crée un marqueur individuel pour le chemin
	 */
	private void CreatePathMarker(Node3D mazeNode, int x, int y, int mazeIndex)
	{
		var marker = new MeshInstance3D();
		marker.Name = $"PathMarker_{x}_{y}";
		
		// Création du mesh (cylindre lumineux)
		var cylinderMesh = new CylinderMesh();
		cylinderMesh.Height = 0.1f;
		cylinderMesh.BottomRadius = 0.2f;
		cylinderMesh.TopRadius = 0.2f;
		marker.Mesh = cylinderMesh;
		
		// Positionnement légèrement au-dessus du sol
		marker.Position = new Vector3(
			x * _mazeGenerator._cellSize,
			0.05f,
			y * _mazeGenerator._cellSize
		);
		
		// Matériau lumineux pour meilleure visibilité
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.2f, 0.8f, 1.0f, 0.7f); // Bleu transparent
		material.EmissionEnabled = true;
		material.Emission = new Color(0.2f, 0.5f, 1.0f);
		material.EmissionEnergyMultiplier = 2.0f;
		
		marker.MaterialOverride = material;
		
		// Ajout au labyrinthe
		mazeNode.AddChild(marker);
		
		// Conservation de la référence pour nettoyage ultérieur
		_pathMarkers.Add(marker);
		
		// Animation de pulsation
		var tween = CreateTween();
		tween.SetLoops();
		tween.TweenProperty(marker, "scale", new Vector3(1.2f, 1.2f, 1.2f), 0.5f);
		tween.TweenProperty(marker, "scale", new Vector3(0.8f, 0.8f, 0.8f), 0.5f);
	}
	
	/**
	 * Supprime tous les marqueurs du chemin
	 */
	private void ClearPathMarkers()
	{
		foreach (var marker in _pathMarkers)
		{
			if (marker != null && marker.IsInsideTree())
			{
				marker.QueueFree();
			}
		}
		
		_pathMarkers.Clear();
	}
	
	/**
	 * Réinitialise le gestionnaire pour une nouvelle partie
	 */
	public void Reset()
	{
		_remainingTime = _initialGameTime;
		_gameOver = false;
		ClearPathMarkers();
		UpdateTimeLabel();
	}
	
	/**
	 * Charge les scores à partir du fichier JSON
	 */
	private void LoadHighScores()
	{
		try
		{
			string filePath = "user://saves/highscores.json";
			
			if (Godot.FileAccess.FileExists(filePath))
			{
				using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Read);
				if (file != null)
				{
					string jsonContent = file.GetAsText();
					
					var loadedScores = JsonSerializer.Deserialize<List<ScoreEntry>>(jsonContent);
					if (loadedScores != null)
					{
						_highScores = loadedScores;
						
						// Tri des scores
						_highScores.Sort((a, b) => {
							int mazeCompare = b.mazesCompleted.CompareTo(a.mazesCompleted);
							if (mazeCompare != 0) return mazeCompare;
							return b.totalCats.CompareTo(a.totalCats);
						});
						
						// Limitation à 10 scores
						if (_highScores.Count > 10)
						{
							_highScores.RemoveRange(10, _highScores.Count - 10);
						}
						
						GD.Print($"GameManager: Chargement réussi de {_highScores.Count} scores depuis {filePath}");
					}
				}
			}
			else
			{
				GD.Print("GameManager: Aucun fichier de scores trouvé");
				_highScores = new List<ScoreEntry>();
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"GameManager: Erreur lors du chargement des scores: {e.Message}");
			_highScores = new List<ScoreEntry>();
		}
	}
	
	/**
	 * Ajoute un nouveau score
	 */
	public void AddScore(int mazes, int cats)
	{
		var newScore = new ScoreEntry(mazes, cats);
		
		_highScores.Add(newScore);
		
		// Tri des scores
		_highScores.Sort((a, b) => {
			int mazeCompare = b.mazesCompleted.CompareTo(a.mazesCompleted);
			if (mazeCompare != 0) return mazeCompare;
			return b.totalCats.CompareTo(a.totalCats);
		});
		
		// Limitation à 10 scores
		if (_highScores.Count > 10)
		{
			_highScores.RemoveRange(10, _highScores.Count - 10);
		}
		
		// Sauvegarde des scores
		SaveHighScores();
	}
	
	/**
	 * Sauvegarde les scores dans un fichier JSON
	 */
	private void SaveHighScores()
	{
		try 
		{
			// Création du répertoire de sauvegarde si nécessaire
			string saveDir = "user://saves";
			if (!Godot.DirAccess.DirExistsAbsolute(saveDir))
			{
				Godot.DirAccess.MakeDirAbsolute(saveDir);
			}
			
			string filePath = "user://saves/highscores.json";
			
			// Sérialisation en JSON
			var jsonOptions = new JsonSerializerOptions 
			{
				WriteIndented = true
			};
			string jsonString = JsonSerializer.Serialize(_highScores, jsonOptions);
			
			// Écriture dans le fichier
			using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
			if (file != null)
			{
				file.StoreString(jsonString);
				GD.Print($"GameManager: Scores sauvegardés avec succès dans {filePath}");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"GameManager: Erreur lors de la sauvegarde des scores: {e.Message}");
		}
	}
}
