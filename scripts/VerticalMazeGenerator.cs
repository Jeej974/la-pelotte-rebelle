using Godot;
using System;
using System.Collections.Generic;

public partial class VerticalMazeGenerator : Node3D
{
	[Export]
	public float _cellSize = 2.0f;
	
	[Export]
	private float _horizontalSpacing = 10.0f;
	
	// Taille de base et incrément pour la génération infinie
	private const int BASE_MAZE_SIZE = 5;
	private const int MAZE_SIZE_INCREMENT = 2;
	private const int MAX_MAZES = 100; // Pour éviter tout problème de performance
	
	// Nous utilisons maintenant une liste dynamique au lieu d'un tableau fixe
	// Les tailles seront calculées dynamiquement - accessible publiquement
	public readonly List<int> _mazeSizes = new List<int>();
	
	// Configuration des murs
	[Export]
	private float _wallHeight = 2.0f;
	
	[Export]
	private float _wallThickness = 0.2f;
	
	// Structure de données pour représenter le labyrinthe
	private enum CellWall
	{
		None = 0,
		Up = 1,
		Right = 2,
		Down = 4,
		Left = 8,
		Visited = 16
	}
	
	// Point d'alignement pour tous les labyrinthes
	private Vector2I _alignmentPoint = new Vector2I(1, 1);
	
	// Liste pour stocker les textures de mur disponibles
	private List<Texture2D> _wallTextures = new List<Texture2D>();
	
	// Dictionnaire pour associer un index de labyrinthe à sa texture
	private Dictionary<int, Texture2D> _mazeWallTextures = new Dictionary<int, Texture2D>();
	
	// Liste pour suivre quels labyrinthes ont été générés
	private List<int> _generatedMazes = new List<int>();
	
	// Largeur totale des labyrinthes déjà générés
	private float _totalWidth = 0;
	
	// Nombre initial de labyrinthes à générer
	private const int INITIAL_MAZE_COUNT = 4;
	
	// Signal émis quand le joueur entre dans un nouveau labyrinthe
	[Signal]
	public delegate void PlayerEnteredMazeEventHandler(int mazeIndex);
	
	// Méthode publique pour accéder au nombre total de labyrinthes
	public int GetTotalMazeCount()
	{
		return _mazeSizes.Count;
	}
	
	// Méthode publique pour obtenir la taille d'un labyrinthe spécifique
	public int GetMazeSize(int mazeIndex)
	{
		if (mazeIndex >= 0 && mazeIndex < _mazeSizes.Count)
		{
			return _mazeSizes[mazeIndex];
		}
		
		// Si l'index est hors limites mais que nous générons des labyrinthes infinis,
		// calculer la taille qu'il aurait
		if (mazeIndex >= 0)
		{
			return BASE_MAZE_SIZE + (mazeIndex * MAZE_SIZE_INCREMENT);
		}
		
		return BASE_MAZE_SIZE; // Valeur par défaut
	}
	
	public override void _Ready()
	{
		// Initialiser les tailles des labyrinthes
		GenerateMazeSizes();
		
		// Charger toutes les textures de mur
		LoadWallTextures();
		
		// Générer seulement les premiers labyrinthes au départ
		for (int i = 0; i < INITIAL_MAZE_COUNT; i++)
		{
			GenerateNextMaze(i);
		}
		
		// Ajoute une caméra pour voir l'ensemble des labyrinthes
		AddGlobalCamera();
		
		// Ajoute un éclairage de base
		AddBasicLighting();
		
		// S'abonner au signal de téléportation pour générer le prochain labyrinthe
		this.Connect("PlayerEnteredMaze", new Callable(this, nameof(OnPlayerEnteredMaze)));
		
		GD.Print($"VerticalMazeGenerator initialisé - {INITIAL_MAZE_COUNT} labyrinthes générés");
	}
	
	// Charger toutes les textures de mur
	private void LoadWallTextures()
	{
		_wallTextures.Clear();
		
		// Charger les textures en utilisant le format correct (Asset_X.png)
		for (int i = 1; i <= 49; i++)
		{
			string texturePath = $"res://assets/wall/Textures/Asset_{i}.png";
			var texture = ResourceLoader.Load<Texture2D>(texturePath);
			
			if (texture != null)
			{
				_wallTextures.Add(texture);
				GD.Print($"Texture de mur chargée: {texturePath}");
			}
			else
			{
				// Essayer un format alternatif au cas où
				texturePath = $"res://assets/wall/Textures/Asset {i}.png";
				texture = ResourceLoader.Load<Texture2D>(texturePath);
				
				if (texture != null)
				{
					_wallTextures.Add(texture);
					GD.Print($"Texture de mur chargée: {texturePath}");
				}
				else
				{
					GD.PrintErr($"Impossible de charger la texture de mur: {i}");
				}
			}
		}
		
		// Si aucune texture n'a été chargée, utiliser la texture par défaut
		if (_wallTextures.Count == 0)
		{
			var defaultTexture = ResourceLoader.Load<Texture2D>("res://assets/wall/wall-texture.png");
			if (defaultTexture != null)
			{
				_wallTextures.Add(defaultTexture);
				GD.Print("Utilisation de la texture de mur par défaut");
			}
			else
			{
				GD.PrintErr("Aucune texture de mur n'a pu être chargée!");
			}
		}
		
		GD.Print($"Total des textures de mur chargées: {_wallTextures.Count}");
	}
	
	// Sélectionner une texture aléatoire pour un labyrinthe
	private Texture2D GetRandomWallTexture(int mazeIndex)
	{
		// Si nous avons déjà sélectionné une texture pour ce labyrinthe, la renvoyer
		if (_mazeWallTextures.ContainsKey(mazeIndex))
		{
			return _mazeWallTextures[mazeIndex];
		}
		
		// Sinon, sélectionner une texture aléatoire
		if (_wallTextures.Count > 0)
		{
			// Utiliser l'index comme seed pour la génération aléatoire, mais avec une source unique
			int tickCount = (int)(Time.GetTicksMsec() % int.MaxValue);
			Random random = new Random(mazeIndex + tickCount);
			int textureIndex = random.Next(0, _wallTextures.Count);
			
			Texture2D selectedTexture = _wallTextures[textureIndex];
			_mazeWallTextures[mazeIndex] = selectedTexture;
			
			GD.Print($"Texture {textureIndex + 1} sélectionnée pour le labyrinthe {mazeIndex}");
			
			return selectedTexture;
		}
		else
		{
			// Cas où aucune texture n'est disponible
			GD.PrintErr($"Aucune texture disponible pour le labyrinthe {mazeIndex}");
			return null;
		}
	}
	
