using Godot;
using System;
using System.IO.Ports;
using System.Globalization;
using System.Threading;

/// <summary>
/// Gestionnaire Arduino implémentant un pattern Singleton pour éviter les problèmes d'accès multiples au port
/// </summary>
public partial class ArduinoManager : Node
{
	// Configuration du port COM
	private const string PORT_NAME = "COM5";  // Port à utiliser (à ajuster si nécessaire)
	private const int BAUD_RATE = 9600;       // Vitesse de communication
	
	// SINGLETON PATTERN - Instance unique
	private static ArduinoManager _instance;
	public static ArduinoManager Instance 
	{ 
		get { return _instance; }
		private set { _instance = value; }
	}
	
	// Communication
	private SerialPort _serialPort;
	private bool _isConnected = false;
	
	// Données du gyroscope
	private float _accelX = 0;
	private float _accelY = 0;
	private float _accelZ = 0;
	
	// Gestion du saut
	private bool _jumpDetected = false;
	private bool _jumpProcessed = true;  // Flag pour indiquer si le saut a été traité
	
	// Gestion du bouton - Refactorisation complète
	public bool _buttonPressed = false;     // État physique du bouton (pressé ou non)
	public bool _buttonJustPressed = false; // Bouton vient juste d'être pressé
	public bool _buttonJustReleased = false; // Bouton vient juste d'être relâché
	public bool _buttonEventProcessed = true; // L'événement a-t-il été traité
	
	// Calibration
	private bool _isCalibrating = false;
	
	// Messages UI
	private Label _statusLabel;
	private Label _jumpLabel;
	private Button _calibrateButton;
	
	// Variable statique pour suivre l'état global du port
	private static bool _portHasBeenReset = false;

	public override void _Ready()
	{
		// Implémenter le pattern Singleton
		if (Instance != null && Instance != this)
		{
			// Si une instance existe déjà, simplement la réutiliser
			GD.Print("Instance d'ArduinoManager déjà existante - Réutilisation complète");
			
			// Utiliser les valeurs de l'instance existante
			_accelX = Instance._accelX;
			_accelY = Instance._accelY;
			_accelZ = Instance._accelZ;
			_jumpDetected = Instance._jumpDetected;
			_jumpProcessed = Instance._jumpProcessed;
			_buttonPressed = Instance._buttonPressed;
			_buttonJustPressed = Instance._buttonJustPressed;
			_buttonJustReleased = Instance._buttonJustReleased;
			_buttonEventProcessed = Instance._buttonEventProcessed;
			_isCalibrating = Instance._isCalibrating;
			_isConnected = Instance._isConnected;
			_serialPort = Instance._serialPort;
			
			// Ne pas recréer une nouvelle connexion
			GD.Print("Réutilisation de l'instance existante, connexion déjà établie");
			
			// Supprimer cette instance pour utiliser uniquement la première
			QueueFree();
			return;
		}
		else
		{
			// Première instance - Initialisation
			Instance = this;
			GD.Print("Nouvelle instance d'ArduinoManager créée comme Singleton");
			
			// Initialiser la connexion seulement s'il n'y a pas de connexion existante
			if (_serialPort == null || !_isConnected)
			{
				ConnectToArduino();
			}
		}
		
		// Trouver les composants UI (à adapter selon votre scène)
		// Utiliser CallDeferred pour rechercher les composants UI
		CallDeferred(nameof(FindUIComponents));
	}
	
	private void FindUIComponents()
	{
		// Recherche des composants UI après que le nœud a été ajouté à la scène
		_statusLabel = GetNodeOrNull<Label>("%StatusLabel");
		_jumpLabel = GetNodeOrNull<Label>("%JumpLabel");
		_calibrateButton = GetNodeOrNull<Button>("%CalibrateButton");
		
		// Connecter le bouton de calibration
		if (_calibrateButton != null)
			_calibrateButton.Pressed += OnCalibrateButtonPressed;
	}

	public override void _Process(double delta)
	{
		// Seule l'instance principale gère la communication
		if (this != Instance) return;

		// Réinitialiser les états transitoires à chaque frame
		_buttonJustPressed = false;
		_buttonJustReleased = false;
		
		// Lire les données de l'Arduino si disponibles
		if (_isConnected && _serialPort != null && _serialPort.IsOpen)
		{
			ReadSerialData();
		}
	}
	
	// Gestionnaire d'événement pour le bouton de calibration
	private void OnCalibrateButtonPressed()
	{
		if (!_isConnected) return;
		RequestCalibration();
	}
	
