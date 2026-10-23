# Contributing to Peyda Browser

Thanks for helping build Peyda! This document covers the practical bits.

## Getting set up

See [docs/BUILDING.md](docs/BUILDING.md) for prerequisites and exact commands.
In short: Windows 10/11, .NET 10 SDK, WebView2 Runtime.

## Workflow

1. Fork the repository and create a topic branch from `main`.
2. Keep changes small and focused; one feature/fix per pull request.
3. Build and run the tests locally before pushing:
   `dotnet build PeydaBrowser.sln -c Debug -p:Platform=x64` and `dotnet test`.
4. Open a pull request. CI (build + test + CodeQL) must be green.
5. Update `CHANGELOG.md` (Unreleased section) and docs when behaviour changes.

## Code guidelines

- **Architecture first**: UI in `PeydaBrowser.App`, platform-free logic in
  `PeydaBrowser.Core`, storage in `PeydaBrowser.Persistence`, policies in
  `PeydaBrowser.WebEngine`, OS/file concerns in `PeydaBrowser.Infrastructure`,
  themes in `PeydaBrowser.Theming`. Dependencies point one way:
  App → everything; Persistence/WebEngine/Infrastructure → Core;
  Core/Theming → nothing.
- **Test the logic, not the pixels**: new Core/WebEngine behaviour needs xUnit
  tests. Repository changes need a round-trip test against a temp SQLite file.
- **No hard-coded UI strings**: add keys to
  `src/PeydaBrowser.Core/Resources/Strings.en.json` **and** `Strings.fa.json`,
  regenerate `LocKeys.cs` / `LocalizedStrings.cs` (the scripts are the python
  snippets referenced in those files' headers), and reference `LocKeys.*`.
  A unit test enforces en/fa key parity. The brand name "Peyda" never translates.
- **Database changes**: never edit a shipped migration. Add a new
  `SchemaMigration` version and a migration test.
- **Security rules**: parameterized SQL only; validate all external input
  (themes, settings, URLs); never log URLs with tokens, cookies, passwords or
  form data; no secrets in the repo - ever (see [docs/SECURITY.md](docs/SECURITY.md)).
- **Style**: file-scoped namespaces, nullable enabled, `.editorconfig` wins.
  Commit messages in imperative mood (`feat:`, `fix:`, `docs:`, `test:`...).

## Reporting bugs

Open an issue with: Peyda version (Settings → About shows it, or
`peyda://about`), Windows version, WebView2 Runtime version, steps to
reproduce, and what you expected vs. what happened. Logs live in
`%LOCALAPPDATA%\PeydaBrowser\Profiles\default\Logs` - please check them for
anything personal before attaching.

For **security vulnerabilities**, do not open a public issue - follow
[SECURITY.md](SECURITY.md).

## License

By contributing you agree that your contributions are licensed under the
project's [MIT License](LICENSE).
