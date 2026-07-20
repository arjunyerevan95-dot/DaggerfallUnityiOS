# Daggerfall Unity iOS bootstrap

## Repository baseline

This repository preserves the complete reachable history of the mobile-oriented Android fork at the following immutable baseline:

- Source repository: `Vwing/daggerfall-unity-android`
- Source branch: `android`
- Source commit: `0fa65294523a132a0e5389d125f58d6566a1e815`
- Unity editor: `2022.3.62f3`

The Android fork is the starting point because it already contains mobile UI, touch input, file import, and mobile performance work. iOS changes must remain isolated on `port/ios-bootstrap` until the bootstrap gates are satisfied.

## Initial iOS target

- iPhone and iPad
- ARM64 device builds
- IL2CPP
- Metal only
- Landscape left and landscape right
- Minimum iOS 15.0
- Unsigned Xcode export first
- Provisional bundle identifier: `com.arjukstudios.daggerfallunityios`

No commercial Daggerfall game data may be committed to this repository. Users will eventually import their own legally obtained game data into the application sandbox.

## Bootstrap gates

### Gate 0: provenance

Pass conditions:

- `main` resolves to the pinned mobile baseline.
- `port/ios-bootstrap` descends directly from that baseline.
- The original commit ancestry remains available.

### Gate 1: Unity editor import

Pass conditions:

- The project imports in Unity `2022.3.62f3` on macOS.
- Editor compilation completes without unexplained errors.
- Any Android-only compilation failure is isolated and attributed before modification.

### Gate 2: unsigned Xcode export

Pass conditions:

- `DaggerfallUnityIOS.Editor.IOSBuild.BuildFromCommandLine` completes.
- `Build/iOS/Unity-iPhone.xcodeproj` exists.
- The export uses ARM64, IL2CPP, Metal, landscape orientation, and iOS 15.0 or later.

### Gate 3: unsigned native compile

Pass conditions:

- `xcodebuild` compiles the generated project for generic `iphoneos` with signing disabled.
- The first reproducible native blocker is documented if compilation fails.

## Commands

Export the Unity project on a macOS host with Unity iOS Build Support installed:

```bash
bash scripts/export-ios.sh
```

Compile the exported Xcode project without signing:

```bash
bash scripts/build-ios-xcode.sh
```

Both scripts accept environment overrides. See the scripts for variable names and defaults.

## Stop conditions

Stop and record evidence rather than broadening the work when:

- Unity cannot import under the pinned editor version.
- An Android-only dependency lacks an iOS implementation.
- A native plugin requires replacement or source-level porting.
- Xcode export succeeds but native compilation fails for an unexplained reason.
- Progress would require signing, provisioning, installation, runtime gameplay work, mod compatibility work, or voxel-character work.

Those are later gated milestones. A successful bootstrap does not silently authorize every subsequent ambition humans can fit into one repository.
