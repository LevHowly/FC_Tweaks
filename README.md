# FC Tweaks

FC Tweaks adds a **2x height** toggle to the Free Company member window. It
doubles the member list's visible row count (up to the number of members).
The member window and the vanilla Free Company background frame grow by the
same added list height, leaving all widths unchanged. Click **1x height** to
restore the original size. The original dimensions are also restored before
either window closes or is destroyed, and when the plugin is unloaded.

The toggle appears in the upper-right of the member window.

The native frame has its own layout node: changing the list or addon dimensions
alone does not stretch it. FC Tweaks sets the frame height before updating the
addon dimensions, then refreshes its draw/collision layout. Expansion and
restoration use the same resize order. Visual layout still needs in-game validation.

## Build

Build with the Dalamud SDK installed by XIVLauncher:

```sh
dotnet build
```
