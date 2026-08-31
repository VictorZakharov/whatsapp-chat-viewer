# Contributing

Thanks for helping improve WhatsApp Chat Viewer.

## Protect private conversations

Never commit or attach a real chat export, extracted media, private screenshot,
phone number, address, token, or credential. Create the smallest synthetic ZIP
that reproduces a parser or media issue and use fictional participants and
messages. Redact screenshots before posting them publicly.

The repository ignores common archive formats and the local `artifacts`
directory as a final safeguard, but contributors remain responsible for
reviewing everything they publish.

## Development setup

Install the .NET SDK selected by `global.json`, then run:

```powershell
dotnet restore WhatsAppChatViewer.slnx
dotnet build WhatsAppChatViewer.slnx
dotnet test WhatsAppChatViewer.slnx
```

The primary supported development target is Windows. Keep ZIP access lazy:
features must not extract an entire archive or load all media into memory.

## Pull requests

- Keep changes focused and explain user-visible behavior.
- Add or update tests for parsing, indexing, archive access, and other logic.
- Verify the solution builds with no warnings; warnings are treated as errors.
- Preserve accessibility names and keyboard behavior when changing UI.
- Update third-party notices when changing dependencies.

By contributing, you agree that your contribution is licensed under the same
MIT License as the project.
