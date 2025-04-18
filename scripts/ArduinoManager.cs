using Godot;
using System;
using System.IO.Ports;
using System.Globalization;
using System.Threading;

/**
 * ArduinoManager - Gestionnaire de communication avec Arduino
 * 
 * Implémente un pattern Singleton pour garantir une connexion unique au port série
 * et éviter les conflits d'accès. Gère la lecture et le traitement des données
 * Arduino (gyroscope, boutons) avec filtrage et calibration.
 */
public partial class ArduinoManager : Node
{
	// Configuration de la communication série
	private const string PORT_NAME = "COM5";
	private const int BAUD_RATE = 9600;
	
	// Implémentation du pattern Singleton
	private static ArduinoManager _instance;
	public static ArduinoManager Instance 
	{ 
		get { return _instance; }
		private set { _instance = value; }
	}
	
	// Objets de communication
	private SerialPort _serialPort;
	private bool _isConnected = false;
	
	// Données du gyroscope et filtrage
	private float _accelX = 0;
	private float _accelY = 0;
	private float _accelZ = 0;
	
	// Paramètres de filtrage du bruit
	private const float NOISE_THRESHOLD = 0.02f;
	private const float FILTER_FACTOR = 0.6f;
	
	// Variables pour le filtrage exponentiel
	private float _filteredAccelX = 0;
	private float _filteredAccelY = 0;
	private float _filteredAccelZ = 0;
	
	// États de détection du saut
	private bool _jumpDetected = false;
	private bool _jumpProcessed = true;
	
	// États de détection des boutons
	public bool _buttonPressed = false;
	public bool _buttonJustPressed = false;
	public bool _buttonJustReleased = false;
	public bool _buttonEventProcessed = true;
	
	// État de calibration
	private bool _isCalibrating = false;
	
	// Références UI
	private Label _statusLabel;
	private Label _jumpLabel;
	private Button _calibrateButton;
	
	// Variable de suivi global de l'état du port
	private static bool _portHasBeenReset = false;

	/**
	 * Initialisation du Node et configuration du Singleton
	 * Vérifie si une instance existe déjà, sinon initialise la connexion Arduino
	 */
	public override void _Ready()
	{
		// Gestion du pattern Singleton
		if (Instance != null && Instance != this)
		{
			// Réutilisation de l'instance existante
			GD.Print("Instance d'ArduinoManager déjà existante - Réutilisation complète");
			
			// Copie des valeurs de l'instance existante
			_accelX = Instance._accelX;
			_accelY = Instance._accelY;
			_accelZ = Instance._accelZ;
			_filteredAccelX = Instance._filteredAccelX;
			_filteredAccelY = Instance._filteredAccelY;
			_filteredAccelZ = Instance._filteredAccelZ;
			_jumpDetected = Instance._jumpDetected;
			_jumpProcessed = Instance._jumpProcessed;
			_buttonPressed = Instance._buttonPressed;
			_buttonJustPressed = Instance._buttonJustPressed;
			_buttonJustReleased = Instance._buttonJustReleased;
			_buttonEventProcessed = Instance._buttonEventProcessed;
			_isCalibrating = Instance._isCalibrating;
			_isConnected = Instance._isConnected;
			_serialPort = Instance._serialPort;
			
			GD.Print("Réutilisation de l'instance existante, connexion déjà établie");
			
			// Suppression de cette instance dupliquée
			QueueFree();
			return;
		}
		else
		{
			// Initialisation de la première instance comme Singleton
			Instance = this;
			GD.Print("Nouvelle instance d'ArduinoManager créée comme Singleton");
			
			// Initialisation de la connexion si nécessaire
			if (_serialPort == null || !_isConnected)
			{
				ConnectToArduino();
			}
		}
		
		// Recherche différée des composants UI
		CallDeferred(nameof(FindUIComponents));
	}
	
