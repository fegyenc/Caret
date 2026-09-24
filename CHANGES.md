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
- **Single-instance activation redirection**: `Microsoft.Windows.AppLifecycle.AppInstance` (`Program.cs`) instead of the original's Mutex + `NamedPipeServerStream` handshake (`Typedown\App.cs`) — a second `Caret.exe` launch (e.g. double-clicking a `.md` file in Explorer) still hands off to the already-running instance instead of starting a new process, and either focuses an already-open window for that file or opens a new one in the existing process, same end result via the Windows App SDK's own purpose-built API instead of hand-rolled IPC. Needed replacing WinUI 3's SDK-generated `Main` with an explicit one (`DISABLE_XAML_GENERATED_MAIN`) so a redirected second launch never creates an `Application` or window in that second process at all.
- **Folder tree clipboard cut/copy/paste**: `Windows.ApplicationModel.DataTransfer.DataPackage`/`Clipboard` (`MainWindow.xaml.cs`'s "Folder tree clipboard & drag-drop" region) instead of the original's `System.Windows.Clipboard` plus a hand-rolled "Preferred DropEffect" byte blob (`Typedown\Services\FileOperation.cs`/`Clipboard.cs`) to distinguish Move from Copy — `DataPackage.RequestedOperation` does that natively, no marker format needed. The actual copy/move/delete still goes through the same shell engine as the original's raw `SHFileOperation` calls, via `Microsoft.VisualBasic.FileIO.FileSystem` (already this project's convention for Delete) instead of P/Invoking `SHFileOperation` directly — same Explorer-native conflict/overwrite prompts and Recycle Bin support.

## New since the fork

Not present in Typedown at all:

- **Live theme push accuracy fix**: the original's `GetCurrentTheme`/`ThemeChanged` payload shape was ported, but getting the accent/background color casing to actually match what `theme.ts` reads required building `background` as a raw JSON object rather than a reflected POCO — confirmed by inspecting the actual wire payload, not assumed
- **Multi-window support**: any number of windows in one process, focus-existing-window-instead-of-duplicate, process exits only when the last one closes
- **MSIX packaging** for `Release` builds — see [PACKAGING.md](PACKAGING.md). Verified through building, signing, and confirming Windows correctly refuses to install it without the dev certificate trusted first (trusting a certificate is a machine-level security-store change, so that step — and installing/launching the trusted package — is intentionally left for a human to run rather than automated here)
- **Warm-autumn rebrand**: a full visual reskin from a brand asset package (`design-system/design-tokens.json`) — new app icon/window icon/MSIX tile assets, a light/dark color theme (`Themes/Caret.xaml`) applied by overriding WinUI 3's own semantic resource keys (`TextFillColorPrimaryBrush`, `CardStrokeColorDefaultBrush`, `SystemAccentColor` + its Light/Dark variants, `ControlCornerRadius`/`OverlayCornerRadius`, ...) so it cascades into stock controls without touching their templates, and the folder tree's folder/file icons rebuilt as brand line-icon `Path` geometries instead of Segoe Fluent glyphs. Mica now defaults to **off** (`SettingsViewModel.UseMicaEffect`) — the brand background is a flat illustrated color field, and Mica's system-mixed translucent tint washed it out; the Settings toggle still turns it back on for anyone who prefers the glass look.
  - The asset package's `brand/app-icons/png/*.png` files were all corrupted — every size, every theme was actually the same cropped fragment of a presentation sheet (confirmed by inspecting pixel content, not just file names/dimensions). The SVG logo sources were clean, so the actual app icon/MSIX tile assets were rasterized from those instead, with a small purpose-built renderer (the SVGs only use a handful of path commands, no need for a full SVG engine).
  - Two WinUI 3 API traps hit and documented in code (`Themes/Caret.xaml`'s comments): `Shape.StrokeLineCap`/`StrokeStartLineCap`/`StrokeEndLineCap` aren't real dependency properties on `Microsoft.UI.Xaml.Shapes.Shape` (that's WPF-only) — setting either one crashes the XAML compiler's `MarkupCompilePass2` at build time with no diagnostic; and `{ThemeResource}` lookups don't resolve across sibling merged `ResourceDictionary` files — a key defined in one file's `ThemeDictionaries` crashes the native XAML engine at runtime (0xc000027b) if referenced from a `Style` in a *different* merged file, even for a standard key, not just a custom one. Both are silent, hard crashes rather than clean errors — same class of tooling fragility as the `[DoNotNotify]` Fody note already in `MainWindow.xaml.cs`.

## Deferred

Called out here rather than silently missing:

- **Store submission** — the MSIX package above is signed with a local throwaway dev certificate, good for proving it installs and runs on the machine that built it. Actually distributing it needs a Microsoft Store listing or a real code-signing certificate.
- Export/Import config UI (upload targets, per-format options beyond the basics), spellcheck, and a few Settings pages (Shortcuts, About) from the original's fuller Settings surface.

## Known issues

- **Folder tree item-to-item drag-and-drop** (dragging a row onto a folder row to move it) is implemented — `CanDrag`/`DragStarting`/`AllowDrop`/`DragOver`/`Drop` on each `TreeViewItem` in `MainWindow.xaml`, structurally the same per-item pattern the original's `FolderPage.xaml` used — but could **not** be confirmed working end-to-end. Log instrumentation showed `DragStarting` firing and setting valid drag data, and `DragOver` correctly accepting the first folder row the pointer crossed, but no further `DragOver` and no `Drop` at all afterward, reproduced identically across three different synthetic-input methods (an automation tool's mouse drag, raw Win32 `SendInput`, and a version with a custom minimal `DragUI` bitmap to rule out a rendering red herring). Cut/Copy/Paste from the context menu is the verified, reliable way to move or copy files in the tree in the meantime; drag-and-drop needs checking with a real mouse before it can be called done.

## Regression testing

Every item above (except the drag-and-drop item under Known issues, which could not be confirmed — see that section) was verified by actually driving the running app — UI Automation clicks, screenshots, and log inspection — not just "it compiles." See individual commit messages on the `winui3-port` branch history for what was checked for each change.
