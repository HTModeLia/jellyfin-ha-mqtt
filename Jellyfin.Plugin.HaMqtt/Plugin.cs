using System;
using System.Collections.Generic;
using Jellyfin.Plugin.HaMqtt.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.HaMqtt;

/// <summary>Extension : appareil « Jellyfin » dans Home Assistant par MQTT.</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Home Assistant MQTT";

    public override Guid Id => Guid.Parse("a757374b-70ab-405b-82ad-bfb8817d3271");

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo { Name = Name, EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html" }
    ];
}
