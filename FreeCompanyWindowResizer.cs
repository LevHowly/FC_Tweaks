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
        }
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
        var listNode = memberList->OwnerNode;
        if (listNode == null)
        {
            log.Warning("FC Tweaks cannot locate the roster owner node to align the divider and footer.");
            return result;
        }

        var listBottom = addon->Y
                         + (GetLocalY((AtkResNode*)listNode) + memberList->ListHeight) * addon->GetScale();
        var footerTop = (listBottom - window->Y) / window->GetScale() - 12;

        // The addon node list excludes component internals, so moving a footer
        // component also moves its text, buttons and collision nodes together.
        for (var i = 0; i < window->UldManager.NodeListCount; i++)
        {
            var node = window->UldManager.NodeList[i];
            if (node == null || node == window->RootNode
                || IsWithin(node, (AtkResNode*)listNode)
                || IsWithin(node, (AtkResNode*)window->WindowNode))
                continue;

            var y = GetLocalY(node);
            var isDivider = name == ParentAddonName
                            && (node->Type == NodeType.Image || node->Type == NodeType.NineGrid)
                            && node->Width <= 8 && node->Height >= memberList->ListHeight / 2;
            var isFooter = y >= footerTop && y < window->GetScaledHeight(false);
            if (isDivider || isFooter)
                result.Add(new ContentNodeLayout((nint)node, node->NodeId, node->Y, node->Height, isDivider));
        }

        var selected = result.Select(node => node.Address).ToHashSet();
        result.RemoveAll(node =>
        {
            var parent = ((AtkResNode*)node.Address)->ParentNode;
            for (var depth = 0; parent != null && depth < 64; depth++, parent = parent->ParentNode)
            {
                if (selected.Contains((nint)parent))
                    return true;
            }

            return false;
        });
        log.Debug("FC Tweaks captured {Count} divider/footer nodes for {Addon}.", result.Count, name);
        if (result.Count == 0)
            log.Warning("FC Tweaks found no divider/footer layout nodes for {Addon}; these decorations will not follow expansion.", name);
        return result;
    }

    private static float GetLocalY(AtkResNode* node)
    {
        var y = node->Y;
        for (var depth = 0; node->ParentNode != null && depth < 64; depth++)
        {
            node = node->ParentNode;
            y = y * node->ScaleY + node->Y;
        }

        return y;
    }

    private static bool IsWithin(AtkResNode* node, AtkResNode* ancestor)
    {
        if (ancestor == null)
            return false;

        for (var depth = 0; node != null && depth < 64; depth++, node = node->ParentNode)
        {
            if (node == ancestor)
                return true;
        }

        return false;
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

        foreach (var content in layout.ContentNodes)
        {
            AtkResNode* node = null;
            for (var i = 0; i < window->UldManager.NodeListCount; i++)
            {
                var candidate = window->UldManager.NodeList[i];
                if (candidate != null && (nint)candidate == content.Address && candidate->NodeId == content.NodeId)
                {
                    node = candidate;
                    break;
                }
            }

            if (node == null)
                continue;

            if (content.Stretch)
            {
                var target = ToUShort(content.Height + extraHeight);
                if (node->Height == target)
                    continue;
                node->SetHeight(target);
            }
            else
            {
                var target = content.Y + extraHeight;
                if (node->Y == target)
                    continue;
                node->SetYFloat(target);
            }

            changed = true;
        }

        if (changed)
        {
            window->SetSize(layout.Width, height);
            if (window->WindowNode != null && window->WindowNode->Component != null)
                window->WindowNode->Component->UldManager.UpdateDrawNodeList();
            window->UpdateCollisionNodeList(false);
        }
    }

    private static ushort ToUShort(int value) => (ushort)Math.Clamp(value, 1, ushort.MaxValue);

    private sealed record ContentNodeLayout(nint Address, uint NodeId, float Y, ushort Height, bool Stretch);

    private sealed record WindowLayout(string Name, nint Address, ushort Width, ushort Height,
        ushort? FrameHeight, List<ContentNodeLayout> ContentNodes);

    private sealed record Layout(
        short ListWidth,
        short ListHeight,
        short RowCount);
}
