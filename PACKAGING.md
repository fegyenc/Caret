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
