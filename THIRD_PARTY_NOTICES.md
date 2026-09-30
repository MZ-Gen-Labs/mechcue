# Third-party notices

MechCue source is distributed under the MIT license in LICENSE.

Solid Edge is a Siemens product and is separately licensed. This repository and
release packages do not contain Siemens interop binaries, CAD models, or the
Solid Edge application. MechCue is an independent prototype, not a Siemens product.

The add-in declares the minimal COM interfaces needed to communicate with the
installed Solid Edge application. COM interface identifiers remain those of Solid Edge.

.NET and Windows Forms are Microsoft technologies, distributed under their own
terms. The .NET Desktop Runtime is installed separately; it is not bundled here.

Installers are built with Inno Setup by JRSoftware. Inno Setup license and source:
https://jrsoftware.org/files/is/license.txt
https://github.com/jrsoftware/issrc


The optional MechCue MCP package uses the official C# MCP SDK (Apache-2.0):
https://github.com/modelcontextprotocol/csharp-sdk
Copyright Model Context Protocol a Series of LF Projects, LLC.

Its Microsoft .NET support packages use the MIT license. Dependency versions
and hashes are pinned in Mcp/packages.lock.json. The MCP distribution includes
ThirdPartyNotices with full license texts, package metadata and supplied notices.
These dependencies are used by the separate MCP process; they are not loaded
into the Solid Edge add-in.
