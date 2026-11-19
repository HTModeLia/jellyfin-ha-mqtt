using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HaMqtt.Configuration;

/// <summary>Réglages du broker MQTT (hôte vide = remontée désactivée).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public string Hote { get; set; } = string.Empty;

    public int Port { get; set; } = 1883;

    public string Utilisateur { get; set; } = string.Empty;

    public string MotDePasse { get; set; } = string.Empty;
}
