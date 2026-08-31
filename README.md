# WhatsApp Chat Viewer

[![CI](https://github.com/VictorZakharov/whatsapp-chat-viewer/actions/workflows/ci.yml/badge.svg)](https://github.com/VictorZakharov/whatsapp-chat-viewer/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A private, read-only desktop viewer for WhatsApp chat export ZIPs, built with
Avalonia 12 and .NET 10. It opens exports directly—no manual unpacking and no
uploading your conversations anywhere.

> This independent project is not affiliated with, endorsed by, or sponsored
> by WhatsApp or Meta. WhatsApp is a trademark of its respective owner.

## Highlights

- Open one or several WhatsApp export ZIPs and switch between chats. Invalid or
  unrelated ZIPs are reported without preventing valid chats from opening.
- Navigate large, multi-year conversations with virtualized scrolling, message
  search, a persistent date indicator, and jump-to-date-and-time.
- Recreate the familiar conversation layout with incoming and outgoing
  bubbles, date separators, system messages, edited markers, and delivery
  checks.
- View images without cropping and browse images and videos in one
  chronological gallery.
- Drag or wheel-scroll the filmstrip, or expand it into a virtualized full
  media drawer. The current item remains highlighted and media dates appear in
  the header and thumbnail tooltips.
- Play videos inline with seeking, volume, play/pause, replay, left/right
  rotation, previous/next media, keyboard controls, and fullscreen playback.
- Lazily load media from the ZIP. Hardware-adaptive parallel thumbnail decoding
  keeps large galleries responsive without loading the archive into memory.
- Open or save any attachment without extracting the full export.

## Privacy model

The application has no upload or network path for chat data. ZIP files stay on
your computer and are opened read-only. Message text and ZIP entry metadata are
indexed in memory; compressed archives and their media remain on disk.

- Images are buffered and decoded only when requested.
- Visible gallery thumbnails are decoded on demand and retained in a bounded
  cache.
- Video playback uses an app-owned temporary copy of only the selected ZIP
  entry because LibVLC requires seeking. Normal shutdown deletes it.
- Saving or opening externally copies only the selected attachment.

Nothing performs a whole-archive extraction. Following an abnormal shutdown,
Windows may retain a session directory under `%TEMP%\WhatsAppChatViewer`; it
can be deleted safely while the app is closed.

## Run from source

Requirements:

- .NET 10 SDK
- Windows 10 or 11 for the bundled LibVLC runtime

```powershell
git clone https://github.com/VictorZakharov/whatsapp-chat-viewer.git
cd whatsapp-chat-viewer
dotnet restore WhatsAppChatViewer.slnx
dotnet run --project src/WhatsAppChatViewer -- "C:\path\to\WhatsApp Chat.zip"
```

Pass several ZIP paths to open several chats. With no arguments, the app looks
for ZIP files near the working directory or executable and offers a picker when
it finds more than one. The plus button can add exports later.

LibVLC is bundled for Windows. Linux and macOS require a compatible system
LibVLC installation and have not received the same release QA.

## Keyboard shortcuts

| Context | Shortcut | Action |
|---|---|---|
| Conversation | `Ctrl+F` | Focus message search |
| Conversation | `Escape` | Clear focused search |
| Media viewer | `Page Up` / `Page Down` | Previous/next image or video |
| Image viewer | `Left` / `Right` | Previous/next media |
| Media viewer | `F` / `F11` | Toggle fullscreen |
| Media viewer | `Escape` | Collapse the media drawer, leave fullscreen, then close |
| Video player | `Space` | Play/pause |
| Video player | `Left` / `Right` | Seek five seconds |
| Video player | `M` | Mute/unmute |
| Video player | `R` | Rotate right |
| Video player | `Shift+R` | Rotate left |

Use the clock button in the conversation header to jump to a precise date and
time.

## Build and test

```powershell
dotnet build WhatsAppChatViewer.slnx
dotnet test WhatsAppChatViewer.slnx
```

The integration smoke test uses a ZIP in the repository root when one is
present and otherwise exits without failing. Export archives, local shortcuts,
and the `artifacts` QA folder are ignored because they may contain private
conversation data.

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before
opening an issue or pull request, especially the privacy guidance for sample
exports and screenshots. Security reports belong in [SECURITY.md](SECURITY.md).

## License

Original source code is licensed under the [MIT License](LICENSE).
LibVLCSharp, LibVLC, and other dependencies retain their own licenses. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [LICENSES](LICENSES).
