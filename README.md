# Home Assistant MQTT — extension Jellyfin

Publie un appareil **« Jellyfin »** dans Home Assistant par MQTT, avec la découverte automatique :
HA crée tout seul l'appareil et ses capteurs, sans YAML ni polling. Indépendant du proxy de streaming.

- Messages de découverte retenus sur `homeassistant/sensor/jellyfin/<champ>/config`.
- État : un JSON retenu sur `jellyfin/etat`, publié **seulement quand il change** (lecture, arrêt, ajout, ou
  compteurs recalculés en local toutes les 5 minutes et différents du dernier envoi).
- Signal de vie : l'état complet est republié toutes les 30 minutes, même sans changement. Les capteurs ont
  `expire_after` = 4000 s : si Jellyfin se fige sans couper la connexion MQTT, HA les passe en indisponible.
- Disponibilité : `jellyfin/dispo` = `online` à la connexion ; `offline` (message de dernière volonté, retenu)
  si Jellyfin s'arrête ou plante. Keepalive MQTT de 60 s. Reconnexion automatique toutes les 10 secondes si le
  broker tombe.

Cible : **Jellyfin 12.4.0** (.NET 10, `targetAbi` 12.4.0.0).

## Compilation

```powershell
.\empaqueter.ps1
```

Produit dans `dist\` :

- `HomeAssistantMqtt_1.0.0.0\` : `Jellyfin.Plugin.HaMqtt.dll` et `MQTTnet.dll` (installation manuelle) ;
- `jellyfin-ha-mqtt_1.0.0.0.zip` et `manifest.json` (dépôt d'extensions).

Tests (logique de découverte, sans broker) : `dotnet test`.

## Installation

### Copie manuelle (le plus simple)

1. Copier le dossier `dist\HomeAssistantMqtt_1.0.0.0` (les **deux** DLL) dans le dossier `plugins` des données
   de Jellyfin :
   - Docker : `/config/plugins/` (le volume monté sur `/config`) ;
   - Debian/Ubuntu : `/var/lib/jellyfin/plugins/` ;
   - Windows : `%ProgramData%\Jellyfin\Server\plugins\`.
2. Redémarrer Jellyfin. L'extension apparaît dans *Tableau de bord → Extensions*.

### Dépôt d'extensions

1. Servir `jellyfin-ha-mqtt_1.0.0.0.zip` et `manifest.json` sur un serveur web joignable par Jellyfin.
2. Régénérer le manifest avec la bonne adresse du zip (la somme MD5 y est recalculée) :
   `.\empaqueter.ps1 -UrlZip http://<serveur>/jellyfin-ha-mqtt_1.0.0.0.zip`
3. *Tableau de bord → Extensions → Dépôts* : ajouter l'URL de `manifest.json`, puis installer
   « Home Assistant MQTT » depuis le catalogue et redémarrer Jellyfin.

## Réglages

*Tableau de bord → Extensions → Home Assistant MQTT* :

| Réglage      | Exemple          |
|--------------|------------------|
| Hôte         | `192.168.13.33`  |
| Port         | `1883`           |
| Utilisateur  | compte Mosquitto |
| Mot de passe | —                |

Les réglages s'appliquent dans les 10 secondes, **sans redémarrer Jellyfin**. Hôte vide = remontée coupée.
Le mot de passe est stocké en clair dans la configuration de l'extension (comme toute extension Jellyfin) :
prévoir un compte Mosquitto dédié.

## Capteurs

| Capteur                    | Champ                     | Mise à jour                      |
|----------------------------|---------------------------|----------------------------------|
| Serveur (connectivité)     | `serveur`                 | `jellyfin/dispo` (online/offline)|
| Version                    | `version`                 | au démarrage                     |
| *Nom de la bibliothèque*   | `bib_<nom>`               | toutes les 5 min                 |
| Lectures en cours          | `lectures_en_cours`       | lecture, arrêt, 5 min            |
| Dernière lecture           | `derniere_lecture`        | début de lecture                 |
| Dernière lecture (client)  | `derniere_lecture_client` | début de lecture                 |
| Dernier ajout              | `dernier_ajout`           | ajout à la bibliothèque          |
| CPU                        | `cpu` (%)                 | 5 min (moyenne sur l'intervalle) |
| RAM                        | `ram` (Mo)                | 5 min                            |

- **Bibliothèques** : un capteur par dossier de collection. Films → nombre de films ; Séries → nombre de séries ;
  Musique → nombre d'albums ; Collections → nombre de collections ; autres → nombre de fichiers.
  Une bibliothèque supprimée disparaît de HA (message de découverte vide) ; « Dessins animés » donne le champ
  `bib_dessins_animes`.
- **Dernière lecture** : « Série - Épisode » pour un épisode ; le client indique « Application (Appareil) - Utilisateur ».
- **Dernier ajout** : films, épisodes, séries, musiques, livres (pas les saisons ni les dossiers). Pendant une
  première analyse complète, le capteur défile au rythme des ajouts.
- **Serveur** : « Connecté » / « Déconnecté » ; reste lisible quand Jellyfin est arrêté (il ne devient pas indisponible).
  La version figure aussi dans la fiche de l'appareil (`sw_version`).
- **CPU** : part du processus Jellyfin sur l'ensemble des cœurs (100 % = tous les cœurs pleins), arrondie à 1 % ;
  absent au premier relevé. **RAM** arrondie à 10 Mo. Ces arrondis évitent d'envoyer un état pour un écart insignifiant.
- Les états texte sont limités à 255 caractères (limite de HA).
