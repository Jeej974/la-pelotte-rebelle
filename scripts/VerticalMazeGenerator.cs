using Godot;
using System;
using System.Collections.Generic;

/**
 * VerticalMazeGenerator - Générateur procédural de labyrinthes
 * 
 * Crée des labyrinthes alignés verticalement avec une difficulté progressive.
 * Gère les téléporteurs entre les labyrinthes, les chats collectables et
 * toute la structure visuelle des environnements.
 */
public partial class VerticalMazeGenerator : Node3D
{
	[Export]
	public float _cellSize = 2.0f;
	
	[Export]
	private float _horizontalSpacing = 10.0f;
	
	// Paramètres de taille des labyrinthes
	private const int BASE_MAZE_SIZE = 5;
	private const int MAZE_SIZE_INCREMENT = 2;
	private const int MAX_MAZES = 100;
	
	// Liste des tailles de labyrinthe, accessible publiquement
	public readonly List<int> _mazeSizes = new List<int>();
	
	// Configuration des murs
	[Export]
	private float _wallHeight = 2.0f;
	
	[Export]
	private float _wallThickness = 0.2f;
	
	// Définition des murs d'une cellule du labyrinthe
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
	
	// Ressources pour les labyrinthes
	private List<Texture2D> _wallTextures = new List<Texture2D>();
	private Dictionary<int, Texture2D> _mazeWallTextures = new Dictionary<int, Texture2D>();
	
	// Suivi des labyrinthes générés
	private List<int> _generatedMazes = new List<int>();
	private float _totalWidth = 0;
	
	// Nombre initial de labyrinthes à générer
	private const int INITIAL_MAZE_COUNT = 4;
	
	// Signal émis quand le joueur entre dans un nouveau labyrinthe
	[Signal]
	public delegate void PlayerEnteredMazeEventHandler(int mazeIndex);
	
	/**
	 * Retourne le nombre total de labyrinthes configurés
	 */
	public int GetTotalMazeCount()
	{
		return _mazeSizes.Count;
	}
	
	/**
	 * Retourne la taille d'un labyrinthe spécifique
	 */
	public int GetMazeSize(int mazeIndex)
	{
		if (mazeIndex >= 0 && mazeIndex < _mazeSizes.Count)
		{
			return _mazeSizes[mazeIndex];
		}
		
		// Calcul de la taille pour un index hors limites
		if (mazeIndex >= 0)
		{
			return BASE_MAZE_SIZE + (mazeIndex * MAZE_SIZE_INCREMENT);
		}
		
		return BASE_MAZE_SIZE;
	}
	
	/**
	 * Initialisation du générateur de labyrinthes
	 */
	public override void _Ready()
	{
		// Création des tailles de labyrinthes
		GenerateMazeSizes();
		
		// Chargement des textures
		LoadWallTextures();
		
		// Génération des premiers labyrinthes
		for (int i = 0; i < INITIAL_MAZE_COUNT; i++)
		{
			GenerateNextMaze(i);
		}
		
		// Ajout de la caméra globale
		AddGlobalCamera();
		
		// Ajout de l'éclairage global
		AddBasicLighting();
		
		// Connexion du signal de téléportation
		this.Connect("PlayerEnteredMaze", new Callable(this, nameof(OnPlayerEnteredMaze)));
		
		GD.Print($"VerticalMazeGenerator initialisé - {INITIAL_MAZE_COUNT} labyrinthes générés");
	}
	
