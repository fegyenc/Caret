# Building an MSIX package

`Typedown.WinUI` builds unpackaged by default (`Debug`/`Debug_Local`, the configs used for day-to-day development) and packaged as MSIX only for `Release`, via the Windows App SDK's single-project packaging (`WindowsPackageType=MSIX` + `EnableMsixTooling`, `Package.appxmanifest`).

## Build the package

```ps
cd Caret\Dev\Typedown.WinUI
msbuild Typedown.WinUI.csproj -p:Configuration=Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
```

This produces a signed `.msix` under `bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\Typedown.WinUI_1.0.0.0_x64_Test\`.

`DebugType` is forced to `None` for Release: the packaging pipeline's symbol-package step shells out to a native VC++ tool (`mspdbcmf.exe`) that doesn't handle .NET 8's portable PDBs and fails the whole build outright rather than just skipping symbol packaging — not needed for an installable package, so no PDBs means nothing for that step to choke on.

## Signing

The repo ships wired up to sign with `Dev/Typedown.WinUI/CaretDevCert.pfx` — a **local, throwaway, self-signed** certificate (`CN=Caret`, matching `Package.appxmanifest`'s `Identity/Publisher`), gitignored, not something anyone else's clone will already have. Generate your own:

```ps
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=Caret" -KeyUsage DigitalSignature `
  -FriendlyName "Caret Dev Signing Certificate" -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

From there, either:

- **Sign via `PackageCertificateThumbprint`** (recommended for command-line builds): the cert above is already in `Cert:\CurrentUser\My`, so
  ```ps
  msbuild Typedown.WinUI.csproj -p:Configuration=Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true `
    -p:PackageCertificateKeyFile= -p:PackageCertificateThumbprint=<thumbprint from $cert.Thumbprint>
  ```
  No password, no PFX file involved in the build itself.
- **Sign via `PackageCertificateKeyFile`** (what the csproj defaults to, `CaretDevCert.pfx`): export the cert to a password-protected PFX (`Export-PfxCertificate -Cert $cert -FilePath CaretDevCert.pfx -Password $securePwd`) and pass `-p:PackageCertificatePassword=<password>` on the build command. A **password-less** PFX export (an empty `SecureString`) looks like it works but actually ties the file to the exact user/session that created it — importing it later from a different (even elevated) session fails with "requires either a different password or membership in an Active Directory principal." Use a real password if you go this route.

**If you regenerate the certificate for any reason** (new machine, fixing a password issue, whatever), the old `.msix` is signed with the *old* certificate's key pair — trusting the new certificate does nothing for a package that's already built. Rebuild after regenerating, or you'll hit `0x800B0109: The root certificate ... must be trusted` even with a certificate that looks right by name.

**Bump `Package.appxmanifest`'s `Identity/Version` before rebuilding to reinstall over an existing install.** Confirmed the hard way: `Add-AppxPackage` refuses a package whose identity (name + version) matches an already-installed one but whose contents differ — `0x80073CFB`, "the provided package has the same identity as an already-installed package but the contents are different," even though the certificate trusted fine and everything else was correct. It's not a signing or trust problem, just AppX's own same-version-different-content guard. A same-version reinstall does work via `Remove-AppxPackage` first, but bumping the version is the normal path for "I rebuilt this with new code" — this project doesn't yet have anything that bumps it automatically, so it's a manual step each time you package a new build for local reinstall.

## Installing locally (sideload)

A self-signed certificate isn't trusted by Windows out of the box, so installing the package requires **trusting it first** — a machine-level security-store change, deliberately a manual step rather than something automated here. This needs a genuinely elevated PowerShell — Windows Terminal doesn't always show "Administrator" in the title bar the way classic `powershell.exe` does, so if in doubt, confirm with:

```ps
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
```

Then, as Administrator:

```ps
# Trust the certificate (PFX or CER both work with the matching cmdlet)
Import-PfxCertificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople -FilePath "CaretDevCert.pfx" -Password $securePwd
# or, from a public-only .cer (no password needed):
# Import-Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople -FilePath "CaretDevCert.cer"