	// Initialiser la liste des tailles de labyrinthe
	private void GenerateMazeSizes()
	{
		_mazeSizes.Clear();
		
		for (int i = 0; i < MAX_MAZES; i++)
		{
			int size = BASE_MAZE_SIZE + (i * MAZE_SIZE_INCREMENT);
			_mazeSizes.Add(size);
		}
		
		GD.Print($"Tailles de labyrinthe générées: de {_mazeSizes[0]} à {_mazeSizes[_mazeSizes.Count - 1]}");
	}
	
	// Méthode pour générer un labyrinthe spécifique
	private void GenerateNextMaze(int mazeIndex)
	{
		// Vérifier si ce labyrinthe a déjà été généré
		if (_generatedMazes.Contains(mazeIndex) || mazeIndex >= MAX_MAZES)
		{
			// GD.Print($"Labyrinthe {mazeIndex} déjà généré ou index invalide");
			return;
		}
		
		// S'assurer que nous avons assez d'éléments dans la liste des tailles
		while (_mazeSizes.Count <= mazeIndex)
		{
			int lastSize = _mazeSizes.Count > 0 ? _mazeSizes[_mazeSizes.Count - 1] : BASE_MAZE_SIZE;
			_mazeSizes.Add(lastSize + MAZE_SIZE_INCREMENT);
		}
		
		// Générer le labyrinthe
		var mazeNode = GenerateMaze(mazeIndex);
		
		// Positionner le labyrinthe
		mazeNode.Position = new Vector3(_totalWidth, 0, 0);
		
		// Ajouter directement le nœud sans CallDeferred pour s'assurer qu'il est immédiatement disponible
		AddChild(mazeNode);
		
		// Mettre à jour la largeur totale
		int size = _mazeSizes[mazeIndex];
		_totalWidth += (size * _cellSize) + _horizontalSpacing;
		
		// Marquer ce labyrinthe comme généré
		_generatedMazes.Add(mazeIndex);
		
		GD.Print($"Labyrinthe {mazeIndex} généré à la position X={_totalWidth - (size * _cellSize) - _horizontalSpacing}, taille={size}x{size}");
	}
	
	// Méthode appelée quand le joueur entre dans un nouveau labyrinthe
	private void OnPlayerEnteredMaze(int mazeIndex)
	{
		// Générer le labyrinthe suivant si nécessaire
		int nextMazeIndex = mazeIndex + 2; // Générer 2 labyrinthes en avance
		
		if (nextMazeIndex < MAX_MAZES && !_generatedMazes.Contains(nextMazeIndex))
		{
			GD.Print($"Joueur dans le labyrinthe {mazeIndex}, génération du labyrinthe {nextMazeIndex}");
			GenerateNextMaze(nextMazeIndex);
		}
		
		// Générer également le labyrinthe après pour avoir plus d'avance (labyrinthe N+3)
		int nextNextMazeIndex = mazeIndex + 3;
		if (nextNextMazeIndex < MAX_MAZES && !_generatedMazes.Contains(nextNextMazeIndex))
		{
			GD.Print($"Génération du labyrinthe {nextNextMazeIndex} en prévision");
			GenerateNextMaze(nextNextMazeIndex);
		}
	}
	