	/**
	 * Charge toutes les textures de mur disponibles
	 */
	private void LoadWallTextures()
	{
		_wallTextures.Clear();
		
		// Chargement des textures selon le format
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
				// Format alternatif si nécessaire
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
		
		// Texture par défaut si aucune n'a été chargée
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
	
	/**
	 * Sélectionne une texture aléatoire pour un labyrinthe
	 */
	private Texture2D GetRandomWallTexture(int mazeIndex)
	{
		// Réutiliser la texture déjà sélectionnée si disponible
		if (_mazeWallTextures.ContainsKey(mazeIndex))
		{
			return _mazeWallTextures[mazeIndex];
		}
		
		// Sélection aléatoire d'une nouvelle texture
		if (_wallTextures.Count > 0)
		{
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
			GD.PrintErr($"Aucune texture disponible pour le labyrinthe {mazeIndex}");
			return null;
		}
	}
	
	/**
	 * Initialise la liste des tailles de labyrinthe
	 */
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
	
	/**
	 * Génère un nouveau labyrinthe et le positionne
	 */
	private void GenerateNextMaze(int mazeIndex)
	{
		// Vérifier si ce labyrinthe existe déjà
		if (_generatedMazes.Contains(mazeIndex) || mazeIndex >= MAX_MAZES)
		{
			return;
		}
		
		// Extension de la liste des tailles si nécessaire
		while (_mazeSizes.Count <= mazeIndex)
		{
			int lastSize = _mazeSizes.Count > 0 ? _mazeSizes[_mazeSizes.Count - 1] : BASE_MAZE_SIZE;
			_mazeSizes.Add(lastSize + MAZE_SIZE_INCREMENT);
		}
		
		// Génération du labyrinthe
		var mazeNode = GenerateMaze(mazeIndex);
		
		// Positionnement du labyrinthe
		mazeNode.Position = new Vector3(_totalWidth, 0, 0);
		
		// Ajout immédiat à la scène
		AddChild(mazeNode);
		
		// Mise à jour de la largeur totale
		int size = _mazeSizes[mazeIndex];
		_totalWidth += (size * _cellSize) + _horizontalSpacing;
		
		// Enregistrement du labyrinthe comme généré
		_generatedMazes.Add(mazeIndex);
		
		GD.Print($"Labyrinthe {mazeIndex} généré à la position X={_totalWidth - (size * _cellSize) - _horizontalSpacing}, taille={size}x{size}");
	}
	
	/**
	 * Réaction à l'entrée du joueur dans un labyrinthe
	 */
	private void OnPlayerEnteredMaze(int mazeIndex)
	{
		// Génération des labyrinthes suivants pour anticiper la progression
		int nextMazeIndex = mazeIndex + 2; // Génération de 2 labyrinthes en avance
		
		if (nextMazeIndex < MAX_MAZES && !_generatedMazes.Contains(nextMazeIndex))
		{
			GD.Print($"Joueur dans le labyrinthe {mazeIndex}, génération du labyrinthe {nextMazeIndex}");
			GenerateNextMaze(nextMazeIndex);
		}
		
		// Génération du 3e labyrinthe en avance
		int nextNextMazeIndex = mazeIndex + 3;
		if (nextNextMazeIndex < MAX_MAZES && !_generatedMazes.Contains(nextNextMazeIndex))
		{
			GD.Print($"Génération du labyrinthe {nextNextMazeIndex} en prévision");
			GenerateNextMaze(nextNextMazeIndex);
		}
	}
	
	/**
	 * Génère la structure complète d'un labyrinthe
	 */
	private Node3D GenerateMaze(int mazeIndex)
	{
		int size = _mazeSizes[mazeIndex];
		
		var mazeNode = new Node3D();
		mazeNode.Name = $"Maze_{mazeIndex}";
		
		// Génération de la structure logique du labyrinthe
		CellWall[,] mazeGrid = new CellWall[size, size];
		
		// Initialisation avec tous les murs
		for (int x = 0; x < size; x++)
		{
			for (int y = 0; y < size; y++)
			{
				mazeGrid[x, y] = CellWall.Up | CellWall.Right | CellWall.Down | CellWall.Left;
			}
		}
		
		// Application de l'algorithme de génération de labyrinthe
		Random random = new Random();
		Stack<Vector2I> cellStack = new Stack<Vector2I>();
		
		// Point de départ aligné
		Vector2I current = new Vector2I(
			Math.Min(_alignmentPoint.X, size - 2),
			Math.Min(_alignmentPoint.Y, size - 2)
		);
		
		if (current.X < 1) current.X = 1;
		if (current.Y < 1) current.Y = 1;
		
		// Marquer la cellule de départ comme visitée
		mazeGrid[current.X, current.Y] |= CellWall.Visited;
		
		// Compteur de cellules non visitées
		int unvisitedCells = size * size - 1;
		
		// Algorithme de génération de labyrinthe par backtracking
		while (unvisitedCells > 0)
		{
			// Recherche des voisins non visités
			List<Vector2I> neighbors = new List<Vector2I>();
			
			// Vérification des 4 directions
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
				// Sélection aléatoire d'un voisin
				Vector2I next = neighbors[random.Next(neighbors.Count)];
				
				// Ajout de la cellule actuelle à la pile
				cellStack.Push(current);
				
				// Suppression des murs entre les cellules
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
				
				// Marquer la cellule suivante comme visitée
				mazeGrid[next.X, next.Y] |= CellWall.Visited;
				unvisitedCells--;
				
				// Déplacer à la nouvelle cellule
				current = next;
			}
			else if (cellStack.Count > 0)
			{
				// Backtracking si pas de voisins disponibles
				current = cellStack.Pop();
			}
			else
			{
				// Recherche d'une cellule non visitée si la pile est vide
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
				
				if (!found) break;
			}
		}
		
		// Définition des entrées et sorties
		Vector2I entrancePos = GetEntrancePosition(size, mazeIndex);
		Vector2I exitPos = GetExitPosition(size, mazeIndex);
		
		// Ouverture de l'entrée (haut)
		mazeGrid[entrancePos.X, entrancePos.Y] &= ~CellWall.Up;
		
		// Ouverture de la sortie (bas)
		mazeGrid[exitPos.X, exitPos.Y] &= ~CellWall.Down;
		
		// Préparation de la structure pour le placement des chats
		MazeCell[][] cells = new MazeCell[size][];
		for (int x = 0; x < size; x++)
		{
			cells[x] = new MazeCell[size];
			for (int y = 0; y < size; y++)
			{
				cells[x][y] = new MazeCell(new Vector2I(x, y));
			}
		}
		
		// Placement des chats
		PlaceCatsInMaze(cells, mazeGrid, size, mazeIndex, entrancePos, exitPos);
		
		// Construction du labyrinthe 3D
		BuildMaze3D(mazeNode, mazeGrid, cells, size, entrancePos, exitPos, mazeIndex);
		
		// Ajout de l'étiquette du labyrinthe
		AddMazeLabel(mazeNode, mazeIndex, size);
		
		// Création des zones de téléportation
		CreateTeleportZones(mazeNode, mazeIndex, size, entrancePos, exitPos);
		
		return mazeNode;
	}
	
