using Godot;
using System;
using System.Collections.Generic;

public partial class VerticalMazeGenerator : Node3D
{
	[Export]
	public float _cellSize = 2.0f;
	
	[Export]
	private float _horizontalSpacing = 10.0f;
	
	// 10 labyrinthes avec des tailles croissantes
	public readonly int[] _mazeSizes = { 5, 7, 9, 11, 13, 15, 17, 19, 21, 23 };
	
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
	
	// Texture pour les murs
	private Texture2D _wallTexture;
	
	public override void _Ready()
	{
		// Charge la texture du mur
		_wallTexture = ResourceLoader.Load<Texture2D>("res://assets/wall/wall-texture.png");
		
		// Génère les 10 labyrinthes avec disposition horizontale (côte à côte)
		float totalWidth = 0;
		
		for (int i = 0; i < 10; i++)
		{
			var mazeNode = GenerateMaze(i);
			
			// Positionne les labyrinthes les uns à côté des autres
			mazeNode.Position = new Vector3(totalWidth, 0, 0);
			
			AddChild(mazeNode);
			
			// Ajoute la largeur de ce labyrinthe plus l'espacement pour le suivant
			int size = _mazeSizes[i];
			totalWidth += (size * _cellSize) + _horizontalSpacing;
		}
		
		// Ajoute une caméra pour voir l'ensemble des labyrinthes
		AddGlobalCamera();
		
		// Ajoute un éclairage de base
		AddBasicLighting();
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
		
		// Construit le labyrinthe 3D
		BuildMaze3D(mazeNode, mazeGrid, size, entrancePos, exitPos);
		
		// Ajoute une étiquette pour le labyrinthe
		AddMazeLabel(mazeNode, mazeIndex, size);
		
		// NOUVEAU: Création des zones de téléportation
		CreateTeleportZones(mazeNode, mazeIndex, size, entrancePos, exitPos);
		
		// Ajouter un mur à l'entrée du premier labyrinthe
			AddFirstMazeEntranceWall(mazeNode, size, entrancePos);

		
		return mazeNode;
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
	
	private void BuildMaze3D(Node3D mazeNode, CellWall[,] grid, int size, Vector2I entrance, Vector2I exit)
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
					CreateWallSegment(mazeNode, x, y, true, false);
				
				if ((cell & CellWall.Right) != 0)
					CreateWallSegment(mazeNode, x, y, false, true);
				
				if ((cell & CellWall.Down) != 0)
					CreateWallSegment(mazeNode, x, y, true, true);
				
				if ((cell & CellWall.Left) != 0)
					CreateWallSegment(mazeNode, x, y, false, false);
			}
		}
		
		// Ajoute les marqueurs d'entrée et de sortie
		CreateMarker(mazeNode, entrance.X, entrance.Y, new Color(0, 1, 0)); // Vert pour l'entrée
		CreateMarker(mazeNode, exit.X, exit.Y, new Color(1, 0, 0)); // Rouge pour la sortie
		
		// Ajoute des murs autour du périmètre du labyrinthe
		AddPerimeterWalls(mazeNode, size, entrance, exit);
	}
	
	// Ajoute des murs autour du périmètre du labyrinthe
	private void AddPerimeterWalls(Node3D mazeNode, int size, Vector2I entrance, Vector2I exit)
	{
		// Murs du haut sauf à l'entrée
		for (int x = 0; x < size; x++)
		{
			if (x != entrance.X) // Ne pas créer de mur à l'entrée
			{
				CreateBoundaryWall(mazeNode, x, 0, true, false);
			}
		}
		
		// Murs du bas sauf à la sortie
		for (int x = 0; x < size; x++)
		{
			if (x != exit.X) // Ne pas créer de mur à la sortie
			{
				CreateBoundaryWall(mazeNode, x, size - 1, true, true);
			}
		}
		
		// Murs de gauche
		for (int y = 0; y < size; y++)
		{
			CreateBoundaryWall(mazeNode, 0, y, false, false);
		}
		
		// Murs de droite
		for (int y = 0; y < size; y++)
		{
			CreateBoundaryWall(mazeNode, size - 1, y, false, true);
		}
	}
	
	private void CreateWallSegment(Node3D mazeNode, int x, int y, bool horizontal, bool positive)
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
		
		// Matériau pour le mur avec texture
		var material = new StandardMaterial3D();
		if (_wallTexture != null)
		{
			material.AlbedoTexture = _wallTexture;
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
	
	// Crée un mur de périmètre (plus épais)
	private void CreateBoundaryWall(Node3D mazeNode, int x, int y, bool horizontal, bool positive)
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
		
		// Matériau pour le mur avec texture
		var material = new StandardMaterial3D();
		if (_wallTexture != null)
		{
			material.AlbedoTexture = _wallTexture;
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
	private void CreateMarker(Node3D mazeNode, int x, int y, Color color)
	{
		var marker = new MeshInstance3D();
		var cubeMesh = new BoxMesh();
		
		cubeMesh.Size = new Vector3(0.5f, 0.5f, 0.5f);
		
		marker.Mesh = cubeMesh;
		
		marker.Position = new Vector3(
			x * _cellSize,
			0.25f,
			y * _cellSize
		);
		
		var material = new StandardMaterial3D();
		material.AlbedoColor = color;
		material.EmissionEnabled = true;
		material.Emission = color;
		
		marker.MaterialOverride = material;
		mazeNode.AddChild(marker);
	}
	
	private void CreateHole(Node3D mazeNode, int x, int y)
	{
		var hole = new MeshInstance3D();
		var cylinderMesh = new CylinderMesh();
		
		cylinderMesh.Height = 0.2f;
		cylinderMesh.BottomRadius = _cellSize * 0.4f;
		cylinderMesh.TopRadius = _cellSize * 0.4f;
		
		hole.Mesh = cylinderMesh;
		
		hole.Position = new Vector3(
			x * _cellSize,
			-0.1f,
			y * _cellSize
		);
		
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0, 0, 0);
		
		hole.MaterialOverride = material;
		mazeNode.AddChild(hole);
		
		// Flèche indicatrice
		var arrow = new MeshInstance3D();
		var arrowMesh = new PrismMesh();
		
		arrowMesh.Size = new Vector3(0.5f, 0.5f, 0.5f);
		
		arrow.Mesh = arrowMesh;
		
		arrow.Position = new Vector3(
			x * _cellSize,
			1.0f,
			y * _cellSize
		);
		
		arrow.RotationDegrees = new Vector3(180, 0, 0);
		
		var arrowMaterial = new StandardMaterial3D();
		arrowMaterial.AlbedoColor = new Color(1, 1, 0);
		
		arrow.MaterialOverride = arrowMaterial;
		mazeNode.AddChild(arrow);
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
		
		// Calcule la position de la caméra pour voir tous les labyrinthes côte à côte
		float totalWidth = 0;
		float maxDepth = 0;
		
		// Calcule la largeur totale et la profondeur maximale
		for (int i = 0; i < _mazeSizes.Length; i++)
		{
			int size = _mazeSizes[i];
			totalWidth += (size * _cellSize);
			
			if (i < _mazeSizes.Length - 1)
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
	
	// Nouvelle méthode pour créer des zones de téléportation aux entrées/sorties
	private void CreateTeleportZones(Node3D mazeNode, int mazeIndex, int size, Vector2I entrancePos, Vector2I exitPos)
	{
		// Ne pas créer de zone de sortie pour le dernier labyrinthe
		bool isLastMaze = mazeIndex == _mazeSizes.Length - 1;
		
		// Créer une zone de téléportation à la sortie (sauf pour le dernier labyrinthe)
		if (!isLastMaze)
		{
			var exitZone = new Area3D();
			exitZone.Name = $"TeleportZone_Exit_{mazeIndex}";
			
			// CollisionShape3D pour l'aire de détection
			var collisionShape = new CollisionShape3D();
			var boxShape = new BoxShape3D();
			
			// Taille de la zone de détection (un peu plus petite que la cellule)
			boxShape.Size = new Vector3(_cellSize * 0.8f, 1.0f, _cellSize * 0.8f);
			collisionShape.Shape = boxShape;
			
			exitZone.AddChild(collisionShape);
			
			// Positionner la zone à la sortie
			exitZone.Position = new Vector3(
				exitPos.X * _cellSize,
				0.5f,  // Légèrement au-dessus du sol
				exitPos.Y * _cellSize
			);
			
			// Ajouter un signal pour détecter quand le joueur entre dans la zone
			exitZone.BodyEntered += (body) => OnBodyEnteredExitZone(body, mazeIndex);
			
			mazeNode.AddChild(exitZone);
			
			// Ajouter un visuel pour la zone de téléportation de sortie
			AddTeleportZoneVisual(exitZone, new Color(0.3f, 0.8f, 1.0f, 0.3f));
		}
		
		// Créer une zone de téléportation à l'entrée (sauf pour le premier labyrinthe)
		if (mazeIndex > 0)
		{
			var entranceZone = new Area3D();
			entranceZone.Name = $"TeleportZone_Entrance_{mazeIndex}";
			
			// CollisionShape3D pour l'aire de détection
			var collisionShape = new CollisionShape3D();
			var boxShape = new BoxShape3D();
			
			// Taille de la zone de détection (un peu plus petite que la cellule)
			boxShape.Size = new Vector3(_cellSize * 0.8f, 1.0f, _cellSize * 0.8f);
			collisionShape.Shape = boxShape;
			
			entranceZone.AddChild(collisionShape);
			
			// Positionner la zone à l'entrée
			entranceZone.Position = new Vector3(
				entrancePos.X * _cellSize,
				0.5f,  // Légèrement au-dessus du sol
				entrancePos.Y * _cellSize
			);
			
			mazeNode.AddChild(entranceZone);
			
			// Ajouter un visuel pour la zone de téléportation d'entrée
			AddTeleportZoneVisual(entranceZone, new Color(0.8f, 0.3f, 1.0f, 0.3f));
		}
	}

	// Ajouter un visuel pour les zones de téléportation
	private void AddTeleportZoneVisual(Area3D zone, Color color)
	{
		var visualizer = new MeshInstance3D();
		var cylinderMesh = new CylinderMesh();
		
		cylinderMesh.Height = 0.1f;
		cylinderMesh.BottomRadius = _cellSize * 0.4f;
		cylinderMesh.TopRadius = _cellSize * 0.4f;
		
		visualizer.Mesh = cylinderMesh;
		
		// Matériau semi-transparent
		var material = new StandardMaterial3D();
		material.AlbedoColor = color;
		material.EmissionEnabled = true;
		material.Emission = new Color(color.R, color.G, color.B, 0.5f);
		
		visualizer.MaterialOverride = material;
		
		// Faire tourner le visuel pour effet d'animation
		var animation = new AnimationPlayer();
		zone.AddChild(animation);
		
		var rotationAnimation = new Animation();
		var trackIdx = rotationAnimation.AddTrack(Animation.TrackType.Value);
		rotationAnimation.TrackSetPath(trackIdx, ".:rotation:y");
		
		// Animation de rotation sur 2 secondes
		rotationAnimation.TrackInsertKey(trackIdx, 0.0f, 0.0f);
		rotationAnimation.TrackInsertKey(trackIdx, 2.0f, Mathf.Tau); // 2*PI (rotation complète)
		rotationAnimation.LoopMode = Animation.LoopModeEnum.Linear;
		
		// Créer une librairie d'animation
		var animLib = new AnimationLibrary();
		animLib.AddAnimation("rotate", rotationAnimation);
		
		// Ajouter la librairie au player d'animation
		animation.AddAnimationLibrary("", animLib);
		animation.Play("rotate");
		
		zone.AddChild(visualizer);
	}

	// Gestionnaire d'événement pour la téléportation à la sortie d'un labyrinthe
	private void OnBodyEnteredExitZone(Node3D body, int currentMazeIndex)
	{
		// Vérifier si c'est le joueur qui entre dans la zone
		if (body is PlayerBall playerBall)
		{
			// S'assurer que nous ne sommes pas au dernier labyrinthe
			if (currentMazeIndex < _mazeSizes.Length - 1)
			{
				int nextMazeIndex = currentMazeIndex + 1;
				
				// Indiquer au joueur que la téléportation commence
				playerBall.StartTeleporting();
				
				// Récupérer la position d'entrée du prochain labyrinthe
				Node3D nextMaze = GetNodeOrNull<Node3D>($"Maze_{nextMazeIndex}");
				if (nextMaze != null)
				{
					// Calculer la position d'entrée du prochain labyrinthe
					int nextSize = _mazeSizes[nextMazeIndex];
					Vector2I entrancePos = GetEntrancePosition(nextSize, nextMazeIndex);
					
					// Téléporter le joueur
					playerBall.GlobalPosition = nextMaze.GlobalPosition + new Vector3(
						entrancePos.X * _cellSize,
						1.0f,  // Légèrement au-dessus du sol
						entrancePos.Y * _cellSize
					);
					
					// Indiquer au joueur que la téléportation est terminée
					playerBall.FinishTeleporting();
					
					// Jouer un son de téléportation
					PlayTeleportSound(playerBall);
				}
			}
		}
	}

	// Jouer un son lors de la téléportation
	private void PlayTeleportSound(Node3D target)
	{
		var audioPlayer = new AudioStreamPlayer3D();
		target.AddChild(audioPlayer);
		
		// Vous devrez créer/charger un son de téléportation
		// audioPlayer.Stream = ResourceLoader.Load<AudioStream>("res://assets/sounds/teleport.wav");
		
		// Configuration du son
		audioPlayer.VolumeDb = 5.0f; // Volume
		audioPlayer.MaxDistance = 100.0f;
		audioPlayer.Autoplay = true;
		
		// Supprimer le lecteur une fois le son terminé
		audioPlayer.Finished += () => audioPlayer.QueueFree();
	}

	// Méthode pour ajouter un mur à l'entrée du premier labyrinthe
	private void AddFirstMazeEntranceWall(Node3D mazeNode, int size, Vector2I entrancePos)
	{
		// Créer un mur devant l'entrée du premier labyrinthe
		var wall = new StaticBody3D();
		var meshInstance = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		
		// Placer le mur juste devant l'entrée
		float offsetZ = -1.0f; // Un peu devant l'entrée
		
		// Taille du mur
		boxMesh.Size = new Vector3(_cellSize, _wallHeight, _wallThickness);
		
		meshInstance.Mesh = boxMesh;
		meshInstance.Position = new Vector3(0, _wallHeight / 2, offsetZ);
		
		// Matériau pour le mur
		var material = new StandardMaterial3D();
		if (_wallTexture != null)
		{
			material.AlbedoTexture = _wallTexture;
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
		
		// Position du mur (à l'entrée du premier labyrinthe)
		wall.Position = new Vector3(
			entrancePos.X * _cellSize,
			0,
			entrancePos.Y * _cellSize - _cellSize / 100 - _wallThickness / 100
		);

		
		mazeNode.AddChild(wall);
	}
}