	// Demander une calibration à l'Arduino
	private void RequestCalibration()
	{
		if (!_isConnected || _serialPort == null || !_serialPort.IsOpen) return;
		
		try
		{
			// Désactiver le bouton pendant la calibration
			if (_calibrateButton != null)
				_calibrateButton.Disabled = true;
			
			// Envoyer la commande de calibration
			_serialPort.WriteLine("CALIBRATE");
			_isCalibrating = true;
			UpdateStatus("Calibration en cours...");
			
			GD.Print("Demande de calibration envoyée");
		}
		catch (Exception e)
		{
			GD.PrintErr("Erreur lors de la demande de calibration: " + e.Message);
			_isCalibrating = false;
			
			// Réactiver le bouton
			if (_calibrateButton != null)
				_calibrateButton.Disabled = false;
		}
	}

	private void ReadSerialData()
	{
		if (_serialPort == null || !_serialPort.IsOpen) return;

		try
		{
			// Vérifier s'il y a des données disponibles
			if (_serialPort.BytesToRead > 0)
			{
				string message = _serialPort.ReadLine().Trim();
				// GD.Print("Données reçues: " + message); // Commenté pour réduire les logs
				
				// Analyser le message
				ParseArduinoData(message);
			}
		}
		catch (TimeoutException)
		{
			// Ignorer les timeouts de lecture
		}
		catch (Exception e)
		{
			GD.PrintErr("Erreur de lecture du port série: " + e.Message);
			
			// Si une erreur se produit, on considère que la connexion est perdue
			_isConnected = false;
			UpdateStatus("Connexion perdue");
			
			if (_serialPort != null && _serialPort.IsOpen)
			{
				try
				{
					_serialPort.Close();
				}
				catch
				{
					// Ignorer les erreurs lors de la fermeture
				}
			}
		}
	}

