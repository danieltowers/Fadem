# Third-party notices

Fadem is under the [MIT licence](LICENSE). It uses the libraries below, under their own licences.
NuGet fetches them at build time: none is checked in. A binary release ships each library as
its own DLL, with this file.

## Libraries in the built app and plugin

| Library | Version | Licence | Copyright | Source |
|---|---|---|---|---|
| NAudio | 2.2.1 | MIT | © Mark Heath & Contributors | https://github.com/naudio/NAudio |
| BunLabs.NAudio.Flac | 2.0.1 | MS-PL | Vivelin and contributors | https://github.com/BunLabs/NAudio.Flac |
| TagLibSharp | 2.3.0 | LGPL-2.1-only | © 2006-2007 Brian Nickel, © 2009-2020 other contributors | https://github.com/mono/taglib-sharp |
| System.Text.Json | 8.0.5 | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| System.Memory | 4.5.5 | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/corefx |
| System.Collections.Immutable | 8.0.0 | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |

**TagLibSharp (LGPL-2.1).** Fadem links to `TagLibSharp.dll` unmodified, as a separate file.
You may replace it with your own build of TagLibSharp. Its source is at the link above, and the
licence text is at https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html.

## Used only to build and test

xunit and xunit.runner.visualstudio (Apache-2.0), Microsoft.NET.Test.Sdk and coverlet.collector
(MIT). None of them ships with Fadem.

## MusicBee's plugin API

`src/Fadem.MusicBee/MusicBeeInterface.cs` is MusicBee's plugin API, revision 53, copied
unchanged except for its header so the plugin can talk to MusicBee. It belongs to MusicBee's
author (https://getmusicbee.com) and is not covered by Fadem's MIT licence.

## BASS

The MusicBee tests call `bass.dll` from an installed MusicBee, when one is there. BASS is not
part of this repo or of any Fadem release.

## Licence texts

### MIT (NAudio, System.*)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### Microsoft Public License (MS-PL) (BunLabs.NAudio.Flac)

This license governs use of the accompanying software. If you use the software, you accept this
license. If you do not accept the license, do not use the software.

1. Definitions

The terms "reproduce," "reproduction," "derivative works," and "distribution" have the same
meaning here as under U.S. copyright law.

A "contribution" is the original software, or any additions or changes to the software.

A "contributor" is any person that distributes its contribution under this license.

"Licensed patents" are a contributor's patent claims that read directly on its contribution.

2. Grant of Rights

(A) Copyright Grant- Subject to the terms of this license, including the license conditions and
limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free
copyright license to reproduce its contribution, prepare derivative works of its contribution,
and distribute its contribution or any derivative works that you create.

(B) Patent Grant- Subject to the terms of this license, including the license conditions and
limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free
license under its licensed patents to make, have made, use, sell, offer for sale, import,
and/or otherwise dispose of its contribution in the software or derivative works of the
contribution in the software.

3. Conditions and Limitations

(A) No Trademark License- This license does not grant you rights to use any contributors' name,
logo, or trademarks.

(B) If you bring a patent claim against any contributor over patents that you claim are
infringed by the software, your patent license from such contributor to the software ends
automatically.

(C) If you distribute any portion of the software, you must retain all copyright, patent,
trademark, and attribution notices that are present in the software.

(D) If you distribute any portion of the software in source code form, you may do so only under
this license by including a complete copy of this license with your distribution. If you
distribute any portion of the software in compiled or object code form, you may only do so
under a license that complies with this license.

(E) The software is licensed "as-is." You bear the risk of using it. The contributors give no
express warranties, guarantees or conditions. You may have additional consumer rights under
your local laws which this license cannot change. To the extent permitted under your local
laws, the contributors exclude the implied warranties of merchantability, fitness for a
particular purpose and non-infringement.
