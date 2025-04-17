using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;

/// <summary>
/// Classe dédiée à la gestion des scores, indépendante de MainScene
/// </summary>
public partial class ScoreManager : Node
{

	// Structure des entrées de score
	[Serializable]
	public struct ScoreEntry
	{
	public int mazesCompleted { get; set; }
	public int totalCats { get; set; }
	public string date { get; set; }
		
		public ScoreEntry(int mazes, int cats)
		{
			mazesCompleted = mazes;
			totalCats = cats;
			date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
		}
		
		public override string ToString()
		{
			return $"Labyrinthe: {mazesCompleted} | Chats: {totalCats}";
		}
	}
	
	// Liste des meilleurs scores
	private List<ScoreEntry> _highScores = new List<ScoreEntry>();
	
	// Nombre maximum de scores à conserver
	private const int MAX_SCORES = 5;
	
	// Chemin du fichier de sauvegarde
	private  string _savePath = "user://saves/highscores.json";
	
	// Singleton
	private static ScoreManager _instance;
	public static ScoreManager Instance
	{
		get { return _instance; }
		private set { _instance = value; }
	}
	
	public override void _Ready()
	{
		// Implémenter le pattern Singleton
		if (Instance != null && Instance != this)
		{
			QueueFree();
			return;
		}
		
		Instance = this;
		
		// Assurer que le répertoire de sauvegarde existe
		EnsureSaveDirectoryExists();
		
		// Charger les scores existants
		LoadHighScores();
		
		// S'assurer que la liste des scores est valide
		ValidateScores();
		
		GD.Print("ScoreManager initialisé comme singleton");
	}
	
	// Assurer que le répertoire de sauvegarde existe
private void EnsureSaveDirectoryExists()
{
	string saveDir = "user://saves/";
	string absPath = ProjectSettings.GlobalizePath(saveDir);

	if (!DirAccess.DirExistsAbsolute(absPath))
	{
		var dir = DirAccess.Open("user://");
		if (dir != null)
		{
			dir.MakeDirRecursive("saves");
			GD.Print("Répertoire de sauvegarde créé");
		}
		else
		{
			GD.PrintErr("Impossible d'ouvrir le répertoire user://");
		}
	}
	else
	{
		GD.Print("Répertoire de sauvegarde déjà existant");
	}
}
	
	// Charger les scores depuis le fichier JSON
// Dans LoadHighScores() du ScoreManager, ajoutons une vérification supplémentaire
private void LoadHighScores()
{
	_highScores.Clear();



	try
	{
		// Vérifier si le fichier existe
		if (Godot.FileAccess.FileExists(_savePath))
		{
			using var file = Godot.FileAccess.Open(_savePath, Godot.FileAccess.ModeFlags.Read);
			if (file != null)
			{
				string jsonContent = file.GetAsText();
				GD.Print($"Contenu du fichier de scores: {jsonContent}");

				try
				{
					// Tenter de désérialiser même si le JSON contient [{}]
					var loadedScores = JsonSerializer.Deserialize<List<ScoreEntry>>(jsonContent);
					if (loadedScores != null)
					{
						foreach (var score in loadedScores)
						{
							// Vérifier si l'objet n'est pas null et contient au moins une donnée significative
							if (score.mazesCompleted >= 0 || score.totalCats >= 0)
								{
								_highScores.Add(score);
								}
						}

						GD.Print($"Chargement réussi de {_highScores.Count} scores valides");

						// Si aucun score valide n'a été trouvé, on peut choisir de régénérer un fichier propre
						if (_highScores.Count == 0)
						{
							GD.Print("Aucun score valide trouvé. Création d'une nouvelle liste propre.");
							SaveHighScores();
						}
					}
					else
					{
						GD.Print("JSON vide ou invalide. Création d'une nouvelle liste.");
						_highScores = new List<ScoreEntry>();
						SaveHighScores();
					}
				}
				catch (JsonException ex)
				{
					GD.PrintErr($"Erreur JSON: {ex.Message}. Création d'une nouvelle liste de scores.");
					_highScores = new List<ScoreEntry>();
					SaveHighScores();
				}
			}
		}
		else
		{
			GD.Print("Aucun fichier de scores trouvé. Création d'une nouvelle liste.");
			_highScores = new List<ScoreEntry>();
		}
	}
	catch (Exception e)
	{
		GD.PrintErr($"Erreur lors du chargement des scores: {e.Message}");
		_highScores = new List<ScoreEntry>();
	}
	
	SortAndLimitScores();
}

	
	// Vérifier et corriger les scores si nécessaire
	private void ValidateScores()
	{
		bool needsSaving = false;
		
		// Supprimer les scores invalides (tous à zéro)
		for (int i = _highScores.Count - 1; i >= 0; i--)
		{
			if (_highScores[i].mazesCompleted <= 0 && _highScores[i].totalCats <= 0)
			{
				_highScores.RemoveAt(i);
				needsSaving = true;
			}
		}
		
		// Si des modifications ont été faites, sauvegarder
		if (needsSaving)
		{
			SaveHighScores();
		}
	}
	
