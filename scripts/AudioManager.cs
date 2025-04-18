using Godot;
using System;
using System.Collections.Generic;

/**
 * AudioManager - Gestionnaire audio hybride pour Godot
 * 
 * Système audio complet qui fonctionne avec FMOD (si disponible) ou le système audio natif de Godot.
 * Implémente un pattern Singleton pour garantir une gestion centralisée de tous les sons du jeu.
 * Gère les sons ponctuels, les sons en boucle, les sons 3D, et les transitions entre différents états sonores.
 */
public partial class AudioManager : Node
{
	// Implémentation du pattern Singleton
	private static AudioManager _instance;
	public static AudioManager Instance => _instance;

	// Chemins des sons de fallback (utilisés quand FMOD n'est pas disponible)
	private Dictionary<string, string> _fallbackPaths = new Dictionary<string, string>
	{
		{ "CatOrange", "res://assets/audio/cat_orange.wav" },
		{ "CatBlack", "res://assets/audio/chat_noir.wav" },
		{ "CatTabby", "res://assets/audio/bruit_chat.wav" },
		{ "CatWhite", "res://assets/audio/chat_blanc.wav" },
		{ "CatSiamese", "res://assets/audio/bruit_chat_siamois.wav" },
		{ "Bonus", "res://assets/audio/son_bonus.wav" },
		{ "Malus", "res://assets/audio/bruit_malus.wav" },
		{ "Teleport", "res://assets/audio/bruit_teleporteur.wav" },
		{ "GameStart", "res://assets/audio/musique_debut_pause.wav" },
		{ "GameOver", "res://assets/audio/ambient.wav" },
		{ "Ambiance", "res://assets/audio/son_ambient.wav" },
		{ "RollingBall", "res://assets/audio/bruit_pellote.wav" },
		{ "PauseMusic", "res://assets/audio/musique_debut_pause.wav" }
	};

	// Suivi des sons en cours de lecture
	private Dictionary<string, AudioStreamPlayer> _loopingSounds = new Dictionary<string, AudioStreamPlayer>();
	private Dictionary<string, AudioStreamPlayer3D> _looping3DSounds = new Dictionary<string, AudioStreamPlayer3D>();
	private List<Node> _activePlayers = new List<Node>();

	// Configuration FMOD
	private bool _useFmod = false;
	private GodotObject _fmodStudio = null;

	// États des catégories de sons
	private bool _ambianceActive = false;
	private bool _pauseMusicActive = false;
	private bool _rollingBallActive = false;
	private bool _gameOverMusicActive = false;
	private bool _gameStartMusicActive = false;

	// Paramètres de volume
	private const float MUSIC_VOLUME = -10.0f;  // Volume de musique plus bas que les effets
	private const float SFX_VOLUME = 0.0f;     // Volume standard pour les effets sonores

	// Catégorisation des sons pour la gestion des priorités
	private enum SoundCategory
	{
		Music,
		SFX,
		UI,
		Ambient
	}

	// Mapping entre les noms de sons et leurs catégories
	private Dictionary<string, SoundCategory> _soundCategories = new Dictionary<string, SoundCategory>()
	{
		{ "GameStart", SoundCategory.Music },
		{ "GameOver", SoundCategory.Music },
		{ "Ambiance", SoundCategory.Music },
		{ "PauseMusic", SoundCategory.Music },
		{ "RollingBall", SoundCategory.SFX },
		{ "Bonus", SoundCategory.SFX },
		{ "Malus", SoundCategory.SFX },
		{ "Teleport", SoundCategory.SFX },
		{ "CatOrange", SoundCategory.SFX },
		{ "CatBlack", SoundCategory.SFX },
		{ "CatTabby", SoundCategory.SFX },
		{ "CatWhite", SoundCategory.SFX },
		{ "CatSiamese", SoundCategory.SFX }
	};