	/**
	 * Classe pour stocker les informations de cellule
	 */
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
	
	/**
	 * Place les chats dans le labyrinthe
	 */
	private void PlaceCatsInMaze(MazeCell[][] cells, CellWall[,] grid, int size, int mazeIndex, Vector2I entrance, Vector2I exit)
	{
		// Détermination du nombre de chats selon le niveau
		int catCount = Cat.GetCatCountForMaze(mazeIndex);
		
		// Création de la liste des positions disponibles
		List<Vector2I> availablePositions = new List<Vector2I>();
		
		for (int x = 0; x < size; x++)
		{
			for (int y = 0; y < size; y++)
			{
				Vector2I pos = new Vector2I(x, y);
				
				// Exclusion des entrées/sorties et cellules adjacentes
				if (!IsNearEntryOrExit(pos, entrance, exit, 1))
				{
					availablePositions.Add(pos);
				}
			}
		}
		
		// Mélange aléatoire des positions
		Random random = new Random();
		for (int i = 0; i < availablePositions.Count; i++)
		{
			int j = random.Next(i, availablePositions.Count);
			Vector2I temp = availablePositions[i];
			availablePositions[i] = availablePositions[j];
			availablePositions[j] = temp;
		}
		
		// Limitation du nombre de chats selon les positions disponibles
		catCount = Math.Min(catCount, availablePositions.Count);
		
		// Placement des chats
		for (int i = 0; i < catCount; i++)
		{
			Vector2I pos = availablePositions[i];
			
			// Sélection du type de chat selon le niveau
			CatType catType = Cat.GetRandomType(mazeIndex);
			
			// Marquage de la cellule
			cells[pos.X][pos.Y].HasCat = true;
			cells[pos.X][pos.Y].CatType = catType;
			
			GD.Print($"Chat {catType} placé en position {pos} dans le labyrinthe {mazeIndex}");
		}
	}
	
	/**
	 * Vérifie si une position est proche de l'entrée ou de la sortie
	 */
	private bool IsNearEntryOrExit(Vector2I pos, Vector2I entrance, Vector2I exit, int distance)
	{
		return (Math.Abs(pos.X - entrance.X) <= distance && Math.Abs(pos.Y - entrance.Y) <= distance) ||
			   (Math.Abs(pos.X - exit.X) <= distance && Math.Abs(pos.Y - exit.Y) <= distance);
	}
	
	/**
	 * Retourne la position d'entrée d'un labyrinthe
	 */
	public Vector2I GetEntrancePosition(int size, int mazeIndex)
	{
		if (mazeIndex == 0)
		{
			// Pour le premier labyrinthe, utilisation du point d'alignement
			return new Vector2I(
				Math.Min(_alignmentPoint.X, size - 2),
				0
			);
		}
		else
		{
			// Pour les labyrinthes suivants, alignement avec la sortie du précédent
			Vector2I prevExit = GetExitPosition(_mazeSizes[mazeIndex - 1], mazeIndex - 1);
			
			return new Vector2I(
				Math.Min(prevExit.X, size - 2),
				0
			);
		}
	}
	
	/**
	 * Retourne la position de sortie d'un labyrinthe
	 */
	public Vector2I GetExitPosition(int size, int mazeIndex)
	{
		// La sortie est alignée horizontalement avec l'entrée
		Vector2I entrance = GetEntrancePosition(size, mazeIndex);
		return new Vector2I(entrance.X, size - 1);
	}
	