	private Node3D GenerateMaze(int mazeIndex)
	{
		int size = _mazeSizes[mazeIndex];
		
		var mazeNode = new Node3D();
		mazeNode.Name = $"Maze_{mazeIndex}";
		
		// Génère la structure du labyrinthe
		CellWall[,] mazeGrid = new CellWall[size, size];
		
		// Initialise toutes les cellules avec tous les murs
		for (int x = 0; x < size; x++)
		{
			for (int y = 0; y < size; y++)
			{
				mazeGrid[x, y] = CellWall.Up | CellWall.Right | CellWall.Down | CellWall.Left;
			}
		}
		
		// Applique l'algorithme de génération de labyrinthe récursif
		Random random = new Random();
		Stack<Vector2I> cellStack = new Stack<Vector2I>();
		
		// Commence par le point d'alignement (ajusté pour ce labyrinthe spécifique)
		Vector2I current = new Vector2I(
			Math.Min(_alignmentPoint.X, size - 2),
			Math.Min(_alignmentPoint.Y, size - 2)
		);
		
		if (current.X < 1) current.X = 1;
		if (current.Y < 1) current.Y = 1;
		
		// Marque la cellule de départ comme visitée
		mazeGrid[current.X, current.Y] |= CellWall.Visited;
		
		// Compte des cellules non visitées
		int unvisitedCells = size * size - 1;
		
		while (unvisitedCells > 0)
		{
			// Trouve tous les voisins non visités
			List<Vector2I> neighbors = new List<Vector2I>();
			
			// Vérifie les 4 directions
			if (current.Y > 0 && (mazeGrid[current.X, current.Y - 1] & CellWall.Visited) == 0)
				neighbors.Add(new Vector2I(current.X, current.Y - 1));
			
			if (current.X < size - 1 && (mazeGrid[current.X + 1, current.Y] & CellWall.Visited) == 0)
				neighbors.Add(new Vector2I(current.X + 1, current.Y));
			
			if (current.Y < size - 1 && (mazeGrid[current.X, current.Y + 1] & CellWall.Visited) == 0)
				neighbors.Add(new Vector2I(current.X, current.Y + 1));
			
			if (current.X > 0 && (mazeGrid[current.X - 1, current.Y] & CellWall.Visited) == 0)
				neighbors.Add(new Vector2I(current.X - 1, current.Y));
			
			if (neighbors.Count > 0)
			{
				// Choisit un voisin aléatoirement
				Vector2I next = neighbors[random.Next(neighbors.Count)];
				
				// Ajoute la cellule actuelle à la pile
				cellStack.Push(current);
				
				// Supprime les murs entre les cellules
				if (next.X < current.X)
				{
					// Voisin à gauche
					mazeGrid[current.X, current.Y] &= ~CellWall.Left;
					mazeGrid[next.X, next.Y] &= ~CellWall.Right;
				}
				else if (next.X > current.X)
				{
					// Voisin à droite
					mazeGrid[current.X, current.Y] &= ~CellWall.Right;
					mazeGrid[next.X, next.Y] &= ~CellWall.Left;
				}
				else if (next.Y < current.Y)
				{
					// Voisin en haut
					mazeGrid[current.X, current.Y] &= ~CellWall.Up;
					mazeGrid[next.X, next.Y] &= ~CellWall.Down;
				}
				else if (next.Y > current.Y)
				{
					// Voisin en bas
					mazeGrid[current.X, current.Y] &= ~CellWall.Down;
					mazeGrid[next.X, next.Y] &= ~CellWall.Up;
				}
				
				// Marque la nouvelle cellule comme visitée
				mazeGrid[next.X, next.Y] |= CellWall.Visited;
				unvisitedCells--;
				
				// Déplace vers la nouvelle cellule
				current = next;
			}
			else if (cellStack.Count > 0)
			{
				// Si pas de voisins, remonte la pile
				current = cellStack.Pop();
			}
			else
			{
				// Si la pile est vide, trouve une cellule non visitée
				bool found = false;
				for (int x = 0; x < size && !found; x++)
				{
					for (int y = 0; y < size && !found; y++)
					{
						if ((mazeGrid[x, y] & CellWall.Visited) == 0)
						{
							current = new Vector2I(x, y);
							mazeGrid[x, y] |= CellWall.Visited;
							unvisitedCells--;
							found = true;
						}
					}
				}
				
				// Si toutes les cellules sont visitées, sortir
				if (!found) break;
			}
		}
		
		// Crée une entrée et une sortie
		Vector2I entrancePos = GetEntrancePosition(size, mazeIndex);
		Vector2I exitPos = GetExitPosition(size, mazeIndex);
		
		// Ouvre l'entrée (supprime le mur du haut)
		mazeGrid[entrancePos.X, entrancePos.Y] &= ~CellWall.Up;
		
		// Ouvre la sortie (supprime le mur du bas)
		mazeGrid[exitPos.X, exitPos.Y] &= ~CellWall.Down;
		
		// Initialise une structure pour le placement des chats
		MazeCell[][] cells = new MazeCell[size][];
		for (int x = 0; x < size; x++)
		{
			cells[x] = new MazeCell[size];
			for (int y = 0; y < size; y++)
			{
				cells[x][y] = new MazeCell(new Vector2I(x, y));
			}
		}
		
		// Place les chats dans le labyrinthe
		PlaceCatsInMaze(cells, mazeGrid, size, mazeIndex, entrancePos, exitPos);
		
		// Construit le labyrinthe 3D
		BuildMaze3D(mazeNode, mazeGrid, cells, size, entrancePos, exitPos, mazeIndex);
		
		// Ajoute une étiquette pour le labyrinthe
		AddMazeLabel(mazeNode, mazeIndex, size);
		
		// Créer les téléporteurs
		CreateTeleportZones(mazeNode, mazeIndex, size, entrancePos, exitPos);
		
		return mazeNode;
	}
	
	// Nouvelle classe pour stocker les informations de cellule
	private class MazeCell
	{
		public Vector2I Position;
		public bool HasCat;
		public CatType CatType;
		
		public MazeCell(Vector2I pos)
		{
			Position = pos;
			HasCat = false;
		}
	}
	
	// Nouvelle méthode pour placer les chats dans le labyrinthe
	private void PlaceCatsInMaze(MazeCell[][] cells, CellWall[,] grid, int size, int mazeIndex, Vector2I entrance, Vector2I exit)
	{
		// Déterminer le nombre de chats pour ce labyrinthe
		int catCount = Cat.GetCatCountForMaze(mazeIndex);
		
		// Créer une liste de positions possibles (exclure l'entrée et la sortie)
		List<Vector2I> availablePositions = new List<Vector2I>();
		
		for (int x = 0; x < size; x++)
		{
			for (int y = 0; y < size; y++)
			{
				Vector2I pos = new Vector2I(x, y);
				
				// Exclure l'entrée, la sortie et les cellules adjacentes
				if (!IsNearEntryOrExit(pos, entrance, exit, 1))
				{
					availablePositions.Add(pos);
				}
			}
		}
		
		// Mélanger les positions disponibles
		Random random = new Random();
		for (int i = 0; i < availablePositions.Count; i++)
		{
			int j = random.Next(i, availablePositions.Count);
			Vector2I temp = availablePositions[i];
			availablePositions[i] = availablePositions[j];
			availablePositions[j] = temp;
		}
		
		// Limiter le nombre de chats en fonction des positions disponibles
		catCount = Math.Min(catCount, availablePositions.Count);
		
		// Placer les chats
		for (int i = 0; i < catCount; i++)
		{
			Vector2I pos = availablePositions[i];
			
			// Déterminer le type de chat en fonction du niveau
			CatType catType = Cat.GetRandomType(mazeIndex);
			
			// Marquer la cellule comme contenant un chat
			cells[pos.X][pos.Y].HasCat = true;
			cells[pos.X][pos.Y].CatType = catType;
			
			GD.Print($"Chat {catType} placé en position {pos} dans le labyrinthe {mazeIndex}");
		}
	}
	
	// Vérifier si une position est proche de l'entrée ou de la sortie
	private bool IsNearEntryOrExit(Vector2I pos, Vector2I entrance, Vector2I exit, int distance)
	{
		return (Math.Abs(pos.X - entrance.X) <= distance && Math.Abs(pos.Y - entrance.Y) <= distance) ||
			   (Math.Abs(pos.X - exit.X) <= distance && Math.Abs(pos.Y - exit.Y) <= distance);
	}
	
