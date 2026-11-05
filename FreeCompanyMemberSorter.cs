using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using static FC_Tweaks.FreeCompanyUi;

namespace FC_Tweaks;

internal sealed unsafe class FreeCompanyMemberSorter : IDisposable
{
    private const string SwapSignature =
        "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 41 54 41 55 41 56 41 57 48 83 EC 30 48 8B 42 28 4C 8B EA";
    private const string RankSignature =
        "80 FA 10 73 12 0F B6 C2 48 6B D0 58 48 8D 81 ?? ?? ?? ?? 48 03 C2 C3 33 C0 C3";

    private readonly IGameGui gameGui;
    private readonly ICondition condition;
    private readonly IAddonLifecycle lifecycle;
    private readonly IPluginLog log;
    private readonly nint swapAddress;
    private readonly delegate* unmanaged<InfoProxyFreeCompany*, byte, InfoProxyFreeCompany.RankData*> getRank;
    private readonly string gobletName;
    private readonly HeaderButton[] buttons =
    [
        new(NameHeaderNodeId, RosterSortMode.Name, "Name: A to Z (online members only)"),
        new(LocationHeaderNodeId, RosterSortMode.Location, "Location: The Goblet first, then A to Z (online members only)"),
        new(RankHeaderNodeId, RosterSortMode.Rank, "Rank: highest to lowest (online members only)"),
    ];
    private SortRequest? pending;
    private nint visibleAddon;
    private byte visiblePage;
    private bool disabled;
    private readonly RosterSortSession sortSession = new();
    private nint observedProxy;
    private nint observedData;
    private uint observedCount;
    private int observedListLength;
    private byte observedPage;
    private long nextSortCheck;
    private string? lastWaitReason;

