using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FC_Tweaks;

internal sealed unsafe class FreeCompanyWindowResizer : IDisposable
{
    private const string AddonName = "FreeCompanyMember";
    private const string ParentAddonName = "FreeCompany";
    private const uint MemberListNodeId = 24;
    private const int SearchIntervalFrames = 30;

    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPluginLog log;
    private readonly ICondition condition;
    private AtkUnitBase* addon;
    private AtkComponentList* memberList;
    private Layout? originalLayout;
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private int framesUntilNextSearch;
    private WindowLayout? memberWindow;
    private WindowLayout? parentWindow;
    private bool toggleRequested;
    private int extraHeight = 252;
    private nint replacedButton;
    private bool buttonWasVisible;
    private (float X, float Y) buttonPosition;
    private long memberReadyAt;
    private int lastMemberCount = -1;

    public FreeCompanyWindowResizer(IGameGui gameGui, IAddonLifecycle addonLifecycle, IPluginLog log,
        ICondition condition, Configuration configuration, Action saveConfiguration)
    {
        this.gameGui = gameGui;
        this.addonLifecycle = addonLifecycle;
        this.log = log;
        this.condition = condition;
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        foreach (var name in new[] { AddonName, ParentAddonName })
        {
            addonLifecycle.RegisterListener(AddonEvent.PreClose, name, OnClosing);
            addonLifecycle.RegisterListener(AddonEvent.PreFinalize, name, OnFinalizing);
            addonLifecycle.RegisterListener(AddonEvent.PostUpdate, name, OnUpdated);
            addonLifecycle.RegisterListener(AddonEvent.PreDraw, name, OnUpdated);
        }
        addonLifecycle.RegisterListener(AddonEvent.PreSetup, AddonName, OnFinalizing);
    }

    public bool IsExpanded => configuration.Expanded;
    public bool CanExpand => parentWindow is not null && replacedButton > 0;
    private bool IsTransitioning => condition[ConditionFlag.BetweenAreas]
                                    || condition[ConditionFlag.BetweenAreas51]
                                    || condition[ConditionFlag.LoggingOut];

    public bool IsFreeCompanyVisible
    {
        get
        {
            if (IsTransitioning)
                return false;
            var parent = gameGui.GetAddonByName<AtkUnitBase>(ParentAddonName);
            return parent != null && parent->IsVisible && parent->RootNode != null
                && parent->WindowNode != null;
        }
    }

    public void Update()
    {
        if (IsTransitioning)
        {
            toggleRequested = false;
            return;
        }

        RefreshWindowLayout(ref parentWindow, ParentAddonName);
        if (!IsFreeCompanyVisible)
        {
            toggleRequested = false;
            return;
        }

        ReplaceCommunityFinder();
        if (toggleRequested)
        {
            toggleRequested = false;
            configuration.Expanded = !configuration.Expanded;
            saveConfiguration();
        }

        ApplyWindowHeight(parentWindow, IsExpanded ? extraHeight : 0);

        var currentAddon = gameGui.GetAddonByName<AtkUnitBase>(AddonName);
        if (currentAddon != addon)
        {
            // The previous addon may already be freed. Never restore through it.
            ForgetMemberLayout();
            addon = currentAddon;
        }

        if (addon == null || !addon->IsReady)
        {
            toggleRequested = false;
            return;
        }

        if (!addon->IsVisible)
            return;

        if (memberList != null && addon->GetComponentListById(MemberListNodeId) != memberList)
        {
            log.Warning("FC Tweaks roster component changed; discarding its old layout and recapturing.");
            ForgetMemberLayout();
            addon = currentAddon;
        }

        if (memberList == null && framesUntilNextSearch == 0)
        {
            CaptureLayout();
            framesUntilNextSearch = SearchIntervalFrames;
        }
        else if (framesUntilNextSearch > 0)
        {
            framesUntilNextSearch--;
        }

        if (memberList != null)
        {
            RefreshWindowLayout(ref memberWindow, AddonName);
            if (lastMemberCount != memberList->ListLength)
            {
                lastMemberCount = memberList->ListLength;
                memberReadyAt = Environment.TickCount64 + 1000;
            }
            if (Environment.TickCount64 < memberReadyAt || memberList->IsUpdatePending)
                return;
        }

        if (IsExpanded)
            ApplyExpandedLayout();
        else
            RestoreLayout();
    }

    public void ToggleExpansion()
    {
        // ImGui runs during Present: native list allocation must wait until Update.
        toggleRequested = !toggleRequested;
    }

    public (float X, float Y) GetButtonPosition() => buttonPosition;

