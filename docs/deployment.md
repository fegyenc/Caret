# Deploying Caret in an organization

A guide for IT administrators: what Caret is, what it does on a device and on the network, and how to deploy it with Microsoft Intune or another tool. *(Version française : [deployment.fr.md](deployment.fr.md).)*

## At a glance

| | |
| --- | --- |
| **What** | A Markdown editor with a built-in converter from Word, Excel, PowerPoint, PDF and CSV to Markdown |
| **Publisher / source** | Open source, MIT licence: <https://github.com/fegyenc/Caret> |
| **Package** | MSIX, per-user. No administrator rights needed; installs no services, drivers or scheduled tasks. |
| **Architectures** | x64 and ARM64 |
| **Requirements** | Windows 10 version 1809 or later; Windows 11 recommended. The Microsoft Edge WebView2 Runtime, which is part of Windows 11 and installed with Microsoft 365 Apps on Windows 10. .NET and the Windows App SDK are included in the package. |
| **Languages** | English, French, Spanish (follows the Windows display language; users can change it) |
| **Account / sign-in** | None |
| **Telemetry** | None |

## Data and network

- **Documents never leave the device.** Editing, saving and conversion all happen locally. There is no cloud service behind Caret.
- **App data**: settings, recent files, templates and crash-recovery backups are stored in the package's data folder (`%LOCALAPPDATA%\Packages\<package family name>\LocalState`). Removing the app removes them.
- **Outbound connections Caret can make:**

| Destination | When | Microsoft Store version | GitHub version | Can be disabled |
| --- | --- | --- | --- | --- |
| `api.github.com` | Update check, at most once a day | Never | Yes | Policy `DisableUpdateCheck` (below), or per user in Settings |
| `pypi.org`, `files.pythonhosted.org` | Installing the optional MarkItDown converter, only when the user clicks *Install* | Never | On request | Policy `DisableMarkItDownInstall` |
| Websites referenced in a note | Images from the web shown in a note (like a browser) | Yes | Yes | No (content-driven) |

Word, Excel, PowerPoint, PDF and CSV conversion is built in and needs no network and no Python. MarkItDown only adds rarer formats.

The privacy policy is at [PRIVACY.md](../PRIVACY.md).

## Option A: Microsoft Store app (recommended)

When Caret is published in the Microsoft Store, Intune deploys and updates it directly from the Store.

1. Intune admin center → **Apps** → **Windows** → **Add** → app type **Microsoft Store app (new)**.
2. **Search the Microsoft Store app (new)** → search for *Caret* → select it. The Store ID (starting with `9`) is filled in.
3. **Install behavior: User.** Caret is a per-user app.
4. Assign it to groups as **Available for enrolled devices** (users install it from Company Portal) or **Required**.

Intune keeps the app up to date through the Store; users need no Store account. The same Store ID works with winget:

```
winget install --source msstore --id <Store ID>
```

## Option B: Line-of-business MSIX

For organizations that don't use the Store, or want to control each version:

1. Download the `.msix` from [GitHub Releases](https://github.com/fegyenc/Caret/releases), or build it from source.
2. **Sign it with your organization's code-signing certificate.** The package's `Publisher` must match the certificate's subject, so this means rebuilding with your publisher name. Build from source with the signing options in [PACKAGING.md](../PACKAGING.md), after setting `Identity/Publisher` in `Package.appxmanifest` to your certificate's subject.
3. Make sure the certificate is trusted on the devices (it usually already is for an internal code-signing CA).
4. Intune → Apps → Windows → Add → **Line-of-business app** → upload the `.msix` → assign.

Updates are redeployed the same way. Set the `DisableUpdateCheck` policy so users aren't told about GitHub releases your organization hasn't approved.

## Policies

Caret reads DWORD values under `HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Caret` (or the same path under `HKEY_CURRENT_USER`). Set them with Group Policy Preferences, an Intune remediation script or a configuration profile.

| Value | Effect |
| --- | --- |
| `DisableUpdateCheck` = `1` | No GitHub update check; the setting is hidden and Settings says updates are managed by your organization |
| `DisableMarkItDownInstall` = `1` | Caret never runs `pip`; for the rare formats that need MarkItDown it explains how to install it instead |

In a Microsoft Store install both behaviours are already on and can't be turned off.

Example (run as administrator):

```
reg add HKLM\SOFTWARE\Policies\Caret /v DisableUpdateCheck /t REG_DWORD /d 1 /f
reg add HKLM\SOFTWARE\Policies\Caret /v DisableMarkItDownInstall /t REG_DWORD /d 1 /f
```

Policies are read when Caret starts.

## Security notes

- **Capabilities:** only `runFullTrust`, the standard capability for packaged desktop apps (needed to open and save files anywhere the user chooses). Like any desktop program it can use the network; the only connections it makes are listed above.
- **File associations:** Caret registers as an *Open with* option for `.md`, `.markdown` and similar extensions. It doesn't take over the default app.
- **Links in notes:** web links open in the default browser. Links to local files open only documents and media; executables and scripts are revealed in File Explorer and never run.
- **Third-party components** and their licences: [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md). The document converters are Microsoft's Open XML SDK (MIT) and PdfPig (Apache 2.0).

## Removing Caret

Uninstall from Settings → Apps, or remove the Intune assignment (Uninstall). App data is removed with the package; the user's own notes and converted `.md` files stay where they were saved.