	public Vector2I GetEntrancePosition(int size, int mazeIndex)
	{
		if (mazeIndex == 0)
		{
			// Pour le premier labyrinthe, utilise le point d'alignement
			return new Vector2I(
				Math.Min(_alignmentPoint.X, size - 2),
				0
			);
		}
		else
		{
			// Pour les labyrinthes suivants, utilise la position de sortie du précédent
			Vector2I prevExit = GetExitPosition(_mazeSizes[mazeIndex - 1], mazeIndex - 1);
			
			// Ajuste pour le nouveau labyrinthe
			return new Vector2I(
				Math.Min(prevExit.X, size - 2),
				0
			);
		}
	}
	
	public Vector2I GetExitPosition(int size, int mazeIndex)
	{
		// La sortie est à la même position X que l'entrée, mais en bas du labyrinthe
		Vector2I entrance = GetEntrancePosition(size, mazeIndex);
		return new Vector2I(entrance.X, size - 1);
	}
	
	// Méthode pour le marqueur visuel simplifié (utilisée pour l'entrée et la sortie)
	private void CreateMarker(Node3D mazeNode, int x, int y, Color color, bool isExit = false)
	{
		// Utiliser le modèle de téléporteur pour l'entrée et la sortie
		// Récupérer l'index du labyrinthe depuis le nom du nœud
		int mazeIndex = 0;
		string mazeName = mazeNode.Name;
		string[] parts = mazeName.Split('_');
		if (parts.Length > 1)
		{
			if (int.TryParse(parts[1], out int index))
			{
				mazeIndex = index;
			}
		}
		
		CreateTeleporterGate(mazeNode, x, y, isExit ? new Color(0, 1, 0) : new Color(1, 0, 0), isExit, mazeIndex);
	}

	// MÉTHODE AMÉLIORÉE: Ajouter un indicateur lumineux pour le téléporteur
	private void AddStaticIndicator(Node3D teleporter, Color color)
	{
		// Créer un point lumineux au-dessus du téléporteur
		var omniLight = new OmniLight3D();
		omniLight.Name = "TeleporterLight";
		
		// Configuration de la lumière avec une intensité améliorée
		omniLight.LightColor = color;
		omniLight.LightEnergy = 2.0f;  // Augmenter l'énergie pour plus de visibilité
		omniLight.OmniRange = 3.0f;    // Augmenter la portée pour un effet plus visible
		omniLight.ShadowEnabled = true; // Activer les ombres pour un meilleur effet visuel
		
		// Ajouter un effet de clignotement pour attirer l'attention
		var animationPlayer = new AnimationPlayer();
		teleporter.AddChild(animationPlayer);
		
		var pulseAnimation = new Animation();
		var trackIdx = pulseAnimation.AddTrack(Animation.TrackType.Value);
		pulseAnimation.TrackSetPath(trackIdx, "%TeleporterLight:light_energy");
		
		// Animation de pulsation de la lumière (sur 2 secondes)
		pulseAnimation.TrackInsertKey(trackIdx, 0.0f, 2.0f);
		pulseAnimation.TrackInsertKey(trackIdx, 1.0f, 4.0f);
		pulseAnimation.TrackInsertKey(trackIdx, 2.0f, 2.0f);
		pulseAnimation.LoopMode = Animation.LoopModeEnum.Linear;
		
		// Créer une librairie d'animation
		var animLib = new AnimationLibrary();
		animLib.AddAnimation("pulse", pulseAnimation);
		
		// Ajouter la librairie au player d'animation
		animationPlayer.AddAnimationLibrary("", animLib);
		
		// Positionner la lumière au-dessus du téléporteur
		omniLight.Position = new Vector3(0, 2.0f, 0);
		
		// Ajouter la lumière au téléporteur
		teleporter.AddChild(omniLight);
		
		// Démarrer l'animation
		animationPlayer.Play("pulse");
		
		// Ajouter également quelques particules pour rendre le téléporteur plus visible
		AddTeleporterParticles(teleporter, color);
	}

	// Méthode pour appliquer une couleur au téléporteur
	private void ApplyColorToTeleporter(Node3D teleporter, Color color)
	{
		// Rechercher tous les MeshInstance3D dans le modèle
		foreach (Node child in teleporter.GetChildren())
		{
			ApplyColorToNode(child, color);
		}
	}

	// Méthode récursive pour appliquer une couleur à tous les meshes
	private void ApplyColorToNode(Node node, Color color)
	{
		if (node is MeshInstance3D meshInstance)
		{
			// Créer un nouveau matériau basé sur le matériau existant
			Material existingMaterial = meshInstance.GetActiveMaterial(0);
			StandardMaterial3D material = new StandardMaterial3D();
			
			// Paramètres de base
			material.AlbedoColor = color;
			material.Metallic = 0.5f;
			material.Roughness = 0.3f;
			material.EmissionEnabled = true;
			material.Emission = new Color(color.R, color.G, color.B, 0.5f);
			material.EmissionEnergyMultiplier = 0.3f;
			
			// Appliquer le matériau
			meshInstance.MaterialOverride = material;
		}
		
		// Appliquer récursivement aux enfants
		foreach (Node child in node.GetChildren())
		{
			ApplyColorToNode(child, color);
		}
	}

	// Amélioration des particules du téléporteur
	private void AddTeleporterParticles(Node3D teleporter, Color color)
	{
		var particles = new GpuParticles3D();
		particles.Name = "TeleporterParticles";
		
		// Configurer les particules
		var particlesMaterial = new ParticleProcessMaterial();
		particlesMaterial.Direction = new Vector3(0, 1, 0);
		particlesMaterial.Spread = 45.0f;
		particlesMaterial.InitialVelocityMin = 0.2f;
		particlesMaterial.InitialVelocityMax = 0.5f;
		particlesMaterial.Color = color;
		particlesMaterial.Gravity = new Vector3(0, 0.2f, 0);
		particlesMaterial.ScaleMin = 0.2f;
		particlesMaterial.ScaleMax = 0.4f;
		
		particles.ProcessMaterial = particlesMaterial;
		
		// Configurer le mesh pour les particules
		var sphereMesh = new SphereMesh();
		sphereMesh.Radius = 0.05f;
		sphereMesh.Height = 0.1f;
		particles.DrawPass1 = sphereMesh;
		
		// Régler les paramètres d'émission
		particles.Amount = 30;          // Plus de particules
		particles.Lifetime = 2.0f;
		particles.OneShot = false;
		particles.Explosiveness = 0.1f;
		particles.FixedFps = 30;        // Fixer les FPS pour améliorer les performances
		
		// Positionner les particules au centre du téléporteur
		particles.Position = new Vector3(0, 1.5f, 0);
		
		teleporter.AddChild(particles);
	}
	