    public void Restore()
    {
        toggleRequested = false;
        if (IsTransitioning)
            return;

        if (addon != null && gameGui.GetAddonByName<AtkUnitBase>(AddonName) == addon
            && addon->IsReady && addon->IsVisible && memberList != null && originalLayout is not null
            && addon->GetComponentListById(MemberListNodeId) == memberList)
            RestoreLayout();

        ApplyWindowHeight(memberWindow, 0);
        ApplyWindowHeight(parentWindow, 0);
        RestoreCommunityFinder();
    }

    public void Dispose()
    {
        Restore();
        addonLifecycle.UnregisterListener(AddonEvent.PreSetup, AddonName, OnFinalizing);
        foreach (var name in new[] { AddonName, ParentAddonName })
        {
            addonLifecycle.UnregisterListener(AddonEvent.PreClose, name, OnClosing);
            addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, name, OnFinalizing);
            addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, name, OnUpdated);
            addonLifecycle.UnregisterListener(AddonEvent.PreDraw, name, OnUpdated);
        }
    }

    private void OnUpdated(AddonEvent type, AddonArgs args)
    {
        // Native animations can reset geometry after Update. Correct it before Draw,
        // but never allocate or resize list rows from a draw callback.
        if (!IsFreeCompanyVisible)
            return;

        ReplaceCommunityFinder();
        var delta = IsExpanded ? extraHeight : 0;
        ApplyWindowHeight(parentWindow, delta);
        if (addon != null && gameGui.GetAddonByName<AtkUnitBase>(AddonName) == addon
            && addon->IsReady && addon->IsVisible
            && addon->GetComponentListById(MemberListNodeId) == memberList)
            ApplyWindowHeight(memberWindow, delta);
    }

    private void OnClosing(AddonEvent type, AddonArgs args)
    {
        // Tab close is not a request to collapse the global FC window.
        toggleRequested = false;
    }

    private void OnFinalizing(AddonEvent type, AddonArgs args)
    {
        ForgetMemberLayout();
        if (args.AddonName == ParentAddonName)
        {
            parentWindow = null;
            replacedButton = 0;
        }
        toggleRequested = false;
    }

    private void ForgetMemberLayout()
    {
        addon = null;
        memberList = null;
        originalLayout = null;
        memberWindow = null;
        framesUntilNextSearch = 0;
        memberReadyAt = Environment.TickCount64 + 1000;
        lastMemberCount = -1;
    }

    private void CaptureLayout()
    {
        var list = addon->GetComponentListById(MemberListNodeId);
        if (list == null || !list->IsVerticalScroll || list->VisibleRowCount <= 0 || list->ItemHeight <= 0)
            return;

        memberList = list;
        originalLayout = new Layout(
            list->ListWidth,
            list->ListHeight,
            list->VisibleRowCount);
        memberWindow = CaptureWindow(AddonName);
        var rowStep = list->RowStepY > 0 ? list->RowStepY : list->ItemHeight;
        extraHeight = originalLayout.RowCount * rowStep;
        if (parentWindow is null)
            log.Warning("FC Tweaks could not capture the Free Company background window; its frame cannot be expanded.");
    }

    private void ApplyExpandedLayout()
    {
        if (addon == null || memberList == null || originalLayout is not { } original)
            return;

        var expandedListHeight = ToUShort(original.ListHeight + extraHeight);
        var expandedListWidth = ToUShort(original.ListWidth);
        if (memberList->ListWidth != expandedListWidth
            || memberList->ListHeight != expandedListHeight)
        {
            // SetSize recalculates the visible rows itself. Calling the row-count
            // setter first traverses stale renderers when a tab has been rebuilt.
            memberList->SetSize(expandedListWidth, expandedListHeight);
        }

        ApplyWindowHeight(memberWindow, extraHeight);
        ApplyWindowHeight(parentWindow, extraHeight);
    }

    private void RestoreLayout()
    {
        if (addon == null || memberList == null || originalLayout is not { } original)
            return;

        if (memberList->ListWidth != original.ListWidth
            || memberList->ListHeight != original.ListHeight)
        {
            memberList->SetSize(ToUShort(original.ListWidth), ToUShort(original.ListHeight));
        }

        ApplyWindowHeight(memberWindow, 0);
    }

    private void ReplaceCommunityFinder()
    {
        var parent = gameGui.GetAddonByName<AtkUnitBase>(ParentAddonName);
        if (parent == null || parentWindow is null || (nint)parent != parentWindow.Address)
            return;
        // freecompany.uld node 2 is the top-right Community Finder button.
        var button = parent->GetNodeById(2);
        if (button == null || (int)button->Type < 1000)
        {
            if (replacedButton != -1)
                log.Error("FC Tweaks cannot replace Community Finder: native button node 2 is unavailable.");
            replacedButton = -1;
            return;
        }
        if (replacedButton != (nint)button)
        {
            replacedButton = (nint)button;
            buttonWasVisible = button->IsVisible();
        }
        buttonPosition = (button->ScreenX, button->ScreenY);
        button->ToggleVisibility(false);
    }

    private void RestoreCommunityFinder()
    {
        var parent = gameGui.GetAddonByName<AtkUnitBase>(ParentAddonName);
        if (parent == null || parentWindow is null || (nint)parent != parentWindow.Address)
            return;
        var button = parent->GetNodeById(2);
        if (button != null && (nint)button == replacedButton)
            button->ToggleVisibility(buttonWasVisible);
    }

    private WindowLayout? CaptureWindow(string name)
    {
        var window = gameGui.GetAddonByName<AtkUnitBase>(name);
        if (window == null)
            return null;

        ushort width = 0;
        ushort height = 0;
        window->GetSize(&width, &height, false);
        return new WindowLayout(name, (nint)window, width, height,
            window->WindowNode == null ? null : window->WindowNode->Height,
            CaptureContentNodes(window, name));
    }

    private void RefreshWindowLayout(ref WindowLayout? layout, string name)
    {
        var window = gameGui.GetAddonByName<AtkUnitBase>(name);
        if (window == null || (layout is not null && layout.Address != (nint)window))
            layout = null;
        // The parent window may not have been ready when the roster was captured.
        layout ??= CaptureWindow(name);
    }

    private List<ContentNodeLayout> CaptureContentNodes(AtkUnitBase* window, string name)
    {
        var result = new List<ContentNodeLayout>();
        // Verified against freecompany.uld and freecompanymember.uld. Move the
        // footer parents, not their children, to avoid applying the offset twice.
        var nodeIds = name == ParentAddonName ? new uint[] { 19 } : new uint[] { 45, 49 };
        foreach (var nodeId in nodeIds)
        {
            var node = window->GetNodeById(nodeId);
            var isDivider = name == ParentAddonName;
            var expectedType = isDivider ? NodeType.NineGrid : NodeType.Res;
            if (node == null || node->Type != expectedType)
            {
                log.Warning("FC Tweaks cannot capture {Addon} layout node {NodeId}: expected {Type}. " +
                            "Its divider/footer adjustment is unavailable.", name, nodeId, expectedType);
                continue;
            }

            result.Add(new ContentNodeLayout((nint)node, nodeId, node->Y, node->Width, isDivider));
        }

        return result;
    }

    private void ApplyWindowHeight(WindowLayout? layout, int extraHeight)
    {
        if (layout is null)
            return;

        var window = gameGui.GetAddonByName<AtkUnitBase>(layout.Name);
        // Match v0.1.0: the parent frame need not have run its own OnSetup.
        // The visible roster, transition guard, and exact addon identity gate access.
        if (IsTransitioning || window == null || (nint)window != layout.Address)
            return;

        var height = ToUShort(layout.Height + extraHeight);
        ushort currentWidth = 0;
        ushort currentHeight = 0;
        window->GetSize(&currentWidth, &currentHeight, false);
        var changed = currentWidth != layout.Width || currentHeight != height;
        // The frame must be sized before SetSize updates the addon's native layout.
        if (window->WindowNode != null && layout.FrameHeight is { } frameHeight)
        {
            var targetFrameHeight = ToUShort(frameHeight + extraHeight);
            if (window->WindowNode->Height != targetFrameHeight)
            {
                window->WindowNode->SetHeight(targetFrameHeight);
                changed = true;
            }
        }

        if (changed)
            window->SetSize(layout.Width, height);

        foreach (var content in layout.ContentNodes)
        {
            var node = window->GetNodeById(content.NodeId);
            if (node == null || (nint)node != content.Address)
                continue;

            // The -90 degree divider is anchored at its bottom. Lengthen its
            // local width and lower its origin together to keep the top fixed.
            if (content.IsRotatedDivider)
            {
                var targetWidth = ToUShort(content.Width + extraHeight);
                if (node->Width != targetWidth)
                {
                    node->SetWidth(targetWidth);
                    changed = true;
                }
            }

            var targetY = content.Y + extraHeight;
            if (node->Y != targetY)
            {
                node->SetYFloat(targetY);
                changed = true;
            }
        }

        if (changed)
        {
            if (window->WindowNode != null && window->WindowNode->Component != null)
                window->WindowNode->Component->UldManager.UpdateDrawNodeList();
            window->UpdateCollisionNodeList(false);
        }
    }

    private static ushort ToUShort(int value) => (ushort)Math.Clamp(value, 1, ushort.MaxValue);

    private sealed record ContentNodeLayout(nint Address, uint NodeId, float Y, ushort Width, bool IsRotatedDivider);

    private sealed record WindowLayout(string Name, nint Address, ushort Width, ushort Height,
        ushort? FrameHeight, List<ContentNodeLayout> ContentNodes);

    private sealed record Layout(
        short ListWidth,
        short ListHeight,
        short RowCount);
}
