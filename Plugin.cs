using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FC_Tweaks;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/fctweaks";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IFramework framework;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly FreeCompanyWindowResizer resizer;
    private readonly FreeCompanyMemberSorter sorter;
    private readonly bool ownsCommand;

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
        resizer = new FreeCompanyWindowResizer(gameGui, addonLifecycle, condition, log, configuration,
            () => pluginInterface.SavePluginConfig(configuration));
        sorter = new FreeCompanyMemberSorter(gameGui, condition, addonLifecycle, log, scanner, dataManager);

        ownsCommand = commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Expand or collapse the Free Company window. \"/fctweaks status\" prints diagnostics.",
        });
        if (!ownsCommand)
            log.Error("FC Tweaks could not register {Command}: it is already registered.", CommandName);

        framework.Update += OnFrameworkUpdate;
        pluginInterface.UiBuilder.Draw += OnDraw;
    }

    public string Name => "FC Tweaks";

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= OnDraw;
        framework.Update -= OnFrameworkUpdate;
        if (ownsCommand)
            commands.RemoveHandler(CommandName);
        sorter.Dispose();
        resizer.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            var state = resizer.DescribeState();
            log.Information("FC Tweaks status: {State}", state);
            chat.Print($"[FC Tweaks] {state}");
            return;
        }

        resizer.ToggleExpansion();
        chat.Print($"[FC Tweaks] Free Company window {(resizer.IsExpanded ? "expanded" : "collapsed")}.");
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        resizer.Update();
        sorter.Update();
    }

    private void OnDraw()
    {
        resizer.Draw();
        sorter.Draw();
    }
}
