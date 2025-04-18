/*
 * Chat-byrinthe - Controleur Arduino
 * 
 * Version optimisée pour BMI160 qui integre:
 * 1. La lecture du gyroscope/accelerometre BMI160
 * 2. La detection de sauts via une acceleration soudaine
 * 3. Gestion d'un bouton physique pour démarrer/redémarrer le jeu
 * 4. L'envoi de donnees incluant l'etat du gyroscope/accelerometre, l'etat du saut et l'état du bouton
 * 5. Support du recalibrage à distance via commande série
 * 6. Mode de contrôle amélioré pour le maintien du mouvement continu
 */

#include <Wire.h>
#include <DFRobot_BMI160.h>

// Instance du capteur BMI160
DFRobot_BMI160 bmi160;

// Parametres de communication
#define SERIAL_BAUD  9600
#define SEND_INTERVAL 16   // Intervalle d'envoi en ms (réduit à 16ms ~ 60Hz)

// Parametres de detection de saut
#define JUMP_THRESHOLD 1.7    // Seuil de detection en g
#define JUMP_COOLDOWN 800     // Temps minimum entre deux sauts (ms)

// Configuration du bouton
#define BUTTON_PIN 2           // Broche du bouton
#define BUTTON_DEBOUNCE 50     // Délai de debounce en ms

// Variables pour les donnees d'acceleration
int16_t accelData[3]; // X, Y, Z
int16_t gyroData[3];  // X, Y, Z

// Variables pour la calibration
float accel_x_offset = 0;
float accel_y_offset = 0;
float accel_z_offset = 0;

// Variables pour la detection de saut
float accel_magnitude = 0;
float last_accel_magnitude = 0;
float accel_delta = 0;
unsigned long last_jump_time = 0;
bool jump_detected = false;
bool is_calibrating = false;

// Variables pour le bouton
bool button_state = false;
bool last_button_state = false;
bool button_pressed = false;
unsigned long button_time = 0;
bool button_state_changed = false;

// Variables pour controler la frequence d'envoi
unsigned long last_send_time = 0;

// Buffer pour la réception de commandes
String inputBuffer = "";
bool commandAvailable = false;

// Variables pour le contrôle amélioré
float accel_x_amplified = 0;
float accel_y_amplified = 0;
float accel_z_amplified = 0;

// Facteurs d'amplification RECIBLÉS
#define TILT_AMPLIFICATION_FACTOR 5.5f  // Augmenté (était 4.0) pour plus de réactivité
#define TILT_THRESHOLD 0.03f           // Réduit (était 0.04) pour plus de sensibilité
#define TILT_DEADZONE 0.01f            // Réduit (était 0.02) pour détecter des mouvements plus subtils

// Variables pour le mode inclinaison
bool is_tilted = false;
float tilt_direction_x = 0;
float tilt_direction_y = 0;
unsigned long tilt_start_time = 0;

// Temps d'inclinaison maximal avant d'atteindre l'amplification maximale (en millisecondes)
#define MAX_TILT_TIME 1200           // Équilibré (était 1500)

// Facteur de décroissance pour l'amplification et la vitesse
#define AMPLIFICATION_DECAY_RATE 0.97  // Augmenté (était 0.95) pour maintenir plus le mouvement

void setup() {
  // Initialiser la communication serie
  Serial.begin(SERIAL_BAUD);
  
  // Initialiser la broche du bouton avec pull-up
  pinMode(BUTTON_PIN, INPUT_PULLUP);
  
  // Initialiser I2C
  Wire.begin();
  
  // Ajuster la vitesse I2C pour plus de stabilite
  Wire.setClock(100000); // 100kHz au lieu de 400kHz par defaut
  
  delay(100);
  
  // Tenter d'initialiser le BMI160 (d'abord avec 0x69, puis avec 0x68 si necessaire)
  bool success = false;
  
  // Essayer l'adresse 0x69 (adresse par defaut DFRobot)
  if (bmi160.softReset() == BMI160_OK && bmi160.I2cInit(0x69) == BMI160_OK) {
    success = true;
  } 
  // Si 0x69 echoue, essayer 0x68
  else if (bmi160.softReset() == BMI160_OK && bmi160.I2cInit(0x68) == BMI160_OK) {
    success = true;
  }
  
  if (!success) {
    // Si l'initialisation échoue, envoyer un message d'erreur
    Serial.println("ERR:BMI160_INIT_FAILED");
  } else {
    Serial.println("INFO:BMI160_INIT_OK");
  }
  
  // Attendre que tout soit stable
  delay(200);
  
  // Lancer la calibration initiale
  calibrate_bmi();
}