	// BuildMaze3D avec les ajustements pour améliorer les téléporteurs et les murs
	private void BuildMaze3D(Node3D mazeNode, CellWall[,] grid, MazeCell[][] cells, int size, Vector2I entrance, Vector2I exit, int mazeIndex)
	{
		// Crée le sol
		CreateFloor(mazeNode, size);
		
		// Pour chaque cellule, vérifie quels murs doivent être construits
		for (int x = 0; x < size; x++)
		{
			for (int y = 0; y < size; y++)
			{
				CellWall cell = grid[x, y];
				
				// Crée les murs pour cette cellule
				if ((cell & CellWall.Up) != 0)
					CreateWallSegment(mazeNode, x, y, true, false, mazeIndex);
				
				if ((cell & CellWall.Right) != 0)
					CreateWallSegment(mazeNode, x, y, false, true, mazeIndex);
				
				if ((cell & CellWall.Down) != 0)
					CreateWallSegment(mazeNode, x, y, true, true, mazeIndex);
				
				if ((cell & CellWall.Left) != 0)
					CreateWallSegment(mazeNode, x, y, false, false, mazeIndex);
				
				// Placer un chat si la cellule en contient un
				if (cells[x][y].HasCat)
				{
					CreateCat(mazeNode, x, y, cells[x][y].CatType);
				}
			}
		}
		
		// Ajoute les marqueurs d'entrée et de sortie
		CreateMarker(mazeNode, entrance.X, entrance.Y, new Color(0, 1, 0)); // Vert pour l'entrée
		CreateMarker(mazeNode, exit.X, exit.Y, new Color(1, 0, 0), true); // Rouge pour la sortie
		
		// Ajoute des murs autour du périmètre du labyrinthe
		AddPerimeterWalls(mazeNode, size, entrance, exit, mazeIndex);
		
		// Ajoute les murs d'entrée et de sortie
		AddMazeWalls(mazeNode, size, entrance, exit, mazeIndex);
	}
	// Méthode pour ajouter des murs d'entrée et de sortie sur TOUS les labyrinthes
	private void AddMazeWalls(Node3D mazeNode, int size, Vector2I entrance, Vector2I exit, int mazeIndex)
	{
		// Ajouter un mur d'entrée pour tous les labyrinthes
		AddEntranceWall(mazeNode, size, entrance, mazeIndex);
		
		// Ajouter un mur de sortie pour tous les labyrinthes
		AddExitWall(mazeNode, size, exit, mazeIndex);
	}

	// Méthode pour ajouter un mur à l'entrée du labyrinthe
	private void AddEntranceWall(Node3D mazeNode, int size, Vector2I entrancePos, int mazeIndex)
	{
		// Créer un mur devant l'entrée du labyrinthe
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Taille du mur
		boxMesh.Size = new Vector3(_cellSize, _wallHeight, _wallThickness);
		
		// Positionner le mur juste devant l'entrée
		meshInstance.Position = new Vector3(0, _wallHeight / 2, -_cellSize / 2 - _wallThickness / 2);
		
		meshInstance.Mesh = boxMesh;
		
		// Utiliser la texture spécifique à ce labyrinthe
		var material = new StandardMaterial3D();
		Texture2D wallTexture = GetRandomWallTexture(mazeIndex);
		
		if (wallTexture != null)
		{
			material.AlbedoTexture = wallTexture;
			material.Uv1Scale = new Vector3(2.0f, 1.0f, 1.0f);
		}
		else
		{
			material.AlbedoColor = new Color(0.5f, 0.3f, 0.2f); // Marron
		}
		
		meshInstance.MaterialOverride = material;
		
		// Collision
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = boxMesh.Size;
		collisionShape.Shape = boxShape;
		collisionShape.Position = meshInstance.Position;
		
		wall.AddChild(meshInstance);
		wall.AddChild(collisionShape);
		
		// Position du mur (à l'entrée du labyrinthe)
		wall.Position = new Vector3(
			entrancePos.X * _cellSize,
			0,
			entrancePos.Y * _cellSize
		);
		
		mazeNode.AddChild(wall);
		
		GD.Print($"Mur d'entrée ajouté au labyrinthe {mazeIndex}");
	}

	// Méthode pour ajouter un mur à la sortie du labyrinthe
	private void AddExitWall(Node3D mazeNode, int size, Vector2I exitPos, int mazeIndex)
	{
		// Créer un mur après la sortie du labyrinthe
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Taille du mur
		boxMesh.Size = new Vector3(_cellSize, _wallHeight, _wallThickness);
		
		// Positionner le mur juste après la sortie
		meshInstance.Position = new Vector3(0, _wallHeight / 2, _cellSize / 2 + _wallThickness / 2);
		
		meshInstance.Mesh = boxMesh;
		
		// Utiliser la texture spécifique à ce labyrinthe
		var material = new StandardMaterial3D();
		Texture2D wallTexture = GetRandomWallTexture(mazeIndex);
		
		if (wallTexture != null)
		{
			material.AlbedoTexture = wallTexture;
			material.Uv1Scale = new Vector3(2.0f, 1.0f, 1.0f);
		}
		else
		{
			material.AlbedoColor = new Color(0.5f, 0.3f, 0.2f); // Marron
		}
		
		meshInstance.MaterialOverride = material;
		
		// Collision
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = boxMesh.Size;
		collisionShape.Shape = boxShape;
		collisionShape.Position = meshInstance.Position;
		
		wall.AddChild(meshInstance);
		wall.AddChild(collisionShape);
		
		// Position du mur (à la sortie du labyrinthe)
		wall.Position = new Vector3(
			exitPos.X * _cellSize,
			0,
			exitPos.Y * _cellSize
		);
		
		mazeNode.AddChild(wall);
		
		GD.Print($"Mur de sortie ajouté au labyrinthe {mazeIndex}");
	}
	
	
	