	/**
	 * Recherche les composants UI nécessaires dans l'arbre de scène
	 * Appelée de manière différée pour s'assurer que les nœuds sont disponibles
	 */
	private void FindUIComponents()
	{
		_statusLabel = GetNodeOrNull<Label>("%StatusLabel");
		_jumpLabel = GetNodeOrNull<Label>("%JumpLabel");
		_calibrateButton = GetNodeOrNull<Button>("%CalibrateButton");
		
		if (_calibrateButton != null)
			_calibrateButton.Pressed += OnCalibrateButtonPressed;
	}

	/**
	 * Traitement à chaque frame 
	 * Réinitialise les états transitoires et lit les données Arduino
	 */
	public override void _Process(double delta)
	{
		// Ne traiter que si c'est l'instance principale
		if (this != Instance) return;

		// Réinitialisation des états transitoires à chaque frame
		_buttonJustPressed = false;
		_buttonJustReleased = false;
		
		// Lecture des données Arduino si disponibles
		if (_isConnected && _serialPort != null && _serialPort.IsOpen)
		{
			ReadSerialData();
		}
	}
	
	/**
	 * Gestionnaire d'événement pour le bouton de calibration UI
	 */
	private void OnCalibrateButtonPressed()
	{
		if (!_isConnected) return;
		RequestCalibration();
	}
	
	/**
	 * Envoie une commande de calibration à l'Arduino
	 */
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

