# FC Tweaks

**Dalamud custom repository URL:**

```text
https://raw.githubusercontent.com/LevHowly/FC_Tweaks/master/pluginmaster.json
```

In Dalamud, open **Settings > Experimental > Custom Plugin Repositories**,
add this URL, save, then install **FC Tweaks** from the plugin installer.
No manual download or developer plugin configuration is needed.

Current release: **0.1.2**.

Version 0.1.2 suspends UI writes during area transitions and logout. Close
callbacks queue restoration instead of resizing rows during teardown; finalization
only discards cached pointers. If the same window survives a transition, it is
restored on a subsequent safe framework update after it becomes visible again.
If it is destroyed, the plugin captures the newly created window instead.
This hardens FC window lifecycle handling; it does not fix or diagnose Penumbra
resource-loading failures. In-game transition testing is still required.

Version 0.1.1 fixes a native crash when expanding from the ImGui button.
The button now queues the resize for the framework update instead of allocating
native list rows during rendering. Addon readiness and component identity are
checked before resizing; decoration updates never allocate list rows.

FC Tweaks adds an **Expand** toggle to the Free Company member window. It
doubles the member list's visible row count (up to the number of members).
The member window and the vanilla Free Company background frame grow by the
same added list height, leaving all widths unchanged. Click **Collapse** to
restore the original size. Closing queues restoration for the next safe update
if the window survives. Destroyed windows are not accessed again. Unloading
restores live windows unless an area transition or logout is in progress.

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
together so its top stays fixed. Decorations are reapplied after native updates
to prevent UI animations from resetting them. Missing or incompatible nodes are
reported in the Dalamud log.

## Build

Build with the Dalamud SDK installed by XIVLauncher:

```sh
dotnet build
```

## Package a release

```sh
bash scripts/package-release.sh
```

This builds Release, creates `build/release/latest.zip`, and updates
`pluginmaster.json` from the generated plugin manifest. Commit the updated
version and feed, push them, and upload the ZIP to the matching GitHub release
(for example, `v0.1.0`). Keep the ZIP filename `latest.zip`: the feed links to it.