	// Nouvelle méthode pour créer un chat dans le labyrinthe
	private void CreateCat(Node3D mazeNode, int x, int y, CatType catType)
	{
		// Charger la scène de chat préfabriquée
		var catScene = ResourceLoader.Load<PackedScene>("res://scenes/Cat.tscn");
		if (catScene == null)
		{
			GD.PrintErr("Erreur: impossible de charger la scène du chat!");
			return;
		}
		
		// Instancier la scène
		var cat = catScene.Instantiate<Cat>();
		if (cat == null)
		{
			GD.PrintErr("Erreur: échec de l'instanciation du chat!");
			return;
		}
		
		// Configurer le type de chat
		cat.Set("_catType", (int)catType);
		
		// Nommer le chat pour faciliter le débogage
		cat.Name = $"Cat_{x}_{y}_{catType}";
		
		// Positionner le chat dans la cellule
		cat.Position = new Vector3(
			x * _cellSize,
			0.1f, // Légèrement au-dessus du sol
			y * _cellSize
		);
		
		// Ajouter le chat au nœud du labyrinthe
		// Utiliser CallDeferred pour éviter les problèmes pendant la génération du labyrinthe
		mazeNode.CallDeferred(Node.MethodName.AddChild, cat);
		
		GD.Print($"Chat {catType} créé en position ({x}, {y})");
	}
	
	private void AddPerimeterWalls(Node3D mazeNode, int size, Vector2I entrance, Vector2I exit, int mazeIndex)
	{
		// Murs du haut sauf à l'entrée
		for (int x = 0; x < size; x++)
		{
			if (x != entrance.X) // Ne pas créer de mur à l'entrée
			{
				CreateBoundaryWall(mazeNode, x, 0, true, false, mazeIndex);
			}
		}
		
		// Murs du bas sauf à la sortie
		for (int x = 0; x < size; x++)
		{
			if (x != exit.X) // Ne pas créer de mur à la sortie
			{
				CreateBoundaryWall(mazeNode, x, size - 1, true, true, mazeIndex);
			}
		}
		
		// Murs de gauche
		for (int y = 0; y < size; y++)
		{
			CreateBoundaryWall(mazeNode, 0, y, false, false, mazeIndex);
		}
		
		// Murs de droite
		for (int y = 0; y < size; y++)
		{
			CreateBoundaryWall(mazeNode, size - 1, y, false, true, mazeIndex);
		}
	}
	
	private void CreateWallSegment(Node3D mazeNode, int x, int y, bool horizontal, bool positive, int mazeIndex)
	{
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Taille du segment de mur
		float length = _cellSize;
		float width = _wallThickness;
		
		if (horizontal)
		{
			// Mur horizontal
			boxMesh.Size = new Vector3(length, _wallHeight, width);
			
			float offsetZ = positive ? _cellSize / 2 : -_cellSize / 2;
			
			meshInstance.Position = new Vector3(
				0,
				_wallHeight / 2,
				offsetZ
			);
		}
		else
		{
			// Mur vertical
			boxMesh.Size = new Vector3(width, _wallHeight, length);
			
			float offsetX = positive ? _cellSize / 2 : -_cellSize / 2;
			
			meshInstance.Position = new Vector3(
				offsetX,
				_wallHeight / 2,
				0
			);
		}
		
		meshInstance.Mesh = boxMesh;
		
		// Utiliser la texture spécifique à ce labyrinthe
		var material = new StandardMaterial3D();
		Texture2D wallTexture = GetRandomWallTexture(mazeIndex);
		
		if (wallTexture != null)
		{
			material.AlbedoTexture = wallTexture;
			// Répétition de la texture
			material.Uv1Scale = new Vector3(2.0f, 1.0f, 1.0f);
		}
		else
		{
			material.AlbedoColor = new Color(0.5f, 0.3f, 0.2f); // Marron par défaut
		}
		
		meshInstance.MaterialOverride = material;
		
		// Collision
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = boxMesh.Size;
		collisionShape.Shape = boxShape;
		collisionShape.Position = meshInstance.Position;
		
		wall.AddChild(meshInstance);
		wall.AddChild(collisionShape);
		
		// Position dans la grille
		wall.Position = new Vector3(
			x * _cellSize,
			0,
			y * _cellSize
		);
		
		mazeNode.AddChild(wall);
	}
	
	private void CreateBoundaryWall(Node3D mazeNode, int x, int y, bool horizontal, bool positive, int mazeIndex)
	{
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Taille du segment de mur (plus épais pour le périmètre)
		float length = _cellSize;
		float width = _wallThickness * 1.5f; // Mur de périmètre plus épais
		
		if (horizontal)
		{
			// Mur horizontal
			boxMesh.Size = new Vector3(length, _wallHeight, width);
			
			float offsetZ = positive ? _cellSize / 2 : -_cellSize / 2;
			
			meshInstance.Position = new Vector3(
				0,
				_wallHeight / 2,
				offsetZ
			);
		}
		else
		{
			// Mur vertical
			boxMesh.Size = new Vector3(width, _wallHeight, length);
			
			float offsetX = positive ? _cellSize / 2 : -_cellSize / 2;
			
			meshInstance.Position = new Vector3(
				offsetX,
				_wallHeight / 2,
				0
			);
		}
		
		meshInstance.Mesh = boxMesh;
		
		// Utiliser la texture spécifique à ce labyrinthe
		var material = new StandardMaterial3D();
		Texture2D wallTexture = GetRandomWallTexture(mazeIndex);
		
		if (wallTexture != null)
		{
			material.AlbedoTexture = wallTexture;
			// Répétition de la texture
			material.Uv1Scale = new Vector3(2.0f, 1.0f, 1.0f);
		}
		else
		{
			material.AlbedoColor = new Color(0.4f, 0.2f, 0.1f); // Marron foncé pour les murs de périmètre
		}
		
		meshInstance.MaterialOverride = material;
		
		// Collision
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = boxMesh.Size;
		collisionShape.Shape = boxShape;
		collisionShape.Position = meshInstance.Position;
		
		wall.AddChild(meshInstance);
		wall.AddChild(collisionShape);
		
		// Position dans la grille
		wall.Position = new Vector3(
			x * _cellSize,
			0,
			y * _cellSize
		);
		
		mazeNode.AddChild(wall);
	}
	