	private void ParseArduinoData(string data)
	{
		// Traiter les messages spéciaux de calibration
		if (data.StartsWith("CAL:"))
		{
			if (data == "CAL:START")
			{
				GD.Print("Calibration de l'Arduino démarrée");
				UpdateStatus("Calibration en cours...");
			}
			else if (data == "CAL:DONE")
			{
				GD.Print("Arduino calibré avec succès");
				_isCalibrating = false;
				UpdateStatus("Calibration terminée");
				
				// Réactiver le bouton de calibration
				if (_calibrateButton != null)
				{
					_calibrateButton.Disabled = false;
				}
			}
			return;
		}
		
		// Traiter les événements de bouton - AMÉLIORÉ
		if (data.StartsWith("BTN:"))
		{
			if (data == "BTN:PRESSED")
			{
				GD.Print("Bouton Arduino pressé!");
				_buttonPressed = true;
				_buttonJustPressed = true;
				_buttonEventProcessed = false; // Très important de le mettre à false
			}
			else if (data == "BTN:RELEASED")
			{
				GD.Print("Bouton Arduino relâché");
				_buttonPressed = false;
				_buttonJustReleased = true;
				// Ne pas réinitialiser _buttonEventProcessed ici pour permettre
				// l'événement de bouton complet (appuyer + relâcher)
			}
			return;
		}
		
		// Traiter les événements de saut
		if (data.StartsWith("EVENT:"))
		{
			if (data == "EVENT:JUMP_DETECTED")
			{
				GD.Print("Saut détecté!");
				_jumpDetected = true;
				_jumpProcessed = false;
			}
			return;
		}

		// Format standard X,Y,Z,JUMP,BUTTON
		string[] values = data.Split(',');
		if (values.Length >= 4)
		{
			// Utiliser InvariantCulture pour assurer la compatibilité avec les décimales
			if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
				_accelX = x;
			
			if (float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
				_accelY = y;
			
			if (float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
				_accelZ = z;
			
			// Vérifier si un saut est détecté (la valeur doit être 1)
			if (values[3] == "1" && !_jumpDetected)
			{
				_jumpDetected = true;
				_jumpProcessed = false;
			}
			
			// Vérifier si un bouton est pressé (dans le cas où il est inclus dans la trame)
			if (values.Length >= 5)
			{
				bool newButtonState = values[4] == "1";
				
				// Détecter les transitions
				if (newButtonState && !_buttonPressed)
				{
					_buttonPressed = true;
					_buttonJustPressed = true;
					_buttonEventProcessed = false;
				}
				else if (!newButtonState && _buttonPressed)
				{
					_buttonPressed = false;
					_buttonJustReleased = true;
				}
			}
		}
	}
	
	// Mettre à jour le message de statut
	private void UpdateStatus(string message)
	{
		if (_statusLabel != null)
		{
			_statusLabel.Text = message;
		}
	}
	
	// Afficher un message de saut temporaire
	private void ShowJumpMessage(string message)
	{
		if (_jumpLabel == null) return;
		
		_jumpLabel.Text = message;
		_jumpLabel.Visible = true;
		
		// Créer un timer pour masquer le message après un délai
		var timer = new Godot.Timer();
		AddChild(timer);
		timer.WaitTime = 1.0; // 1 seconde
		timer.OneShot = true;
		timer.Timeout += () => {
			_jumpLabel.Visible = false;
			timer.QueueFree();
		};
		timer.Start();
	}
	
	private void ConnectToArduino()
	{
		// Ne pas tenter de se connecter si déjà connecté
		if (_isConnected && _serialPort != null && _serialPort.IsOpen) 
		{
			GD.Print("ArduinoManager: Port déjà connecté, pas de reconnexion nécessaire");
			return;
		}
		
		try
		{
			// Si un port est déjà ouvert, le fermer d'abord
			if (_serialPort != null && _serialPort.IsOpen)
			{
				_serialPort.Close();
				_isConnected = false;
				GD.Print("Port série fermé avant reconnexion");
			}
			
			// NOUVEAU: Destruction et recréation complète du port série pour éviter les problèmes résiduels
			_serialPort = null;
			GD.Print("Port série complètement réinitialisé");
			
			// Configurer et ouvrir le port série
			_serialPort = new SerialPort(PORT_NAME, BAUD_RATE)
			{
				ReadTimeout = 500,          // Timeout de lecture
				WriteTimeout = 500,         // Timeout d'écriture
				Handshake = Handshake.None, // Pas de contrôle de flux
				DtrEnable = true,           // Data Terminal Ready pour éviter le reset d'Arduino
				RtsEnable = true,           // Request To Send
				NewLine = "\n"              // S'assurer que le retour à la ligne est correct
			};
			
			// Essayer d'ouvrir avec plusieurs tentatives
			int attempts = 0;
			const int MAX_ATTEMPTS = 3;
			bool success = false;
			
			while (!success && attempts < MAX_ATTEMPTS)
			{
				try
				{
					_serialPort.Open();
					success = true;
					GD.Print($"Port COM ouvert avec succès après {attempts+1} tentatives");
				}
				catch (Exception e)
				{
					attempts++;
					GD.PrintErr($"Tentative {attempts}/{MAX_ATTEMPTS} échouée: {e.Message}");
					
					if (attempts < MAX_ATTEMPTS)
					{
						// Attendre un peu avant de réessayer
						System.Threading.Thread.Sleep(100);
					}
					else
					{
						throw; // Relancer l'exception si toutes les tentatives échouent
					}
				}
			}
			
			_isConnected = true;
			UpdateStatus("Connecté à l'Arduino sur " + PORT_NAME);
			GD.Print("Connecté à l'Arduino sur le port " + PORT_NAME);
			
			// Ignorer les données initiales
			if (_serialPort.BytesToRead > 0)
			{
				_serialPort.DiscardInBuffer();
			}
			
			// NOUVEAU: Demander une calibration immédiate
			RequestCalibration();
		}
		catch (Exception e)
		{
			_isConnected = false;
			UpdateStatus("Erreur de connexion: " + e.Message);
			GD.PrintErr("Erreur de connexion à l'Arduino: " + e.Message);
		}
	}


	// Modifier les méthodes de lecture pour ne jamais renvoyer de valeurs trop petites
	public float GetAccelX()
	{
		ValidateConnection();
		// Ignorer les valeurs trop petites qui pourraient être du bruit
		return Math.Abs(_accelX) < 0.03f ? 0 : _accelX;
	}

	public float GetAccelY()
	{
		ValidateConnection();
		// Ignorer les valeurs trop petites qui pourraient être du bruit
		return Math.Abs(_accelY) < 0.03f ? 0 : _accelY;
	}

	public float GetAccelZ()
	{
		ValidateConnection();
		// Ignorer les valeurs trop petites qui pourraient être du bruit
		return Math.Abs(_accelZ) < 0.03f ? 0 : _accelZ;
	}


	// NOUVELLE MÉTHODE: Vérifier si un saut vient d'être détecté (une seule fois)
	public bool IsJumpDetected()
	{
		if (_jumpDetected && !_jumpProcessed)
		{
			_jumpProcessed = true; // Marquer comme traité
			return true;
		}
		return false;
	}
	
	public bool IsButtonJustPressed()
	{
		// Si le bouton vient d'être pressé, le traiter une seule fois
		if (_buttonJustPressed)
		{
			_buttonJustPressed = false; // Réinitialiser immédiatement
			_buttonEventProcessed = true; // Marquer comme traité
			
			// Réinitialisation complète des états du bouton avec un timer
			var timer = new Godot.Timer();
			timer.WaitTime = 0.05f;
			timer.OneShot = true;
			AddChild(timer);
			timer.Timeout += () => {
				_buttonJustPressed = false;
				_buttonEventProcessed = true;
				timer.QueueFree();
			};
			timer.Start();
			
			ShowJumpMessage("BOUTON PRESSÉ!");
			return true;
		}
		
		// Important: Ne PAS détecter l'état appuyé continu
		// Supprimer ou commenter ce bloc pour éviter les détections constantes
		/*
		if (_buttonPressed && !_buttonEventProcessed)
		{
			_buttonEventProcessed = true;
			ShowJumpMessage("BOUTON DÉTECTÉ!");
			return true;
		}
		*/
		
		return false;
	}
	
	public void ForceResetButtonState()
	{
		_buttonPressed = false;
		_buttonJustPressed = false;
		_buttonJustReleased = false;
		_buttonEventProcessed = true;
		
		GD.Print("État du bouton Arduino réinitialisé de force");
	}


	
	// NOUVELLE MÉTHODE: Vérifier si le bouton est actuellement pressé
	public bool IsButtonPressed()
	{
		return _buttonPressed;
	}

	// Vérifier si la calibration est en cours
	public bool IsCalibrating()
	{
		return _isCalibrating;
	}

	public bool ValidateConnection()
	{
		if (_serialPort == null || !_serialPort.IsOpen)
		{
			GD.PrintErr("Port série non valide - tentative de reconnexion");
			ConnectToArduino();
		}
		
		return _isConnected;
	}
	
	// Override de _ExitTree pour garantir la fermeture du port
	public override void _ExitTree()
	{
		// Ne fermer le port que si c'est l'instance principale
		if (this != Instance) return;
		
		// Reset l'instance singleton si c'est nous
		if (Instance == this)
		{
			Instance = null;
		}
		
		// S'assurer que le port est fermé
		ForceClosePort();
	}
	
	// Méthode ForceClosePort améliorée
	public void ForceClosePort()
	{
		// Fermer le port série s'il est ouvert
		if (_serialPort != null)
		{
			try
			{
				if (_serialPort.IsOpen)
				{
					_serialPort.Close();
					_isConnected = false;
					GD.Print("Port série fermé via ForceClosePort");
				}
				_serialPort = null; // IMPORTANT: Détruire complètement la référence
			}
			catch (Exception e)
			{
				GD.PrintErr($"Erreur lors de la fermeture forcée du port: {e.Message}");
			}
		}
		else
		{
			GD.Print("ForceClosePort - Aucun port série à fermer");
		}
	}

	
	/// <summary>
	/// Réinitialise l'état global du port série pour éviter les problèmes lors des redémarrages
	/// </summary>
	public static void ResetPortState()
	{
		// Fermer le port série si l'instance existe
		if (Instance != null)
		{
			Instance.ForceClosePort();
			GD.Print("État du port réinitialisé via ResetPortState");
		}
		else
		{
			GD.Print("ResetPortState appelée mais aucune instance d'ArduinoManager n'existe");
		}
		
		// Marquer l'état comme réinitialisé
		_portHasBeenReset = true;
	}
	
	/// <summary>
	/// Vérifie si le port a été réinitialisé
	/// </summary>
	public static bool HasBeenReset()
	{
		return _portHasBeenReset;
	}
	
	/// <summary>
	/// Réinitialise complètement l'état du bouton
	/// </summary>
	public void ResetButtonState()
	{
		_buttonPressed = false;
		_buttonJustPressed = false;
		_buttonJustReleased = false;
		_buttonEventProcessed = true;
		
		GD.Print("État du bouton Arduino réinitialisé");
	}
	
	/// <summary>
	/// Force une reconnexion en cas de problème persistant
	/// </summary>
	public void ForceReconnect()
	{
		// Forcer une nouvelle calibration si connecté
		if (_isConnected && _serialPort != null && _serialPort.IsOpen)
		{
			RequestCalibration();
			GD.Print("Recalibration forcée");
		}
		else
		{
			// Tenter une nouvelle connexion propre
			ForceClosePort();
			_serialPort = null;
			
			// Attendre un court instant
			var timer = new Godot.Timer();
			AddChild(timer);
			timer.WaitTime = 0.5f;
			timer.OneShot = true;
			timer.Timeout += () => {
				ConnectToArduino();
				timer.QueueFree();
			};
			timer.Start();
			
			GD.Print("Reconnexion forcée après délai");
		}
	}
}