    public FreeCompanyMemberSorter(IGameGui gameGui, ICondition condition, IAddonLifecycle lifecycle,
        IPluginLog log, ISigScanner scanner, IDataManager dataManager)
    {
        this.gameGui = gameGui;
        this.condition = condition;
        this.lifecycle = lifecycle;
        this.log = log;
        if (!scanner.TryScanText(SwapSignature, out swapAddress)
            || !scanner.TryScanText(RankSignature, out var rankAddress))
        {
            Disable("The native FC sorting functions could not be verified for this game version.");
        }
        else
        {
            // The SDK's Ranks field has an outdated offset; use the game's checked accessor.
            getRank = (delegate* unmanaged<InfoProxyFreeCompany*, byte, InfoProxyFreeCompany.RankData*>)rankAddress;
        }

        gobletName = dataManager.GetExcelSheet<TerritoryType>().GetRow(341).PlaceName.Value.Name.ToString();
        if (string.IsNullOrWhiteSpace(gobletName))
            Disable("The localized name of The Goblet could not be loaded.");

        lifecycle.RegisterListener(AddonEvent.PreClose, MembersAddon, OnClosing);
        lifecycle.RegisterListener(AddonEvent.PreFinalize, MembersAddon, OnClosing);
        lifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, MembersAddon, OnRosterUpdated);
    }

    public void Dispose()
    {
        lifecycle.UnregisterListener(AddonEvent.PreClose, MembersAddon, OnClosing);
        lifecycle.UnregisterListener(AddonEvent.PreFinalize, MembersAddon, OnClosing);
        lifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, MembersAddon, OnRosterUpdated);
        sortSession.Close();
        Clear();
    }

    public void Update()
    {
        if (condition[ConditionFlag.LoggingOut])
        {
            sortSession.Close();
            Clear();
            return;
        }

        // Zoning keeps the chosen sort if the window stays open; logging out (above) forgets it.
        if (IsTransitioning(condition))
        {
            Clear();
            return;
        }

        var addon = gameGui.GetAddonByName<AtkUnitBase>(MembersAddon);
        var agent = AgentFreeCompany.Instance();
        if (addon == null || !addon->IsReady || !addon->IsVisible || agent == null)
        {
            sortSession.Close();
            Clear();
            return;
        }

        if (visibleAddon != 0 && visibleAddon != (nint)addon)
            sortSession.Close();
        ObserveRoster(addon, agent);

        var request = pending;
        pending = null;
        if (request is { } queued)
        {
            if (queued.Addon == (nint)addon && queued.Page == agent->CurrentMemberPageIndex)
            {
                sortSession.Mode = queued.Mode;
                nextSortCheck = 0;
                lastWaitReason = null;
            }
            else
            {
                log.Warning("FC Tweaks sorting cancelled because the roster window or page changed.");
            }
        }

        if (!disabled && sortSession.Mode is { } mode && sortSession.CanSort(Environment.TickCount64)
            && Environment.TickCount64 >= nextSortCheck)
        {
            nextSortCheck = Environment.TickCount64 + 250;
            var contextMenu = gameGui.GetAddonByName<AtkUnitBase>("ContextMenu");
            var list = addon->GetComponentListById(MemberListNodeId);
            if ((contextMenu == null || !contextMenu->IsVisible)
                && list != null && list->HeldItemIndex < 0)
                Sort(addon, agent, mode);
        }

        visibleAddon = (nint)addon;
        visiblePage = agent->CurrentMemberPageIndex;
        var scale = addon->GetScale();
        foreach (var button in buttons)
        {
            var node = addon->GetTextNodeById(button.NodeId);
            button.Visible = node != null && node->IsVisible();
            if (!button.Visible)
                continue;

            ushort width = 0;
            ushort height = 0;
            node->GetTextDrawSize(&width, &height);
            button.Position = new Vector2(node->ScreenX + (width + 4) * scale, node->ScreenY);
        }
    }

    public void Draw()
    {
        if (visibleAddon == 0)
            return;

        foreach (var button in buttons)
        {
            if (!button.Visible)
                continue;

            ImGui.SetNextWindowPos(button.Position);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 1));
            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
                        | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
                        | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize;
            if (ImGui.Begin($"FC Sort {button.Mode}", flags))
            {
                ImGui.BeginDisabled(disabled);
                if (ImGui.SmallButton($"Sort##FC{button.Mode}"))
                    pending = new SortRequest(visibleAddon, visiblePage, button.Mode);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(disabled ? "Sorting is unavailable; see the Dalamud log."
                        : button.Tooltip + (sortSession.Mode == button.Mode
                            ? "\nActive; waits for one second without roster updates. Cleared on close." : ""));
            }
            ImGui.End();
            ImGui.PopStyleVar(2);
        }
    }

    private void Sort(AtkUnitBase* addon, AgentFreeCompany* agent, RosterSortMode mode)
    {
        var proxy = agent->InfoProxyFreeCompanyMember;
        var company = agent->InfoProxyFreeCompany;
        var stage = AtkStage.Instance();
        var list = addon->GetComponentListById(MemberListNodeId);
        if (proxy == null || company == null || stage == null || list == null
            || proxy->CharData == null || proxy->EntryCount == 0 || proxy->EntryCount > 200
            || proxy->EntryCount != list->ListLength)
        {
            WaitForRoster("the roster page is not fully loaded");
            return;
        }

        if ((*(nint**)proxy)[30] != swapAddress)
        {
            Disable("The FC member row-swap handler does not match the verified native function.");
            return;
        }

        var numbers = stage->GetNumberArrayData(NumberArrayType.FreeCompanyMember);
        var strings = stage->GetStringArrayData(StringArrayType.FreeCompanyMember);
        if (numbers == null || strings == null || numbers->IntArray == null || strings->StringArray == null
            || numbers->Size != 2806 || strings->Size != 1401)
        {
            Disable("The FC member UI arrays have an unsupported layout.");
            return;
        }

        // Let the addon consume pending native data before moving rows again.
        if (numbers->UpdateState != 0 || strings->UpdateState != 0)
        {
            WaitForRoster("the native roster update is still pending");
            return;
        }

        var members = new List<RosterMember>((int)proxy->EntryCount);
        var ids = new HashSet<ulong>();
        for (var i = 0; i < proxy->EntryCount; i++)
        {
            var entry = proxy->CharData[i];
            if (entry.ContentId == 0 || !ids.Add(entry.ContentId))
            {
                WaitForRoster("the roster contains incomplete or duplicate member data");
                return;
            }

            var locationPointer = strings->StringArray[i * 5 + 1].Value;
            if (locationPointer == null)
            {
                WaitForRoster("a member's location text is not loaded");
                return;
            }

            var location = SeString.Parse(locationPointer).TextValue;
            var rankOrder = 0;
            if (mode == RosterSortMode.Rank && RosterMember.IsOnlineState((ulong)entry.State))
            {
                var rankId = (byte)((entry.ExtraFlags >> 12) & 0xFF);
                var rank = getRank(company, rankId);
                if (rank == null || rank->Name[0] == 0 || rank->RankNumber >= 16)
                {
                    WaitForRoster("a member's FC rank hierarchy is not loaded");
                    return;
                }
                rankOrder = rank->RankNumber;
            }

            members.Add(new RosterMember(entry.ContentId, (ulong)entry.State, entry.NameString, location,
                string.Equals(location, gobletName, StringComparison.CurrentCultureIgnoreCase), rankOrder));
        }

        var order = RosterSort.CreateOrder(members, mode);
        lastWaitReason = null;
        var swaps = RosterSort.GetSwaps(order).ToArray();
        if (swaps.Length == 0)
            return;

        // Match native sorting: move the entire entry, then swap its UI data (including FC memo indexes).
        var swap = (delegate* unmanaged<InfoProxyFreeCompanyMember*, NumberArrayData*, StringArrayData*, int, int, byte>)swapAddress;
        var selected = list->SelectedItemIndex;
        foreach (var (first, second) in swaps)
        {
            var temporary = proxy->CharData[first];
            proxy->CharData[first] = proxy->CharData[second];
            proxy->CharData[second] = temporary;
            swap(proxy, numbers, strings, first, second);
        }

        if (selected >= 0 && selected < order.Length)
            list->SelectItem(Array.IndexOf(order, selected));
        numbers->UpdateState = 1;
        strings->UpdateState = 1;
    }

    private void OnClosing(AddonEvent type, AddonArgs args)
    {
        sortSession.Close();
        Clear();
    }

    private void OnRosterUpdated(AddonEvent type, AddonArgs args)
        => sortSession.MarkUpdated(Environment.TickCount64);

    private void ObserveRoster(AtkUnitBase* addon, AgentFreeCompany* agent)
    {
        var proxy = agent->InfoProxyFreeCompanyMember;
        var list = addon->GetComponentListById(MemberListNodeId);
        var data = proxy == null ? 0 : (nint)proxy->CharData;
        var count = proxy == null ? 0 : proxy->EntryCount;
        var length = list == null ? 0 : list->ListLength;
        if (visibleAddon != (nint)addon || observedProxy != (nint)proxy || observedData != data
            || observedCount != count || observedListLength != length
            || observedPage != agent->CurrentMemberPageIndex)
            sortSession.MarkUpdated(Environment.TickCount64);
        observedProxy = (nint)proxy;
        observedData = data;
        observedCount = count;
        observedListLength = length;
        observedPage = agent->CurrentMemberPageIndex;
    }

    private void Clear()
    {
        pending = null;
        nextSortCheck = 0;
        lastWaitReason = null;
        visibleAddon = 0;
        foreach (var button in buttons)
            button.Visible = false;
    }

    private void WaitForRoster(string reason)
    {
        if (lastWaitReason != reason)
            log.Debug("FC Tweaks sorting deferred: {Reason}. It will retry when the roster is ready.", reason);
        lastWaitReason = reason;
    }

    private void Disable(string reason)
    {
        disabled = true;
        log.Error($"FC Tweaks member sorting disabled: {reason}");
    }

    private readonly record struct SortRequest(nint Addon, byte Page, RosterSortMode Mode);

    private sealed class HeaderButton(uint nodeId, RosterSortMode mode, string tooltip)
    {
        public uint NodeId { get; } = nodeId;
        public RosterSortMode Mode { get; } = mode;
        public string Tooltip { get; } = tooltip;
        public bool Visible { get; set; }
        public Vector2 Position { get; set; }
    }
}