# Then install the package
Add-AppxPackage -Path "bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\Typedown.WinUI_1.0.0.0_x64_Test\Typedown.WinUI_1.0.0.0_x64.msix"
```

Once installed, Caret appears in the Start menu with its own tile and can be uninstalled the normal Windows way (Settings → Apps, or right-click the Start tile).

## Troubleshooting: saves silently "work" but the file stays empty (0 bytes)

Symptom: File > Save (or Save As) shows no error, the Save dialog behaves normally, a file does appear on disk at the chosen path — but it's 0 bytes, the title bar keeps showing the unsaved-changes dot, and nothing in the app's log (`%TEMP%\caret_winui_probe.log`) records a completed save (no `Save: <path>` line, no exception either).

This is **Windows Defender's Controlled Folder Access** (ransomware protection) silently blocking the packaged app's write — not a bug in Caret. It only shows up for the *installed* package, not the dev build: Controlled Folder Access allowlists by exact executable path, and `C:\Program Files\WindowsApps\Caret_...\Typedown.WinUI.exe` is a path Windows has never seen before, while the dev build (`bin\...\Typedown.WinUI.exe`, run repeatedly all through development) had already been implicitly trusted. The `FileSavePicker` dialog itself still creates the empty target file, because that part runs through a trusted system broker process — it's specifically *Caret's own* write of the actual content that gets dropped.

**Confirm it's this** by checking Event Viewer's `Microsoft-Windows-Windows Defender/Operational` log for event ID 1123 ("... has been blocked from modifying ... by Controlled Folder Access") around the time of the failed save, or from PowerShell:

```ps
Get-WinEvent -LogName "Microsoft-Windows-Windows Defender/Operational" -MaxEvents 50 |
  Where-Object Id -eq 1123 | Select-Object TimeCreated, Message -First 5
```

**Fix**: Windows Security → Virus & threat protection → "Manage ransomware protection" → "Allow an app through Controlled folder access" → Add an allowed app. Caret usually shows up directly under "Recently blocked apps"; otherwise browse to `C:\Program Files\WindowsApps\Caret_1.0.0.0_x64__<hash>\Typedown.WinUI.exe` (the exact hash suffix varies per install — `(Get-AppxPackage -Name Caret).InstallLocation` prints the real path). This is a security-setting change, so it's a manual step for whoever's installing the package, not something the build or install process can do on your behalf.

## Installing on another PC

The package + certificate travel as two files — copy both to the other machine (USB drive, a synced cloud folder, whatever's convenient):

- `Typedown.WinUI_1.0.0.0_x64.msix` — the app package itself
- `CaretDevCert.cer` — the **public** certificate only (export with `Export-Certificate -Cert $cert -FilePath CaretDevCert.cer`; never copy the `.pfx` anywhere for this — that's the private signing key, not needed on the install side and not something to hand out)

Then, on the other machine, as Administrator:

```ps
Import-Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople -FilePath "CaretDevCert.cer"
Add-AppxPackage -Path "Typedown.WinUI_1.0.0.0_x64.msix"
```

Same trust-then-install shape as above, just with the plain `Import-Certificate` cmdlet since a `.cer` has no password to worry about. Every machine that will run this package needs this done once — that's the tradeoff of a self-signed dev certificate instead of a real code-signing certificate or a Store listing.

## Known gaps

- **x64 only has been verified end-to-end** (build → sign → trust → install → launch). x86/ARM64 use the same csproj configuration and should work the same way, but haven't been packaged and installed as part of this pass.
- **Not submitted to the Microsoft Store.** That's a separate step requiring a Partner Center developer account and Store-managed signing/certification, outside what's set up here. Store distribution would also sidestep the whole self-signed-certificate-trust dance above — every install would just be trusted automatically.