void loop() {
  // Vérifier si des commandes sont disponibles
  check_for_commands();
  
  // Lire l'état du bouton
  read_button();
  
  // Si en cours de calibration, ne pas faire le reste
  if (is_calibrating) return;
  
  // Lire les donnees d'acceleration
  read_sensor_data();
  
  // Calculer les valeurs d'acceleration en g (±2g = 16384 LSB/g)
  float accel_x_g = accelData[0] / 16384.0 - accel_x_offset;
  float accel_y_g = accelData[1] / 16384.0 - accel_y_offset;
  float accel_z_g = accelData[2] / 16384.0 - accel_z_offset;
  
  // Amplification initiale pour plus de sensibilité
  accel_x_g *= 1.2f;
  accel_y_g *= 1.2f;
  
  // Traiter les valeurs d'accélération pour un meilleur contrôle
  process_acceleration_values(accel_x_g, accel_y_g, accel_z_g);
  
  // Détection de sauts améliorée
  detect_jump(accel_x_g, accel_y_g, accel_z_g);
  
  // Envoyer les donnees a intervalle regulier
  unsigned long current_time = millis();
  if (current_time - last_send_time >= SEND_INTERVAL) {
    // Envoyer les valeurs amplifiées plutôt que les valeurs brutes
    send_data(accel_x_amplified, accel_y_amplified, accel_z_amplified, jump_detected, button_pressed);
    
    // Reinitialiser l'etat du saut apres l'envoi
    jump_detected = false;
    
    // Réinitialiser l'état du bouton après l'envoi
    if (button_state_changed) {
      button_state_changed = false;
    }
    
    last_send_time = current_time;
  }
}

