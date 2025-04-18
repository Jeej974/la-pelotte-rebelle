using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;

/**
 * ScoreManager - Gestionnaire des scores centralisé
 * 
 * Classe indépendante qui gère la sauvegarde, le chargement et le tri
 * des scores du jeu avec persistance entre les sessions.
 * Implémente un pattern Singleton.
 */
public partial class ScoreManager : Node
{
	// Structure de données pour les entrées de score
	[Serializable]
	public struct ScoreEntry
	{
		public int mazesCompleted { get; set; }
		public int totalCats { get; set; }
		public string date { get; set; }
		
		/**
		 * Crée une nouvelle entrée de score
		 */
		public ScoreEntry(int mazes, int cats)
		{
			mazesCompleted = mazes;
			totalCats = cats;
			date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
		}
		
		/**
		 * Format d'affichage personnalisé pour l'UI
		 */
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
	private string _savePath = "user://saves/highscores.json";
	
	// Implémentation du Singleton
	private static ScoreManager _instance;
	public static ScoreManager Instance
	{
		get { return _instance; }
		private set { _instance = value; }
	}
	
	/**
	 * Initialisation du gestionnaire de scores
	 */
	public override void _Ready()
	{
		// Configuration du Singleton
		if (Instance != null && Instance != this)
		{
			QueueFree();
			return;
		}
		
		Instance = this;
		
		// Préparation de la persistance
		EnsureSaveDirectoryExists();
		
		// Chargement des scores existants
		LoadHighScores();
		
		// Validation des données
		ValidateScores();
		
		GD.Print("ScoreManager initialisé comme singleton");
	}
	
	/**
	 * S'assure que le répertoire de sauvegarde existe
	 */
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
	
	/**
	 * Charge les scores depuis le fichier JSON avec gestion d'erreurs robuste
	 */
	private void LoadHighScores()
	{
		_highScores.Clear();

		try
		{
			// Vérification de l'existence du fichier
			if (Godot.FileAccess.FileExists(_savePath))
			{
				using var file = Godot.FileAccess.Open(_savePath, Godot.FileAccess.ModeFlags.Read);
				if (file != null)
				{
					string jsonContent = file.GetAsText();
					GD.Print($"Contenu du fichier de scores: {jsonContent}");

					try
					{
						// Désérialisation avec validation des données
						var loadedScores = JsonSerializer.Deserialize<List<ScoreEntry>>(jsonContent);
						if (loadedScores != null)
						{
							foreach (var score in loadedScores)
							{
								// Validation des données
								if (score.mazesCompleted >= 0 || score.totalCats >= 0)
								{
									_highScores.Add(score);
								}
							}

							GD.Print($"Chargement réussi de {_highScores.Count} scores valides");

							// Régénération si aucun score valide
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
	
	/**
	 * Vérifie et corrige les scores si nécessaire
	 */
	private void ValidateScores()
	{
		bool needsSaving = false;
		
		// Suppression des scores invalides
		for (int i = _highScores.Count - 1; i >= 0; i--)
		{
			if (_highScores[i].mazesCompleted <= 0 && _highScores[i].totalCats <= 0)
			{
				_highScores.RemoveAt(i);
				needsSaving = true;
			}
		}
		
		// Sauvegarde si des modifications ont été faites
		if (needsSaving)
		{
			SaveHighScores();
		}
	}
	
	/**
	 * Trie et limite les scores selon les paramètres configurés
	 */
	private void SortAndLimitScores()
	{
		// Tri par nombre de labyrinthes puis par nombre de chats
		_highScores.Sort((a, b) => {
			int mazeCompare = b.mazesCompleted.CompareTo(a.mazesCompleted);
			if (mazeCompare != 0) return mazeCompare;
			return b.totalCats.CompareTo(a.totalCats);
		});
		
		// Limitation au nombre maximum configuré
		if (_highScores.Count > MAX_SCORES)
		{
			_highScores.RemoveRange(MAX_SCORES, _highScores.Count - MAX_SCORES);
		}
	}
	
	/**
	 * Sauvegarde les scores dans un fichier JSON
	 */
	private void SaveHighScores()
	{
		try
		{
			// S'assurer qu'il y a quelque chose à sauvegarder
			var scoresToSave = _highScores.Count > 0 ? _highScores : new List<ScoreEntry>();
			
			// Configuration de la sérialisation
			var jsonOptions = new JsonSerializerOptions
			{
				WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase // Important pour la compatibilité
			};
			string jsonString = JsonSerializer.Serialize(scoresToSave, jsonOptions);
			
			// Création du répertoire si nécessaire
			EnsureSaveDirectoryExists();
			
			// Écriture dans le fichier
			using var file = Godot.FileAccess.Open(_savePath, Godot.FileAccess.ModeFlags.Write);
			if (file != null)
			{
				file.StoreString(jsonString);
				file.Flush();
				GD.Print($"Scores sauvegardés avec succès dans {_savePath}");
				
				// Affichage des scores sauvegardés pour débogage
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
	
	/**
	 * Ajoute un nouveau score à la liste
	 */
	public void AddScore(int mazesCompleted, int totalCats)
	{
		// Validation des données d'entrée
		if (mazesCompleted <= 0 && totalCats <= 0)
		{
			GD.PrintErr("Tentative d'ajout d'un score invalide");
			return;
		}
		
		GD.Print($"Ajout du score: Labyrinthes={mazesCompleted}, Chats={totalCats}");
		
		// Création et ajout du score
		var newScore = new ScoreEntry(mazesCompleted, totalCats);
		_highScores.Add(newScore);
		
		// Tri et limitation
		SortAndLimitScores();
		
		// Sauvegarde des modifications
		SaveHighScores();
	}
	
	/**
	 * Efface tous les scores (réinitialisation)
	 */
	public void ClearAllScores()
	{
		_highScores.Clear();
		SaveHighScores();
		GD.Print("Tous les scores ont été effacés");
	}
	
	/**
	 * Retourne une copie de la liste des scores
	 */
	public List<ScoreEntry> GetHighScores()
	{
		return new List<ScoreEntry>(_highScores);
	}
}
