# AI Usage Dock agent instructions

## Normal development loop

Use this loop for iterative extension development:

1. Keep PowerToys running unelevated unless the user explicitly needs to test interaction with an elevated application.
2. Build and test from the repository root:

   ```powershell
   dotnet test .\tests\AIUsageDock.Tests\AIUsageDock.Tests.csproj -c Debug
   # For the signed MSIX build, follow Current verified workflow below.
   ```

3. Build the signed `Debug` / `x64` MSIX using the Current verified workflow below, then update the one installed package with `Add-AppxPackage`.
4. In Command Palette, run **Reload** with the subtitle **Reload Command Palette Extension**. If Reload does not refresh the Dock, restart `Microsoft.CmdPal.UI.exe` or Command Palette itself.

Do not repeatedly call `Add-AppxPackage`, run `Add-AppDevPackage.ps1`, uninstall/reinstall the MSIX, or increment `Package.appxmanifest` version numbers for ordinary code changes. Those actions create stale Command Palette registrations and can remove or duplicate Dock entries. If a reload does not refresh the extension, restart `Microsoft.CmdPal.UI.exe` or Command Palette itself; restarting only the PowerToys settings process is insufficient. If Windows rejects an in-place package update because the installed package has different contents at the same version, increment the manifest version once for that real package update; a successful package upgrade should still leave one installed package.

When the development signer is already trusted, the built MSIX can be updated from unelevated PowerShell without UAC:

```powershell
Add-AppxPackage -Path .\src\AIUsageDock.Extension\AppPackages\<current-package>\*.msix -ForceApplicationShutdown
```

If this reports an untrusted root, the signer trust setup is incomplete; do not work around it by launching PowerShell with `RunAs` unattended.

Codex and Claude use persistent embedded web sessions by default. Each provider has an independent Web-first/CLI setting. Web-first falls back to the corresponding CLI only when its browser login is unavailable or expired; transient web failures retain stale web data. The CLIs are optional unless selected or needed as an authentication fallback. Configure non-PATH executables with AI_USAGE_CODEX_PATH or AI_USAGE_CLAUDE_PATH. The Dock exposes four stable bands: com.erikbaender.aiusage.codex.session, com.erikbaender.aiusage.codex.weekly, com.erikbaender.aiusage.claude.session, and com.erikbaender.aiusage.claude.weekly.

## Current verified workflow

The local development PFX is password-protected. For an unattended build, export the already-trusted current-user certificate to the ignored PFX file with a random in-memory password, then pass that password to MSBuild:

    $certThumbprint = '6C6DB65B27842D52C0F0AC0EEC3D442585091F2C'
    $cert = Get-ChildItem "Cert:\CurrentUser\My\$certThumbprint"
    $certPassword = [Guid]::NewGuid().ToString('N')
    $securePassword = ConvertTo-SecureString $certPassword -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath .\src\AIUsageDock.Extension\Certificates\AIUsageDock.Dev.pfx -Password $securePassword -Force | Out-Null
    dotnet build .\src\AIUsageDock.Extension\AIUsageDock.Extension.csproj -c Debug -p:Platform=x64 -p:PackageCertificatePassword=$certPassword

Update the resulting MSIX once with Add-AppxPackage. If the installed package has different contents at the same version, increment the manifest version once for that package update. Do not uninstall or repeatedly register the package.

Command Palette's Dock has no per-band width setting. Do not write an unsupported DockSize value into its settings; this Command Palette build resets Large back to Default. If the Command Palette UI exposes a larger Dock size in the future, that is a user-level global setting, not an extension setting. After deployment, reload Command Palette; restart Microsoft.CmdPal.UI.exe only if reload does not refresh it.

Web setup requires no settings-file or token changes. Run **Connect Codex web usage** or **Connect Claude web usage** in Command Palette and authenticate inside the dedicated WebView2 window. A confirmed login closes the window and shows a Windows notification. The provider websites own cookie rotation inside their isolated profiles; the extension never exports cookie values.

## Signing and UAC

Signing/developer-mode setup is a one-time workstation prerequisite. Routine builds and Visual Studio deployment should run in the current user context after that setup.

Never launch an unattended elevated process with `Start-Process -Verb RunAs`, `runas`, or an equivalent command. A UAC prompt cannot be answered by an unattended agent and will leave the operation waiting or timing out. If elevation is required, stop and report the exact administrator-only step to the user.

Do not import development certificates into machine-wide certificate stores from an unattended task. Do not create or export an unprotected or empty-password PFX. Keep local `.pfx` and `.cer` files ignored by Git.

PowerToys administrator mode is unrelated to routine extension deployment. It is only needed when PowerToys must interact with another elevated application. Running PowerToys unelevated is the preferred development configuration.

## Verification checklist

- `dotnet test` passes.
- The Debug MSIX builds without errors.
- Add-AppxPackage completes without an elevation prompt.
- Command Palette Reload is run after deployment, or Microsoft.CmdPal.UI.exe is restarted.
- `Get-AppxPackage -Name Erik.AIUsageDock` reports one installed package.
- Command Palette has one AI Usage Dock provider and four Dock bands: Codex Session, Codex Weekly, Claude Session, and Claude Weekly.
Claude diagnostics are written to the packaged app's user-local path: `%LOCALAPPDATA%\Packages\Erik.AIUsageDock_trvxfnfmwyq8y\LocalCache\Local\AIUsage\claude-cli.log`. The log is bounded and redacts common credential/session identifiers.