	/**
	 * Initialisation du node et configuration du Singleton
	 * Détecte et configure FMOD si disponible
	 */
	public override void _Ready()
	{
		// Configuration du singleton
		if (_instance != null && _instance != this)
		{
			QueueFree();
			return;
		}
		_instance = this;

		// Détection et initialisation de FMOD
		if (Engine.HasSingleton("FmodStudio"))
		{
			_fmodStudio = Engine.GetSingleton("FmodStudio");
			_useFmod = true;
			GD.Print("AudioManager: FMOD disponible et activé");
		}
		else
		{
			GD.Print("AudioManager: FMOD non disponible, utilisation du système audio Godot");
		}
	}

	/**
	 * Mise à jour par frame pour nettoyer les lecteurs audio inactifs
	 */
	public override void _Process(double delta)
	{
		// Nettoyage des lecteurs inactifs
		for (int i = _activePlayers.Count - 1; i >= 0; i--)
		{
			if (!IsInstanceValid(_activePlayers[i]) || !_activePlayers[i].IsInsideTree())
			{
				_activePlayers.RemoveAt(i);
			}
		}
	}

	/**
	 * Joue un son 2D ponctuel
	 * @param soundName Nom identifiant du son à jouer
	 */
	public void PlaySound(string soundName)
	{
		// Essayer FMOD en priorité
		if (_useFmod && _fmodStudio != null)
		{
			try
			{
				string eventPath = GetFmodEventPath(soundName);
				_fmodStudio.Call("play_one_shot", eventPath);
				GD.Print($"AudioManager: Son '{soundName}' joué via FMOD");
				return;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"AudioManager: Erreur FMOD ({ex.Message}), utilisation du fallback");
			}
		}

