using Godot;
using System;

public static class FMODHelper
{
	// Dictionnaire public des événements sonores
	public static readonly System.Collections.Generic.Dictionary<string, string> SoundEvents = 
		new System.Collections.Generic.Dictionary<string, string>
	{
		{ "CatMeow", "event:/Chat/Meow" },
		{ "Bonus", "event:/UI/Bonus" },
		{ "Malus", "event:/UI/Malus" },
		{ "Teleport", "event:/World/Teleport" },
		{ "GameOver", "event:/UI/GameOver" }
	};
	
	// Cache du singleton FMOD pour éviter de le récupérer à chaque fois
	private static Node _fmodStudio = null;
	
	// Obtenir l'instance du singleton FMOD
	public static Node GetFMODStudio()
	{
		if (_fmodStudio == null)
		{
			_fmodStudio = Engine.GetSingleton("FmodStudio") as Node;
		}
		return _fmodStudio;
	}
	
	// Jouer un son à partir de son nom (2D global)
	public static void PlaySound(string soundName)
	{
		PlaySound(soundName, null);
	}
	
	// Jouer un son à partir de son nom (3D attaché à un objet)
	public static void PlaySound(string soundName, Node3D target)
	{
		var fmod = GetFMODStudio();
		
		if (fmod != null && SoundEvents.TryGetValue(soundName, out string eventPath))
		{
			try
			{
				if (target != null)
				{
					// Son 3D
					fmod.Call("play_one_shot_attached", eventPath, target);
					GD.Print($"Son FMOD 3D joué: {eventPath} sur {target.Name}");
				}
				else
				{
					// Son 2D
					fmod.Call("play_one_shot", eventPath);
					GD.Print($"Son FMOD 2D joué: {eventPath}");
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Erreur FMOD: {ex.Message}");
				PlaySoundFallback(soundName, target);
			}
		}
		else
		{
			PlaySoundFallback(soundName, target);
		}
	}
	
	// Méthode publique pour obtenir le chemin d'un événement
	public static string GetEventPath(string soundName)
	{
		if (SoundEvents.TryGetValue(soundName, out string path))
		{
			return path;
		}
		return null;
	}
	
	// Méthode de secours pour jouer des sons avec le système audio de Godot
	private static void PlaySoundFallback(string soundName, Node3D target)
	{
		string soundPath = GetFallbackSoundPath(soundName);
		if (soundPath == null) return;
		
		var audioStream = ResourceLoader.Load<AudioStream>(soundPath);
		if (audioStream == null) return;
		
		if (target != null)
		{
			// Son 3D
			var audioPlayer3D = new AudioStreamPlayer3D();
			target.AddChild(audioPlayer3D);
			audioPlayer3D.Stream = audioStream;
			audioPlayer3D.VolumeDb = 0.0f;
			audioPlayer3D.MaxDistance = 10.0f;
			audioPlayer3D.Play();
			
			// Nettoyer après la lecture
			audioPlayer3D.Finished += () => audioPlayer3D.QueueFree();
		}
		else
		{
			// Son 2D
			var audioPlayer = new AudioStreamPlayer();
			var sceneTree = (SceneTree)Engine.GetMainLoop();
			if (sceneTree != null)
			{
				sceneTree.Root.AddChild(audioPlayer);
				audioPlayer.Stream = audioStream;
				audioPlayer.Play();
				
				// Nettoyer après la lecture
				audioPlayer.Finished += () => audioPlayer.QueueFree();
			}
		}
		
		GD.Print($"Son joué via système de secours: {soundPath}");
	}
	
	// Obtenir le chemin du son de secours
	private static string GetFallbackSoundPath(string soundName)
	{
		switch (soundName)
		{
			case "CatMeow":
				return "res://assets/audio/bruit_chat.wav";
			case "Bonus":
				return "res://assets/audio/bruit_bonus.wav";
			case "Malus":
				return "res://assets/audio/bruit_malus.wav";
			case "Teleport":
				return "res://assets/audio/bruit_teleporteur.wav";
			case "GameOver":
				return "res://assets/audio/bruit_malus.wav";
			default:
				GD.PrintErr($"Son non reconnu: {soundName}");
				return null;
		}
	}
	
	// Définir un paramètre global FMOD
	public static void SetGlobalParameter(string name, float value)
	{
		var fmod = GetFMODStudio();
		if (fmod != null)
		{
			try
			{
				fmod.Call("set_global_parameter", name, value);
				GD.Print($"Paramètre FMOD global défini: {name} = {value}");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Erreur lors de la définition du paramètre FMOD: {ex.Message}");
			}
		}
	}
}
