using Dalamud.Configuration;

namespace FC_Tweaks;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Expanded { get; set; }
}
