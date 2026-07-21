# Daggerfall Unity iOS first-run data import

## Device evidence before this checkpoint

The unsigned Build #23 IPA was re-signed and installed with SideStore on an iPhone. The app launched, rendered the custom Daggerfall Unity splash through Metal, and then remained on a black screen after the splash faded.

The IPA intentionally contains no commercial Daggerfall data. The preserved Android fork normally handles this state with a ZIP-import setup screen, but the iOS build still entered the desktop folder-browser path and the temporary iOS compatibility shim returned no file from picker requests.

## Bounded runtime checkpoint

This checkpoint adds an iOS-only first-run runtime layer that:

1. Writes Unity and application diagnostics to `DaggerfallUnity-iOS.log` under the app sandbox.
2. Displays a visible fallback interface even if the legacy setup wizard renders nothing.
3. Opens the existing native iOS document picker.
4. Accepts a user-selected ZIP containing legally obtained Daggerfall files and an `arena2` folder.
5. Reuses the fork's ZIP extraction, nested `arena2` discovery, case-insensitive validation, and `PACKED.DAT` expansion utilities.
6. Copies validated files into the persistent app sandbox.
7. Saves the selected Daggerfall path and loads the game scene.
8. Allows the diagnostic log to be exported through the iOS document picker when startup or import fails.

No commercial game data is committed or bundled.

## Accepted archive layouts

The importer supports the same layouts as the Android fork, including:

```text
archive.zip/arena2/...
archive.zip/Daggerfall/arena2/...
archive.zip/<one or more parent folders>/arena2/...
```

Archives containing `PACKED.DAT` can be expanded using the existing fork utility when the unpacked core files are absent.

## Device test procedure

1. Download the authoritative Unity Build Automation artifact for the current commit.
2. Extract the nested unsigned IPA.
3. Re-sign and install it with SideStore.
4. Launch the app.
5. Confirm that a visible iOS first-run panel appears after the splash instead of an unexplained black screen.
6. Tap **Import Daggerfall Data ZIP** and choose a ZIP created from the user's legally obtained Daggerfall files.
7. Observe unzip, validation, and copy progress.
8. Confirm that the Daggerfall Unity main menu appears.

If the importer reports an error, tap **Export Diagnostic Log** and preserve the exported log as evidence.

## Success condition

This checkpoint passes only when a physical iPhone reaches the Daggerfall Unity main menu using imported user-owned game data.

## Stop conditions

Stop and report evidence if:

- the iOS document picker does not appear;
- the picker returns an inaccessible file;
- ZIP extraction or validation fails unexpectedly;
- imported data validates but the game scene does not reach the main menu;
- progress would require gameplay fixes, control redesign, mod compatibility work, or voxel-character work.

Reaching the main menu does not authorize gameplay validation or any broader runtime milestone. Humans have a remarkable ability to interpret one successful menu as an entire shipped game.
