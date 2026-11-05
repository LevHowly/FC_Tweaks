# FC Tweaks

**Dalamud custom repository URL:**

```text
https://raw.githubusercontent.com/LevHowly/FC_Tweaks/master/pluginmaster.json
```

In Dalamud, open **Settings > Experimental > Custom Plugin Repositories**,
add this URL, save, then install **FC Tweaks** from the plugin installer.
No manual download or developer plugin configuration is needed.

Current release: **0.1.1**.

Version 0.1.1 fixes a native crash when expanding from the ImGui button.
The button now queues the resize for the framework update instead of allocating
native list rows during rendering. Addon readiness and component identity are
checked before resizing; decoration updates never allocate list rows.

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
