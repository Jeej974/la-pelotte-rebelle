using Godot;
using System;
using System.Collections.Generic;

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
	
	// Temps de jeu restant
	private float _remainingTime;
	private bool _gameOver = false;
	
	// Signal émis quand le temps est écoulé
	[Signal]
	public delegate void GameOverEventHandler();
	
	// Signal émis quand un effet de chat est appliqué
	[Signal]
	public delegate void CatEffectAppliedEventHandler(CatType type, float value);
	
	// Référence au générateur de labyrinthe
	private VerticalMazeGenerator _mazeGenerator;
	
	// Nœuds pour l'affichage du chemin
	private List<Node3D> _pathMarkers = new List<Node3D>();
	
	public override void _Ready()
	{
		// Initialiser le temps de jeu
		_remainingTime = _initialGameTime;
		
		// Mettre à jour le label de temps
		UpdateTimeLabel();
		
		// Rechercher le générateur de labyrinthe
		CallDeferred(nameof(FindMazeGenerator));
	}
	
	private void FindMazeGenerator()
	{
		_mazeGenerator = GetTree().Root.FindChild("MazeGenerator", true, false) as VerticalMazeGenerator;
		if (_mazeGenerator == null)
		{
			GD.PrintErr("Générateur de labyrinthe non trouvé!");
		}
	}
	
	public override void _Process(double delta)
	{
		if (_gameOver || !_enableTimeLimit) return;
		
		// Réduire le temps restant
		_remainingTime -= (float)delta;
		
		// Mettre à jour l'affichage du temps
		UpdateTimeLabel();
		
		// Vérifier si le temps est écoulé
		if (_remainingTime <= 0)
		{
			_remainingTime = 0;
			_gameOver = true;
			EmitSignal(SignalName.GameOver);
			GD.Print("Temps écoulé! Game Over!");
		}
	}
	
	// Mettre à jour l'affichage du temps
	private void UpdateTimeLabel()
	{
		if (_timeLabel != null)
		{
			int minutes = (int)(_remainingTime / 60);
			int seconds = (int)(_remainingTime % 60);
			_timeLabel.Text = $"Temps: {minutes:00}:{seconds:00}";
			
			// Changer la couleur si le temps est presque écoulé
			if (_remainingTime < 30)
			{
				_timeLabel.Modulate = new Color(1, 0, 0); // Rouge
			}
			else if (_remainingTime < 60)
			{
				_timeLabel.Modulate = new Color(1, 0.5f, 0); // Orange
			}
			else
			{
				_timeLabel.Modulate = new Color(1, 1, 1); // Blanc
			}
		}
	}
	
	// Ajouter du temps (positif ou négatif)
	public void AddTime(float seconds)
	{
		if (_gameOver) return;
		
		_remainingTime += seconds;
		
		// S'assurer que le temps ne devient pas négatif
		if (_remainingTime < 0)
		{
			_remainingTime = 0;
		}
		
		// Afficher un message indiquant l'effet
		ShowCatEffect(seconds);
		
		// Émettre le signal pour informer d'autres composants
		EmitSignal(SignalName.CatEffectApplied, (int)CatType.Orange, seconds); // Type provisoire
	}
	
	// Afficher un message temporaire pour l'effet du chat
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
	
	// Révéler le chemin (effet du chat siamois)
	public void RevealPath()
	{
		if (_mazeGenerator == null)
		{
			GD.PrintErr("Impossible de révéler le chemin: MazeGenerator non trouvé");
			return;
		}
		
		// D'abord, supprimer les marqueurs de chemin existants
		ClearPathMarkers();
		
		// Obtenir le labyrinthe actuel
		int currentMazeIndex = GetCurrentMazeIndex();
		if (currentMazeIndex < 0) return;
		
		// Calculer le chemin du labyrinthe actuel
		CreatePathMarkers(currentMazeIndex);
		
		// Créer un timer pour supprimer les marqueurs après un certain temps
		var timer = new Timer();
		AddChild(timer);
		timer.WaitTime = 7.0f; // 7 secondes pour voir le chemin
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
	
	// Obtenir l'index du labyrinthe actuel
	private int GetCurrentMazeIndex()
	{
		// Cette fonction devrait être synchronisée avec la méthode équivalente dans MainScene
		// Pour simplifier, nous utilisons une approche similaire
		
		// Trouver le joueur
		var player = GetTree().Root.FindChild("PlayerBall", true, false) as PlayerBall;
		if (player == null)
		{
			GD.PrintErr("Joueur non trouvé!");
			return -1;
		}
		
		// Vérifier dans quel labyrinthe se trouve le joueur
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
	
	// Créer des marqueurs pour visualiser le chemin
	private void CreatePathMarkers(int mazeIndex)
	{
		// Obtenir la taille du labyrinthe
		int mazeSize = _mazeGenerator._mazeSizes[mazeIndex];
		
		// Récupérer les positions d'entrée et de sortie
		Vector2I entrancePos = _mazeGenerator.GetEntrancePosition(mazeSize, mazeIndex);
		Vector2I exitPos = _mazeGenerator.GetExitPosition(mazeSize, mazeIndex);
		
		// Récupérer le nœud du labyrinthe actuel
		Node3D mazeNode = GetNode<Node3D>($"/root/MainScene/MazeGenerator/Maze_{mazeIndex}");
		if (mazeNode == null) return;
		
		// Dans une implémentation réelle, nous utiliserions un algorithme de recherche de chemin
		// comme A* pour trouver le chemin optimal. Pour simplifier, nous allons créer un chemin direct.
		
		// Créer des marqueurs d'entrée à la sortie
		for (int x = entrancePos.X; x <= exitPos.X; x++)
		{
			CreatePathMarker(mazeNode, x, entrancePos.Y, mazeIndex);
		}
		
		for (int y = entrancePos.Y; y <= exitPos.Y; y++)
		{
			CreatePathMarker(mazeNode, exitPos.X, y, mazeIndex);
		}
	}
	
	// Créer un marqueur de chemin à une position spécifique
	private void CreatePathMarker(Node3D mazeNode, int x, int y, int mazeIndex)
	{
		var marker = new MeshInstance3D();
		marker.Name = $"PathMarker_{x}_{y}";
		
		// Créer un mesh pour le marqueur (un petit cylindre)
		var cylinderMesh = new CylinderMesh();
		cylinderMesh.Height = 0.1f;
		cylinderMesh.BottomRadius = 0.2f;
		cylinderMesh.TopRadius = 0.2f;
		marker.Mesh = cylinderMesh;
		
		// Positionner le marqueur légèrement au-dessus du sol
		marker.Position = new Vector3(
			x * _mazeGenerator._cellSize,
			0.05f,
			y * _mazeGenerator._cellSize
		);
		
		// Matériau brillant pour le marqueur
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.2f, 0.8f, 1.0f, 0.7f); // Bleu clair semi-transparent
		material.EmissionEnabled = true;
		material.Emission = new Color(0.2f, 0.5f, 1.0f);
		material.EmissionEnergyMultiplier = 2.0f;
		
		marker.MaterialOverride = material;
		
		// Ajouter le marqueur au labyrinthe
		mazeNode.AddChild(marker);
		
		// Ajouter à la liste pour pouvoir le supprimer plus tard
		_pathMarkers.Add(marker);
		
		// Animation de pulsation
		var tween = CreateTween();
		tween.SetLoops();
		tween.TweenProperty(marker, "scale", new Vector3(1.2f, 1.2f, 1.2f), 0.5f);
		tween.TweenProperty(marker, "scale", new Vector3(0.8f, 0.8f, 0.8f), 0.5f);
	}
	
	// Supprimer tous les marqueurs de chemin
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
}