	/**
	 * Crée un marqueur visuel pour entrée/sortie
	 */
	private void CreateMarker(Node3D mazeNode, int x, int y, Color color, bool isExit = false)
	{
		// Extraction de l'index du labyrinthe depuis le nom
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
		
		// Utilisation d'un téléporteur comme marqueur
		CreateTeleporterGate(mazeNode, x, y, isExit ? new Color(0, 1, 0) : new Color(1, 0, 0), isExit, mazeIndex);
	}

	/**
	 * Ajoute un indicateur lumineux pour le téléporteur
	 */
	private void AddStaticIndicator(Node3D teleporter, Color color)
	{
		// Création d'une lumière ponctuelle
		var omniLight = new OmniLight3D();
		omniLight.Name = "TeleporterLight";
		
		// Configuration de la lumière
		omniLight.LightColor = color;
		omniLight.LightEnergy = 2.0f;
		omniLight.OmniRange = 3.0f;
		omniLight.ShadowEnabled = true;
		
		// Animation de clignotement
		var animationPlayer = new AnimationPlayer();
		teleporter.AddChild(animationPlayer);
		
		var pulseAnimation = new Animation();
		var trackIdx = pulseAnimation.AddTrack(Animation.TrackType.Value);
		pulseAnimation.TrackSetPath(trackIdx, "%TeleporterLight:light_energy");
		
		// Keyframes de l'animation
		pulseAnimation.TrackInsertKey(trackIdx, 0.0f, 2.0f);
		pulseAnimation.TrackInsertKey(trackIdx, 1.0f, 4.0f);
		pulseAnimation.TrackInsertKey(trackIdx, 2.0f, 2.0f);
		pulseAnimation.LoopMode = Animation.LoopModeEnum.Linear;
		
		// Configuration de l'animation
		var animLib = new AnimationLibrary();
		animLib.AddAnimation("pulse", pulseAnimation);
		animationPlayer.AddAnimationLibrary("", animLib);
		
		// Positionnement de la lumière
		omniLight.Position = new Vector3(0, 2.0f, 0);
		
		// Ajout au téléporteur
		teleporter.AddChild(omniLight);
		
		// Démarrage de l'animation
		animationPlayer.Play("pulse");
		
		// Ajout de particules pour meilleure visibilité
		AddTeleporterParticles(teleporter, color);
	}

	/**
	 * Applique une couleur à un téléporteur
	 */
	private void ApplyColorToTeleporter(Node3D teleporter, Color color)
	{
		foreach (Node child in teleporter.GetChildren())
		{
			ApplyColorToNode(child, color);
		}
	}

	/**
	 * Applique récursivement une couleur à tous les meshes
	 */
	private void ApplyColorToNode(Node node, Color color)
	{
		if (node is MeshInstance3D meshInstance)
		{
			// Création d'un nouveau matériau
			Material existingMaterial = meshInstance.GetActiveMaterial(0);
			StandardMaterial3D material = new StandardMaterial3D();
			
			// Configuration du matériau
			material.AlbedoColor = color;
			material.Metallic = 0.5f;
			material.Roughness = 0.3f;
			material.EmissionEnabled = true;
			material.Emission = new Color(color.R, color.G, color.B, 0.5f);
			material.EmissionEnergyMultiplier = 0.3f;
			
			// Application du matériau
			meshInstance.MaterialOverride = material;
		}
		
		// Application récursive aux enfants
		foreach (Node child in node.GetChildren())
		{
			ApplyColorToNode(child, color);
		}
	}

	/**
	 * Ajoute des particules à un téléporteur
	 */
	private void AddTeleporterParticles(Node3D teleporter, Color color)
	{
		var particles = new GpuParticles3D();
		particles.Name = "TeleporterParticles";
		
		// Configuration du matériau des particules
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
		
		// Configuration du mesh des particules
		var sphereMesh = new SphereMesh();
		sphereMesh.Radius = 0.05f;
		sphereMesh.Height = 0.1f;
		particles.DrawPass1 = sphereMesh;
		
		// Paramètres d'émission
		particles.Amount = 30;
		particles.Lifetime = 2.0f;
		particles.OneShot = false;
		particles.Explosiveness = 0.1f;
		particles.FixedFps = 30;
		
		// Positionnement des particules
		particles.Position = new Vector3(0, 1.5f, 0);
		
		teleporter.AddChild(particles);
	}
	