	/**
	 * Lit les données disponibles sur le port série
	 * Gère les exceptions pour assurer la robustesse de la communication
	 */
	private void ReadSerialData()
	{
		if (_serialPort == null || !_serialPort.IsOpen) return;

		try
		{
			if (_serialPort.BytesToRead > 0)
			{
				string message = _serialPort.ReadLine().Trim();
				
				// Analyse du message reçu
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
			
			// Gestion de la perte de connexion
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

	/**
	 * Analyse les données reçues de l'Arduino
	 * Supporte plusieurs formats: calibration, événements et données gyroscope
	 */
	private void ParseArduinoData(string data)
	{
		// Traitement des messages de calibration
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
				
				if (_calibrateButton != null)
				{
					_calibrateButton.Disabled = false;
				}
			}
			return;
		}
		
		// Traitement des événements de bouton
		if (data.StartsWith("BTN:"))
		{
			if (data == "BTN:PRESSED")
			{
				GD.Print("Bouton Arduino pressé!");
				_buttonPressed = true;
				_buttonJustPressed = true;
				_buttonEventProcessed = false;
			}
			else if (data == "BTN:RELEASED")
			{
				GD.Print("Bouton Arduino relâché");
				_buttonPressed = false;
				_buttonJustReleased = true;
			}
			return;
		}
		
		// Traitement des événements de saut
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

		// Traitement des données d'accélération au format X,Y,Z,JUMP,BUTTON
		string[] values = data.Split(',');
		if (values.Length >= 4)
		{
			// Traitement des données d'accélération X
			if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
			{
				// Application du filtre de bruit à seuil
				if (Math.Abs(x) < NOISE_THRESHOLD)
					x = 0;
				
				// Application du filtre de lissage exponentiel
				_filteredAccelX = (_filteredAccelX * (1 - FILTER_FACTOR)) + (x * FILTER_FACTOR);
				_accelX = _filteredAccelX;
			}
			
			// Traitement des données d'accélération Y
			if (float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
			{
				if (Math.Abs(y) < NOISE_THRESHOLD)
					y = 0;
				
				_filteredAccelY = (_filteredAccelY * (1 - FILTER_FACTOR)) + (y * FILTER_FACTOR);
				_accelY = _filteredAccelY;
			}
			
			// Traitement des données d'accélération Z
			if (float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
			{
				if (Math.Abs(z) < NOISE_THRESHOLD)
					z = 0;
				
				_filteredAccelZ = (_filteredAccelZ * (1 - FILTER_FACTOR)) + (z * FILTER_FACTOR);
				_accelZ = _filteredAccelZ;
			}
			
			// Détection du saut dans les données
			if (values[3] == "1" && !_jumpDetected)
			{
				_jumpDetected = true;
				_jumpProcessed = false;
			}
			
			// Traitement de l'état du bouton s'il est inclus
			if (values.Length >= 5)
			{
				bool newButtonState = values[4] == "1";
				
				// Détection des transitions d'état du bouton
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
	
	/**
	 * Met à jour le message de statut dans l'UI
	 */
	private void UpdateStatus(string message)
	{
		if (_statusLabel != null)
		{
			_statusLabel.Text = message;
		}
	}
	
	/**
	 * Affiche un message temporaire lors de la détection d'un saut
	 */
	private void ShowJumpMessage(string message)
	{
		if (_jumpLabel == null) return;
		
		_jumpLabel.Text = message;
		_jumpLabel.Visible = true;
		
		// Timer pour masquer le message après un délai
		var timer = new Godot.Timer();
		AddChild(timer);
		timer.WaitTime = 1.0;
		timer.OneShot = true;
		timer.Timeout += () => {
			_jumpLabel.Visible = false;
			timer.QueueFree();
		};
		timer.Start();
	}
	
	/**
	 * Établit la connexion avec l'Arduino
	 * Gère les tentatives multiples et la robustesse
	 */
	private void ConnectToArduino()
	{
		// Éviter les connexions redondantes
		if (_isConnected && _serialPort != null && _serialPort.IsOpen) 
		{
			GD.Print("ArduinoManager: Port déjà connecté, pas de reconnexion nécessaire");
			return;
		}
		
		try
		{
			// Fermeture propre d'un port existant
			if (_serialPort != null && _serialPort.IsOpen)
			{
				_serialPort.Close();
				_isConnected = false;
				GD.Print("Port série fermé avant reconnexion");
			}
			
			// Réinitialisation complète du port pour éviter les problèmes résiduels
			_serialPort = null;
			GD.Print("Port série complètement réinitialisé");
			
			// Configuration du nouveau port série
			_serialPort = new SerialPort(PORT_NAME, BAUD_RATE)
			{
				ReadTimeout = 500,
				WriteTimeout = 500,
				Handshake = Handshake.None,
				DtrEnable = true,
				RtsEnable = true,
				NewLine = "\n"
			};
			
			// Tentatives multiples d'ouverture du port
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
						System.Threading.Thread.Sleep(100);
					}
					else
					{
						throw;
					}
				}
			}
			
			_isConnected = true;
			UpdateStatus("Connecté à l'Arduino sur " + PORT_NAME);
			GD.Print("Connecté à l'Arduino sur le port " + PORT_NAME);
			
			// Nettoyage du buffer d'entrée
			if (_serialPort.BytesToRead > 0)
			{
				_serialPort.DiscardInBuffer();
			}
			
			// Réinitialisation des valeurs filtrées
			_filteredAccelX = 0;
			_filteredAccelY = 0;
			_filteredAccelZ = 0;
			
			// Démarrage d'une calibration immédiate
			RequestCalibration();
		}
		catch (Exception e)
		{
			_isConnected = false;
			UpdateStatus("Erreur de connexion: " + e.Message);
			GD.PrintErr("Erreur de connexion à l'Arduino: " + e.Message);
		}
	}

	/**
	 * Accesseur pour la valeur d'accélération X filtrée
	 */
	public float GetAccelX()
	{
		ValidateConnection();
		return _accelX;
	}

	/**
	 * Accesseur pour la valeur d'accélération Y filtrée
	 */
	public float GetAccelY()
	{
		ValidateConnection();
		return _accelY;
	}

	/**
	 * Accesseur pour la valeur d'accélération Z filtrée
	 */
	public float GetAccelZ()
	{
		ValidateConnection();
		return _accelZ;
	}

	/**
	 * Vérifie si un saut vient d'être détecté
	 * Utilise une logique one-shot pour ne pas traiter plusieurs fois le même saut
	 */
	public bool IsJumpDetected()
	{
		if (_jumpDetected && !_jumpProcessed)
		{
			_jumpProcessed = true;
			return true;
		}
		return false;
	}
	
	/**
	 * Vérifie si le bouton vient d'être pressé
	 * Implémente une logique one-shot pour éviter les déclenchements multiples
	 */
	public bool IsButtonJustPressed()
	{
		if (_buttonJustPressed && !_buttonEventProcessed)
		{
			_buttonJustPressed = false;
			_buttonEventProcessed = true;
			
			ShowJumpMessage("BOUTON PRESSÉ!");
			return true;
		}
		
		return false;
	}
	
	/**
	 * Force la réinitialisation de l'état du bouton
	 * Utile en cas de comportement incohérent
	 */
	public void ForceResetButtonState()
	{
		_buttonPressed = false;
		_buttonJustPressed = false;
		_buttonJustReleased = false;
		_buttonEventProcessed = true;
		
		GD.Print("État du bouton Arduino réinitialisé de force");
	}
	
	/**
	 * Vérifie si le bouton est actuellement pressé
	 */
	public bool IsButtonPressed()
	{
		return _buttonPressed;
	}

	/**
	 * Vérifie si la calibration est en cours
	 */
	public bool IsCalibrating()
	{
		return _isCalibrating;
	}

	/**
	 * Vérifie et tente de rétablir la connexion si nécessaire
	 */
	public bool ValidateConnection()
	{
		if (_serialPort == null || !_serialPort.IsOpen)
		{
			GD.PrintErr("Port série non valide - tentative de reconnexion");
			ConnectToArduino();
		}
		
		return _isConnected;
	}
	
	/**
	 * Nettoyage avant destruction du node
	 * S'assure que le port série est fermé proprement
	 */
	public override void _ExitTree()
	{
		// Ne traiter que l'instance principale
		if (this != Instance) return;
		
		// Réinitialisation du singleton si c'est cette instance
		if (Instance == this)
		{
			Instance = null;
		}
		
		// Fermeture propre du port
		ForceClosePort();
	}
	
	/**
	 * Force la fermeture du port série
	 * Méthode robuste qui gère toutes les erreurs potentielles
	 */
	public void ForceClosePort()
	{
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
				_serialPort = null;
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
	
	/**
	 * Réinitialise l'état global du port série
	 * Méthode statique utilisable sans instance
	 */
	public static void ResetPortState()
	{
		if (Instance != null)
		{
			Instance.ForceClosePort();
			GD.Print("État du port réinitialisé via ResetPortState");
		}
		else
		{
			GD.Print("ResetPortState appelée mais aucune instance d'ArduinoManager n'existe");
		}
		
		_portHasBeenReset = true;
	}
	
	/**
	 * Vérifie si le port a été réinitialisé
	 */
	public static bool HasBeenReset()
	{
		return _portHasBeenReset;
	}
	
	/**
	 * Réinitialise complètement l'état du bouton
	 */
	public void ResetButtonState()
	{
		_buttonPressed = false;
		_buttonJustPressed = false;
		_buttonJustReleased = false;
		_buttonEventProcessed = true;
		
		GD.Print("État du bouton Arduino réinitialisé");
	}
	
	/**
	 * Force une reconnexion en cas de problème persistant
	 * Tente d'abord une recalibration, puis une reconnexion complète si nécessaire
	 */
	public void ForceReconnect()
	{
		if (_isConnected && _serialPort != null && _serialPort.IsOpen)
		{
			RequestCalibration();
			GD.Print("Recalibration forcée");
		}
		else
		{
			// Reconnexion complète après un bref délai
			ForceClosePort();
			_serialPort = null;
			
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
