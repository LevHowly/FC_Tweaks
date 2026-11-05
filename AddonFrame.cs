using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FC_Tweaks;

/// <summary>A node that must follow the bottom edge when its window grows.</summary>
/// <param name="IsRotatedDivider">
/// A -90° rotated NineGrid line anchored at its bottom: it is lengthened (local width) and
/// lowered together so its top stays fixed. Other nodes are only moved down.
/// </param>
internal readonly record struct DecorationSpec(uint NodeId, NodeType Type, bool IsRotatedDivider);

/// <summary>Which parts of a window are resized.</summary>
/// <param name="HasFrame">Whether the addon draws its own window frame (WindowNode).</param>
internal sealed record FrameSpec(string Name, bool HasFrame, DecorationSpec[] Decorations);

/// <summary>
/// The original geometry of one native window instance, and the only code that resizes it.
/// Bound to a single addon address; discard it when that addon is set up again or finalized.
/// </summary>
internal sealed unsafe class AddonFrame
{
    private readonly FrameSpec spec;
    private readonly ushort width;
    private readonly ushort height;
    private readonly Decoration?[] decorations;
    private readonly IPluginLog log;
    private ushort? frameHeight;
    private bool reportedMissingParts;

    private AddonFrame(AtkUnitBase* addon, FrameSpec spec, ushort width, ushort height, IPluginLog log)
    {
        Address = (nint)addon;
        this.spec = spec;
        this.width = width;
        this.height = height;
        decorations = new Decoration?[spec.Decorations.Length];
        this.log = log;
    }

    public nint Address { get; }

    /// <summary>Captures the current size as the original; call only before this plugin has resized it.</summary>
    public static AddonFrame? TryCapture(AtkUnitBase* addon, FrameSpec spec, IPluginLog log)
    {
        if (!FreeCompanyUi.IsShown(addon))
            return null;

        ushort width = 0;
        ushort height = 0;
        addon->GetSize(&width, &height, false);
        if (width == 0 || height == 0)
            return null;

        var frame = new AddonFrame(addon, spec, width, height, log);
        frame.CaptureMissingParts(addon);
        log.Debug("FC Tweaks captured {Addon} at {Width}x{Height} (frame height {Frame}).",
            spec.Name, width, height, frame.frameHeight?.ToString() ?? "pending");
        return frame;
    }

    /// <summary>Sizes the window to its original height plus <paramref name="extraHeight"/>.</summary>
    public void Apply(AtkUnitBase* addon, int extraHeight)
    {
        if ((nint)addon != Address)
            return;

        CaptureMissingParts(addon);
        var changed = false;

        // The frame node must be sized before SetSize lays out the addon.
        if (frameHeight is { } originalFrame && addon->WindowNode != null)
        {
            var target = ToUShort(originalFrame + extraHeight);
            if (addon->WindowNode->Height != target)
            {
                addon->WindowNode->SetHeight(target);
                changed = true;
            }
        }

        ushort currentWidth = 0;
        ushort currentHeight = 0;
        addon->GetSize(&currentWidth, &currentHeight, false);
        var targetHeight = ToUShort(height + extraHeight);
        if (changed || currentWidth != width || currentHeight != targetHeight)
        {
            addon->SetSize(width, targetHeight);
            changed = true;
        }

        foreach (var decoration in decorations)
        {
            if (decoration is { } part)
                changed |= ApplyDecoration(addon, part, extraHeight);
        }

        if (!changed)
            return;

        if (addon->WindowNode != null && addon->WindowNode->Component != null)
            addon->WindowNode->Component->UldManager.UpdateDrawNodeList();
        addon->UpdateCollisionNodeList(false);
    }

    public string Describe(AtkUnitBase* addon)
    {
        var current = "gone";
        if ((nint)addon == Address)
        {
            ushort w = 0;
            ushort h = 0;
            addon->GetSize(&w, &h, false);
            var frame = addon->WindowNode == null ? "none" : addon->WindowNode->Height.ToString();
            current = $"{w}x{h} frame {frame}";
        }

        var parts = decorations.Count(d => d is not null);
        return $"{spec.Name} original {width}x{height} frame {frameHeight?.ToString() ?? "missing"} " +
               $"decorations {parts}/{spec.Decorations.Length}, current {current}";
    }

    /// <summary>
    /// The frame node and decorations can appear after the window becomes visible. They are
    /// captured on first sight; this is safe because nothing resizes a part before it is captured.
    /// </summary>
    private void CaptureMissingParts(AtkUnitBase* addon)
    {
        if (frameHeight is null && addon->WindowNode != null && addon->WindowNode->Height > 0)
            frameHeight = addon->WindowNode->Height;

        var specs = spec.Decorations;
        for (var i = 0; i < specs.Length; i++)
        {
            if (decorations[i] is not null)
                continue;

            var node = addon->GetNodeById(specs[i].NodeId);
            if (node != null && node->Type == specs[i].Type)
                decorations[i] = new Decoration((nint)node, specs[i], node->Y, node->Width);
        }

        var frameMissing = spec.HasFrame && frameHeight is null;
        if (reportedMissingParts || (!frameMissing && decorations.All(d => d is not null)))
            return;

        reportedMissingParts = true;
        log.Warning("FC Tweaks has not yet found every part of {Addon}: frame {Frame}, missing nodes [{Nodes}]. " +
                    "Missing parts keep their size until they appear.", spec.Name,
            frameMissing ? "missing" : "ok",
            string.Join(", ", specs.Where((_, i) => decorations[i] is null).Select(s => s.NodeId)));
    }

    private static bool ApplyDecoration(AtkUnitBase* addon, Decoration part, int extraHeight)
    {
        var node = addon->GetNodeById(part.Spec.NodeId);
        if (node == null || (nint)node != part.Address)
            return false;

        var changed = false;
        if (part.Spec.IsRotatedDivider)
        {
            var targetWidth = ToUShort(part.Width + extraHeight);
            if (node->Width != targetWidth)
            {
                node->SetWidth(targetWidth);
                changed = true;
            }
        }

        var targetY = part.Y + extraHeight;
        if (node->Y != targetY)
        {
            node->SetYFloat(targetY);
            changed = true;
        }

        return changed;
    }

    private static ushort ToUShort(int value) => (ushort)Math.Clamp(value, 1, ushort.MaxValue);

    private sealed record Decoration(nint Address, DecorationSpec Spec, float Y, ushort Width);
}
