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

## Current execution strategy

The ordinary Unity Build Automation iOS configuration requires an Apple signing credential set. The project owner does not currently have an Apple Developer Program membership, so the normal signed-iOS target is intentionally not used for this bootstrap milestone.

The active experiment uses a Unity Build Automation **macOS Standard** target as a licensed carrier environment. Its repository hooks perform the bounded Gate 1 through Gate 3 work around the normal macOS carrier build:

1. Verify that Build Automation provisioned Unity `2022.3.62f3` on a macOS builder.
2. Verify that the provisioned Editor includes iOS Build Support.
3. Run `DaggerfallUnityIOS.Editor.IOSBuild.BuildFromCloudPreExport` to export `Build/iOS/Unity-iPhone.xcodeproj`.
4. Compile the generic `iphoneos` target with signing disabled from the post-build hook.
5. Package the Xcode project and logs under Build Automation's `extra_data` artifacts.

This is an experimental use of supported Build Automation script hooks. It does not claim that a macOS target is an iOS distribution build. It exists only to determine whether the licensed builder image can perform the unsigned bootstrap gates without Apple credentials.

### Unity Build Automation configuration

Active configuration:

- Target name: `ios-bootstrap`
- Branch: `port/ios-bootstrap`
- Project subfolder path: blank
- Platform: macOS
- Auto-detect Unity version: enabled
- Detected Unity version: `2022.3.62f3`
- Builder: macOS Standard
- Auto-build: enabled
- Auto-cancel: enabled
- Scheduled builds: disabled
- Pre-build script path: `scripts/uba-prebuild-ios.sh`
- Post-build script path: `scripts/uba-postbuild-ios.sh`
- Pre-export method: `DaggerfallUnityIOS.Editor.IOSBuild.BuildFromCloudPreExport`

Every new commit on `port/ios-bootstrap` should now enqueue a fresh build automatically. Auto-cancel should prevent an obsolete queued or running revision from consuming further build time after a newer fix is pushed.

Do not add Apple signing credentials to this carrier target.

### Bounded experiment outcomes

- If the managed Editor includes iOS Build Support, the script proceeds to the first actual Unity import or iOS native blocker.
- If iOS Build Support is absent, the script stops explicitly and records that fact. Do not silently install unrelated toolchains or broaden the milestone.
- A successful unsigned compile does not produce an installable IPA and does not authorize signing, packaging, installation, runtime, mod, or voxel work.

### Iteration evidence

- Build #1 proved that the configured pre-build hook runs on the managed macOS builder.
- Build #2 proved that the licensed Unity `2022.3.62f3` Editor can import and compile the project after correcting the Build Automation Editor-path discovery and Unity API mismatches.
- Build #3 reached iOS player compilation and exposed the first Android-only dependency: `NativeFilePickerNamespace` referenced by `TouchscreenLayoutsManager`.
- Builds #4 through #8 advanced the real iOS IL2CPP export and isolated the optional runtime C# compiler addon as incompatible with iOS because its `System.CodeDom` APIs are unavailable.
- Build #9 proved that the iOS-only runtime compiler boundary works. Unity completed the iOS export with `result=Succeeded`, zero errors, and produced `Build/iOS/Unity-iPhone.xcodeproj`.
- Build #9 also proved that the post-build hook invokes unsigned Release `xcodebuild` for generic `iphoneos` with `CODE_SIGNING_ALLOWED=NO` and `CODE_SIGNING_REQUIRED=NO`.
- Build #9 reached the final ARM64 `UnityFramework` link. Its only fatal native blocker was missing `MobileCoreServices` linkage for `UTTypeCreatePreferredIdentifierForTag` and `kUTTagClassFilenameExtension`, both referenced by the retained NativeFilePicker iOS source.
- The current branch adds a generated-Xcode-project postprocessor that links `MobileCoreServices.framework` only to the `UnityFramework` target. It also packages the Xcode project and logs before propagating any native-build failure so future evidence is not lost to shell `errexit` behavior.

Build #10 should prove that the NativeFilePicker UTI symbols resolve and that the unsigned generic-device Xcode build either succeeds or stops at the next unrelated native incompatibility.

## Retired GitHub-hosted licensing experiment

The GitHub-hosted macOS workflow proved that Unity `2022.3.62f3`, iOS Build Support, and Unity Hub can be installed on an ephemeral runner. It also proved that current Unity Personal activation requires an interactive signed-in user session. The official CLI reported `NOT_SIGNED_IN`; Unity ID email/password secrets could not activate the Personal entitlement headlessly.

That workflow remains historical evidence only. `UNITY_USERNAME` and `UNITY_PASSWORD` repository secrets are not required for the current Build Automation carrier experiment and should not be retained.

## Bootstrap gates

### Gate 0: provenance

Pass conditions:

- `main` resolves to the pinned mobile baseline.
- `port/ios-bootstrap` descends directly from that baseline.
- The original commit ancestry remains available.

### Gate 1: Unity editor import

Pass conditions:

- The project imports in Unity `2022.3.62f3` on the managed macOS builder.
- Editor compilation completes without unexplained errors.
- Any Android-only compilation failure is isolated and attributed before modification.

### Gate 2: unsigned Xcode export

Pass conditions:

- `DaggerfallUnityIOS.Editor.IOSBuild.BuildFromCloudPreExport` completes.
- `Build/iOS/Unity-iPhone.xcodeproj` exists.
- The export uses ARM64, IL2CPP, Metal, landscape orientation, and iOS 15.0 or later.

Build #9 satisfies Gate 2.

### Gate 3: unsigned native compile

Pass conditions:

- `xcodebuild` compiles the generated project for generic `iphoneos` with signing disabled.
- The first reproducible native blocker is documented if compilation fails.

Build #9 reached the final ARM64 link and documented the missing NativeFilePicker framework dependency. Gate 3 remains open until the unsigned link completes.

## Local reproduction fallback

The following commands remain available on any macOS host with Unity `2022.3.62f3` and iOS Build Support installed.

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
- The managed macOS target lacks iOS Build Support.
- An Android-only dependency lacks an iOS implementation.
- A native plugin requires replacement or source-level porting.
- Xcode export succeeds but native compilation fails for an unexplained reason.
- Progress would require signing, provisioning, installation, runtime gameplay work, mod compatibility work, or voxel-character work.

Those are later gated milestones. A successful bootstrap does not silently authorize every subsequent ambition humans can fit into one repository.
