using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FC_Tweaks;

/// <summary>Native Free Company UI identifiers verified against freecompany.uld and freecompanymember.uld.</summary>
internal static unsafe class FreeCompanyUi
{
    /// <summary>The outer FC window with the tab bar; it owns the visible background frame.</summary>
    public const string WindowAddon = "FreeCompany";

    /// <summary>The Members tab, a separate addon drawn inside <see cref="WindowAddon"/>.</summary>
    public const string MembersAddon = "FreeCompanyMember";

    /// <summary>The Topics tab (freecompanytopics.uld).</summary>
    public const string TopicsAddon = "FreeCompanyTopics";

    /// <summary>The Activity tab (freecompanyactivity.uld).</summary>
    public const string ActivityAddon = "FreeCompanyActivity";

    public const uint MemberListNodeId = 24;
    /// <summary>"Members Online and on Home World", the bottom section of Topics.</summary>
    public const uint TopicsOnlineListNodeId = 27;
    public const uint ActivityListNodeId = 10;
    public const uint CommunityFinderButtonNodeId = 2;
    public const uint NameHeaderNodeId = 12;
    public const uint LocationHeaderNodeId = 14;
    public const uint RankHeaderNodeId = 15;

    /// <summary>No FC addon may be touched while the game is zoning or logging out.</summary>
    public static bool IsTransitioning(ICondition condition)
        => condition[ConditionFlag.BetweenAreas]
           || condition[ConditionFlag.BetweenAreas51]
           || condition[ConditionFlag.LoggingOut];

    /// <summary>
    /// Visible with its node tree loaded. The FC window's <c>IsReady</c> flag is deliberately not
    /// used: requiring it on the outer window is what stopped the background frame from growing.
    /// </summary>
    public static bool IsShown(AtkUnitBase* addon)
        => addon != null && addon->IsVisible && addon->RootNode != null
           && addon->UldManager.LoadedState == AtkLoadState.Loaded;
}
