# FC Tweaks

FC Tweaks adds an **Expand** toggle to the Free Company member window. It
doubles the member list's visible row count (up to the number of members).
The member window and the vanilla Free Company background frame grow by the
same added list height, leaving all widths unchanged. Click **Collapse** to
restore the original size. The original dimensions are also restored before
either window closes or is destroyed, and when the plugin is unloaded.

The toggle appears in the upper-right of the member window.

The native frame has its own layout node: changing the list or addon dimensions
alone does not stretch it. FC Tweaks sets the frame height before updating the
addon dimensions, then refreshes its draw/collision layout. Expansion and
restoration use the same resize order. Visual layout still needs in-game validation.

The left divider stretches by the same height delta, and the bottom pagination
controls and horizontal separator move down with the roster. Their original
dimensions and positions are restored on collapse. The plugin targets the
verified native layout nodes: FreeCompany node 19 (a rotated divider strip),
and FreeCompanyMember groups 45 and 49 (page selectors and the footer, including
its horizontal separator). The divider's local width and bottom position grow
together so its top stays fixed. Adjustments are reapplied before native drawing
to prevent UI animations from resetting them. Missing or incompatible nodes are
reported in the Dalamud log.

## Build

Build with the Dalamud SDK installed by XIVLauncher:

```sh
dotnet build
```
