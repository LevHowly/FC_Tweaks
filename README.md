# FC Tweaks

**Dalamud custom repository URL:**

```text
https://raw.githubusercontent.com/LevHowly/FC_Tweaks/master/pluginmaster.json
```

In Dalamud, open **Settings > Experimental > Custom Plugin Repositories**,
add this URL, save, then install **FC Tweaks** from the plugin installer.
No manual download or developer plugin configuration is needed.

Current release: **0.1.9**.

## Expand / Collapse

An **Expand / Collapse** button replaces Community Finder at the top right of
the Free Company window (the native button is hidden while the plugin runs and
restored when it unloads).

- **Expand** makes the whole FC window taller on every tab and gives the Members
  list that many extra rows. Widths never change. The left divider stretches and
  the Members page selector/footer move down with it.
- The choice is **saved**: switching tabs, closing the window, zoning, and
  reloading the plugin keep it.
- `/fctweaks` toggles the same setting (even with the window closed).
  `/fctweaks status` prints what the plugin sees, which helps with bug reports.

Other tabs keep their original content layout inside the taller frame.

How it works, briefly:

- The saved preference is the only state. Each framework update reconciles the
  native windows towards it; clicking never touches native memory directly.
- The outer `FreeCompany` window is captured once it is visible with its nodes
  loaded. Its `IsReady` flag is not used: it does not reliably report true for
  this window, which previously stopped the background frame from growing.
- The Members list is resized only from the framework update, only after the
  roster has stopped changing for one second, and only with the game's own
  `SetSize` (which recalculates visible rows). Its row count is never forced.
- Addons that are set up again or destroyed are forgotten, never restored
  through. Nothing native is touched while zoning or logging out.

Verified native layout nodes: FreeCompany node 2 (Community Finder) and node 19
(the rotated left divider); FreeCompanyMember node 24 (member list) and groups
45 and 49 (page selectors and footer separator).

## Online member sorting

Three small **Sort** buttons sit immediately to the right of the native **Name**,
**Location**, and **Rank** column labels. Click one to sort online members on the
**current loaded page** (up to 200 members):

- **Name:** alphabetical, ignoring case.
- **Location:** The Goblet first, then alphabetical by the location text displayed
  by the game. The Goblet is identified using its localized game-data name.
- **Rank:** numerical FC hierarchy order, highest first, not alphabetical rank names.

Offline rows stay in their exact slots. Online members visiting other worlds are
included. Equal sort keys keep their previous order.

The selected sort is reapplied after roster refreshes, but only once the roster
has gone a full second without updates, so members arriving one by one during
loading are not shuffled repeatedly. It also waits for pending native updates,
open context menus, and held rows. Closing or hiding the window forgets the
sort; zoning keeps it if the window stays open.

Sorting uses the game's own FC row-swap handler, so names, status, rank, memos,
and right-click member identity stay together. If the native functions or data
layout do not match this game version, sorting disables itself and logs an
error; Expand / Collapse keeps working.

## Changelog

- **0.1.9** Fix the vanilla FC frame not growing (its baseline could be captured
  before the window was set up). Refactor: clicking only changes the saved
  setting; the Members frame follows the list's real height; `/fctweaks status`.
- **0.1.8** `/fctweaks` command.
- **0.1.6–0.1.7** Saved, global expansion; button replaces Community Finder;
  fixed a crash when expanding after switching tabs (row count is no longer forced).
- **0.1.5** One-second sorting debounce; sort forgotten on close.
- **0.1.3–0.1.4** Online member sorting.
- **0.1.1–0.1.2** Crash fixes: no native list changes from rendering, during
  zoning, or from close/finalize callbacks.
- **0.1.0** First release.

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
