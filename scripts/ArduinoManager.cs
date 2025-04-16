using Godot;
using System;
using System.IO.Ports;
using System.Globalization;

public partial class ArduinoManager : Node
{
	// Configuration du port COM
	private const string PORT_NAME = "COM5";  // Port à utiliser (à ajuster si nécessaire)
	private const int BAUD_RATE = 9600;       // Vitesse de communication
	
	// Communication
	private SerialPort _serialPort;
	private bool _isConnected = false;
	private ulong _reconnectTimer = 0;
	private const ulong RECONNECT_INTERVAL = 5000; // Tenter de reconnecter toutes les 5 secondes
	
	// Données du gyroscope
	private float _accelX = 0;
	private float _accelY = 0;
	private float _accelZ = 0;
	private bool _jumpDetected = false;
	private bool _isCalibrating = false;
	
	// Gestion du bouton - Nouveau
	private bool _buttonPressed = false;
	private bool _buttonPressProcessed = true; // Pour éviter les activations multiples
	private ulong _lastButtonPressTime = 0;
	private const ulong BUTTON_DEBOUNCE_TIME = 500; // Temps de debounce en ms
	
	// Messages UI
	private Label _statusLabel;
	private Label _jumpLabel;
	private Button _calibrateButton;

	public override void _Ready()
	{
		// Trouver les composants UI (à adapter selon votre scène)
		// Utiliser CallDeferred pour rechercher les composants UI
		CallDeferred(nameof(FindUIComponents));
		
		// Tenter de se connecter à l'Arduino
		// Différer la connexion pour s'assurer que le nœud est complètement ajouté à l'arbre
		CallDeferred(nameof(ConnectToArduino));
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
		// Si on n'est pas connecté, essayer de se reconnecter périodiquement
		if (!_isConnected)
		{
			ulong currentTime = Time.GetTicksMsec();
			if (currentTime - _reconnectTimer > RECONNECT_INTERVAL)
			{
				_reconnectTimer = currentTime;
				ConnectToArduino();
			}
			return;
		}

		// Lire les données de l'Arduino si disponibles
		ReadSerialData();
		
		// Traitement du bouton avec debounce
		if (_buttonPressed && !_buttonPressProcessed)
		{
			ulong currentTime = Time.GetTicksMsec();
			if (currentTime - _lastButtonPressTime > BUTTON_DEBOUNCE_TIME)
			{
				_jumpDetected = true; // Utiliser le même signal que pour le saut
				_buttonPressProcessed = true;
				_lastButtonPressTime = currentTime;
				ShowJumpMessage("BOUTON PRESSÉ!");
			}
		}
		
		// Réinitialiser l'état du bouton après un certain temps
		if (_buttonPressed && _buttonPressProcessed)
		{
			ulong currentTime = Time.GetTicksMsec();
			if (currentTime - _lastButtonPressTime > BUTTON_DEBOUNCE_TIME * 2)
			{
				_buttonPressed = false;
			}
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

	private void ConnectToArduino()
	{
		// Si un port est déjà ouvert, le fermer d'abord
		if (_serialPort != null && _serialPort.IsOpen)
		{
			_serialPort.Close();
			_isConnected = false;
		}

		try
		{
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
			
			_serialPort.Open();
			_isConnected = true;
			UpdateStatus("Connecté à l'Arduino sur " + PORT_NAME);
			GD.Print("Connecté à l'Arduino sur le port " + PORT_NAME);
			
			// Ignorer les données initiales
			if (_serialPort.BytesToRead > 0)
			{
				_serialPort.DiscardInBuffer();
			}
		}
		catch (Exception e)
		{
			_isConnected = false;
			UpdateStatus("Erreur de connexion: " + e.Message);
			GD.PrintErr("Erreur de connexion à l'Arduino: " + e.Message);
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
				GD.Print("Données reçues: " + message);
				
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
				_serialPort.Close();
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
		
		// Traiter les événements de bouton - Nouveau
		if (data.StartsWith("BTN:"))
		{
			if (data == "BTN:PRESSED")
			{
				GD.Print("Bouton Arduino pressé!");
				_buttonPressed = true;
				_buttonPressProcessed = false;
			}
			else if (data == "BTN:RELEASED")
			{
				GD.Print("Bouton Arduino relâché");
				_buttonPressed = false;
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
			}
			return;
		}

		// Format standard X,Y,Z,JUMP
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
			if (values[3] == "1")
			{
				_jumpDetected = true;
			}
			
			// Vérifier si un bouton est pressé (dans le cas où il est inclus dans la trame) - Nouveau
			if (values.Length >= 5 && values[4] == "1")
			{
				_buttonPressed = true;
				_buttonPressProcessed = false;
			}
			else if (values.Length >= 5)
			{
				_buttonPressed = false;
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
		var timer = new Timer();
		AddChild(timer);
		timer.WaitTime = 1.0; // 1 seconde
		timer.OneShot = true;
		timer.Timeout += () => {
			_jumpLabel.Visible = false;
			timer.QueueFree();
		};
		timer.Start();
		
		// Réinitialiser le flag après avoir traité l'événement
		_jumpDetected = false;
	}

	// Récupérer la valeur X de l'accéléromètre (pour le mouvement latéral)
	public float GetAccelX()
	{
		return _accelX;
	}

	// Récupérer la valeur Y de l'accéléromètre (pour le mouvement avant/arrière)
	public float GetAccelY()
	{
		return _accelY;
	}

	// Récupérer la valeur Z de l'accéléromètre
	public float GetAccelZ()
	{
		return _accelZ;
	}

	// Vérifier si un saut est détecté
	public bool IsJumpDetected()
	{
		// Retourne et réinitialise la détection
		bool result = _jumpDetected;
		_jumpDetected = false;
		return result;
	}
	
	// Vérifier si le bouton est pressé - Nouveau
	public bool IsButtonPressed()
	{
		return _buttonPressed && !_buttonPressProcessed;
	}

	// Vérifier si la calibration est en cours
	public bool IsCalibrating()
	{
		return _isCalibrating;
	}

	public override void _ExitTree()
	{
		// S'assurer que le port est fermé lorsqu'on quitte
		if (_serialPort != null && _serialPort.IsOpen)
		{
			try
			{
				_serialPort.Close();
				GD.Print("Port série fermé");
			}
			catch (Exception e)
			{
				GD.PrintErr("Erreur lors de la fermeture du port série: " + e.Message);
			}
		}
	}
}
