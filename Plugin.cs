using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
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
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IPluginLog log;

    public Plugin(IDalamudPluginInterface pluginInterface, IGameGui gameGui, IFramework framework,
        IAddonLifecycle addonLifecycle, IPluginLog log, ICondition condition, ISigScanner scanner,
        IDataManager dataManager, ICommandManager commands, IChatGui chat)
    {
        this.pluginInterface = pluginInterface;
        this.framework = framework;
        this.commands = commands;
        this.chat = chat;
        this.log = log;
        var configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        resizer = new FreeCompanyWindowResizer(gameGui, addonLifecycle, log, condition, configuration,
            () => pluginInterface.SavePluginConfig(configuration));
        sorter = new FreeCompanyMemberSorter(gameGui, condition, addonLifecycle, log, scanner, dataManager);

        if (!commands.AddHandler("/fctweaks", new CommandInfo(OnCommand)
            { HelpMessage = "Toggle global FC window expansion (also saves the choice while closed)." }))
            log.Error("FC Tweaks could not register /fctweaks: the command is already registered.");
        framework.Update += OnFrameworkUpdate;
        pluginInterface.UiBuilder.Draw += Draw;
    }

    public string Name => "FC Tweaks";

    public void Dispose()
    {
        if (commands.Commands.TryGetValue("/fctweaks", out var command) && command.Handler == OnCommand)
            commands.RemoveHandler("/fctweaks");
        pluginInterface.UiBuilder.Draw -= Draw;
        framework.Update -= OnFrameworkUpdate;
        sorter.Dispose();
        resizer.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        resizer.ToggleExpansion();
        chat.Print("[FC Tweaks] Expand/Collapse toggle queued. Open the FC window to see the saved size.");
        log.Information("FC Tweaks command: {State}", resizer.GetDiagnosticState());
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