	private void CreateFloor(Node3D mazeNode, int size)
	{
		// 1. Créer un StaticBody3D pour les collisions
		var staticBody = new StaticBody3D();
		staticBody.Name = "Floor";
		
		// 2. Créer le mesh visuel
		var meshInstance = new MeshInstance3D();
		var planeMesh = new PlaneMesh();
		
		planeMesh.Size = new Vector2(size * _cellSize, size * _cellSize);
		meshInstance.Mesh = planeMesh;
		
		// 3. Position du sol
		Vector3 floorPosition = new Vector3(
			(size * _cellSize) / 2 - _cellSize / 2,
			0,
			(size * _cellSize) / 2 - _cellSize / 2
		);
		
		meshInstance.Position = Vector3.Zero; // Le mesh sera relatif au corps statique
		staticBody.Position = floorPosition;  // Position globale sur le corps statique
		
		// 4. Matériau pour le sol
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.8f, 0.8f, 0.8f); // Gris clair
		meshInstance.MaterialOverride = material;
		
		// 5. Ajouter le mesh au StaticBody3D
		staticBody.AddChild(meshInstance);
		
		// 6. Ajouter le CollisionShape3D pour la physique
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		
		// Forme de collision légèrement plus fine que le sol visible
		boxShape.Size = new Vector3(size * _cellSize, 0.1f, size * _cellSize);
		collisionShape.Shape = boxShape;
		
		// Position de la collision (au centre du StaticBody3D)
		collisionShape.Position = new Vector3(0, -0.05f, 0); // Légèrement sous la surface
		
		staticBody.AddChild(collisionShape);
		