	// Trier et limiter les scores
	private void SortAndLimitScores()
	{
		// Trier les scores (par nombre de labyrinthes puis par nombre de chats)
		_highScores.Sort((a, b) => {
			int mazeCompare = b.mazesCompleted.CompareTo(a.mazesCompleted);
			if (mazeCompare != 0) return mazeCompare;
			return b.totalCats.CompareTo(a.totalCats);
		});
		
		// Limiter à MAX_SCORES
		if (_highScores.Count > MAX_SCORES)
		{
			_highScores.RemoveRange(MAX_SCORES, _highScores.Count - MAX_SCORES);
		}
	}
	
	// Sauvegarder les scores
	private void SaveHighScores()
	{
		try
		{
			// S'assurer qu'il y a des scores à sauvegarder ou créer un tableau vide
			var scoresToSave = _highScores.Count > 0 ? _highScores : new List<ScoreEntry>();
			
			// Sérialiser la liste en JSON
			var jsonOptions = new JsonSerializerOptions
			{
				WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase // 💥 clé du truc !
			};
			string jsonString = JsonSerializer.Serialize(scoresToSave, jsonOptions);
			
			// Créer le répertoire si nécessaire
			EnsureSaveDirectoryExists();
			
			// Écrire dans le fichier
			using var file = Godot.FileAccess.Open(_savePath, Godot.FileAccess.ModeFlags.Write);
			if (file != null)
			{
				file.StoreString(jsonString);
				file.Flush();
				GD.Print($"Scores sauvegardés avec succès dans {_savePath}");
				
				// Afficher les scores pour débogage
				GD.Print("=== SCORES SAUVEGARDÉS ===");
				for (int i = 0; i < _highScores.Count; i++)
				{
					GD.Print($"{i+1}. Labyrinthes: {_highScores[i].mazesCompleted}, Chats: {_highScores[i].totalCats}, Date: {_highScores[i].date}");
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Erreur lors de la sauvegarde des scores: {e.Message}");
		}
	}
	
	// Ajouter un nouveau score
	public void AddScore(int mazesCompleted, int totalCats)
	{
		// Vérifier que le score est valide
		if (mazesCompleted <= 0 && totalCats <= 0)
		{
			GD.PrintErr("Tentative d'ajout d'un score invalide");
			return;
		}
		
		GD.Print($"Ajout du score: Labyrinthes={mazesCompleted}, Chats={totalCats}");
		
		// Créer et ajouter le score
		var newScore = new ScoreEntry(mazesCompleted, totalCats);
		_highScores.Add(newScore);
		
		// Trier et limiter
		SortAndLimitScores();
		
		// Sauvegarder
		SaveHighScores();
	}
	
	// Effacer tous les scores (pour réinitialiser)
	public void ClearAllScores()
	{
		_highScores.Clear();
		SaveHighScores();
		GD.Print("Tous les scores ont été effacés");
	}
	
	// Obtenir la liste des scores pour l'affichage
	public List<ScoreEntry> GetHighScores()
	{
		return new List<ScoreEntry>(_highScores);
	}
}
