MechCue.res contains original bitmap resources for all 35 action IDs and an extra pause icon set (451-454). Stop remains an internal/MCP-compatible action but is excluded from the ribbon. Each set uses 101+10*n (16px color), 102+10*n (32px color), 103+10*n (16px monochrome), 104+10*n (32px monochrome).

Build-Icons.py regenerates the MIT-licensed artwork using Python and Pillow. The checked-in .res is embedded in the add-in DLL; build/install requires no Python or resource compiler. ComContractCheck checks every bitmap set.
