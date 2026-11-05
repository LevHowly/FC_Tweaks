using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FC_Tweaks;

internal sealed unsafe class FreeCompanyWindowResizer
{
    private const string AddonName = "FreeCompanyMember";
    private const uint MemberListNodeId = 24;
    private const int SearchIntervalFrames = 30;

    private readonly IGameGui gameGui;
    private AtkUnitBase* addon;
    private AtkComponentList* memberList;
    private Layout? originalLayout;
    private bool isExpanded;
    private int framesUntilNextSearch;

    public FreeCompanyWindowResizer(IGameGui gameGui)
    {
        this.gameGui = gameGui;
    }

    public bool IsExpanded => isExpanded;
    public bool CanExpand => memberList != null && originalLayout is not null;
    public bool IsFreeCompanyVisible => addon != null && addon->IsVisible;

    public void Update()
    {
        var currentAddon = gameGui.GetAddonByName<AtkUnitBase>(AddonName);
        if (currentAddon != addon)
        {
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
        if (addon != null && memberList != null && originalLayout is not null)
            RestoreLayout();

        isExpanded = false;
    }

    private void CaptureLayout()
    {
        var list = addon->GetComponentListById(MemberListNodeId);
        if (list == null || !list->IsVerticalScroll || list->VisibleRowCount <= 0 || list->ItemHeight <= 0)
            return;

        ushort windowWidth = 0;
        ushort windowHeight = 0;
        addon->GetSize(&windowWidth, &windowHeight, false);

        memberList = list;
        originalLayout = new Layout(
            windowWidth,
            windowHeight,
            list->ListWidth,
            list->ListHeight,
            list->VisibleRowCount);
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
        var expandedWindowWidth = original.WindowWidth;
        var expandedWindowHeight = ToUShort(original.WindowHeight * 2);

        ushort currentWindowWidth = 0;
        ushort currentWindowHeight = 0;
        addon->GetSize(&currentWindowWidth, &currentWindowHeight, false);
        if (currentWindowWidth != expandedWindowWidth || currentWindowHeight != expandedWindowHeight)
            addon->SetSize(expandedWindowWidth, expandedWindowHeight);

        if (memberList->VisibleRowCount != expandedRowCount
            || memberList->ListWidth != expandedListWidth
            || memberList->ListHeight != expandedListHeight)
        {
            memberList->SetVisibleRowCount(expandedRowCount);
            memberList->SetSize(expandedListWidth, expandedListHeight);
        }
    }

    private void RestoreLayout()
    {
        if (addon == null || memberList == null || originalLayout is not { } original)
            return;

        ushort currentWindowWidth = 0;
        ushort currentWindowHeight = 0;
        addon->GetSize(&currentWindowWidth, &currentWindowHeight, false);
        if (currentWindowWidth != original.WindowWidth || currentWindowHeight != original.WindowHeight)
            addon->SetSize(original.WindowWidth, original.WindowHeight);

        if (memberList->VisibleRowCount != original.RowCount
            || memberList->ListWidth != original.ListWidth
            || memberList->ListHeight != original.ListHeight)
        {
            memberList->SetVisibleRowCount(original.RowCount);
            memberList->SetSize(ToUShort(original.ListWidth), ToUShort(original.ListHeight));
        }
    }

    private static ushort ToUShort(int value) => (ushort)Math.Clamp(value, 1, ushort.MaxValue);

    private sealed record Layout(
        ushort WindowWidth,
        ushort WindowHeight,
        short ListWidth,
        short ListHeight,
        short RowCount);
}
