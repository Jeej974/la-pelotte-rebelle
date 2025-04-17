using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Gestionnaire audio simple qui marche avec ou sans FMOD
/// </summary>
public partial class AudioManager : Node
{
	// Singleton
	private static AudioManager _instance;
	public static AudioManager Instance => _instance;

	// Stockage des chemins de sons de fallback
	private Dictionary<string, string> _fallbackPaths = new Dictionary<string, string>
	{
		{ "CatOrange", "res://assets/audio/cat_orange.wav" },
		{ "CatBlack", "res://assets/audio/chat_noir.wav" },
		{ "CatTabby", "res://assets/audio/bruit_chat.wav" },
		{ "CatWhite", "res://assets/audio/chat_blanc.wav" },
		{ "CatSiamese", "res://assets/audio/bruit_chat_siamois.wav" },
		{ "Bonus", "res://assets/audio/bruit_bonus.mp3" },
		{ "Malus", "res://assets/audio/bruit_malus.wav" },
		{ "Teleport", "res://assets/audio/bruit_teleporteur.wav" },
		{ "GameStart", "res://assets/audio/ambient.wav" },
		{ "GameOver", "res://assets/audio/bruit_malus.wav" },
		{ "Ambiance", "res://assets/audio/son_ambient.wav" },
		{ "RollingBall", "res://assets/audio/bruit_pellote.wav" },
		{ "PauseMusic", "res://assets/audio/ambient.wav" }
	};

	// Liste des lecteurs audio actifs
	private List<Node> _activePlayers = new List<Node>();

	// Utilisation de FMOD si disponible
	private bool _useFmod = false;
	private GodotObject _fmodStudio = null;

	public override void _Ready()
	{
		// Configuration du singleton
		if (_instance != null && _instance != this)
		{
			QueueFree();
			return;
		}
		_instance = this;

		// Tenter d'utiliser FMOD si disponible
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

	public override void _Process(double delta)
	{
		// Nettoyer les lecteurs inactifs
		for (int i = _activePlayers.Count - 1; i >= 0; i--)
		{
			if (!IsInstanceValid(_activePlayers[i]) || !_activePlayers[i].IsInsideTree())
			{
				_activePlayers.RemoveAt(i);
			}
		}
	}

	/// <summary>
	/// Joue un son 2D
	/// </summary>
	public void PlaySound(string soundName)
	{
		// Essayer FMOD d'abord
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

		// Fallback au système audio Godot
		PlaySoundFallback(soundName);
	}

	/// <summary>
	/// Joue un son 3D attaché à un objet
	/// </summary>
	public void PlaySound3D(string soundName, Node3D target)
	{
		if (target == null)
		{
			GD.PrintErr("AudioManager: Cible nulle pour son 3D");
			return;
		}

		// Essayer FMOD d'abord
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

	/// <summary>
	/// Arrête tous les sons
	/// </summary>
	public void StopAllSounds()
	{
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
		_activePlayers.Clear();
	}

	/// <summary>
	/// Définit un paramètre global FMOD
	/// </summary>
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

	// Méthodes privées

	private string GetFmodEventPath(string soundName)
	{
		// Mapper les noms de sons vers les chemins d'événements FMOD
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
			
			// Configuration basique
			if (soundName == "Ambiance" || soundName == "PauseMusic")
			{
				player.VolumeDb = -10;
			}
			
			player.Play();
			player.Finished += () => OnSoundFinished(player);
			
			GD.Print($"AudioManager: Son '{soundName}' joué via fallback");
		}
	}

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
			
			// Configuration basique
			player.MaxDistance = 20.0f;
			
			// Personnalisation selon le type de chat
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

	private void OnSoundFinished(Node player)
	{
		if (_activePlayers.Contains(player))
		{
			_activePlayers.Remove(player);
		}
		player.QueueFree();
	}

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
