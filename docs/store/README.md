# Publishing Caret in the Microsoft Store

Everything needed for the submission is in this folder:

| File | What it is |
| --- | --- |
| [listing.en.md](listing.en.md), [listing.fr.md](listing.fr.md), [listing.es.md](listing.es.md) | Store listing texts, field by field |
| [screenshots/](screenshots) | 1920×1080 screenshots for each language (`en`, `fr`, `es`) |
| [../../PRIVACY.md](../../PRIVACY.md) | Privacy policy (English, French, Spanish) |

Once Caret is in the Store, companies can deploy it with Intune: see [../deployment.md](../deployment.md).

## Before you start

- **Turn on GitHub Issues** for the repository (Settings → Features → Issues). The privacy policy and the Store listing give it as the support contact.
- **Privacy policy URL:** `https://github.com/fegyenc/Caret/blob/main/PRIVACY.md` (valid once this branch is merged).

## 1. Create a Partner Center account

Go to <https://partner.microsoft.com/dashboard/registration> and sign up with a Microsoft account.

- **Individual** account: registration is free for individual developers (check the current terms when you sign up). The publisher name shown in the Store is your own name or the name you choose.
- **Company** account: a one-time fee and verification of the company. Only choose this if the company itself will publish Caret.

Identity verification can take from a few hours to a few days.

## 2. Reserve the name

Partner Center → Apps and games → **New product** → MSIX or PWA app → reserve **Caret**. If it's taken, try **Caret Markdown** (the listing files mention alternatives).

## 3. Send me the package identity

Open the new app → Product management → **Product identity** and copy these three values:

```xml
<Identity Name="…" Publisher="CN=…" />
<PublisherDisplayName>…</PublisherDisplayName>
```

I'll put them into `Package.appxmanifest`. They must match exactly, or the Store rejects the package. (The current values, `Caret` and `CN=Caret`, are only for the self-signed GitHub builds; sideloaded installs will need one reinstall after the switch, because a different publisher means a different app to Windows.)

## 4. Build the Store package

The Store signs the package itself, so it's built unsigned. It covers x64 and ARM64 (Snapdragon laptops).

**With Visual Studio (simplest):** open `Caret.sln` → right-click **Typedown.WinUI** → Package and Publish → **Create App Packages** → *Microsoft Store as Caret* (sign in and pick the reserved app) → architectures **x64** and **ARM64**, configuration **Release** → Create. Upload the `.msixupload` it produces.

**From the command line:** build each platform unsigned, then bundle them:

```ps
cd Dev\Typedown.WinUI
msbuild Typedown.WinUI.csproj -restore -p:Configuration=Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxPackageDir=bin\Store\x64\
msbuild Typedown.WinUI.csproj -restore -p:Configuration=Release -p:Platform=ARM64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxPackageDir=bin\Store\arm64\
mkdir bin\Store\bundle
copy bin\Store\x64\*\*.msix bin\Store\bundle\
copy bin\Store\arm64\*\*.msix bin\Store\bundle\
makeappx bundle /d bin\Store\bundle /p bin\Store\Caret.msixbundle /bv <version>
```

`makeappx.exe` is in the Windows SDK (`C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\`). Both packages must have the same version. Bump `Package.appxmanifest`'s version for every submission.

**Optional but recommended:** run the Windows App Certification Kit on the package before uploading (`appcert.exe`, also in the Windows SDK). It runs the same basic tests as Store certification.

## 5. Fill in the submission

| Section | What to enter |
| --- | --- |
| **Pricing and availability** | Free. All markets (or pick yours). Visibility: public. |
| **Properties** | Category **Productivity**. Privacy policy URL (above). Website: `https://github.com/fegyenc/Caret`. Support contact: `https://github.com/fegyenc/Caret/issues`. |
| **Age ratings** | Answer the questionnaire; see below. Expected result: suitable for everyone (3+ / Everyone). |
| **Packages** | Upload the `.msixupload` or `.msixbundle`. Device family: Desktop. |
| **Store listings** | Add **English**, **French** and **Spanish**; paste from the listing files and upload the screenshots from `screenshots/<language>/` in order 1-2-3 with their captions. |
| **Submission options → restricted capabilities** | Justification for `runFullTrust`, below. |
| **Notes for certification** | Below. |

### Age rating questionnaire

Caret is a productivity app: choose the category **Productivity / utility (not a game)**, then answer:

- Violence, fear, sexuality, profanity, drugs, gambling: **No**
- Does the app let users interact or exchange content with other users? **No**
- Does the app share the user's location? **No**
- Does the app allow digital purchases? **No**
- Does the app collect personal information? **No**
- Unrestricted internet access (web browsing)? **No.** Links in notes open in the user's own browser.

### runFullTrust justification

> Caret is a desktop app built with WinUI 3 and the Windows App SDK, packaged as MSIX. runFullTrust is the standard capability for packaged desktop apps. Caret needs it to open, edit and save the user's Markdown and Office files anywhere on their disk (including a folder workspace), to host the editor in WebView2, and to use the Windows clipboard and file dialogs. It installs no services or drivers and needs no administrator rights.

### Notes for certification

> No account or sign-in is needed.
> To test the main feature, click "Convert to Markdown" in the left sidebar (just below Home), then "Choose files..." and pick any .docx, .xlsx, .pptx or .pdf file. Conversion runs entirely offline; a .md file is written next to the original and listed with its size and an estimated token count. "Open" shows the result in the editor.
> In the Microsoft Store version the app never downloads code: the optional MarkItDown installer and the GitHub update check are turned off automatically.

## 6. After certification

Certification usually takes a few days. When the app is live, share its Store link, and the Store ID (it starts with `9`) with your IT department for Intune: see [../deployment.md](../deployment.md).

For updates: bump the version, rebuild, and create a new submission with the new package. Listings and screenshots carry over.
