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
Export-PfxCertificate -Cert $cert -FilePath "Dev\Typedown.WinUI\CaretDevCert.pfx" -Password (New-Object System.Security.SecureString)
```

This is a dev-only signing key, good for proving the package builds/installs/runs on your own machine. It is **not** suitable for distributing the package to anyone else — real distribution needs either a Microsoft Store listing (Store-managed signing via Partner Center) or your own trusted code-signing certificate.

## Installing locally (sideload)

A self-signed certificate isn't trusted by Windows out of the box, so installing the package requires **trusting it first** — a machine-level security-store change, deliberately a manual step rather than something automated here:

```ps
# Trust the certificate (run as Administrator)
$cert = Get-PfxCertificate -FilePath "Dev\Typedown.WinUI\CaretDevCert.pfx"
Import-Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople -FilePath "Dev\Typedown.WinUI\CaretDevCert.pfx"
# (or, from the exported .cer: Import-Certificate -FilePath CaretDevCert.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople)

# Then install the package
Add-AppxPackage -Path "bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\Typedown.WinUI_1.0.0.0_x64_Test\Typedown.WinUI_1.0.0.0_x64.msix"
```

Once installed, Caret appears in the Start menu with its own tile and can be uninstalled the normal Windows way (Settings → Apps, or right-click the Start tile).

## Known gaps

- **x64 only has been verified end-to-end** (build → sign → package). x86/ARM64 use the same csproj configuration and should work the same way, but haven't been packaged and installed as part of this pass.
- **Not submitted to the Microsoft Store.** That's a separate step requiring a Partner Center developer account and Store-managed signing/certification, outside what's set up here.
