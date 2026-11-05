using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static FC_Tweaks.FreeCompanyUi;

namespace FC_Tweaks;

/// <summary>
/// Makes the Free Company window taller and gives the lists on its tabs (see <see cref="TabList"/>)
/// the extra height.
/// <para>
/// The saved <see cref="Configuration.Expanded"/> preference is the only state; every frame the
/// native windows are reconciled towards it. Toggling therefore never touches native memory.
/// </para>
/// </summary>
internal sealed unsafe class FreeCompanyWindowResizer : IDisposable
{
    /// <summary>The Members list's native height (freecompanymember.uld node 24), so it doubles.</summary>
    private const int ExpandedExtraHeight = 252;
    private static readonly Vector2 FallbackButtonOffset = new(-100, 8);
    private static readonly Vector2 FallbackButtonSize = new(64, 28);

    private static readonly FrameSpec WindowSpec = new(WindowAddon, HasFrame: true,
        [new DecorationSpec(19, NodeType.NineGrid, IsRotatedDivider: true)]);

    private static readonly TabListSpec[] TabListSpecs =
    [
        // Move the footer groups, not their children, so the offset is not applied twice.
        new(MemberListNodeId, new FrameSpec(MembersAddon, HasFrame: false,
        [
            new DecorationSpec(45, NodeType.Res, IsRotatedDivider: false),
            new DecorationSpec(49, NodeType.Res, IsRotatedDivider: false),
        ])),
        new(RankListNodeId, new FrameSpec(RankAddon, HasFrame: false,
        [
            new DecorationSpec(RankFooterNodeId, NodeType.Res, IsRotatedDivider: false),
        ])),
        new(TopicsOnlineListNodeId, new FrameSpec(TopicsAddon, HasFrame: false, [])),
        new(ActivityListNodeId, new FrameSpec(ActivityAddon, HasFrame: false, [])),
    ];

    private static readonly string[] Addons = [WindowAddon, .. TabListSpecs.Select(t => t.Frame.Name)];
    private static readonly AddonEvent[] RefreshEvents = [AddonEvent.PostUpdate, AddonEvent.PreDraw];
    private static readonly AddonEvent[] ResetEvents = [AddonEvent.PreSetup, AddonEvent.PreFinalize];

    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;

    private readonly TabList[] tabLists;
    private AddonFrame? windowFrame;
    private HiddenButton? hiddenButton;
    private bool reportedMissingButton;
    private (Vector2 Position, Vector2 Size)? expandButton;

    public FreeCompanyWindowResizer(IGameGui gameGui, IAddonLifecycle addonLifecycle, ICondition condition,
        IPluginLog log, Configuration configuration, Action saveConfiguration)
    {
        this.gameGui = gameGui;
        this.addonLifecycle = addonLifecycle;
        this.condition = condition;
        this.log = log;
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        tabLists = TabListSpecs.Select(spec => new TabList(spec, log)).ToArray();
        foreach (var addon in Addons)
        {
            foreach (var type in ResetEvents)
                addonLifecycle.RegisterListener(type, addon, OnAddonReset);
            foreach (var type in RefreshEvents)
                addonLifecycle.RegisterListener(type, addon, OnAddonRefresh);
        }
    }

    public bool IsExpanded => configuration.Expanded;

    private int ExtraHeight => IsExpanded ? ExpandedExtraHeight : 0;

    public void ToggleExpansion()
    {
        configuration.Expanded = !configuration.Expanded;
        saveConfiguration();
    }

    public void Update()
    {
        expandButton = null;
        if (IsTransitioning(condition))
            return;

        var window = GetAddon(WindowAddon);
        if (!IsShown(window))
            return;

        if (windowFrame is not null && windowFrame.Address != (nint)window)
            windowFrame = null;
        windowFrame ??= AddonFrame.TryCapture(window, WindowSpec, log);
        windowFrame?.Apply(window, ExtraHeight);
        expandButton = HideCommunityFinder(window);

        foreach (var tabList in tabLists)
        {
            var tab = GetAddon(tabList.AddonName);
            if (IsShown(tab) && tab->IsReady)
                tabList.Update(tab, ExtraHeight);
        }
    }

