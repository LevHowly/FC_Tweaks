# FC Tweaks

**Dalamud custom repository URL:**

```text
https://raw.githubusercontent.com/LevHowly/FC_Tweaks/master/pluginmaster.json
```

In Dalamud, open **Settings > Experimental > Custom Plugin Repositories**,
add this URL, save, then install **FC Tweaks** from the plugin installer.
No manual download or developer plugin configuration is needed.

Current release: **0.1.7**.

Version 0.1.7 fixes the missing Expand/Collapse overlay: displaying the control
does not require the parent addon's optional WindowNode pointer. Its position is
calculated from the parent window's live position/scale and Community Finder's
local coordinates, rather than a hidden node's stale screen transform.

Version 0.1.6 makes expansion a saved, global FC-window preference. Switching
tabs or closing the window no longer collapses it or resets the button. The
Expand/Collapse control replaces Community Finder at the top-right of the parent
FC window on every tab. The native Community Finder button is hidden while the
plugin is active and restored on safe unload; the replacement is a Dalamud button.
Other tabs keep their existing content layouts for now, inside the taller frame.
The Members list uses native SetSize/recalculation rather than separately forcing
the visible-row count, and waits for incremental member loading to settle before
resizing. Native teardown never resizes rows.

Version 0.1.5 restores v0.1.0's frame capture/sizing behavior, including sizing
the parent frame without requiring its own OnSetup flag. Pre-draw corrections
use the intended expanded height, not a list height that native updates may reset.
Row allocation remains on framework updates, with transition/identity guards.
Sorting waits for a full second without roster updates and is forgotten on close.

Version 0.1.4 reapplies native frame/divider/footer geometry immediately before
drawing, so late native layout updates cannot leave the background at its old
height. Native row allocation stays exclusively on framework updates. Background
windows that are not ready when the roster first opens are captured later.
The selected sort now survives roster refreshes and loading screens, pausing
native access during zoning and resuming only after the roster is ready.

Version 0.1.3 adds online-only sorting buttons beside Name, Location, and Rank.
Sorting is limited to the currently loaded roster page and leaves offline rows
untouched. See [Online member sorting](#online-member-sorting) below.

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

FC Tweaks replaces Community Finder with an **Expand / Collapse** toggle in the
upper-right of the Free Company window. It doubles the Members list's vertical
space, leaving widths unchanged. The parent FC frame stays taller on all tabs,
even while the Members addon is hidden or recreated. Expansion is saved in
Dalamud configuration on each click and restored when reopening the FC window
or reloading the plugin. Closing does not change this preference. Destroyed
windows are never accessed again. Safe unload restores live native geometry and
the original Community Finder button without changing the saved preference.

## Online member sorting

Three small **Sort** buttons sit immediately to the right of the native **Name**,
**Location**, and **Rank** column labels. Click one to sort online members on the
**current loaded page** (up to 200 members):

- **Name:** alphabetical, ignoring case.
- **Location:** The Goblet first, then alphabetical by the location text displayed
  by the game. The Goblet is identified using its localized game-data name.
- **Rank:** numerical FC hierarchy order, highest first, not alphabetical rank names.

Offline rows remain in their exact slots and retain their relative order.
Online members visiting other worlds are included, even when their location shows
a world name or elapsed-time text. Equal sort keys retain their previous order.
The selected sort is remembered only while the window remains open. It is reapplied
to the current loaded page after native roster refreshes or page changes, but only
after a full second without roster updates. Every native roster update and change
in loaded member count restarts this delay, so members arriving one by one are not
repeatedly shuffled during loading. Checks run at most four times per second once
the delay has elapsed; an already sorted roster causes no native row swaps or UI
update requests. Sorting also waits for pending native data updates to finish and
pauses while a context menu is open or a list row is held. Closing, hiding, or
destroying the window clears the selected sort; reopening requires another click.

Sorting runs on the framework update, never in the rendering callback. It uses
the native FC row-swap handler to keep names, status, rank, memo indexes, and
right-click member identity together. Pending button requests and UI pointers are discarded on close,
destruction, zoning, or logout. The selected sort survives zoning only if the
window remains open, and is cleared on close, hide, destruction, or logout.
Unsupported native functions or array layouts disable sorting and report an error in the
Dalamud log; expansion remains independent. Native interaction and layout still
require in-game testing.

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

The dependency-free ordering tests can be run with:

```sh
dotnet run --project tests/FC_Tweaks.SortTests.csproj
```

## Package a release

```sh
bash scripts/package-release.sh
```

This builds Release, creates `build/release/latest.zip`, and updates
`pluginmaster.json` from the generated plugin manifest. Commit the updated
version and feed, push them, and upload the ZIP to the matching GitHub release
(for example, `v0.1.0`). Keep the ZIP filename `latest.zip`: the feed links to it.
