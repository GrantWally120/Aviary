# Getting Aviary onto your PC

**21 October 2026:** the [first Windows CI run](https://github.com/GrantWally120/Aviary/actions/runs/39507147323)
and the [exact SDK pin's CI run](https://github.com/GrantWally120/Aviary/actions/runs/39508704024)
both passed. The [final release-source run](https://github.com/GrantWally120/Aviary/actions/runs/39512372624)
also passed and uploaded the final source-only Windows ZIP. Alpha Build 5 is version 0.5.0,
The Long Run, with all sixteen milestones complete.

## Local build

Open the prepared release folder, extract its ZIP, then double-click `Aviary.exe`
inside the extracted `Aviary` folder. Keep the whole folder together. This does not
replace an existing published copy automatically. Your profile is stored separately.

There are two package types:

- **local-win-x64** includes the existing private Collection copies. Keep it local
  unless Grant approves sharing those projects.
- **source-win-x64** excludes personal projects, thumbnails and game exports.
  The Collection shell remains; unavailable exhibits say **Not in this copy**.

Both packages carry .NET and Windows App SDK. They require Microsoft Edge WebView2
Runtime, normally present on Windows 11. The app is unsigned. macOS is planned later.

## Download a GitHub build

1. Open the Aviary repository on GitHub and choose **Actions**.
2. Choose **Build Aviary (win-x64)**, then a run with a green check.
3. Under **Artifacts**, download **Aviary-BP1-source-win-x64.zip** (Beta Prep 1, from 25 October
   2026; the name follows the build, so a later prep will say BP2). Alpha Build 5 runs use
   **Aviary-AB5-source-win-x64.zip** and earlier pre-release runs **Aviary-AB5-RC-source-win-x64.zip**.
4. Extract it and open **Aviary.exe** inside the **Aviary** folder.

A grey or yellow run is still waiting or building. A red run failed; it does not
replace an already-downloaded copy. If no run exists, the repository has not yet
received the workflow. Download access may require signing into GitHub yourself.

The **Aviary-build-evidence** artifact contains the SHA-256 ZIP checksum and a manifest
listing every packaged file, its size and hash. Evidence also records the source
commit, whether its working tree was dirty, and the SDK version used.

## When a build fails

Open the red run, choose its build job and expand the failed step. Report the first
useful error line. No full machine/environment binary log is uploaded automatically.
The workflow checks settings, player actions, the filter engine and Collection sync
safety before packaging. Package validation fails if runtime files are missing or a
source-only package contains personal Collection payloads.

Artifacts are retained for 14 days. Download a ZIP you want to keep. Each is a full
folder containing both runtimes, so its size is substantial.

## For development

See the root [README](../README.md) for the exact SDK, locked restore, build commands
and release preparation script. Release preparation makes a new staging folder under
`.artifact`; it never overwrites `publish/Aviary` or runs a Collection sync implicitly.
All tests use an explicit scratch profile, separate from the normal user profile.

The workflow uses the official [setup-dotnet](https://github.com/actions/setup-dotnet)
`global-json-file` input and [upload-artifact](https://github.com/actions/upload-artifact)
single-file upload support. A configured workflow is not evidence that CI has passed.
