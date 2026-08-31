# Third-party notices

WhatsApp Chat Viewer's original source code is licensed under the
[MIT License](LICENSE). Dependencies are separate works and retain their own
licenses; the project's MIT license does not change those terms.

This inventory reflects the Windows runtime graph audited on 2026-08-30.
Direct versions are pinned in the project file so the reviewed versions and
published notices stay aligned.

## Runtime dependencies

| Component | Version | License | Upstream |
|---|---:|---|---|
| Avalonia UI packages | 12.1.1 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia BuildServices | 11.3.2 | MIT | https://github.com/AvaloniaUI/Avalonia.BuildServices |
| Avalonia ANGLE Windows natives | 2.1.27548.20260419 | BSD-3-Clause-style | https://github.com/AvaloniaUI/angle |
| Inter font | bundled with 12.1.1 | SIL OFL-1.1 | https://github.com/rsms/inter |
| LibVLCSharp and LibVLCSharp.Avalonia | 3.10.1 | LGPL-2.1-or-later | https://code.videolan.org/videolan/LibVLCSharp |
| VideoLAN.LibVLC.Windows / LibVLC | 3.0.23.1 / 3.0.23 | LGPL-2.1-or-later | https://download.videolan.org/pub/videolan/vlc/3.0.23/ |
| SkiaSharp and native assets | 3.119.4 | MIT, plus bundled notices | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp and native assets | 8.3.1.3 | MIT, plus bundled notices | https://github.com/mono/SkiaSharp/tree/main/binding/HarfBuzzSharp |
| MicroCom.Runtime | 0.11.6 | MIT | https://github.com/kekekeks/MicroCom |
| Tmds.DBus.Protocol | 0.94.1 | MIT | https://github.com/tmds/Tmds.DBus |

## Test-only dependencies

The test project uses Microsoft.NET.Test.Sdk 17.14.1 (MIT), xUnit 2.9.3
(Apache-2.0), and xunit.runner.visualstudio 3.1.5 (Apache-2.0). They are not
included in application publish output.

## LGPL components and corresponding source

The application loads LibVLCSharp and LibVLC as replaceable shared libraries;
they are not relicensed under MIT. Windows builds bundle LibVLC 3.0.23 through
the VideoLAN package. Its matching source is linked in the table above. On
other desktop platforms, the application loads a compatible system-provided
LibVLC installation.

Anyone redistributing application binaries must keep `LICENSE.txt`, this file,
the `LICENSES` directory, and package-generated notices with the application.
The full LGPL-2.1-or-later, OFL-1.1, BSD-3-Clause, and Avalonia notice texts are
stored in `LICENSES`. Builds also copy the exact ANGLE, SkiaSharp, and
HarfBuzzSharp notice files from the restored packages.

Dependency upgrades require a new license, vulnerability, and notice review.