    /// <summary>Draws Expand/Collapse where the hidden Community Finder button was.</summary>
    public void Draw()
    {
        if (expandButton is not { } button)
            return;

        ImGui.SetNextWindowPos(button.Position, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
                                       | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize;
        if (ImGui.Begin("FC Tweaks Expand", flags))
        {
            if (ImGui.Button(IsExpanded ? "Collapse" : "Expand", button.Size))
                ToggleExpansion();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Make the Free Company window taller (saved). Also: /fctweaks");
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    public string DescribeState()
    {
        if (IsTransitioning(condition))
            return $"expanded={IsExpanded}; zoning, native state not read";

        var window = GetAddon(WindowAddon);
        var tabs = tabLists.Select(t =>
        {
            var tab = GetAddon(t.AddonName);
            return $"{t.AddonName} {DescribeAddon(tab)}: {t.Describe(tab)}";
        });
        return $"expanded={IsExpanded}, extraHeight={ExtraHeight}; " +
               $"window {DescribeAddon(window)}: {windowFrame?.Describe(window) ?? "not captured"}; " +
               $"{string.Join("; ", tabs)}; " +
               $"button {(expandButton is { } b ? $"{b.Position} {b.Size}" : "hidden")}";
    }

    public void Dispose()
    {
        foreach (var addon in Addons)
        {
            foreach (var type in ResetEvents)
                addonLifecycle.UnregisterListener(type, addon, OnAddonReset);
            foreach (var type in RefreshEvents)
                addonLifecycle.UnregisterListener(type, addon, OnAddonRefresh);
        }

        if (IsTransitioning(condition))
            return;

        foreach (var tabList in tabLists)
        {
            var tab = GetAddon(tabList.AddonName);
            if (tab != null && tab->IsReady)
                tabList.Restore(tab);
        }

        var window = GetAddon(WindowAddon);
        windowFrame?.Apply(window, 0);
        if (window != null && hiddenButton is { } button)
        {
            var node = window->GetNodeById(CommunityFinderButtonNodeId);
            if ((nint)node == button.Address)
                node->ToggleVisibility(button.WasVisible);
        }
    }

    /// <summary>Hides the native Community Finder button and returns where to draw Expand instead.</summary>
    private (Vector2 Position, Vector2 Size) HideCommunityFinder(AtkUnitBase* window)
    {
        short x = 0;
        short y = 0;
        window->GetPosition(&x, &y);
        var origin = new Vector2(x, y);
        var scale = window->GetScale();

        var node = window->GetNodeById(CommunityFinderButtonNodeId);
        if (node == null || (int)node->Type < 1000)
        {
            if (!reportedMissingButton)
                log.Warning("FC Tweaks: Community Finder button not found; drawing Expand at the top right instead.");
            reportedMissingButton = true;
            ushort width = 0;
            ushort height = 0;
            window->GetSize(&width, &height, false);
            return (origin + (new Vector2(width, 0) + FallbackButtonOffset) * scale, FallbackButtonSize * scale);
        }

        if (hiddenButton?.Address != (nint)node)
            hiddenButton = new HiddenButton((nint)node, node->IsVisible());
        if (node->IsVisible())
            node->ToggleVisibility(false);

        // Hidden nodes stop refreshing ScreenX/Y; the button is a direct child of the root,
        // so its local position is relative to the window.
        return (origin + new Vector2(node->X, node->Y) * scale, new Vector2(node->Width, node->Height) * scale);
    }

    /// <summary>Native animations can reset geometry after our update; reapply before drawing.</summary>
    private void OnAddonRefresh(AddonEvent type, AddonArgs args)
    {
        if (IsTransitioning(condition))
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (args.AddonName == WindowAddon)
        {
            windowFrame?.Apply(addon, ExtraHeight);
            if (hiddenButton is { } button)
            {
                var node = addon->GetNodeById(CommunityFinderButtonNodeId);
                if ((nint)node == button.Address && node->IsVisible())
                    node->ToggleVisibility(false);
            }
        }
        else
        {
            // Visual only: lists themselves are never resized from a draw callback.
            FindTabList(args.AddonName)?.RefreshFooter(addon);
        }
    }

    /// <summary>A set-up or finalized addon has fresh (or freed) nodes: forget, never restore through them.</summary>
    private void OnAddonReset(AddonEvent type, AddonArgs args)
    {
        if (args.AddonName == WindowAddon)
        {
            windowFrame = null;
            hiddenButton = null;
        }
        else
        {
            FindTabList(args.AddonName)?.Forget();
        }
    }

    private TabList? FindTabList(string addonName) => tabLists.FirstOrDefault(t => t.AddonName == addonName);

    private AtkUnitBase* GetAddon(string name) => (AtkUnitBase*)gameGui.GetAddonByName(name).Address;

    private static string DescribeAddon(AtkUnitBase* addon) => addon == null
        ? "absent"
        : $"{(nint)addon:X} visible={addon->IsVisible} ready={addon->IsReady} " +
          $"loaded={addon->UldManager.LoadedState} windowNode={(nint)addon->WindowNode:X}";

    private readonly record struct HiddenButton(nint Address, bool WasVisible);
}
