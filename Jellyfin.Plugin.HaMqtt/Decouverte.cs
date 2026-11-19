using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.HaMqtt;

/// <summary>Messages de découverte MQTT de Home Assistant. Sans dépendance Jellyfin, pour être testable seule.</summary>
public static class Decouverte
{
    public const string Base = "jellyfin";
    public const string Dispo = Base + "/dispo";
    public const string Etat = Base + "/etat";

    /// <summary>Entité HA : Champ est la clé du JSON publié sur <see cref="Etat"/>.</summary>
    public sealed record Entite(string Composant, string Champ, string Nom, string Icone, string? Unite = null);

    /// <summary>Signal de vie : état complet republié même sans changement.</summary>
    public const int SignalDeVie = 1800;

    /// <summary>Plus de deux signaux de vie sans état : HA passe les capteurs en indisponible (Jellyfin figé, MQTT encore connecté).</summary>
    public const int ExpireApres = 4000;

    public static readonly Entite[] Fixes =
    [
        new("binary_sensor", "serveur", "Serveur", "mdi:server-network"),
        new("sensor", "version", "Version", "mdi:information-outline"),
        new("sensor", "lectures_en_cours", "Lectures en cours", "mdi:play-network"),
        new("sensor", "derniere_lecture", "Dernière lecture", "mdi:play-circle"),
        new("sensor", "derniere_lecture_client", "Dernière lecture (client)", "mdi:cellphone-play"),
        new("sensor", "dernier_ajout", "Dernier ajout", "mdi:new-box"),
        new("sensor", "cpu", "CPU", "mdi:cpu-64-bit", "%"),
        new("sensor", "ram", "RAM", "mdi:memory", "Mo"),
    ];

    /// <summary>Capteur d'une bibliothèque (dossier de collection) : « Dessins animés » -> champ « bib_dessins_animes ».</summary>
    public static Entite Bibliotheque(string nom) => new("sensor", "bib_" + Slug(nom), nom, "mdi:folder-play");

    public static string Slug(string nom)
    {
        var sansAccents = new string(nom.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(sansAccents.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
    }

    public static string Topic(Entite e) => $"homeassistant/{e.Composant}/{Base}/{e.Champ}/config";

    public static string Config(Entite e, string version)
    {
        var conf = new Dictionary<string, object>
        {
            ["name"] = e.Nom,
            ["unique_id"] = $"{Base}_{e.Champ}",
            ["icon"] = e.Icone,
            ["device"] = new Dictionary<string, object>
            {
                ["identifiers"] = new[] { Base },
                ["name"] = "Jellyfin",
                ["manufacturer"] = "Jellyfin",
                ["sw_version"] = version,
            },
        };
        if (e.Composant == "binary_sensor")
        {
            // « Serveur » : lit directement la dispo, pour rester affiché « Déconnecté » (et non indisponible) quand Jellyfin s'arrête
            conf["state_topic"] = Dispo;
            conf["payload_on"] = "online";
            conf["payload_off"] = "offline";
            conf["device_class"] = "connectivity";
            return JsonSerializer.Serialize(conf);
        }

        conf["state_topic"] = Etat;
        conf["value_template"] = $"{{{{ value_json.{e.Champ} }}}}";
        conf["availability_topic"] = Dispo;
        conf["expire_after"] = ExpireApres;
        // Compteurs et mesures : historique numérique (graphiques) dans HA
        if (e.Unite is not null || e.Champ.StartsWith("bib_", System.StringComparison.Ordinal) || e.Champ == "lectures_en_cours")
        {
            conf["state_class"] = "measurement";
        }

        if (e.Unite is not null)
        {
            conf["unit_of_measurement"] = e.Unite;
        }

        return JsonSerializer.Serialize(conf);
    }

    /// <summary>HA rejette un état de plus de 255 caractères.</summary>
    public static string Tronquer(string? texte) => texte is null ? string.Empty : texte.Length > 255 ? texte[..255] : texte;
}
