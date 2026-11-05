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
    private readonly FreeCompanyMemberSorter sorter;

    public Plugin(IDalamudPluginInterface pluginInterface, IGameGui gameGui, IFramework framework,
        IAddonLifecycle addonLifecycle, IPluginLog log, ICondition condition, ISigScanner scanner,
        IDataManager dataManager)
    {
        this.pluginInterface = pluginInterface;
        this.framework = framework;
        var configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        resizer = new FreeCompanyWindowResizer(gameGui, addonLifecycle, log, condition, configuration,
            () => pluginInterface.SavePluginConfig(configuration));
        sorter = new FreeCompanyMemberSorter(gameGui, condition, addonLifecycle, log, scanner, dataManager);

        framework.Update += OnFrameworkUpdate;
        pluginInterface.UiBuilder.Draw += Draw;
    }

    public string Name => "FC Tweaks";

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= Draw;
        framework.Update -= OnFrameworkUpdate;
        sorter.Dispose();
        resizer.Dispose();
    }

    private void OnFrameworkUpdate(IFramework frameworkContext)
    {
        resizer.Update();
        sorter.Update();
    }

    private void Draw()
    {
        sorter.Draw();
        if (!resizer.IsFreeCompanyVisible || !resizer.CanExpand)
            return;

        var (x, y) = resizer.GetButtonPosition();
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var flags = ImGuiWindowFlags.NoDecoration
                    | ImGuiWindowFlags.NoBackground
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing
                    | ImGuiWindowFlags.NoNav
                    | ImGuiWindowFlags.AlwaysAutoResize;

        if (!ImGui.Begin("FC Tweaks Overlay", flags))
        {
            ImGui.End();
            ImGui.PopStyleVar();
            return;
        }

        if (ImGui.Button(resizer.IsExpanded ? "Collapse##FC" : "Expand##FC"))
            resizer.ToggleExpansion();

        ImGui.End();
        ImGui.PopStyleVar();
    }
}
