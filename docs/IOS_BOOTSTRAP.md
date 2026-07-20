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

## Primary execution environment

The primary Gate 1 through Gate 3 environment is the GitHub-hosted `macos-15` runner defined in `.github/workflows/ios-github-runner.yml`.

That workflow:

1. Verifies the pinned baseline ancestry and exact Unity version.
2. Installs and caches Unity `2022.3.62f3` with iOS Build Support through GameCI.
3. Runs `DaggerfallUnityIOS.Editor.IOSBuild.BuildFromCommandLine`.
4. Verifies the generated Xcode project.
5. Compiles the generic `iphoneos` target with signing disabled.
6. Uploads the Xcode project and build logs as workflow evidence.

The workflow accepts either:

- `UNITY_LICENSE`, or
- `UNITY_SERIAL`, `UNITY_EMAIL`, and `UNITY_PASSWORD`

as repository Actions secrets. It fails explicitly before editor installation when neither credential form is available.

## Bootstrap gates

### Gate 0: provenance

Pass conditions:

- `main` resolves to the pinned mobile baseline.
- `port/ios-bootstrap` descends directly from that baseline.
- The original commit ancestry remains available.

### Gate 1: Unity editor import

Pass conditions:

- The project imports in Unity `2022.3.62f3` on the GitHub macOS runner.
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

## Local reproduction fallback

A physical Mac is not required for the primary workflow. The following commands remain available for reproducing a runner failure on any macOS host with Unity `2022.3.62f3` and iOS Build Support installed.

Export the Unity project:

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