	/**
	 * Construit la représentation 3D du labyrinthe
	 */
	private void BuildMaze3D(Node3D mazeNode, CellWall[,] grid, MazeCell[][] cells, int size, Vector2I entrance, Vector2I exit, int mazeIndex)
	{
		// Création du sol
		CreateFloor(mazeNode, size);
		
		// Construction des murs pour chaque cellule
		for (int x = 0; x < size; x++)
		{
			for (int y = 0; y < size; y++)
			{
				CellWall cell = grid[x, y];
				
				// Création des murs selon la configuration de la cellule
				if ((cell & CellWall.Up) != 0)
					CreateWallSegment(mazeNode, x, y, true, false, mazeIndex);
				
				if ((cell & CellWall.Right) != 0)
					CreateWallSegment(mazeNode, x, y, false, true, mazeIndex);
				
				if ((cell & CellWall.Down) != 0)
					CreateWallSegment(mazeNode, x, y, true, true, mazeIndex);
				
				if ((cell & CellWall.Left) != 0)
					CreateWallSegment(mazeNode, x, y, false, false, mazeIndex);
				
				// Placement des chats
				if (cells[x][y].HasCat)
				{
					CreateCat(mazeNode, x, y, cells[x][y].CatType);
				}
			}
		}
		
		// Ajout des marqueurs d'entrée et sortie
		CreateMarker(mazeNode, entrance.X, entrance.Y, new Color(0, 1, 0));
		CreateMarker(mazeNode, exit.X, exit.Y, new Color(1, 0, 0), true);
		
		// Ajout des murs du périmètre
		AddPerimeterWalls(mazeNode, size, entrance, exit, mazeIndex);
		
		// Ajout des murs d'entrée et sortie
		AddMazeWalls(mazeNode, size, entrance, exit, mazeIndex);
	}

	/**
	 * Ajoute des murs d'entrée et de sortie sur tous les labyrinthes
	 */
	private void AddMazeWalls(Node3D mazeNode, int size, Vector2I entrance, Vector2I exit, int mazeIndex)
	{
		AddEntranceWall(mazeNode, size, entrance, mazeIndex);
		AddExitWall(mazeNode, size, exit, mazeIndex);
	}

	/**
	 * Ajoute un mur à l'entrée du labyrinthe
	 */
	private void AddEntranceWall(Node3D mazeNode, int size, Vector2I entrancePos, int mazeIndex)
	{
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Configuration de la taille du mur
		boxMesh.Size = new Vector3(_cellSize, _wallHeight, _wallThickness);
		
		// Positionnement du mur devant l'entrée
		meshInstance.Position = new Vector3(0, _wallHeight / 2, -_cellSize / 2 - _wallThickness / 2);
		
		meshInstance.Mesh = boxMesh;
		
		// Application de la texture
		var material = new StandardMaterial3D();
		Texture2D wallTexture = GetRandomWallTexture(mazeIndex);
		
		if (wallTexture != null)
		{
			material.AlbedoTexture = wallTexture;
			material.Uv1Scale = new Vector3(2.0f, 1.0f, 1.0f);
		}
		else
		{
			material.AlbedoColor = new Color(0.5f, 0.3f, 0.2f);
		}
		
		meshInstance.MaterialOverride = material;
		
		// Configuration de la collision
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = boxMesh.Size;
		collisionShape.Shape = boxShape;
		collisionShape.Position = meshInstance.Position;
		
		wall.AddChild(meshInstance);
		wall.AddChild(collisionShape);
		
		// Positionnement global
		wall.Position = new Vector3(
			entrancePos.X * _cellSize,
			0,
			entrancePos.Y * _cellSize
		);
		
		mazeNode.AddChild(wall);
		
		GD.Print($"Mur d'entrée ajouté au labyrinthe {mazeIndex}");
	}

	/**
	 * Ajoute un mur à la sortie du labyrinthe
	 */
	private void AddExitWall(Node3D mazeNode, int size, Vector2I exitPos, int mazeIndex)
	{
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Configuration de la taille du mur
		boxMesh.Size = new Vector3(_cellSize, _wallHeight, _wallThickness);
		
		// Positionnement du mur après la sortie
		meshInstance.Position = new Vector3(0, _wallHeight / 2, _cellSize / 2 + _wallThickness / 2);
		
		meshInstance.Mesh = boxMesh;
		
		// Application de la texture
		var material = new StandardMaterial3D();
		Texture2D wallTexture = GetRandomWallTexture(mazeIndex);
		
		if (wallTexture != null)
		{
			material.AlbedoTexture = wallTexture;
			material.Uv1Scale = new Vector3(2.0f, 1.0f, 1.0f);
		}
		else
		{
			material.AlbedoColor = new Color(0.5f, 0.3f, 0.2f);
		}
		
		meshInstance.MaterialOverride = material;
		
		// Configuration de la collision
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = boxMesh.Size;
		collisionShape.Shape = boxShape;
		collisionShape.Position = meshInstance.Position;
		
		wall.AddChild(meshInstance);
		wall.AddChild(collisionShape);
		
		// Positionnement global
		wall.Position = new Vector3(
			exitPos.X * _cellSize,
			0,
			exitPos.Y * _cellSize
		);
		
		mazeNode.AddChild(wall);
		
		GD.Print($"Mur de sortie ajouté au labyrinthe {mazeIndex}");
	}
	
