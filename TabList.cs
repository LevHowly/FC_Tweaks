using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FC_Tweaks;

/// <summary>A native list on one FC tab that should receive the window's extra height.</summary>
/// <param name="Frame">The tab addon and the nodes below the list that must move down with it.</param>
internal sealed record TabListSpec(uint ListNodeId, FrameSpec Frame);

/// <summary>
/// Grows one tab's list by the window's extra height, then moves that tab's footer by however much
/// the list actually grew.
/// <para>
/// The list is only resized from the framework update (never draw callbacks or close/finalize) and
/// not while it has a native update pending. Its row count is never set directly: native
/// <c>SetSize</c> resizes the list's owner node and lets the game recalculate the visible rows.
/// It never updates <c>ListWidth</c>/<c>ListHeight</c>, so the owner node is what gets measured.
/// </para>
/// </summary>
internal sealed unsafe class TabList(TabListSpec spec, IPluginLog log)
{
    private AddonFrame? frame;
    private Baseline? baseline;

    public string AddonName => spec.Frame.Name;

    public void Update(AtkUnitBase* addon, int extraHeight)
    {
        if (frame is not null && frame.Address != (nint)addon)
            Forget();

        var list = addon->GetComponentListById(spec.ListNodeId);
        if (list == null)
            return;
        if (baseline is not null && baseline.Component != (nint)list)
        {
            log.Warning("FC Tweaks: the {Addon} list component changed; recapturing its layout.", AddonName);
            Forget();
        }

        if (baseline is null)
        {
            var owner = list->OwnerNode;
            if (owner == null || owner->Width == 0 || owner->Height == 0)
                return;
            frame = AddonFrame.TryCapture(addon, spec.Frame, log);
            if (frame is null)
                return;
            baseline = new Baseline((nint)list, owner->Width, owner->Height);
        }

        if (!list->IsUpdatePending)
            Resize(list, baseline.Height + extraHeight);
        RefreshFooter(addon);
    }

    /// <summary>Visual only, safe from draw callbacks: the footer follows the list's real height.</summary>
    public void RefreshFooter(AtkUnitBase* addon)
    {
        var list = GetList(addon);
        frame?.Apply(addon, list == null || list->OwnerNode == null
            ? 0
            : Math.Max(0, list->OwnerNode->Height - baseline!.Height));
    }

    public void Restore(AtkUnitBase* addon)
    {
        var list = GetList(addon);
        if (list != null)
            Resize(list, baseline!.Height);
        frame?.Apply(addon, 0);
    }

    /// <summary>The addon was set up again or destroyed: forget it, never restore through it.</summary>
    public void Forget()
    {
        frame = null;
        baseline = null;
    }

    public string Describe(AtkUnitBase* addon)
    {
        var list = GetList(addon);
        var current = list == null || list->OwnerNode == null
            ? "gone"
            : $"{list->OwnerNode->Width}x{list->OwnerNode->Height} rows {list->VisibleRowCount}";
        return $"{frame?.Describe(addon) ?? "not captured"}, list " +
               (baseline is null ? "not captured" : $"original {baseline.Width}x{baseline.Height}, current {current}");
    }

    private void Resize(AtkComponentList* list, int height)
    {
        var owner = list->OwnerNode;
        if (owner != null && (owner->Width != baseline!.Width || owner->Height != height))
            list->SetSize((ushort)baseline.Width, (ushort)height);
    }

    private AtkComponentList* GetList(AtkUnitBase* addon)
    {
        if (baseline is null || addon == null || frame?.Address != (nint)addon)
            return null;
        var list = addon->GetComponentListById(spec.ListNodeId);
        return (nint)list == baseline.Component ? list : null;
    }

    private sealed record Baseline(nint Component, int Width, int Height);
}
