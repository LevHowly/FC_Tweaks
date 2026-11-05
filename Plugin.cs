using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace FC_Tweaks;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IFramework framework;
    private readonly FreeCompanyWindowResizer resizer;

    public Plugin(IDalamudPluginInterface pluginInterface, IGameGui gameGui, IFramework framework,
        IAddonLifecycle addonLifecycle, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.framework = framework;
        resizer = new FreeCompanyWindowResizer(gameGui, addonLifecycle, log);

        framework.Update += OnFrameworkUpdate;
        pluginInterface.UiBuilder.Draw += Draw;
    }

    public string Name => "FC Tweaks";

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= Draw;
        framework.Update -= OnFrameworkUpdate;
        resizer.Dispose();
    }

    private void OnFrameworkUpdate(IFramework frameworkContext) => resizer.Update();

    private void Draw()
    {
        if (!resizer.IsFreeCompanyVisible || !resizer.CanExpand)
            return;

        var (x, y) = resizer.GetButtonPosition();
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0);

        var flags = ImGuiWindowFlags.NoDecoration
                    | ImGuiWindowFlags.NoBackground
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing
                    | ImGuiWindowFlags.NoNav
                    | ImGuiWindowFlags.AlwaysAutoResize;

        if (!ImGui.Begin("FC Tweaks Overlay", flags))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Button(resizer.IsExpanded ? "Collapse##FC" : "Expand##FC"))
            resizer.ToggleExpansion();

        ImGui.End();
    }
}
