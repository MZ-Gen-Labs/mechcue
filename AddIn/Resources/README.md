MechCue.res contains original bitmap resources for all 41 action IDs and an extra pause icon set (511-514). Stop remains an internal/MCP-compatible action but is excluded from the ribbon. Each set uses 101+10*n (16px color), 102+10*n (32px color), 103+10*n (16px monochrome), 104+10*n (32px monochrome).

Build-Icons.py regenerates the MIT-licensed artwork using Python and Pillow. The checked-in .res is embedded in the add-in DLL; build/install requires no Python or resource compiler. ComContractCheck checks every bitmap set.

Color icons use a neutral white background rather than an assumed magenta transparency key, which Solid Edge 2026 displayed visibly. Antialiasing is composited against white, avoiding purple fringes. GUI version 9 reloads the cached artwork.
