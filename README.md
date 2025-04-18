# 🧶 Chat-byrinthe : La Pelote Rebelle 🐱

![Version](https://img.shields.io/badge/version-1.0-blue.svg)
![Godot Version](https://img.shields.io/badge/Godot-4.x-brightgreen.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

## 📜 Description

**Chat-byrinthe : La Pelote Rebelle** est un jeu unique où vous incarnez une pelote de laine qui doit traverser une série de labyrinthes générés procéduralement. Contrôlé par un gyroscope Arduino, naviguez à travers des labyrinthes de plus en plus complexes, collectez des chats aux effets variés et luttez contre le temps qui s'écoule !

## ✨ Fonctionnalités implémentées

- 🧩 **Labyrinthes verticaux générés procéduralement** qui deviennent plus complexes à chaque niveau
- 🎮 **Contrôle physique par gyroscope** via un capteur BMI160 sur Arduino
- ⏱️ **Système de temps** avec bonus et malus selon les chats collectés
- 🐈 **5 types de chats** aux effets uniques :
  - 🟠 **Chat Roux** : +5 secondes
  - ⚫ **Chat Noir** : -10 secondes
  - 🐅 **Chat Tigré** : +7/-7 secondes (aléatoire)
  - ⚪ **Chat Blanc** : +15 secondes
  - 🔵 **Chat Siamois** : Révèle le chemin et +20 secondes
- 🚪 **Système de téléportation** entre les labyrinthes
- 🔊 **Audio avancé** intégré avec FMOD
- 🏆 **Système de score et classement**

## 🛠️ Architecture technique

### Composants clés

- 🎮 **Godot Engine** pour le développement du jeu
- 🤖 **Arduino avec BMI160** pour les contrôles par gyroscope
- 🔊 **FMOD** pour la gestion audio avancée

### Structure du projet

```
res://
├── addons/
│   └── fmod/                # Intégration FMOD pour l'audio
├── arduino_comunication_capteur/  # Code pour l'interface Arduino
├── assets/                  # Ressources graphiques et modèles 3D
├── fonts/                   # Polices d'écriture
├── scenes/
│   ├── cat.tscn             # Scène des chats collectables
│   ├── MainScene.tscn       # Scène principale du jeu
│   ├── PlayerBall.tscn      # Pelote de laine jouable
│   └── teleporter.tscn      # Système de téléportation
└── scripts/
    ├── ArduinoManager.cs    # Gestion de la communication Arduino
    ├── AudioManager.cs      # Gestion audio avec FMOD
    ├── Cat.cs               # Comportement des chats
    ├── GameManager.cs       # Gestion globale du jeu
    ├── MainScene.cs         # Contrôleur de la scène principale
    ├── PlayerBall.cs        # Contrôle de la pelote de laine
    ├── ScoreManager.cs      # Gestion des scores
    ├── Teleporter.cs        # Logique de téléportation
    └── VerticalMazeGenerator.cs  # Génération des labyrinthes
```

## 🔌 Configuration matérielle

### Composants nécessaires

- 💻 PC sous Windows, macOS ou Linux avec Godot Engine
- 🎛️ Arduino UNO ou compatible
- 📡 Capteur BMI160 (gyroscope/accéléromètre)
- 🔘 Bouton poussoir
- 🔌 Câble USB

### Branchement du BMI160

```
BMI160  ->  Arduino
VCC     ->  5V
GND     ->  GND
SCL     ->  A5
SDA     ->  A4
INT     ->  D2
```

## 📋 Installation

1. **Cloner/télécharger le projet**
2. **Ouvrir dans Godot Engine**
3. **Configurer Arduino**:
   - Connecter l'Arduino avec le BMI160 selon le schéma
   - Installer la bibliothèque DFRobot_BMI160
   - Téléverser le code Arduino fourni
4. **Configurer le port série**:
   - Vérifier que le bon port COM est configuré dans ArduinoManager.cs (COM5 par défaut)
5. **Lancer le jeu**

## 🎮 Contrôles

- **Incliner le gyroscope** pour faire rouler la pelote de laine
- **Secouer rapidement** le gyroscope pour effectuer un saut
- **Appuyer sur le bouton** pour mettre en pause/reprendre le jeu ou redémarrer après un game over

## 🧠 Fonctionnement technique

### 🕹️ Système de contrôle amélioré

Le code Arduino implémente un contrôle sophistiqué avec:

```cpp
// Extrait du code Arduino
void process_acceleration_values(float x, float y, float z) {
  // Appliquer un filtrage des micro-mouvements
  if (abs(x) < TILT_DEADZONE) x = 0;
  if (abs(y) < TILT_DEADZONE) y = 0;
  
  // Détecter si le contrôleur est incliné au-delà du seuil
  bool currently_tilted = (abs(x) > TILT_THRESHOLD) || (abs(y) > TILT_THRESHOLD);
  
  // ...logique d'amplification adaptative...
}
```

- **Persistance du mouvement**: Le mouvement continue même après une légère inclinaison
- **Amplification progressive**: Plus l'inclinaison dure, plus le mouvement est amplifié
- **Filtrage intelligent**: Élimine les micro-tremblements pour un contrôle fluide
- **Détection de saut**: Via l'accéléromètre pour franchir des obstacles

### 🧩 Génération des labyrinthes

Les labyrinthes sont générés avec l'algorithme de "Recursive Backtracking":

```csharp
// Extrait de VerticalMazeGenerator.cs
private Node3D GenerateMaze(int mazeIndex)
{
    // ...génération du labyrinthe...
    
    // Algorithme de génération de labyrinthe par backtracking
    while (unvisitedCells > 0)
    {
        // Recherche des voisins non visités
        List<Vector2I> neighbors = new List<Vector2I>();
        
        // ...recherche et connexion des cellules...
    }
    
    // ...placement des chats, téléporteurs, etc...
}
```

- Taille croissante à chaque niveau
- Placement stratégique des entrées/sorties
- Téléporteurs pour passer d'un labyrinthe à l'autre

### 🐱 Système de chats

Chaque type de chat a des effets uniques sur le jeu:

```csharp
// Extrait de Cat.cs
private void ApplyEffectToMainScene()
{
    // ...
    switch (_catType)
    {
        case CatType.Orange: // +5 secondes
            mainScene.Call("AddTime", 5.0f);
            mainScene.Call("AddCatCollected", (int)_catType);
            effectText = "+5 secondes";
            effectColor = Colors.Green;
            soundEvent = "Bonus";
            break;
        // ...autres types de chats...
    }
    // ...
}
```

## 🔧 Aspects techniques notables

### 🔄 Communication Arduino-Godot

Le système utilise une communication série à 9600 bauds avec un format spécifique:

```
X,Y,Z,JUMP,BUTTON
```

Où:
- **X, Y, Z** sont les valeurs d'accélération
- **JUMP** indique si un saut est détecté
- **BUTTON** indique l'état du bouton

### 🏠 Architecture Singleton

Le jeu utilise des singletons pour gérer les systèmes critiques:

```csharp
// Extrait d'ArduinoManager.cs
public partial class ArduinoManager : Node
{
    // Implémentation du pattern Singleton
    private static ArduinoManager _instance;
    public static ArduinoManager Instance 
    { 
        get { return _instance; }
        private set { _instance = value; }
    }
    
    // ...reste du code...
}
```

## 🏆 Développement futur

Améliorations potentielles:

- 🎨 Plus de variété dans les textures et thèmes des labyrinthes
- 🌟 Nouveaux types de chats avec des effets différents
- 🏆 Système de défis et succès
- 🎵 Sons et musique additionnels
- 💾 Synchronisation des scores en ligne

## 📝 Notes de développement

Le projet utilise C# avec Godot Engine pour le jeu principal, et C++ sur Arduino pour le contrôleur. L'intégration audio avancée est assurée par FMOD, permettant des sons 3D positionnels.

La génération procédurale garantit une rejouabilité élevée, tandis que le système de difficulté croissante maintient l'intérêt du joueur.

## 🔧 Dépannage courant

- **Problème de connexion Arduino**: Vérifiez le port COM dans ArduinoManager.cs
- **Mouvement trop sensible**: Ajustez les seuils dans le code Arduino
- **Sauts non détectés**: Modifiez `JUMP_THRESHOLD` dans le code Arduino

---

🧶 Développé avec patience et passion... comme une pelote de laine devant un chat! 🐱