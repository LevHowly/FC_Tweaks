using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
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
    private AtkUnitBase* addon;
    private AtkComponentList* memberList;
    private Layout? originalLayout;
    private bool isExpanded;
    private int framesUntilNextSearch;
    private WindowLayout? memberWindow;
    private WindowLayout? parentWindow;

    public FreeCompanyWindowResizer(IGameGui gameGui, IAddonLifecycle addonLifecycle, IPluginLog log)
    {
        this.gameGui = gameGui;
        this.addonLifecycle = addonLifecycle;
        this.log = log;
        foreach (var name in new[] { AddonName, ParentAddonName })
        {
            addonLifecycle.RegisterListener(AddonEvent.PreClose, name, OnClosing);
            addonLifecycle.RegisterListener(AddonEvent.PreFinalize, name, OnClosing);
            addonLifecycle.RegisterListener(AddonEvent.PreDraw, name, OnDrawing);
        }
    }

    public bool IsExpanded => isExpanded;
    public bool CanExpand => memberList != null && originalLayout is not null;
    public bool IsFreeCompanyVisible => addon != null && addon->IsVisible;

    public void Update()
    {
        var currentAddon = gameGui.GetAddonByName<AtkUnitBase>(AddonName);
        if (currentAddon != addon)
        {
            Restore();
            addon = currentAddon;
            memberList = null;
            originalLayout = null;
            isExpanded = false;
            framesUntilNextSearch = 0;
        }

        if (addon == null)
            return;

        if (!addon->IsVisible)
        {
            if (isExpanded)
            {
                RestoreLayout();
                isExpanded = false;
            }

            return;
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

        if (isExpanded)
            ApplyExpandedLayout();
    }

    public void ToggleExpansion()
    {
        if (!CanExpand || addon == null || !addon->IsVisible)
            return;

        isExpanded = !isExpanded;
        if (isExpanded)
            ApplyExpandedLayout();
        else
            RestoreLayout();
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
        if (addon != null && gameGui.GetAddonByName<AtkUnitBase>(AddonName) == addon
            && memberList != null && originalLayout is not null)
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
            addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, name, OnClosing);
            addonLifecycle.UnregisterListener(AddonEvent.PreDraw, name, OnDrawing);
        }
    }

    private void OnDrawing(AddonEvent type, AddonArgs args)
    {
        if (isExpanded && IsFreeCompanyVisible)
            ApplyExpandedLayout();
    }

    private void OnClosing(AddonEvent type, AddonArgs args)
    {
        Restore();
        memberList = null;
        originalLayout = null;
        memberWindow = null;
        parentWindow = null;
        framesUntilNextSearch = 0;
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
        if (window == null || (nint)window != layout.Address)
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
