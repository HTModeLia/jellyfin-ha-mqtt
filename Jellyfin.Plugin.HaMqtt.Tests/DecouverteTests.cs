using System.Text.Json;
using Jellyfin.Plugin.HaMqtt;

namespace Jellyfin.Plugin.HaMqtt.Tests;

public class DecouverteTests
{
    internal const string Id = "508673e842f94630ada5c59699689459";

    [Fact]
    public void Topic_et_config_d_un_capteur_fixe()
    {
        var entite = Decouverte.Fixes.Single(e => e.Champ == "derniere_lecture");
        Assert.Equal("homeassistant/sensor/jellyfin/derniere_lecture/config", Decouverte.Topic(entite));

        var conf = JsonDocument.Parse(Decouverte.Config(entite, "12.4.0", Id)).RootElement;
        Assert.Equal("Dernière lecture", conf.GetProperty("name").GetString());
        Assert.Equal("jellyfin_derniere_lecture", conf.GetProperty("unique_id").GetString());
        Assert.Equal($"jellyfin/{Id}/derniere_lecture/state", conf.GetProperty("state_topic").GetString());
        Assert.False(conf.TryGetProperty("value_template", out _)); // valeur brute
        Assert.Equal($"jellyfin/{Id}/availability", conf.GetProperty("availability_topic").GetString());
        Assert.False(conf.TryGetProperty("state_class", out _)); // texte : pas d'historique numérique
        Assert.Equal(4000, conf.GetProperty("expire_after").GetInt32());

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

        var conf = JsonDocument.Parse(Decouverte.Config(entite, "12.4.0", Id)).RootElement;
        Assert.Equal("Dessins animés & Mangas", conf.GetProperty("name").GetString());
        Assert.Equal("measurement", conf.GetProperty("state_class").GetString());
    }

    [Fact]
    public void Mesures_avec_unite()
    {
        var conf = JsonDocument.Parse(Decouverte.Config(Decouverte.Fixes.Single(e => e.Champ == "cpu"), "x", Id)).RootElement;
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

public class ServeurEtVersionTests
{
    private const string Id = DecouverteTests.Id;

    [Fact]
    public void Serveur_lit_la_dispo_sans_expiration()
    {
        var entite = Decouverte.Fixes.Single(e => e.Champ == "serveur");
        Assert.Equal("homeassistant/binary_sensor/jellyfin/serveur/config", Decouverte.Topic(entite));

        var conf = JsonDocument.Parse(Decouverte.Config(entite, "12.4.0", Id)).RootElement;
        Assert.Equal($"jellyfin/{Id}/availability", conf.GetProperty("state_topic").GetString());
        Assert.Equal("online", conf.GetProperty("payload_on").GetString());
        Assert.Equal("offline", conf.GetProperty("payload_off").GetString());
        Assert.Equal("connectivity", conf.GetProperty("device_class").GetString());
        // Doit afficher « Déconnecté » quand Jellyfin est arrêté, pas « indisponible »
        Assert.False(conf.TryGetProperty("availability_topic", out _));
        Assert.False(conf.TryGetProperty("expire_after", out _));
    }

    [Fact]
    public void Redemarrage_requis_binaire_sur_son_topic_d_etat()
    {
        var entite = Decouverte.Fixes.Single(e => e.Champ == "redemarrage_requis");
        Assert.Equal("homeassistant/binary_sensor/jellyfin/redemarrage_requis/config", Decouverte.Topic(entite));

        var conf = JsonDocument.Parse(Decouverte.Config(entite, "12.4.0", Id)).RootElement;
        Assert.Equal($"jellyfin/{Id}/redemarrage_requis/state", conf.GetProperty("state_topic").GetString());
        Assert.Equal("update", conf.GetProperty("device_class").GetString());
        Assert.Equal($"jellyfin/{Id}/availability", conf.GetProperty("availability_topic").GetString());
        Assert.False(conf.TryGetProperty("payload_on", out _)); // ON / OFF par défaut, comme Decouverte.Valeur(bool)
    }

    [Fact]
    public void Version_lit_l_etat_et_expire_apres_deux_signaux_de_vie()
    {
        var conf = JsonDocument.Parse(Decouverte.Config(Decouverte.Fixes.Single(e => e.Champ == "version"), "12.4.0", Id)).RootElement;
        Assert.Equal($"jellyfin/{Id}/version/state", conf.GetProperty("state_topic").GetString());
        Assert.True(Decouverte.ExpireApres > 2 * Decouverte.SignalDeVie);
    }
}

public class ValeursTests
{
    [Fact]
    public void Valeurs_brutes()
    {
        Assert.Equal("ON", Decouverte.Valeur(true));
        Assert.Equal("OFF", Decouverte.Valeur(false));
        Assert.Equal("12", Decouverte.Valeur(12));
        Assert.Equal("3.5", Decouverte.Valeur(3.5)); // pas de virgule, même sur un serveur en français
        Assert.Equal(255, Decouverte.Valeur(new string('a', 300)).Length);
    }

    [Fact]
    public void Identifiant_sans_tirets_et_anciens_topics()
    {
        Assert.Equal("508673e842f94630ada5c59699689459", Decouverte.Identifiant("508673E8-42F9-4630-ADA5-C59699689459"));
        Assert.Equal(["jellyfin/dispo", "jellyfin/etat"], Decouverte.AnciensTopics);
    }
}
