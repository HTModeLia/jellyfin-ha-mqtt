using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;

namespace Jellyfin.Plugin.HaMqtt;

/// <summary>
/// Publie chaque champ sur son topic retenu jellyfin/&lt;id&gt;/&lt;champ&gt;/state, seulement quand il change
/// (événements Jellyfin, compteurs recalculés toutes les 5 min), et tous les champs à chaque signal de vie.
/// Jellyfin arrêté ou planté = jellyfin/&lt;id&gt;/availability à offline (dernière volonté).
/// </summary>
public sealed class PublieurMqtt : BackgroundService
{
    private static readonly TimeSpan Verification = TimeSpan.FromSeconds(10); // reconnexion et prise en compte des réglages
    private static readonly TimeSpan Intervalle = TimeSpan.FromMinutes(5);

    private readonly ILibraryManager _bibliotheque;
    private readonly ISessionManager _sessions;
    private readonly ILogger<PublieurMqtt> _log;
    private readonly string _version;
    private readonly string _id;
    private readonly Dictionary<string, object> _etat = new()
    {
        ["lectures_en_cours"] = 0, ["derniere_lecture"] = "", ["derniere_lecture_client"] = "", ["dernier_ajout"] = "",
    };

    private readonly Dictionary<string, Decouverte.Entite> _bibliotheques = new(); // champ -> entité
    private IMqttClient? _client;
    private volatile bool _reconfigurer;
    private readonly Dictionary<string, string> _envoye = new(); // champ -> dernière valeur publiée
    private TimeSpan _cpuPrecedent;
    private DateTime _mesurePrecedente;

    public PublieurMqtt(ILibraryManager bibliotheque, ISessionManager sessions, IServerApplicationHost hote, ILogger<PublieurMqtt> log)
    {
        _bibliotheque = bibliotheque;
        _sessions = sessions;
        _log = log;
        _version = hote.ApplicationVersionString;
        _id = Decouverte.Identifiant(hote.SystemId);
        _etat["version"] = _version;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _sessions.PlaybackStart += SurLecture;
        _sessions.PlaybackStopped += SurArret;
        _bibliotheque.ItemAdded += SurAjout;
        Plugin.Instance!.ConfigurationChanged += SurReglages;
        var prochainComptage = DateTime.MinValue;
        var prochainSignal = DateTime.UtcNow + TimeSpan.FromSeconds(Decouverte.SignalDeVie);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_reconfigurer)
                    {
                        _reconfigurer = false;
                        await DeconnecterAsync().ConfigureAwait(false);
                    }

                    var conf = Plugin.Instance.Configuration;
                    if (!string.IsNullOrWhiteSpace(conf.Hote) && _client is not { IsConnected: true })
                    {
                        await ConnecterAsync(conf, stoppingToken).ConfigureAwait(false);
                    }

                    if (DateTime.UtcNow >= prochainComptage)
                    {
                        prochainComptage = DateTime.UtcNow + Intervalle;
                        await CompterAsync().ConfigureAwait(false);
                    }

                    if (DateTime.UtcNow >= prochainSignal)
                    {
                        prochainSignal = DateTime.UtcNow + TimeSpan.FromSeconds(Decouverte.SignalDeVie);
                        await PublierEtatAsync(forcer: true).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning("[MQTT] {Erreur}", ex.Message);
                }

