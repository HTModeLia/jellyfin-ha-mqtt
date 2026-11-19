using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HaMqtt;

/// <summary>Démarre le publieur MQTT avec Jellyfin.</summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        => serviceCollection.AddHostedService<PublieurMqtt>();
}