// Traiter les valeurs d'accélération pour un meilleur contrôle
void process_acceleration_values(float x, float y, float z) {
  // Appliquer un filtrage des micro-mouvements
  if (abs(x) < TILT_DEADZONE) x = 0;
  if (abs(y) < TILT_DEADZONE) y = 0;
  
  // Détecter si le contrôleur est incliné au-delà du seuil
  bool currently_tilted = (abs(x) > TILT_THRESHOLD) || (abs(y) > TILT_THRESHOLD);
  
  if (currently_tilted) {
    // Si c'est une nouvelle inclinaison, enregistrer l'heure et la direction
    if (!is_tilted) {
      is_tilted = true;
      tilt_start_time = millis();
      
      // Enregistrer la direction de l'inclinaison
      if (abs(x) > TILT_DEADZONE) tilt_direction_x = (x > 0) ? 1 : -1;
      else tilt_direction_x = 0;
      
      if (abs(y) > TILT_DEADZONE) tilt_direction_y = (y > 0) ? 1 : -1;
      else tilt_direction_y = 0;
      
      // Conserver une partie des valeurs précédentes pour un démarrage progressif
      if (accel_x_amplified != 0 || accel_y_amplified != 0) {
        accel_x_amplified = x * 1.5f;
        accel_y_amplified = y * 1.5f;
      }
    }
    
    // Calculer la durée de l'inclinaison
    unsigned long tilt_duration = millis() - tilt_start_time;
    
    // Amplifier les valeurs de manière progressive
    // Formule: progression quadratique pour un démarrage plus rapide
    float progress = min(1.0f, (float)tilt_duration / MAX_TILT_TIME);
    float amplification_factor = 1.0f + (TILT_AMPLIFICATION_FACTOR - 1.0f) * (progress * progress);
    
    // Appliquer l'amplification progressive en fonction de l'angle d'inclinaison
    accel_x_amplified = apply_amplification(x, amplification_factor);
    accel_y_amplified = apply_amplification(y, amplification_factor);
    accel_z_amplified = z;  // La composante Z reste inchangée
  } 
  else {
    // Si le contrôleur n'est plus incliné mais était incliné auparavant
    if (is_tilted) {
      // Diminuer progressivement l'amplification
      unsigned long tilt_duration = millis() - tilt_start_time;
      if (tilt_duration > 300) {  // Réduit (était 500) - Même les inclinaisons brèves conservent du momentum
        // Maintenir une partie de la direction (persistance du mouvement)
        accel_x_amplified *= AMPLIFICATION_DECAY_RATE;
        accel_y_amplified *= AMPLIFICATION_DECAY_RATE;
      } else {
        // Pour les inclinaisons très brèves, réinitialiser progressivement
        accel_x_amplified = x;
        accel_y_amplified = y;
        tilt_direction_x = 0;
        tilt_direction_y = 0;
      }
      
      // Si l'amplification est devenue trop faible, considérer que l'inclinaison est terminée
      if (abs(accel_x_amplified) < TILT_DEADZONE && abs(accel_y_amplified) < TILT_DEADZONE) {
        is_tilted = false;
        accel_x_amplified = 0;
        accel_y_amplified = 0;
      }
    } 
    else {
      // Pas d'inclinaison, mais on garde une petite inertie si en mouvement
      if (abs(accel_x_amplified) > TILT_DEADZONE || abs(accel_y_amplified) > TILT_DEADZONE) {
        // Réduction progressive des valeurs (inertie)
        accel_x_amplified *= AMPLIFICATION_DECAY_RATE;
        accel_y_amplified *= AMPLIFICATION_DECAY_RATE;
        
        // Éteindre si trop faible
        if (abs(accel_x_amplified) < TILT_DEADZONE) accel_x_amplified = 0;
        if (abs(accel_y_amplified) < TILT_DEADZONE) accel_y_amplified = 0;
      } else {
        // Réinitialiser à zéro si sous le seuil
        accel_x_amplified = 0;
        accel_y_amplified = 0;
        accel_z_amplified = z;
      }
    }
  }
}

// Appliquer l'amplification avec une courbe non-linéaire
float apply_amplification(float value, float amplification) {
  // Si la valeur est trop petite, la considérer comme nulle
  if (abs(value) < TILT_DEADZONE) return 0;
  
  // Préserver le signe
  float sign = value > 0 ? 1.0 : -1.0;
  
  // Valeur absolue de l'inclinaison
  float abs_value = abs(value);
  
  // Courbe d'amplification ajustée pour une meilleure réactivité:
  if (abs_value < 0.15) {
    // Pour les petites inclinaisons: amplification quadratique pour une réactivité immédiate
    return sign * abs_value * abs_value * amplification * 25.0; // Augmenté (était 15.0)
  } else if (abs_value < 0.4) {
    // Pour les inclinaisons moyennes: amplification linéaire
    return sign * abs_value * amplification * 1.0; // Augmenté (était 0.8)
  } else {
    // Pour les grandes inclinaisons: saturation progressive pour éviter les mouvements trop brusques
    float saturation = 1.0 - pow(0.6, (abs_value - 0.4) * 5.0); // Courbe de saturation ajustée
    float max_value = 0.9; // Valeur maximale saturée augmentée (était 0.8)
    return sign * ((abs_value * (1.0 - saturation) + max_value * saturation) * amplification * 0.9);
  }
}

