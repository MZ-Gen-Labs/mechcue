MechCue.res contains seven-command horizontal bitmap strips. Resources 101/102 are 16/32 pixel color icons and 103/104 are monochrome equivalents. Commands: open chart, play, stop, maximize, minimize, compact view, save to CAD. Ribbon buttons use Solid Edge icon-and-caption-below style for large icons. The original artwork is MIT licensed.

Build-Icons.py regenerates the resource using Python and Pillow. The checked-in .res is embedded in MechCue.AddIn.dll; builds and installations require neither Python nor a resource compiler. ComContractCheck verifies all four strip dimensions.
