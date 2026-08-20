# Caret vs. Typedown: what changed

Caret is a fork of [Typedown](https://github.com/byxiaozhi/Typedown), ported from WPF + XAML Islands on .NET Core 3.1 to native **WinUI 3 + WebView2** on **.NET 8** (`Dev/Typedown.WinUI`). The Markdown editor itself (`Dev/Typedown.Editor`, React/TypeScript) is unchanged — this document is about the native host around it.

Three things happen to each piece of the original as it crosses over:

- **Ported** — same behavior, same wire protocol to the editor, translated line-for-line where the old and new platforms allow it.
- **Reimplemented** — same end result, different mechanism, because WinUI 3 / Windows App SDK offers a more direct native way to do it than the original's WPF-era approach (or the original's exact mechanism isn't available at all outside XAML Islands).
- **Deferred** — not yet in `Typedown.WinUI`. Listed explicitly rather than silently dropped.

## Ported

- Core editing surface: the wire protocol between the native host and the editor (`PostMessage`/`RemoteInvoke`/`EventCenter`, the same message names and payload shapes) — traced against `Dev/Typedown.Editor/src/services` on the JS side
- Find & Replace, driven by the same Search/Find/Replace wire messages
- Settings persistence shape (JSON file, same property list) and the live SettingsChanged push for font size/line height/tab size/etc.
- AutoBackup: same hash-named backup file scheme, same recover/discard flow on load
- Recent files, folder workspace (ExplorerItem tree, live FileSystemWatcher updates), export (HTML/PDF/text) and print
- GetCurrentTheme/ThemeChanged payload shape (`{ theme, accentColor, background }`), including a wire-contract quirk in `theme.ts` where `accentColor` and `background` use different key casing — matched exactly, not "fixed," since that JS is unmodified

## Reimplemented against modern APIs

- **Window chrome**: `ExtendsContentIntoTitleBar` + `SetTitleBar` instead of a custom-drawn caption bar (the original's private `Typedown.XamlUI` package isn't available outside that repo)
- **Window placement**: `AppWindow`/`OverlappedPresenter` instead of raw `WINDOWPLACEMENT` P/Invoke
- **Mica backdrop**: `Window.SystemBackdrop` (`MicaBackdrop`/`MicaKind`) instead of the manual `WindowsSystemDispatcherQueueHelper` + `MicaController` compositor setup XAML Islands needed
- **Local image loading**: `CoreWebView2.WebResourceRequested` serves `file:///` image requests directly, instead of relying on Chromium loading `file://` from an `https://` virtual-host origin (it doesn't, regardless of `--disable-web-security`)
- **Native folder picker**: raw `IFileOpenDialog` COM interop on a dedicated STA thread — `Windows.Storage.Pickers.FolderPicker` throws `E_FAIL` reliably in this unpackaged app
- **Keyboard shortcuts while the editor has focus**: WebView2 owns a real child HWND, so XAML `KeyboardAccelerator`s never fire there — a small injected JS `keydown` listener forwards the relevant combinations back to the host instead

## New since the fork

Not present in Typedown at all:

- **Live theme push accuracy fix**: the original's `GetCurrentTheme`/`ThemeChanged` payload shape was ported, but getting the accent/background color casing to actually match what `theme.ts` reads required building `background` as a raw JSON object rather than a reflected POCO — confirmed by inspecting the actual wire payload, not assumed
- **Multi-window support**: any number of windows in one process, focus-existing-window-instead-of-duplicate, process exits only when the last one closes

## Deferred

Called out here rather than silently missing:

- **Single-instance activation redirection** — the original's Mutex + named-pipe handshake, where a second `Typedown.exe` launch (e.g. double-clicking a `.md` file in Explorer) hands off to the already-running instance instead of starting a new process. Porting this needs an explicit `Main()` replacing WinUI 3's SDK-generated one (`DISABLE_XAML_GENERATED_MAIN`) so a second launch can redirect via `Microsoft.Windows.AppLifecycle.AppInstance` before ever creating a window.
- **Folder tree drag-and-drop and clipboard cut/copy/paste** — needs `IFileOperation`/`IClipboard`-equivalent infrastructure not yet ported. New File/Folder/Rename/Delete/Reveal-in-Explorer cover the common case in the meantime.
- **MSIX packaging** — Caret currently builds and runs unpackaged.
- Export/Import config UI (upload targets, per-format options beyond the basics), spellcheck, and a few Settings pages (Shortcuts, About) from the original's fuller Settings surface.

## Regression testing

Every item above was verified by actually driving the running app — UI Automation clicks, screenshots, and log inspection — not just "it compiles." See individual commit messages on the `winui3-port` branch history for what was checked for each change.