// Lire l'état du bouton avec debouncing
void read_button() {
  // Lire l'état actuel du bouton (inverser car INPUT_PULLUP fait que LOW = pressé)
  bool reading = !digitalRead(BUTTON_PIN);
  
  // Si l'état a changé, réinitialiser le compteur de debounce
  if (reading != last_button_state) {
    button_time = millis();
  }
  
  // Si le temps écoulé depuis le dernier changement est supérieur au délai de debounce
  if ((millis() - button_time) > BUTTON_DEBOUNCE) {
    // Si l'état a changé
    if (reading != button_state) {
      button_state = reading;
      button_state_changed = true;
      
      // Si le bouton vient d'être pressé
      if (button_state) {
        button_pressed = true;
        // Envoyer un message spécifique pour le bouton pressé
        Serial.println("BTN:PRESSED");
      } else {
        // Envoyer un message spécifique pour le bouton relâché
        Serial.println("BTN:RELEASED");
      }
    }
  }
  
  // Sauvegarder l'état actuel
  last_button_state = reading;
}

// Vérifier et traiter les commandes reçues
void check_for_commands() {
  while (Serial.available() > 0) {
    char c = Serial.read();
    
    // Si on reçoit un retour à la ligne, on traite la commande
    if (c == '\n' || c == '\r') {
      if (inputBuffer.length() > 0) {
        process_command(inputBuffer);
        inputBuffer = "";
      }
    } else {
      // Sinon, on ajoute le caractère au buffer
      inputBuffer += c;
    }
  }
}

// Traiter la commande reçue
void process_command(String command) {
  command.trim();
  
  if (command == "CALIBRATE") {
    Serial.println("CAL:START");
    calibrate_bmi();
  } 
  else if (command == "PING") {
    Serial.println("PONG");
  }
}

// Lire les donnees du capteur BMI160
void read_sensor_data() {
  // Lire les valeurs de l'accelerometre
  bmi160.getAccelData(accelData);
  
  // Lire les valeurs du gyroscope
  bmi160.getGyroData(gyroData);
}

// Calibrer l'accelerometre
void calibrate_bmi() {
  is_calibrating = true;
  Serial.println("CAL:START");
  
  const int num_samples = 100;
  float sum_x = 0, sum_y = 0, sum_z = 0;
  
  // Collecter des echantillons pour la calibration
  for (int i = 0; i < num_samples; i++) {
    read_sensor_data();
    
    sum_x += accelData[0] / 16384.0;
    sum_y += accelData[1] / 16384.0;
    sum_z += accelData[2] / 16384.0;
    
    delay(10);
  }
  
  // Calculer les offsets moyens
  accel_x_offset = sum_x / num_samples;
  accel_y_offset = sum_y / num_samples;
  
  // Pour Z, on soustrait 1g pour compenser la gravite
  accel_z_offset = sum_z / num_samples - 1.0;
  
  // Réinitialiser les variables d'amplification
  accel_x_amplified = 0;
  accel_y_amplified = 0;
  accel_z_amplified = 0;
  is_tilted = false;
  
  // Envoyer un message de confirmation
  Serial.println("CAL:DONE");
  delay(100);
  is_calibrating = false;
}

// Detecter un mouvement de saut
void detect_jump(float x, float y, float z) {
  // Calculer la magnitude de l'accélération totale
  accel_magnitude = sqrt(x*x + y*y + z*z);
  
  // Calculer le changement de magnitude depuis la dernière lecture
  accel_delta = abs(accel_magnitude - last_accel_magnitude);
  last_accel_magnitude = accel_magnitude;
  
  // Si le delta dépasse le seuil et que le temps de cooldown est passé
  if (accel_delta > JUMP_THRESHOLD && millis() - last_jump_time > JUMP_COOLDOWN) {
    // Marquer qu'un saut a été détecté
    jump_detected = true;
    last_jump_time = millis();
    
    // Envoyer un message spécifique pour le saut détecté
    Serial.println("EVENT:JUMP_DETECTED");
  }
}

// Envoyer les donnees formatees
void send_data(float x, float y, float z, bool jump, bool button) {
  // Format: X,Y,Z,JUMP,BUTTON
  Serial.print(x, 2);
  Serial.print(",");
  Serial.print(y, 2);
  Serial.print(",");
  Serial.print(z, 2);
  Serial.print(",");
  Serial.print(jump ? "1" : "0");
  Serial.print(",");
  Serial.println(button ? "1" : "0");
}