		// 7. Ajouter le sol au nœud du labyrinthe
		mazeNode.AddChild(staticBody);
	}
	
	private void AddMazeLabel(Node3D mazeNode, int mazeIndex, int size)
	{
		var label3D = new Label3D();
		label3D.Text = $"Labyrinthe {mazeIndex + 1} ({size}x{size})";
		label3D.FontSize = 24;
		label3D.Modulate = new Color(1, 1, 0); // Jaune
		
		label3D.Position = new Vector3(
			size * _cellSize / 2,
			3.0f,
			-2.0f
		);
		
		mazeNode.AddChild(label3D);
	}
	
	private void AddGlobalCamera()
	{
		var camera = new Camera3D();
		camera.Name = "GlobalCamera";
		
		// Calcule la position de la caméra pour voir les premiers labyrinthes
		float totalWidth = 0;
		float maxDepth = 0;
		
		// Calcule la largeur totale et la profondeur maximale pour les premiers labyrinthes
		int labyrinthsToShow = Math.Min(INITIAL_MAZE_COUNT + 1, _mazeSizes.Count);
		for (int i = 0; i < labyrinthsToShow; i++)
		{
			int size = _mazeSizes[i];
			totalWidth += (size * _cellSize);
			
			if (i < labyrinthsToShow - 1)
				totalWidth += _horizontalSpacing;
				
			maxDepth = Mathf.Max(maxDepth, size * _cellSize);
		}
		
		// Positionne la caméra pour voir l'ensemble
		float cameraHeight = totalWidth * 0.2f;
		float cameraDistance = maxDepth * 1.2f;
		
		camera.Position = new Vector3(
			totalWidth / 2,  // Centre en X
			cameraHeight,    // Hauteur
			maxDepth + cameraDistance  // Recul
		);
		AddChild(camera);
		
		camera.LookAt(new Vector3(totalWidth / 2, 0, maxDepth / 2), Vector3.Up);
		
		camera.Projection = Camera3D.ProjectionType.Perspective;
		camera.Fov = 60;
		camera.Current = true;
	}
	
	private void AddBasicLighting()
	{
		var directionalLight = new DirectionalLight3D();
		directionalLight.Position = new Vector3(0, 20, 0);
		directionalLight.RotationDegrees = new Vector3(-45, 45, 0);
		directionalLight.ShadowEnabled = true;
		
		AddChild(directionalLight);
		
		var ambientLight = new OmniLight3D();
		ambientLight.Position = new Vector3(0, 10, 0);
		ambientLight.LightEnergy = 0.5f;
		ambientLight.OmniRange = 100.0f;
		
		AddChild(ambientLight);
	}

	// Créer et configurer les téléporteurs d'entrée et de sortie
	private void CreateTeleportZones(Node3D mazeNode, int mazeIndex, int size, Vector2I entrancePos, Vector2I exitPos)
	{
		// Créer le téléporteur d'entrée (première moitié du tunnel)
		CreateTeleporterGate(mazeNode, entrancePos.X, entrancePos.Y, new Color(0, 0.5f, 1.0f), false, mazeIndex);
		
		// Créer le téléporteur de sortie (deuxième moitié du tunnel)
		CreateTeleporterGate(mazeNode, exitPos.X, exitPos.Y, new Color(1.0f, 0.3f, 0.0f), true, mazeIndex);
	}

	// Modification de la méthode CreateTeleporterGate pour utiliser la scène Teleporter
	private void CreateTeleporterGate(Node3D mazeNode, int x, int y, Color color, bool isExit, int mazeIndex = 0)
	{
		// Charger la scène du téléporteur
		var teleporterScene = ResourceLoader.Load<PackedScene>("res://scenes/Teleporter.tscn");
		if (teleporterScene == null)
		{
			GD.PrintErr("Erreur: impossible de charger la scène du téléporteur!");
			return;
		}
		
		// Instancier la scène
		var teleporter = teleporterScene.Instantiate<Teleporter>();
		if (teleporter == null)
		{
			GD.PrintErr("Erreur: échec de l'instanciation du téléporteur!");
			return;
		}
		
		// Configurer le téléporteur
		teleporter.Name = isExit ? "ExitTeleporter" : "EntranceTeleporter";
		teleporter.IsExit = isExit;
		teleporter.TeleporterColor = color;
		teleporter.MazeIndex = mazeIndex;
		
		// Ajustement de la position Y selon qu'il s'agit d'un téléporteur d'entrée ou de sortie
		float yOffset = isExit ? -0.5f : 0.5f;
		
		// Positionner le téléporteur précisément à l'emplacement d'entrée/sortie avec l'ajustement Y
		teleporter.Position = new Vector3(
			x * _cellSize, 
			-0.25f, 
			y * _cellSize + yOffset // Ajout de l'offset pour mieux placer dans le labyrinthe
		);
		
		// Orienter correctement
		if (isExit)
		{
			teleporter.RotationDegrees = new Vector3(0, 180, 0); // vers le sud
		}
		else
		{
			teleporter.RotationDegrees = new Vector3(0, 0, 0); // vers le nord
		}
		
		// Si c'est un téléporteur de sortie, connecter le signal
		if (isExit)
		{
			teleporter.Connect("PlayerEnteredExitTeleporter", new Callable(this, nameof(OnPlayerEnteredExitTeleporter)));
		}
		
		// Ajouter le téléporteur au labyrinthe
		mazeNode.AddChild(teleporter);
		
		GD.Print($"Téléporteur {(isExit ? "de sortie" : "d entrée")} créé à la position {teleporter.Position} pour le labyrinthe {mazeIndex}");
	}

	// Amélioration de la méthode de téléportation
	private void OnPlayerEnteredExitTeleporter(Node3D body, int currentMazeIndex)
	{
		// Vérifier si c'est le joueur qui entre en contact
		if (body is PlayerBall playerBall)
		{
			// S'assurer que nous ne sommes pas au dernier labyrinthe
			if (currentMazeIndex < MAX_MAZES - 1)
			{
				int nextMazeIndex = currentMazeIndex + 1;
				
				// Indiquer au joueur que la téléportation commence
				playerBall.StartTeleporting();
				
				// Générer le prochain labyrinthe s'il n'existe pas déjà
				GenerateNextMaze(nextMazeIndex);
				
				// Important: Générer aussi les labyrinthes d'après pour avoir de l'avance
				GenerateNextMaze(nextMazeIndex + 1); // Labyrinthe N+2
				GenerateNextMaze(nextMazeIndex + 2); // Labyrinthe N+3
				
				// Récupérer la position d'entrée du prochain labyrinthe
				Node3D nextMaze = GetNodeOrNull<Node3D>($"Maze_{nextMazeIndex}");
				if (nextMaze != null)
				{
					// Trouver le téléporteur d'entrée du prochain labyrinthe
					Teleporter entranceTeleporter = nextMaze.FindChild("EntranceTeleporter", true, false) as Teleporter;
					
					if (entranceTeleporter != null)
					{
						// Téléporter le joueur directement au-dessus du téléporteur d'entrée
						Vector3 teleportPosition = entranceTeleporter.GlobalPosition + new Vector3(0, 0.7f, 0);
						
						// Téléporter le joueur
						playerBall.GlobalPosition = teleportPosition;
						
						// Utiliser CallDeferred pour terminer la téléportation après la mise à jour de la physique
						CallDeferred(nameof(FinishPlayerTeleporting), playerBall);
						
						// Jouer un son de téléportation
						PlayTeleportSound(playerBall);
						
						// Émettre le signal pour indiquer que le joueur a changé de labyrinthe
						EmitSignal(SignalName.PlayerEnteredMaze, nextMazeIndex);
						
						// Ajouter du temps
						AddTimeToMainScene(nextMazeIndex);
						
						GD.Print($"Joueur téléporté vers le labyrinthe {nextMazeIndex} à la position {teleportPosition}");
					}
					else
					{
						GD.PrintErr($"Téléporteur d'entrée non trouvé dans le labyrinthe {nextMazeIndex}");
					}
				}
				else
				{
					GD.PrintErr($"Erreur: Labyrinthe {nextMazeIndex} introuvable!");
				}
			}
		}
	}

	// Méthode pour ajouter du temps au MainScene
	private void AddTimeToMainScene(int mazeIndex)
	{
		// Trouver le MainScene
		var mainScene = GetTree().Root.GetNode<Node>("MainScene");
		if (mainScene != null)
		{
			// Ajouter du temps en fonction du labyrinthe
			// Plus le labyrinthe est avancé, plus on donne de temps
			int bonusTime = 30 + (mazeIndex * 5); // 30s pour le premier, +5s par niveau
			
			// Appeler la méthode AddTime du MainScene
			mainScene.Call("AddTime", bonusTime);
		}
	}

	// Jouer un son lors de la téléportation
	private void PlayTeleportSound(Node3D target)
	{
		var audioPlayer = new AudioStreamPlayer3D();
		target.AddChild(audioPlayer);
		
		// Charger le son de téléportation
		audioPlayer.Stream = ResourceLoader.Load<AudioStream>("res://assets/audio/bruit_teleporteur.wav");
		
		// Configuration du son
		audioPlayer.VolumeDb = 5.0f; // Volume
		audioPlayer.MaxDistance = 100.0f;
		audioPlayer.Autoplay = true;
		
		// Supprimer le lecteur une fois le son terminé
		audioPlayer.Finished += () => audioPlayer.QueueFree();
	}
	
	// Méthode pour terminer la téléportation du joueur avec CallDeferred
	private void FinishPlayerTeleporting(PlayerBall playerBall)
	{
		if (playerBall != null)
		{
			playerBall.FinishTeleporting();
			// S'assurer que les contrôles sont bien activés
			playerBall.EnableControls();
			GD.Print("Téléportation du joueur terminée, contrôles réactivés");
		}
	}
}