                await Task.Delay(Verification, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt de Jellyfin
        }
        finally
        {
            _sessions.PlaybackStart -= SurLecture;
            _sessions.PlaybackStopped -= SurArret;
            _bibliotheque.ItemAdded -= SurAjout;
            Plugin.Instance.ConfigurationChanged -= SurReglages;
            await DeconnecterAsync().ConfigureAwait(false);
        }
    }

    private void SurReglages(object? sender, MediaBrowser.Model.Plugins.BasePluginConfiguration e) => _reconfigurer = true;

    private async Task ConnecterAsync(Configuration.PluginConfiguration conf, CancellationToken ct)
    {
        _client?.Dispose();
        _client = new MqttClientFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(conf.Hote.Trim(), conf.Port > 0 ? conf.Port : 1883)
            .WithClientId("jellyfin_ha_mqtt")
            .WithWillTopic(Decouverte.Dispo(_id)).WithWillPayload("offline").WithWillRetain()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));
        if (!string.IsNullOrEmpty(conf.Utilisateur))
        {
            options.WithCredentials(conf.Utilisateur, conf.MotDePasse);
        }

        await _client.ConnectAsync(options.Build(), ct).ConfigureAwait(false);
        foreach (var ancien in Decouverte.AnciensTopics)
        {
            await PublierAsync(ancien, string.Empty).ConfigureAwait(false); // charge vide retenue = topic effacé du broker
        }

        foreach (var entite in Decouverte.Fixes.Concat(Bibliotheques()))
        {
            await PublierAsync(Decouverte.Topic(entite), Decouverte.Config(entite, _version, _id)).ConfigureAwait(false);
        }

        await PublierAsync(Decouverte.Dispo(_id), "online").ConfigureAwait(false);
        await PublierEtatAsync(forcer: true).ConfigureAwait(false);
        _log.LogInformation("[MQTT] Connecté à {Hote}, appareil publié pour Home Assistant", conf.Hote);
    }

    private async Task DeconnecterAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                await PublierAsync(Decouverte.Dispo(_id), "offline").ConfigureAwait(false);
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug("[MQTT] Déconnexion : {Erreur}", ex.Message);
        }

        _client.Dispose();
        _client = null;
    }

    private async Task<bool> PublierAsync(string topic, string contenu)
    {
        if (_client is not { IsConnected: true })
        {
            return false;
        }

        var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(contenu).WithRetainFlag().Build();
        await _client.PublishAsync(message).ConfigureAwait(false);
        return true;
    }

    /// <summary>Publie les champs dont la valeur a changé depuis le dernier envoi, ou tous avec <paramref name="forcer"/> (connexion, signal de vie).</summary>
    private async Task PublierEtatAsync(bool forcer = false)
    {
        List<KeyValuePair<string, string>> aEnvoyer;
        lock (_etat)
        {
            aEnvoyer = _etat.Select(c => KeyValuePair.Create(c.Key, Decouverte.Valeur(c.Value)))
                // valeur vide (rien lu ni ajouté depuis le démarrage) : on garde la valeur retenue sur le broker
                .Where(c => c.Value.Length > 0 && (forcer || !_envoye.TryGetValue(c.Key, out var avant) || avant != c.Value))
                .ToList();
        }

        foreach (var (champ, valeur) in aEnvoyer)
        {
            if (await PublierAsync(Decouverte.EtatTopic(_id, champ), valeur).ConfigureAwait(false))
            {
                lock (_etat)
                {
                    _envoye[champ] = valeur;
                }
            }
        }
    }

    /// <summary>Met à jour des champs de l'état puis le publie (appelé depuis les événements Jellyfin).</summary>
    private async void Signaler(params (string Champ, object Valeur)[] champs)
    {
        try
        {
            lock (_etat)
            {
                foreach (var (champ, valeur) in champs)
                {
                    _etat[champ] = valeur;
                }
            }

            await PublierEtatAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[MQTT] Publication impossible : {Erreur}", ex.Message);
        }
    }

    private IEnumerable<Decouverte.Entite> Bibliotheques()
    {
        lock (_bibliotheques)
        {
            return _bibliotheques.Values.ToList();
        }
    }

    private int LecturesEnCours() => _sessions.Sessions.Count(s => s.NowPlayingItem is not null);

    private static string Titre(BaseItem item) => item is Episode ep ? $"{ep.SeriesName} - {ep.Name}" : item.Name;

    private void SurLecture(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Item is null)
        {
            return;
        }

        var qui = e.Session?.UserName;
        var client = $"{e.ClientName} ({e.DeviceName})" + (string.IsNullOrEmpty(qui) ? "" : $" - {qui}");
        Signaler(("derniere_lecture", Titre(e.Item)), ("derniere_lecture_client", client), ("lectures_en_cours", LecturesEnCours()));
    }

    private void SurArret(object? sender, PlaybackStopEventArgs e) => Signaler(("lectures_en_cours", LecturesEnCours()));

    private void SurAjout(object? sender, ItemChangeEventArgs e)
    {
        // Ignore saisons, personnes, dossiers… : seuls les médias et les séries comptent comme « ajout »
        if (e.Item is (Video or Series or Audio or Book) and { IsVirtualItem: false })
        {
            Signaler(("dernier_ajout", Titre(e.Item)));
        }
    }

    /// <summary>Compteurs par bibliothèque, lectures en cours, CPU et RAM ; déclare les nouvelles bibliothèques, retire les supprimées.</summary>
    private async Task CompterAsync()
    {
        var champs = new List<(string, object)> { ("lectures_en_cours", LecturesEnCours()) };
        var vues = new Dictionary<string, Decouverte.Entite>();
        foreach (var dossier in _bibliotheque.GetVirtualFolders())
        {
            if (!Guid.TryParse(dossier.ItemId, out var id) || _bibliotheque.GetItemById(id) is not { } racine)
            {
                continue;
            }

            BaseItemKind[] types = dossier.CollectionType?.ToString().ToLowerInvariant() switch
            {
                "movies" => [BaseItemKind.Movie],
                "tvshows" => [BaseItemKind.Series],
                "music" => [BaseItemKind.MusicAlbum],
                "boxsets" => [BaseItemKind.BoxSet],
                _ => [],
            };
            var requete = new InternalItemsQuery { Parent = racine, Recursive = true, IsVirtualItem = false, IncludeItemTypes = types };
            if (types.Length == 0)
            {
                requete.IsFolder = false; // autres bibliothèques : nombre d'éléments (fichiers)
            }

            var entite = Decouverte.Bibliotheque(dossier.Name);
            vues[entite.Champ] = entite;
            champs.Add((entite.Champ, _bibliotheque.GetCount(requete)));
        }

        List<Decouverte.Entite> nouvelles, retirees;
        lock (_bibliotheques)
        {
            nouvelles = vues.Values.Where(v => !_bibliotheques.ContainsKey(v.Champ)).ToList();
            retirees = _bibliotheques.Values.Where(b => !vues.ContainsKey(b.Champ)).ToList();
            _bibliotheques.Clear();
            foreach (var v in vues)
            {
                _bibliotheques[v.Key] = v.Value;
            }
        }

        foreach (var entite in nouvelles)
        {
            await PublierAsync(Decouverte.Topic(entite), Decouverte.Config(entite, _version, _id)).ConfigureAwait(false);
        }

        foreach (var entite in retirees)
        {
            await PublierAsync(Decouverte.Topic(entite), string.Empty).ConfigureAwait(false); // config vide = entité supprimée dans HA
            await PublierAsync(Decouverte.EtatTopic(_id, entite.Champ), string.Empty).ConfigureAwait(false);
            lock (_etat)
            {
                _etat.Remove(entite.Champ);
                _envoye.Remove(entite.Champ);
            }
        }

        // CPU : moyenne sur l'intervalle, ramenée à 100 % pour tous les cœurs
        using var processus = Process.GetCurrentProcess();
        var maintenant = DateTime.UtcNow;
        if (_mesurePrecedente != default)
        {
            var cpu = (processus.TotalProcessorTime - _cpuPrecedent).TotalMilliseconds
                      / (maintenant - _mesurePrecedente).TotalMilliseconds / Environment.ProcessorCount * 100;
            champs.Add(("cpu", Math.Round(cpu))); // arrondis : pas de publication pour un écart insignifiant
        }

        _cpuPrecedent = processus.TotalProcessorTime;
        _mesurePrecedente = maintenant;
        champs.Add(("ram", processus.WorkingSet64 / (10 * 1024 * 1024) * 10)); // à 10 Mo près
        Signaler(champs.ToArray());
    }
}
