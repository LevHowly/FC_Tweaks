using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static FC_Tweaks.FreeCompanyUi;

namespace FC_Tweaks;

/// <summary>
/// Makes the Free Company window taller and gives the Members list the extra rows.
/// <para>
/// The saved <see cref="Configuration.Expanded"/> preference is the only state; every frame the
/// native windows are reconciled towards it. Toggling therefore never touches native memory.
/// </para>
/// <para>
/// Safety rules learned from crashes: the member list is only resized from the framework update,
/// never from draw callbacks or close/finalize, and never while the roster is still loading.
/// Its row count is never set directly; <c>SetSize</c> lets the game recalculate visible rows.
/// </para>
/// </summary>
internal sealed unsafe class FreeCompanyWindowResizer : IDisposable
{
    /// <summary>Used until the Members list has been seen and its row height measured.</summary>
    private const int DefaultExtraHeight = 252;
    private const long ListSettleMilliseconds = 1000;
    private static readonly Vector2 FallbackButtonOffset = new(-100, 8);
    private static readonly Vector2 FallbackButtonSize = new(64, 28);

    private static readonly FrameSpec WindowSpec = new(WindowAddon, HasFrame: true,
        [new DecorationSpec(19, NodeType.NineGrid, IsRotatedDivider: true)]);

    // Move the footer groups, not their children, so the offset is not applied twice.
    private static readonly FrameSpec MembersSpec = new(MembersAddon, HasFrame: false,
    [
        new DecorationSpec(45, NodeType.Res, IsRotatedDivider: false),
        new DecorationSpec(49, NodeType.Res, IsRotatedDivider: false),
    ]);

    private static readonly string[] Addons = [WindowAddon, MembersAddon];
    private static readonly AddonEvent[] RefreshEvents = [AddonEvent.PostUpdate, AddonEvent.PreDraw];
    private static readonly AddonEvent[] ResetEvents = [AddonEvent.PreSetup, AddonEvent.PreFinalize];

    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;

    private AddonFrame? windowFrame;
    private AddonFrame? membersFrame;
    private ListBaseline? listBaseline;
    private int settlingListLength = -1;
    private long settlingSince;
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
        foreach (var addon in Addons)
        {
            foreach (var type in ResetEvents)
                addonLifecycle.RegisterListener(type, addon, OnAddonReset);
            foreach (var type in RefreshEvents)
                addonLifecycle.RegisterListener(type, addon, OnAddonRefresh);
        }
    }

    public bool IsExpanded => configuration.Expanded;

    /// <summary>The outer window's extra height; it does not wait for the Members list.</summary>
    private int ExtraHeight => IsExpanded ? listBaseline?.ExtraHeight ?? DefaultExtraHeight : 0;

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

        var members = GetAddon(MembersAddon);
        if (IsShown(members) && members->IsReady)
            UpdateMembers(members);
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
        var members = GetAddon(MembersAddon);
        return $"expanded={IsExpanded}, extraHeight={ExtraHeight}; " +
               $"window {DescribeAddon(window)}: {windowFrame?.Describe(window) ?? "not captured"}; " +
               $"members {DescribeAddon(members)}: {membersFrame?.Describe(members) ?? "not captured"}, " +
               $"list {listBaseline?.ToString() ?? "not captured"}; " +
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

        var members = GetAddon(MembersAddon);
        var list = members != null && members->IsReady ? TryGetList(members) : null;
        if (list != null)
            ResizeList(list, listBaseline!.Width, listBaseline.Height);
        membersFrame?.Apply(members, 0);

        var window = GetAddon(WindowAddon);
        windowFrame?.Apply(window, 0);
        if (window != null && hiddenButton is { } button)
        {
            var node = window->GetNodeById(CommunityFinderButtonNodeId);
            if ((nint)node == button.Address)
                node->ToggleVisibility(button.WasVisible);
        }
    }

    private void UpdateMembers(AtkUnitBase* members)
    {
        if (membersFrame is not null && membersFrame.Address != (nint)members)
            ForgetMembers();

        var list = members->GetComponentListById(MemberListNodeId);
        if (list == null)
            return;
        if (listBaseline is not null && listBaseline.Component != (nint)list)
        {
            log.Warning("FC Tweaks: the member list component changed; recapturing its layout.");
            ForgetMembers();
        }

        if (listBaseline is null)
        {
            if (!list->IsVerticalScroll || list->VisibleRowCount <= 0 || list->ItemHeight <= 0)
                return;
            membersFrame = AddonFrame.TryCapture(members, MembersSpec, log);
            if (membersFrame is null)
                return;
            var rowStep = list->RowStepY > 0 ? list->RowStepY : list->ItemHeight;
            listBaseline = new ListBaseline((nint)list, list->ListWidth, list->ListHeight,
                list->VisibleRowCount * rowStep);
        }

        if (IsListSettled(list))
        {
            var height = listBaseline.Height + (IsExpanded ? listBaseline.ExtraHeight : 0);
            ResizeList(list, listBaseline.Width, height);
        }

        membersFrame?.Apply(members, MembersExtraHeight(members));
    }

    /// <summary>Members arrive one by one while loading; only resize after a second without changes.</summary>
    private bool IsListSettled(AtkComponentList* list)
    {
        var now = Environment.TickCount64;
        if (list->ListLength != settlingListLength)
        {
            settlingListLength = list->ListLength;
            settlingSince = now;
        }

        return now - settlingSince >= ListSettleMilliseconds && !list->IsUpdatePending;
    }

    private static void ResizeList(AtkComponentList* list, int width, int height)
    {
        if (list->ListWidth != width || list->ListHeight != height)
            list->SetSize((ushort)width, (ushort)height);
    }

    /// <summary>The Members frame follows the list's actual height, so they never disagree.</summary>
    private int MembersExtraHeight(AtkUnitBase* members)
    {
        var list = TryGetList(members);
        return list == null ? 0 : Math.Max(0, list->ListHeight - listBaseline!.Height);
    }

    private AtkComponentList* TryGetList(AtkUnitBase* members)
    {
        if (listBaseline is null || members == null || membersFrame?.Address != (nint)members)
            return null;
        var list = members->GetComponentListById(MemberListNodeId);
        return (nint)list == listBaseline.Component ? list : null;
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
            // Visual only: the list itself is never resized from a draw callback.
            membersFrame?.Apply(addon, MembersExtraHeight(addon));
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
            ForgetMembers();
        }
    }

    private void ForgetMembers()
    {
        membersFrame = null;
        listBaseline = null;
        settlingListLength = -1;
    }

    private AtkUnitBase* GetAddon(string name) => (AtkUnitBase*)gameGui.GetAddonByName(name).Address;

    private static string DescribeAddon(AtkUnitBase* addon) => addon == null
        ? "absent"
        : $"{(nint)addon:X} visible={addon->IsVisible} ready={addon->IsReady} " +
          $"loaded={addon->UldManager.LoadedState} windowNode={(nint)addon->WindowNode:X}";

    private sealed record ListBaseline(nint Component, int Width, int Height, int ExtraHeight);

    private readonly record struct HiddenButton(nint Address, bool WasVisible);
}