		// Fallback au système Godot
		PlaySoundFallback(soundName);
	}

	/**
	 * Joue un son en boucle
	 * Arrête automatiquement les sons concurrents de la même catégorie
	 * @param soundName Nom identifiant du son à jouer en boucle
	 */
	public void PlayLoopingSound(string soundName)
	{
		GD.Print($"AudioManager: Tentative de jouer '{soundName}' en boucle");

		// Éviter de relancer un son déjà en cours
		if (IsLoopingSoundPlaying(soundName))
		{
			GD.Print($"AudioManager: Son '{soundName}' déjà en cours de lecture");
			return;
		}

		// Arrêter les sons concurrents de la même catégorie
		StopCompetingSounds(soundName);

		// Essayer FMOD en priorité
		if (_useFmod && _fmodStudio != null)
		{
			try
			{
				string eventPath = GetFmodEventPath(soundName);
				var instance = _fmodStudio.Call("create_event_instance", eventPath);
				_fmodStudio.Call("start_event", instance);
				
				// Marquer le son comme actif
				MarkSoundActive(soundName, true);
				
				GD.Print($"AudioManager: Son en boucle '{soundName}' démarré via FMOD");
				return;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"AudioManager: Erreur FMOD ({ex.Message}), utilisation du fallback");
			}
		}

		// Fallback au système audio Godot
		PlayLoopingSoundFallback(soundName);
	}

	/**
	 * Arrête les sons concurrents de la même catégorie
	 * Par exemple, arrête toutes les musiques si on démarre une nouvelle musique
	 * @param soundName Nom du son dont on veut arrêter les concurrents
	 */
	private void StopCompetingSounds(string soundName)
	{
		// Vérifier que la catégorie du son est connue
		if (!_soundCategories.TryGetValue(soundName, out SoundCategory category))
		{
			return;
		}

		// Logique spécifique pour la catégorie Music
		if (category == SoundCategory.Music)
		{
			if (soundName == "Ambiance")
			{
				StopLoopingSound("GameStart");
				StopLoopingSound("PauseMusic");
				StopLoopingSound("GameOver");
			}
			else if (soundName == "PauseMusic")
			{
				StopLoopingSound("Ambiance");
				StopLoopingSound("GameStart");
				StopLoopingSound("GameOver");
			}
			else if (soundName == "GameStart")
			{
				StopLoopingSound("Ambiance");
				StopLoopingSound("PauseMusic");
				StopLoopingSound("GameOver");
			}
			else if (soundName == "GameOver")
			{
				StopLoopingSound("Ambiance");
				StopLoopingSound("PauseMusic");
				StopLoopingSound("GameStart");
			}
		}
	}

	/**
	 * Arrête un son spécifique qui joue en boucle
	 * @param soundName Nom du son à arrêter
	 */
	public void StopLoopingSound(string soundName)
	{
		GD.Print($"AudioManager: Tentative d'arrêter '{soundName}'");

		// Arrêter via FMOD si disponible
		if (_useFmod && _fmodStudio != null)
		{
			try
			{
				string eventPath = GetFmodEventPath(soundName);
				_fmodStudio.Call("stop_events_by_name", eventPath);
				
				// Marquer le son comme inactif
				MarkSoundActive(soundName, false);
				
				GD.Print($"AudioManager: Son en boucle '{soundName}' arrêté via FMOD");
				return;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"AudioManager: Erreur FMOD lors de l'arrêt ({ex.Message}), utilisation du fallback");
			}
		}

		// Fallback pour les lecteurs Godot 2D
		if (_loopingSounds.ContainsKey(soundName))
		{
			var player = _loopingSounds[soundName];
			if (IsInstanceValid(player) && player.IsInsideTree())
			{
				player.Stop();
				player.QueueFree();
				_activePlayers.Remove(player);
			}
			
			_loopingSounds.Remove(soundName);
			MarkSoundActive(soundName, false);
			
			GD.Print($"AudioManager: Son en boucle '{soundName}' arrêté");
		}
		// Fallback pour les lecteurs Godot 3D
		else if (_looping3DSounds.ContainsKey(soundName))
		{
			var player = _looping3DSounds[soundName];
			if (IsInstanceValid(player) && player.IsInsideTree())
			{
				player.Stop();
				player.QueueFree();
				_activePlayers.Remove(player);
			}
			
			_looping3DSounds.Remove(soundName);
			MarkSoundActive(soundName, false);
			
			GD.Print($"AudioManager: Son 3D en boucle '{soundName}' arrêté");
		}
		else 
		{
			// Réinitialiser le flag même sans lecteur trouvé
			MarkSoundActive(soundName, false);
			GD.Print($"AudioManager: Aucun lecteur trouvé pour '{soundName}', mais flag réinitialisé");
		}
	}

	/**
	 * Arrête tous les sons d'une catégorie spécifique
	 * @param category Nom de la catégorie de sons à arrêter
	 */
	public void StopSoundCategory(string category)
	{
		GD.Print($"AudioManager: Arrêt de la catégorie '{category}'");

		switch (category)
		{
			case "Music":
				StopLoopingSound("Ambiance");
				StopLoopingSound("PauseMusic");
				StopLoopingSound("GameStart");
				StopLoopingSound("GameOver");
				break;
				
			case "SFX":
				StopLoopingSound("RollingBall");
				break;
				
			case "All":
				StopAllSounds();
				break;
		}
	}

	/**
	 * Met à jour l'état interne de lecture pour un son spécifique
	 * @param soundName Nom du son
	 * @param active État d'activation
	 */
	private void MarkSoundActive(string soundName, bool active)
	{
		switch (soundName)
		{
			case "Ambiance":
				_ambianceActive = active;
				GD.Print($"AudioManager: Ambiance est maintenant {(active ? "actif" : "inactif")}");
				break;
				
			case "PauseMusic":
				_pauseMusicActive = active;
				GD.Print($"AudioManager: PauseMusic est maintenant {(active ? "actif" : "inactif")}");
				break;
				
			case "RollingBall":
				_rollingBallActive = active;
				GD.Print($"AudioManager: RollingBall est maintenant {(active ? "actif" : "inactif")}");
				break;
				
			case "GameOver":
				_gameOverMusicActive = active;
				GD.Print($"AudioManager: GameOver est maintenant {(active ? "actif" : "inactif")}");
				break;
				
			case "GameStart":
				_gameStartMusicActive = active;
				GD.Print($"AudioManager: GameStart est maintenant {(active ? "actif" : "inactif")}");
				break;
		}

		// Log de débogage de l'état audio
		if (active)
		{
			DumpCurrentAudioState();
		}
	}

	/**
	 * Affiche dans la console l'état actuel de tous les sons
	 * Utile pour le débogage
	 */
	private void DumpCurrentAudioState()
	{
		GD.Print("=== ÉTAT AUDIO ACTUEL ===");
		GD.Print($"Ambiance: {_ambianceActive}");
		GD.Print($"PauseMusic: {_pauseMusicActive}");
		GD.Print($"GameStart: {_gameStartMusicActive}");
		GD.Print($"GameOver: {_gameOverMusicActive}");
		GD.Print($"RollingBall: {_rollingBallActive}");
		GD.Print($"Sons en boucle actifs: {_loopingSounds.Count}");
		GD.Print($"Sons 3D en boucle actifs: {_looping3DSounds.Count}");
		GD.Print($"Total lecteurs actifs: {_activePlayers.Count}");
		GD.Print("=========================");
	}

	/**
	 * Vérifie si un son en boucle est actuellement joué
	 * @param soundName Nom du son à vérifier
	 * @return true si le son est en cours de lecture
	 */
	public bool IsLoopingSoundPlaying(string soundName)
	{
		switch (soundName)
		{
			case "Ambiance":
				return _ambianceActive;
				
			case "PauseMusic":
				return _pauseMusicActive;
				
			case "RollingBall":
				return _rollingBallActive;
				
			case "GameOver":
				return _gameOverMusicActive;
				
			case "GameStart":
				return _gameStartMusicActive;
				
			default:
				// Vérifier dans les dictionnaires de lecteurs
				return _loopingSounds.ContainsKey(soundName) || _looping3DSounds.ContainsKey(soundName);
		}
	}

	/**
	 * Joue un son 3D à la position d'un objet
	 * @param soundName Nom du son à jouer
	 * @param target Objet 3D auquel le son est attaché
	 */
	public void PlaySound3D(string soundName, Node3D target)
	{
		if (target == null)
		{
			GD.PrintErr("AudioManager: Cible nulle pour son 3D");
			return;
		}

		// Essayer FMOD en priorité
		if (_useFmod && _fmodStudio != null)
		{
			try
			{
				string eventPath = GetFmodEventPath(soundName);
				_fmodStudio.Call("play_one_shot_attached", eventPath, target);
				GD.Print($"AudioManager: Son 3D '{soundName}' joué via FMOD");
				return;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"AudioManager: Erreur FMOD 3D ({ex.Message}), utilisation du fallback");
			}
		}

		// Fallback au système audio Godot
		PlaySound3DFallback(soundName, target);
	}

	/**
	 * Joue un son 3D en boucle attaché à un objet
	 * @param soundName Nom du son à jouer
	 * @param target Objet 3D auquel le son est attaché
	 */
	public void PlayLoopingSound3D(string soundName, Node3D target)
	{
		if (target == null)
		{
			GD.PrintErr("AudioManager: Cible nulle pour son 3D en boucle");
			return;
		}

		// Éviter de relancer un son déjà en cours
		if (IsLoopingSoundPlaying(soundName))
		{
			GD.Print($"AudioManager: Son 3D en boucle '{soundName}' déjà actif");
			return;
		}

		// Arrêter les sons concurrents
		StopCompetingSounds(soundName);

		// Essayer FMOD en priorité
		if (_useFmod && _fmodStudio != null)
		{
			try
			{
				string eventPath = GetFmodEventPath(soundName);
				var instance = _fmodStudio.Call("create_event_instance", eventPath);
				_fmodStudio.Call("set_event_3d_attributes", instance, target);
				_fmodStudio.Call("start_event", instance);
				
				// Marquer comme actif
				MarkSoundActive(soundName, true);
				
				GD.Print($"AudioManager: Son 3D en boucle '{soundName}' démarré via FMOD");
				return;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"AudioManager: Erreur FMOD 3D boucle ({ex.Message}), utilisation du fallback");
			}
		}

		// Fallback au système audio Godot
		PlayLoopingSound3DFallback(soundName, target);
	}

	/**
	 * Arrête tous les sons en cours
	 * Nettoie tous les lecteurs et réinitialise les flags
	 */
	public void StopAllSounds()
	{
		GD.Print("AudioManager: Arrêt de tous les sons");

		// Arrêter les sons FMOD si disponible
		if (_useFmod && _fmodStudio != null)
		{
			try
			{
				_fmodStudio.Call("stop_all_events");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"AudioManager: Erreur lors de l'arrêt des sons FMOD: {ex.Message}");
			}
		}

		// Arrêter tous les lecteurs Godot
		foreach (var player in _activePlayers)
		{
			if (IsInstanceValid(player))
			{
				if (player is AudioStreamPlayer audioPlayer)
				{
					audioPlayer.Stop();
					audioPlayer.QueueFree();
				}
				else if (player is AudioStreamPlayer3D audioPlayer3D)
				{
					audioPlayer3D.Stop();
					audioPlayer3D.QueueFree();
				}
			}
		}
		
		// Vider les collections
		_activePlayers.Clear();
		_loopingSounds.Clear();
		_looping3DSounds.Clear();
		
		// Réinitialiser tous les flags
		_ambianceActive = false;
		_pauseMusicActive = false;
		_rollingBallActive = false;
		_gameOverMusicActive = false;
		_gameStartMusicActive = false;
		
		GD.Print("AudioManager: Tous les sons ont été arrêtés");
	}

	/**
	 * Définit un paramètre global FMOD
	 * @param name Nom du paramètre
	 * @param value Valeur du paramètre
	 */
	public void SetGlobalParameter(string name, float value)
	{
		if (_useFmod && _fmodStudio != null)
		{
			try
			{
				_fmodStudio.Call("set_global_parameter", name, value);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"AudioManager: Erreur lors de la définition du paramètre '{name}': {ex.Message}");
			}
		}
	}

	/**
	 * Effectue une transition douce entre deux états sonores
	 * @param newSoundName Nouveau son à démarrer
	 * @param oldSoundName Ancien son à arrêter (optionnel)
	 */
	public void TransitionToSound(string newSoundName, string oldSoundName = null)
	{
		GD.Print($"AudioManager: Transition de '{oldSoundName}' à '{newSoundName}'");

		// Arrêter l'ancien son si spécifié
		if (!string.IsNullOrEmpty(oldSoundName))
		{
			StopLoopingSound(oldSoundName);
		}
		else
		{
			// Arrêter la catégorie entière si pas de son spécifique
			if (_soundCategories.TryGetValue(newSoundName, out SoundCategory category))
			{
				if (category == SoundCategory.Music)
				{
					StopSoundCategory("Music");
				}
			}
		}
		
		// Démarrer le nouveau son
		PlayLoopingSound(newSoundName);
	}

	/**
	 * Conversion du nom de son vers le chemin d'événement FMOD
	 * @param soundName Nom du son dans le système
	 * @return Chemin d'événement FMOD correspondant
	 */
	private string GetFmodEventPath(string soundName)
	{
		// Mappage des noms vers les chemins d'événements FMOD
		switch (soundName)
		{
			case "CatOrange":
			case "CatMeowOrange":
				return "event:/Son jeu/son_chatroux";
			case "CatBlack":
			case "CatMeowBlack":
				return "event:/Son jeu/son_chatnoir";
			case "CatTabby":
			case "CatMeowTabby":
				return "event:/Son jeu/son_chattigre";
			case "CatWhite":
			case "CatMeowWhite":
				return "event:/Son jeu/son_chatblanc";
			case "CatSiamese":
			case "CatMeowSiamese":
				return "event:/Son jeu/son_chatsiamois";
			case "Bonus":
				return "event:/Son jeu/son_bonus";
			case "Malus":
				return "event:/Son jeu/son_malus";
			case "Teleport":
				return "event:/Son jeu/son_teleportation";
			case "GameStart":
			case "GameOver":
			case "GameStartEnd":
				return "event:/Son jeu/son_debut-fin";
			case "Ambiance":
				return "event:/Son jeu/son_ambiance";
			case "RollingBall":
				return "event:/Son jeu/bruit_pellote";
			case "PauseMusic":
				return "event:/Son jeu/musique_pause";
			default:
				return "event:/Son jeu/son_chatroux"; // Fallback
		}
	}

	/**
	 * Joue un son via le système audio Godot (fallback)
	 * @param soundName Nom du son à jouer
	 */
	private void PlaySoundFallback(string soundName)
	{
		string path = GetFallbackPath(soundName);
		var stream = ResourceLoader.Load<AudioStream>(path);
		
		if (stream != null)
		{
			var player = new AudioStreamPlayer();
			AddChild(player);
			_activePlayers.Add(player);
			
			player.Stream = stream;
			
			// Ajustement du volume selon la catégorie
			if (soundName == "Ambiance" || soundName == "PauseMusic" || soundName == "GameStart" || soundName == "GameOver")
			{
				player.VolumeDb = MUSIC_VOLUME;
			}
			else
			{
				player.VolumeDb = SFX_VOLUME;
			}
			
			player.Play();
			player.Finished += () => OnSoundFinished(player);
			
			GD.Print($"AudioManager: Son '{soundName}' joué via fallback");
		}
	}

	/**
	 * Joue un son en boucle via le système audio Godot (fallback)
	 * @param soundName Nom du son à jouer en boucle
	 */
	private void PlayLoopingSoundFallback(string soundName)
	{
		string path = GetFallbackPath(soundName);
		var stream = ResourceLoader.Load<AudioStream>(path);
		
		if (stream != null)
		{
			// Nettoyer l'ancien lecteur s'il existe
			if (_loopingSounds.ContainsKey(soundName))
			{
				var oldPlayer = _loopingSounds[soundName];
				if (IsInstanceValid(oldPlayer))
				{
					oldPlayer.Stop();
					oldPlayer.QueueFree();
					_activePlayers.Remove(oldPlayer);
				}
				
				_loopingSounds.Remove(soundName);
				GD.Print($"AudioManager: Ancien lecteur pour '{soundName}' nettoyé");
			}
			
			// Créer un nouveau lecteur
			var player = new AudioStreamPlayer();
			AddChild(player);
			_activePlayers.Add(player);
			
			player.Stream = stream;
			
			// Configuration du volume
			if (soundName == "Ambiance" || soundName == "PauseMusic" || soundName == "GameStart" || soundName == "GameOver")
			{
				player.VolumeDb = MUSIC_VOLUME;
			}
			else
			{
				player.VolumeDb = SFX_VOLUME;
			}
			
			// Configuration pour la lecture en boucle
			player.Finished += () => {
				if (IsInstanceValid(player) && player.IsInsideTree())
				{
					player.Play();
					GD.Print($"AudioManager: Rebouclage de '{soundName}'");
				}
			};
			
			// Stocker la référence
			_loopingSounds[soundName] = player;
			
			// Marquer comme actif
			MarkSoundActive(soundName, true);
			
			player.Play();
			
			GD.Print($"AudioManager: Son en boucle '{soundName}' démarré via fallback");
		}
	}

	/**
	 * Joue un son 3D via le système audio Godot (fallback)
	 * @param soundName Nom du son à jouer
	 * @param target Objet 3D auquel le son est attaché
	 */
	private void PlaySound3DFallback(string soundName, Node3D target)
	{
		string path = GetFallbackPath(soundName);
		var stream = ResourceLoader.Load<AudioStream>(path);
		
		if (stream != null)
		{
			var player = new AudioStreamPlayer3D();
			target.AddChild(player);
			_activePlayers.Add(player);
			
			player.Stream = stream;
			
			// Configuration de base
			player.MaxDistance = 20.0f;
			
			// Personnalisation pour les sons de chats
			if (soundName.StartsWith("Cat"))
			{
				switch (soundName)
				{
					case "CatBlack":
					case "CatMeowBlack":
						player.PitchScale = 0.8f;
						break;
					case "CatTabby":
					case "CatMeowTabby":
						player.PitchScale = 1.1f;
						break;
					case "CatWhite":
					case "CatMeowWhite":
						player.PitchScale = 1.2f;
						break;
					case "CatSiamese":
					case "CatMeowSiamese":
						player.PitchScale = 1.3f;
						break;
				}
			}
			
			player.Play();
			player.Finished += () => OnSoundFinished(player);
			
			GD.Print($"AudioManager: Son 3D '{soundName}' joué via fallback");
		}
	}

	/**
	 * Joue un son 3D en boucle via le système audio Godot (fallback)
	 * @param soundName Nom du son à jouer en boucle
	 * @param target Objet 3D auquel le son est attaché
	 */
	private void PlayLoopingSound3DFallback(string soundName, Node3D target)
	{
		string path = GetFallbackPath(soundName);
		var stream = ResourceLoader.Load<AudioStream>(path);
		
		if (stream != null)
		{
			// Nettoyer l'ancien lecteur s'il existe
			if (_looping3DSounds.ContainsKey(soundName))
			{
				var oldPlayer = _looping3DSounds[soundName];
				if (IsInstanceValid(oldPlayer))
				{
					oldPlayer.Stop();
					oldPlayer.QueueFree();
					_activePlayers.Remove(oldPlayer);
				}
				
				_looping3DSounds.Remove(soundName);
				GD.Print($"AudioManager: Ancien lecteur 3D pour '{soundName}' nettoyé");
			}
			
			// Créer un nouveau lecteur 3D
			var player = new AudioStreamPlayer3D();
			target.AddChild(player);
			_activePlayers.Add(player);
			
			player.Stream = stream;
			
			// Configuration pour le son 3D
			player.MaxDistance = 20.0f;
			
			// Ajustement du volume selon le type
			if (soundName == "RollingBall")
			{
				player.VolumeDb = -5.0f; // Volume réduit pour le son de roulement
			}
			else
			{
				player.VolumeDb = 0.0f;
			}
			
			// Personnalisation pour les sons de chats
			if (soundName.StartsWith("Cat"))
			{
				AdjustCatSound(player, soundName);
			}
			
			// Configuration pour la lecture en boucle
			player.Finished += () => {
				if (IsInstanceValid(player) && player.IsInsideTree())
				{
					player.Play();
					GD.Print($"AudioManager: Rebouclage 3D de '{soundName}'");
				}
			};
			
			// Stocker la référence
			_looping3DSounds[soundName] = player;
			
			// Marquer comme actif
			MarkSoundActive(soundName, true);
			
			player.Play();
			
			GD.Print($"AudioManager: Son 3D en boucle '{soundName}' démarré via fallback");
		}
	}

	/**
	 * Ajuste les paramètres sonores pour les sons de chats
	 * @param player Lecteur audio 3D à configurer
	 * @param soundName Nom du son de chat
	 */
	private void AdjustCatSound(AudioStreamPlayer3D player, string soundName)
	{
		switch (soundName)
		{
			case "CatBlack":
			case "CatMeowBlack":
				player.PitchScale = 0.8f;
				break;
			case "CatTabby":
			case "CatMeowTabby":
				player.PitchScale = 1.1f;
				break;
			case "CatWhite":
			case "CatMeowWhite":
				player.PitchScale = 1.2f;
				break;
			case "CatSiamese":
			case "CatMeowSiamese":
				player.PitchScale = 1.3f;
				break;
		}
	}

	/**
	 * Gestion de la fin d'un son ponctuel
	 * @param player Lecteur audio à nettoyer
	 */
	private void OnSoundFinished(Node player)
	{
		if (_activePlayers.Contains(player))
		{
			_activePlayers.Remove(player);
		}
		player.QueueFree();
	}

	/**
	 * Obtient le chemin de fichier pour le son fallback
	 * @param soundName Nom du son
	 * @return Chemin du fichier audio
	 */
	private string GetFallbackPath(string soundName)
	{
		// Normaliser d'abord le nom
		string normalizedName = NormalizeEventName(soundName);
		
		if (_fallbackPaths.TryGetValue(normalizedName, out string path))
		{
			return path;
		}
		
		// Fallback par défaut
		return "res://assets/audio/bruit_chat.wav";
	}

	/**
	 * Normalise les noms d'événements pour les anciennes conventions
	 * @param soundName Nom du son à normaliser
	 * @return Nom normalisé
	 */
	private string NormalizeEventName(string soundName)
	{
		// Conversion des anciens noms vers les nouveaux
		if (soundName.StartsWith("CatMeow"))
		{
			return soundName.Replace("CatMeow", "Cat");
		}
		if (soundName == "GameStartEnd")
		{
			return "GameStart";
		}
		return soundName;
	}
}
