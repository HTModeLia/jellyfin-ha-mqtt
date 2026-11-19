using System.Text.Json;
using Jellyfin.Plugin.HaMqtt;

namespace Jellyfin.Plugin.HaMqtt.Tests;

public class DecouverteTests
{
    [Fact]
    public void Topic_et_config_d_un_capteur_fixe()
    {
        var entite = Decouverte.Fixes.Single(e => e.Champ == "derniere_lecture");
        Assert.Equal("homeassistant/sensor/jellyfin/derniere_lecture/config", Decouverte.Topic(entite));

        var conf = JsonDocument.Parse(Decouverte.Config(entite, "12.4.0")).RootElement;
        Assert.Equal("Dernière lecture", conf.GetProperty("name").GetString());
        Assert.Equal("jellyfin_derniere_lecture", conf.GetProperty("unique_id").GetString());
        Assert.Equal("jellyfin/etat", conf.GetProperty("state_topic").GetString());
        Assert.Equal("{{ value_json.derniere_lecture }}", conf.GetProperty("value_template").GetString());
        Assert.Equal("jellyfin/dispo", conf.GetProperty("availability_topic").GetString());
        Assert.False(conf.TryGetProperty("state_class", out _)); // texte : pas d'historique numérique

        var appareil = conf.GetProperty("device");
        Assert.Equal("jellyfin", appareil.GetProperty("identifiers")[0].GetString());
        Assert.Equal("Jellyfin", appareil.GetProperty("name").GetString());
        Assert.Equal("12.4.0", appareil.GetProperty("sw_version").GetString());
    }

    [Fact]
    public void Capteur_de_bibliotheque_numerique_et_slug_sans_accents()
    {
        var entite = Decouverte.Bibliotheque("Dessins animés & Mangas");
        Assert.Equal("bib_dessins_animes_mangas", entite.Champ);
        Assert.Equal("homeassistant/sensor/jellyfin/bib_dessins_animes_mangas/config", Decouverte.Topic(entite));

        var conf = JsonDocument.Parse(Decouverte.Config(entite, "12.4.0")).RootElement;
        Assert.Equal("Dessins animés & Mangas", conf.GetProperty("name").GetString());
        Assert.Equal("measurement", conf.GetProperty("state_class").GetString());
    }

    [Fact]
    public void Mesures_avec_unite()
    {
        var conf = JsonDocument.Parse(Decouverte.Config(Decouverte.Fixes.Single(e => e.Champ == "cpu"), "x")).RootElement;
        Assert.Equal("%", conf.GetProperty("unit_of_measurement").GetString());
        Assert.Equal("measurement", conf.GetProperty("state_class").GetString());
    }

    [Fact]
    public void Champs_uniques_et_etats_tronques_a_255()
    {
        Assert.Equal(Decouverte.Fixes.Length, Decouverte.Fixes.Select(e => e.Champ).Distinct().Count());
        Assert.Equal(255, Decouverte.Tronquer(new string('a', 300)).Length);
        Assert.Equal("court", Decouverte.Tronquer("court"));
        Assert.Equal("", Decouverte.Tronquer(null));
    }
}