	/**
	 * Crée un chat dans le labyrinthe
	 */
	private void CreateCat(Node3D mazeNode, int x, int y, CatType catType)
	{
		// Chargement de la scène de chat
		var catScene = ResourceLoader.Load<PackedScene>("res://scenes/Cat.tscn");
		if (catScene == null)
		{
			GD.PrintErr("Erreur: impossible de charger la scène du chat!");
			return;
		}
		
		// Instanciation du chat
		var cat = catScene.Instantiate<Cat>();
		if (cat == null)
		{
			GD.PrintErr("Erreur: échec de l'instanciation du chat!");
			return;
		}
		
		// Configuration du type
		cat.Set("_catType", (int)catType);
		
		// Nommage pour le débogage
		cat.Name = $"Cat_{x}_{y}_{catType}";
		
		// Positionnement dans la cellule
		cat.Position = new Vector3(
			x * _cellSize,
			0.1f, // Légèrement au-dessus du sol
			y * _cellSize
		);
		
		// Ajout différé au labyrinthe
		mazeNode.CallDeferred(Node.MethodName.AddChild, cat);
		
		GD.Print($"Chat {catType} créé en position ({x}, {y})");
	}
	
	/**
	 * Ajoute les murs du périmètre du labyrinthe
	 */
	private void AddPerimeterWalls(Node3D mazeNode, int size, Vector2I entrance, Vector2I exit, int mazeIndex)
	{
		// Murs du haut (sauf à l'entrée)
		for (int x = 0; x < size; x++)
		{
			if (x != entrance.X)
			{
				CreateBoundaryWall(mazeNode, x, 0, true, false, mazeIndex);
			}
		}
		
		// Murs du bas (sauf à la sortie)
		for (int x = 0; x < size; x++)
		{
			if (x != exit.X)
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
	
	/**
	 * Crée un segment de mur interne du labyrinthe
	 */
	private void CreateWallSegment(Node3D mazeNode, int x, int y, bool horizontal, bool positive, int mazeIndex)
	{
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Dimensions selon l'orientation
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
		
		// Application de la texture
		var material = new StandardMaterial3D();
		Texture2D wallTexture = GetRandomWallTexture(mazeIndex);
		
		if (wallTexture != null)
		{
			material.AlbedoTexture = wallTexture;
			material.Uv1Scale = new Vector3(2.0f, 1.0f, 1.0f);
		}
		else
		{
			material.AlbedoColor = new Color(0.5f, 0.3f, 0.2f);
		}
		
		meshInstance.MaterialOverride = material;
		
		// Configuration de la collision
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = boxMesh.Size;
		collisionShape.Shape = boxShape;
		collisionShape.Position = meshInstance.Position;
		
		wall.AddChild(meshInstance);
		wall.AddChild(collisionShape);
		
		// Positionnement dans la grille
		wall.Position = new Vector3(
			x * _cellSize,
			0,
			y * _cellSize
		);
		
		mazeNode.AddChild(wall);
	}
	
	/**
	 * Crée un mur de périmètre (plus épais que les murs internes)
	 */
	private void CreateBoundaryWall(Node3D mazeNode, int x, int y, bool horizontal, bool positive, int mazeIndex)
	{
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Dimensions avec épaisseur augmentée
		float length = _cellSize;
		float width = _wallThickness * 1.5f;
		
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
		
		// Application de la texture
		var material = new StandardMaterial3D();
		Texture2D wallTexture = GetRandomWallTexture(mazeIndex);
		
		if (wallTexture != null)
		{
			material.AlbedoTexture = wallTexture;
			material.Uv1Scale = new Vector3(2.0f, 1.0f, 1.0f);
		}
		else
		{
			material.AlbedoColor = new Color(0.4f, 0.2f, 0.1f); // Couleur plus foncée pour les murs extérieurs
		}
		
		meshInstance.MaterialOverride = material;
		
		// Configuration de la collision
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = boxMesh.Size;
		collisionShape.Shape = boxShape;
		collisionShape.Position = meshInstance.Position;
		
		wall.AddChild(meshInstance);
		wall.AddChild(collisionShape);
		
		// Positionnement dans la grille
		wall.Position = new Vector3(
			x * _cellSize,
			0,
			y * _cellSize
		);
		
		mazeNode.AddChild(wall);
	}
	
	/**
	 * Crée le sol du labyrinthe
	 */
	private void CreateFloor(Node3D mazeNode, int size)
	{
		// Création du corps statique
		var staticBody = new StaticBody3D();
		staticBody.Name = "Floor";
		
		// Création du mesh visuel
		var meshInstance = new MeshInstance3D();
		var planeMesh = new PlaneMesh();
		
		planeMesh.Size = new Vector2(size * _cellSize, size * _cellSize);
		meshInstance.Mesh = planeMesh;
		
		// Positionnement du sol
		Vector3 floorPosition = new Vector3(
			(size * _cellSize) / 2 - _cellSize / 2,
			0,
			(size * _cellSize) / 2 - _cellSize / 2
		);
		
		meshInstance.Position = Vector3.Zero;
		staticBody.Position = floorPosition;
		
		// Matériau du sol
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.8f, 0.8f, 0.8f);
		meshInstance.MaterialOverride = material;
		
		staticBody.AddChild(meshInstance);
		
		// Collision pour le sol
		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		
		boxShape.Size = new Vector3(size * _cellSize, 0.1f, size * _cellSize);
		collisionShape.Shape = boxShape;
		
		collisionShape.Position = new Vector3(0, -0.05f, 0);
		
		staticBody.AddChild(collisionShape);
		
		mazeNode.AddChild(staticBody);
	}
	
	/**
	 * Ajoute une étiquette de numérotation au labyrinthe
	 */
	private void AddMazeLabel(Node3D mazeNode, int mazeIndex, int size)
	{
		var label3D = new Label3D();
		label3D.Text = $"Labyrinthe {mazeIndex + 1} ({size}x{size})";
		label3D.FontSize = 24;
		label3D.Modulate = new Color(1, 1, 0);
		
		label3D.Position = new Vector3(
			size * _cellSize / 2,
			3.0f,
			-2.0f
		);
		
		mazeNode.AddChild(label3D);
	}
	
	/**
	 * Ajoute une caméra globale pour la vue d'ensemble
	 */
	private void AddGlobalCamera()
	{
		var camera = new Camera3D();
		camera.Name = "GlobalCamera";
		
		// Calcul de la position optimale
		float totalWidth = 0;
		float maxDepth = 0;
		
		// Analyse des dimensions pour les premiers labyrinthes
		int labyrinthsToShow = Math.Min(INITIAL_MAZE_COUNT + 1, _mazeSizes.Count);
		for (int i = 0; i < labyrinthsToShow; i++)
		{
			int size = _mazeSizes[i];
			totalWidth += (size * _cellSize);
			
			if (i < labyrinthsToShow - 1)
				totalWidth += _horizontalSpacing;
				
			maxDepth = Mathf.Max(maxDepth, size * _cellSize);
		}
		
		// Positionnement de la caméra
		float cameraHeight = totalWidth * 0.2f;
		float cameraDistance = maxDepth * 1.2f;
		
		camera.Position = new Vector3(
			totalWidth / 2,
			cameraHeight,
			maxDepth + cameraDistance
		);
		AddChild(camera);
		
		camera.LookAt(new Vector3(totalWidth / 2, 0, maxDepth / 2), Vector3.Up);
		
		camera.Projection = Camera3D.ProjectionType.Perspective;
		camera.Fov = 60;
		camera.Current = true;
	}
	
	/**
	 * Ajoute l'éclairage global de la scène
	 */
	private void AddBasicLighting()
	{
		// Lumière directionnelle principale
		var directionalLight = new DirectionalLight3D();
		directionalLight.Position = new Vector3(0, 20, 0);
		directionalLight.RotationDegrees = new Vector3(-45, 45, 0);
		directionalLight.ShadowEnabled = true;
		
		AddChild(directionalLight);
		
		// Lumière ambiante pour adoucir les ombres
		var ambientLight = new OmniLight3D();
		ambientLight.Position = new Vector3(0, 10, 0);
		ambientLight.LightEnergy = 0.5f;
		ambientLight.OmniRange = 100.0f;
		
		AddChild(ambientLight);
	}

	/**
	 * Crée les zones de téléportation entre labyrinthes
	 */
	private void CreateTeleportZones(Node3D mazeNode, int mazeIndex, int size, Vector2I entrancePos, Vector2I exitPos)
	{
		// Téléporteur d'entrée
		CreateTeleporterGate(mazeNode, entrancePos.X, entrancePos.Y, new Color(0, 0.5f, 1.0f), false, mazeIndex);
		
		// Téléporteur de sortie
		CreateTeleporterGate(mazeNode, exitPos.X, exitPos.Y, new Color(1.0f, 0.3f, 0.0f), true, mazeIndex);
	}

	/**
	 * Crée un téléporteur à partir de la scène préfabriquée
	 */
	private void CreateTeleporterGate(Node3D mazeNode, int x, int y, Color color, bool isExit, int mazeIndex = 0)
	{
		// Chargement de la scène
		var teleporterScene = ResourceLoader.Load<PackedScene>("res://scenes/Teleporter.tscn");
		if (teleporterScene == null)
		{
			GD.PrintErr("Erreur: impossible de charger la scène du téléporteur!");
			return;
		}
		
		// Instanciation
		var teleporter = teleporterScene.Instantiate<Teleporter>();
		if (teleporter == null)
		{
			GD.PrintErr("Erreur: échec de l'instanciation du téléporteur!");
			return;
		}
		
		// Configuration
		teleporter.Name = isExit ? "ExitTeleporter" : "EntranceTeleporter";
		teleporter.IsExit = isExit;
		teleporter.TeleporterColor = color;
		teleporter.MazeIndex = mazeIndex;
		
		// Ajustement de position selon le type
		float yOffset = isExit ? -0.5f : 0.5f;
		
		// Positionnement
		teleporter.Position = new Vector3(
			x * _cellSize, 
			-0.25f, 
			y * _cellSize + yOffset
		);
		
		// Orientation
		if (isExit)
		{
			teleporter.RotationDegrees = new Vector3(0, 180, 0); // Vers le sud
		}
		else
		{
			teleporter.RotationDegrees = new Vector3(0, 0, 0); // Vers le nord
		}
		
		// Connexion du signal pour les téléporteurs de sortie
		if (isExit)
		{
			teleporter.Connect("PlayerEnteredExitTeleporter", new Callable(this, nameof(OnPlayerEnteredExitTeleporter)));
		}
		
		// Ajout au labyrinthe
		mazeNode.AddChild(teleporter);
		
		GD.Print($"Téléporteur {(isExit ? "de sortie" : "d entrée")} créé à la position {teleporter.Position} pour le labyrinthe {mazeIndex}");
	}

	/**
	 * Gère la téléportation du joueur entre labyrinthes
	 */
	private void OnPlayerEnteredExitTeleporter(Node3D body, int currentMazeIndex)
	{
		// Vérification que c'est bien le joueur
		if (body is PlayerBall playerBall)
		{
			// Vérification que ce n'est pas le dernier labyrinthe
			if (currentMazeIndex < MAX_MAZES - 1)
			{
				int nextMazeIndex = currentMazeIndex + 1;
				
				// Notification au joueur pour l'animation de téléportation
				playerBall.StartTeleporting();
				
				// Génération anticipée des labyrinthes suivants
				GenerateNextMaze(nextMazeIndex);
				GenerateNextMaze(nextMazeIndex + 1);
				GenerateNextMaze(nextMazeIndex + 2);
				
				// Récupération du prochain labyrinthe
				Node3D nextMaze = GetNodeOrNull<Node3D>($"Maze_{nextMazeIndex}");
				if (nextMaze != null)
				{
					// Recherche du téléporteur d'entrée
					Teleporter entranceTeleporter = nextMaze.FindChild("EntranceTeleporter", true, false) as Teleporter;
					
					if (entranceTeleporter != null)
					{
						// Positionnement au-dessus du téléporteur d'entrée
						Vector3 teleportPosition = entranceTeleporter.GlobalPosition + new Vector3(0, 0.7f, 0);
						
						// Téléportation
						playerBall.GlobalPosition = teleportPosition;
						
						// Fin différée de la téléportation pour synchronisation
						CallDeferred(nameof(FinishPlayerTeleporting), playerBall);
						
						// Effet sonore
						PlayTeleportSound(playerBall);
						
						// Notification du changement de labyrinthe
						EmitSignal(SignalName.PlayerEnteredMaze, nextMazeIndex);
						
						// Ajout de temps au joueur
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

	/**
	 * Ajoute du temps au compteur du jeu selon le niveau du labyrinthe
	 */
	private void AddTimeToMainScene(int mazeIndex)
	{
		// Recherche de la scène principale
		var mainScene = GetTree().Root.GetNode<Node>("MainScene");
		if (mainScene != null)
		{
			// Calcul du temps bonus selon le niveau
			int bonusTime = 5 + (mazeIndex * 2);
			
			// Ajout du temps
			mainScene.Call("AddTime", bonusTime);
		}
	}

	/**
	 * Joue un effet sonore de téléportation
	 */
	private void PlayTeleportSound(Node3D target)
	{
		if (AudioManager.Instance != null) {
			AudioManager.Instance.PlaySound3D("Teleport", target);
			GD.Print("Son de téléportation joué");
		}
	}
	
	/**
	 * Finalise la téléportation du joueur
	 */
	private void FinishPlayerTeleporting(PlayerBall playerBall)
	{
		if (playerBall != null)
		{
			playerBall.FinishTeleporting();
			// Réactivation des contrôles
			playerBall.EnableControls();
			GD.Print("Téléportation du joueur terminée, contrôles réactivés");
		}
	}
}
