# Daggerfall Unity iOS bootstrap

## Repository baseline

This repository preserves the complete reachable history of the mobile-oriented Android fork at the following immutable baseline:

- Source repository: `Vwing/daggerfall-unity-android`
- Source branch: `android`
- Source commit: `0fa65294523a132a0e5389d125f58d6566a1e815`
- Unity editor: `2022.3.62f3`

The Android fork is the starting point because it already contains mobile UI, touch input, file import, and mobile performance work. iOS changes remain isolated on `port/ios-bootstrap` while the bounded bootstrap and unsigned-packaging checkpoints are validated.

## Initial iOS target

- iPhone and iPad
- ARM64 device builds
- IL2CPP
- Metal only
- Landscape left and landscape right
- Minimum iOS 15.0
- Bundle identifier `com.arjukstudios.daggerfallunityios`
- Unsigned Xcode export and generic-device compile before any signing or installation work

No commercial Daggerfall game data may be committed to this repository. Users will eventually import their own legally obtained game data into the application sandbox.

## Unity Build Automation execution path

The active experiment uses a Unity Build Automation **macOS Standard** target as a licensed carrier environment. Its repository hooks perform the bounded iOS work around the normal macOS carrier build:

1. Verify that Build Automation provisioned Unity `2022.3.62f3` on macOS.
2. Verify that the Editor includes iOS Build Support and that Xcode is present.
3. Run `DaggerfallUnityIOS.Editor.IOSBuild.BuildFromCloudPreExport` to create `Build/iOS/Unity-iPhone.xcodeproj`.
4. Compile the generated Release project for generic `iphoneos` with signing disabled.
5. Validate and package the resulting unsigned iOS `.app` and `.ipa` artifacts.
6. Copy the generated Xcode project, logs, manifests, checksums, and package artifacts into Build Automation `extra_data`.

This does not turn the macOS carrier artifact into an iOS application. The iOS result is produced separately by the pre-export and post-build hooks.

### Active Build Automation configuration

- Target name: `ios-bootstrap`
- Branch: `port/ios-bootstrap`
- Platform: macOS
- Builder: macOS Standard
- Unity: `2022.3.62f3`
- Auto-build: enabled
- Auto-cancel: enabled
- Scheduled builds: disabled
- Pre-build script: `scripts/uba-prebuild-ios.sh`
- Pre-export method: `DaggerfallUnityIOS.Editor.IOSBuild.BuildFromCloudPreExport`
- Post-build script: `scripts/uba-postbuild-ios.sh`

Do not add Apple signing credentials to this carrier target.

## Iteration evidence

- Build #1 proved that the configured pre-build hook runs on the managed macOS builder.
- Build #2 proved that the licensed Unity Editor can import and compile the project.
- Build #3 exposed the Android-only `NativeFilePickerNamespace` dependency.
- Builds #4 through #8 advanced the real iOS IL2CPP export and isolated the optional runtime C# compiler addon as incompatible with iOS because its `System.CodeDom` APIs are unavailable.
- Build #9 proved the iOS-only runtime-compiler boundary and passed the unsigned Xcode export. It reached the final ARM64 native link and isolated missing `MobileCoreServices.framework` linkage for two NativeFilePicker UTI symbols.
- Build #10 linked `MobileCoreServices.framework` into `UnityFramework`, exported the iOS project with zero errors, and completed unsigned Release `xcodebuild` for `arm64-apple-ios15.0` with `** BUILD SUCCEEDED **`.

Build #10 completes the original unsigned bootstrap milestone.

## Bootstrap gates

### Gate 0: provenance

Passed.

- `main` resolves to the pinned mobile baseline.
- `port/ios-bootstrap` descends from that baseline.
- The original commit ancestry remains available.

### Gate 1: Unity editor import and player script compilation

Passed.

- Unity `2022.3.62f3` imports the project on the managed macOS builder.
- iOS player script compilation passes.
- Android-only and runtime-compiler incompatibilities are isolated behind explicit platform boundaries.

### Gate 2: unsigned Xcode export

Passed in Build #9 and reconfirmed in Build #10.

- `BuildFromCloudPreExport` completes.
- `Build/iOS/Unity-iPhone.xcodeproj` exists.
- The export uses ARM64, IL2CPP, Metal, landscape orientations, and iOS 15.0.

### Gate 3: unsigned native compile

Passed in Build #10.

- Release `xcodebuild` targets generic `iphoneos`.
- `CODE_SIGNING_ALLOWED=NO` and `CODE_SIGNING_REQUIRED=NO` are applied.
- The generated app links for `arm64-apple-ios15.0`.
- Xcode reports `** BUILD SUCCEEDED **`.

## Unsigned packaging checkpoint

The next bounded checkpoint packages the successful Build #10 product without signing it.

`scripts/package-ios-unsigned.sh` must:

- Locate the Release device product under `Build/DerivedData-iOS/Build/Products/Release-iphoneos`.
- Require bundle identifier `com.arjukstudios.daggerfallunityios`.
- Require minimum iOS version `15.0`.
- Require platform `iPhoneOS` and architecture `arm64`.
- Reject simulator architectures.
- Reject any pre-existing code signature.
- Produce an unsigned `.app.tar.gz` archive.
- Produce an unsigned `.ipa` with `Payload/DaggerfallUnity.app` layout.
- Produce a manifest and SHA-256 checksums.
- Optionally archive the generated dSYM when present.

The package is explicitly **not installable until separately signed**. This checkpoint does not authorize signing, provisioning, installation, launch testing, game-data import, gameplay validation, touch-control redesign, mod work, or voxel-character work.

Build #11 should prove that the unsigned package is reproducible and copied into Build Automation `extra_data/daggerfall-ios-bootstrap/unsigned-ios-package`.

## Local reproduction

Export the Unity project:

```bash
bash scripts/export-ios.sh
```

Compile the exported Xcode project without signing:

```bash
bash scripts/build-ios-xcode.sh
```

Package the successful unsigned app and IPA:

```bash
bash scripts/package-ios-unsigned.sh
```

The scripts accept environment overrides documented in their source.

## Stop conditions

Stop and record evidence rather than broadening the work when:

- The generated Release device app is missing.
- Bundle identifier, minimum iOS version, supported platform, or architecture differs from the pinned target.
- The product unexpectedly contains simulator architecture slices.
- The product is already signed.
- The unsigned IPA does not contain the expected `Payload/DaggerfallUnity.app` structure.
- Progress would require signing, provisioning, installation, runtime gameplay work, game-data import, mod compatibility work, or voxel-character work.

A successful unsigned package is an artifact checkpoint, not proof that the application installs or runs on physical hardware. Humans do love promoting a ZIP file to “finished product” before it has met a device.
