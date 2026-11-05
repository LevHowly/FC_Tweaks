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
    private bool isExpanded;
    private int framesUntilNextSearch;
    private WindowLayout? memberWindow;
    private WindowLayout? parentWindow;
    private bool toggleRequested;
    private bool restorePending;

    public FreeCompanyWindowResizer(IGameGui gameGui, IAddonLifecycle addonLifecycle, IPluginLog log,
        ICondition condition)
    {
        this.gameGui = gameGui;
        this.addonLifecycle = addonLifecycle;
        this.log = log;
        this.condition = condition;
        foreach (var name in new[] { AddonName, ParentAddonName })
        {
            addonLifecycle.RegisterListener(AddonEvent.PreClose, name, OnClosing);
            addonLifecycle.RegisterListener(AddonEvent.PreFinalize, name, OnFinalizing);
            addonLifecycle.RegisterListener(AddonEvent.PostUpdate, name, OnUpdated);
            addonLifecycle.RegisterListener(AddonEvent.PreDraw, name, OnUpdated);
        }
    }

    public bool IsExpanded => isExpanded;
    public bool CanExpand => memberList != null && originalLayout is not null;
    private bool IsTransitioning => condition[ConditionFlag.BetweenAreas]
                                    || condition[ConditionFlag.BetweenAreas51]
                                    || condition[ConditionFlag.LoggingOut];

    public bool IsFreeCompanyVisible => !IsTransitioning && addon != null
        && gameGui.GetAddonByName<AtkUnitBase>(AddonName) == addon
        && addon->IsReady && addon->IsVisible;

    public void Update()
    {
        if (IsTransitioning)
        {
            QueueRestore();
            return;
        }

        var currentAddon = gameGui.GetAddonByName<AtkUnitBase>(AddonName);
        if (currentAddon != addon)
        {
            // The previous addon may already be freed. Never restore through it.
            ForgetLayout();
            addon = currentAddon;
        }

        if (addon == null || !addon->IsReady)
        {
            toggleRequested = false;
            return;
        }

        if (!addon->IsVisible)
        {
            QueueRestore();
            return;
        }

        if (memberList != null && addon->GetComponentListById(MemberListNodeId) != memberList)
        {
            log.Warning("FC Tweaks roster component changed; cancelling expansion and recapturing its layout.");
            ForgetLayout();
            addon = currentAddon;
        }

        if (restorePending)
        {
            restorePending = false;
            Restore();
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
            RefreshWindowLayout(ref parentWindow, ParentAddonName);
        }

        if (toggleRequested)
        {
            toggleRequested = false;
            if (CanExpand)
            {
                isExpanded = !isExpanded;
                if (!isExpanded)
                    RestoreLayout();
            }
        }

        if (isExpanded)
            ApplyExpandedLayout();
    }

    public void ToggleExpansion()
    {
        // ImGui runs during Present: native list allocation must wait until Update.
        toggleRequested = !toggleRequested;
    }

    public (float X, float Y) GetButtonPosition()
    {
        if (addon == null)
            return (0, 0);

        short x = 0;
        short y = 0;
        addon->GetPosition(&x, &y);
        return (x + addon->GetScaledWidth(true) - 112, y + 8);
    }

    public void Restore()
    {
        toggleRequested = false;
        if (IsTransitioning)
        {
            QueueRestore();
            return;
        }

        if (addon != null && gameGui.GetAddonByName<AtkUnitBase>(AddonName) == addon
            && addon->IsReady && memberList != null && originalLayout is not null
            && addon->GetComponentListById(MemberListNodeId) == memberList)
            RestoreLayout();

        ApplyWindowHeight(memberWindow, 0);
        ApplyWindowHeight(parentWindow, 0);
        isExpanded = false;
    }

    public void Dispose()
    {
        Restore();
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
        if (IsTransitioning || restorePending || !isExpanded || originalLayout is not { } original
            || addon == null || gameGui.GetAddonByName<AtkUnitBase>(AddonName) != addon
            || !addon->IsReady || !addon->IsVisible
            || addon->GetComponentListById(MemberListNodeId) != memberList)
            return;

        var expandedRowCount = Math.Min(memberList->ListLength, original.RowCount * 2);
        var rowStep = memberList->RowStepY > 0 ? memberList->RowStepY : memberList->ItemHeight;
        var extraHeight = Math.Max(0, expandedRowCount - original.RowCount) * rowStep;
        ApplyWindowHeight(memberWindow, extraHeight);
        ApplyWindowHeight(parentWindow, extraHeight);
    }

    private void OnClosing(AddonEvent type, AddonArgs args)
    {
        // Closing may run inside zone teardown; do not invoke any native resizing here.
        QueueRestore();
    }

    private void OnFinalizing(AddonEvent type, AddonArgs args)
    {
        // Finalization destroys nodes. Only discard managed bookkeeping.
        ForgetLayout();
    }

    private void QueueRestore()
    {
        restorePending |= isExpanded;
        isExpanded = false;
        toggleRequested = false;
    }

    private void ForgetLayout()
    {
        addon = null;
        memberList = null;
        originalLayout = null;
        memberWindow = null;
        parentWindow = null;
        framesUntilNextSearch = 0;
        isExpanded = false;
        toggleRequested = false;
        restorePending = false;
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
        parentWindow = CaptureWindow(ParentAddonName);
        if (parentWindow is null)
            log.Warning("FC Tweaks could not capture the Free Company background window; its frame cannot be expanded.");
    }

    private void ApplyExpandedLayout()
    {
        if (addon == null || memberList == null || originalLayout is not { } original)
            return;

        var expandedRowCount = (short)Math.Min(memberList->ListLength, original.RowCount * 2);
        if (expandedRowCount <= original.RowCount)
            return;

        var rowStep = memberList->RowStepY > 0 ? memberList->RowStepY : memberList->ItemHeight;
        var expandedListHeight = ToUShort(original.ListHeight + (expandedRowCount - original.RowCount) * rowStep);
        var expandedListWidth = ToUShort(original.ListWidth);
        var extraHeight = expandedListHeight - original.ListHeight;

        if (memberList->VisibleRowCount != expandedRowCount
            || memberList->ListWidth != expandedListWidth
            || memberList->ListHeight != expandedListHeight)
        {
            memberList->SetVisibleRowCount(expandedRowCount);
            memberList->SetSize(expandedListWidth, expandedListHeight);
        }

        ApplyWindowHeight(memberWindow, extraHeight);
        ApplyWindowHeight(parentWindow, extraHeight);
    }

    private void RestoreLayout()
    {
        if (addon == null || memberList == null || originalLayout is not { } original)
            return;

        if (memberList->VisibleRowCount != original.RowCount
            || memberList->ListWidth != original.ListWidth
            || memberList->ListHeight != original.ListHeight)
        {
            memberList->SetVisibleRowCount(original.RowCount);
            memberList->SetSize(ToUShort(original.ListWidth), ToUShort(original.ListHeight));
        }

        ApplyWindowHeight(memberWindow, 0);
        ApplyWindowHeight(parentWindow, 0);
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
