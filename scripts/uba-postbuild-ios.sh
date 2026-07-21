#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="${PROJECT_DIRECTORY:-${PROJECT_PATH:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}}"
build_root="$repo_root/Build"
export_path="$build_root/iOS"
log_dir="$build_root/logs"
artifact_dir="$build_root/uba-ios-bootstrap"
xcode_project="$export_path/Unity-iPhone.xcodeproj"
primary_download_bundle="$build_root/DaggerfallUnity-iOS-unsigned-package.zip"

mkdir -p "$log_dir" "$artifact_dir"
exec > >(tee "$log_dir/uba-postbuild-ios.log") 2>&1

if [[ ! -d "$xcode_project" ]]; then
  echo "Unity pre-export did not produce the expected Xcode project: $xcode_project" >&2
  exit 2
fi

export IOS_EXPORT_PATH="$export_path"
export IOS_DERIVED_DATA_PATH="$build_root/DerivedData-iOS"
export IOS_XCODE_LOG="$log_dir/ios-xcodebuild.log"

printf 'Running unsigned iphoneos compile from Unity Build Automation post-build hook.\n'
set +e
bash "$repo_root/scripts/build-ios-xcode.sh"
xcode_status=$?
set -e

printf '%s\n' "$xcode_status" > "$artifact_dir/ios-xcodebuild-exit-code.txt"

package_status=125
if (( xcode_status == 0 )); then
  printf 'Packaging the unsigned iOS app and IPA artifacts.\n'
  export IOS_PACKAGE_OUTPUT_DIR="$artifact_dir/unsigned-ios-package"
  set +e
  bash "$repo_root/scripts/package-ios-unsigned.sh"
  package_status=$?
  set -e
else
  echo 'Skipping unsigned app packaging because xcodebuild did not succeed.' >&2
fi
printf '%s\n' "$package_status" > "$artifact_dir/ios-package-exit-code.txt"

rm -f "$artifact_dir/daggerfall-unity-ios-xcode-project.tar.gz"
tar -czf "$artifact_dir/daggerfall-unity-ios-xcode-project.tar.gz" -C "$build_root" iOS

rm -rf "$artifact_dir/logs"
cp -R "$log_dir" "$artifact_dir/logs"

delivery_status=125
rm -f "$primary_download_bundle"
if (( package_status == 0 )); then
  bundle_stage="$(mktemp -d "${TMPDIR:-/tmp}/daggerfall-ios-download.XXXXXX")"
  trap 'rm -rf "$bundle_stage"' EXIT

  cp -R "$artifact_dir/unsigned-ios-package" "$bundle_stage/"
  cp "$artifact_dir/ios-xcodebuild-exit-code.txt" "$bundle_stage/"
  cp "$artifact_dir/ios-package-exit-code.txt" "$bundle_stage/"
  cat > "$bundle_stage/README.txt" <<'EOF_README'
Daggerfall Unity iOS unsigned package

This archive contains an unsigned ARM64 iOS app and IPA generated with code signing disabled.
It is not installable until separately signed outside this bounded build checkpoint.
No installation or runtime behavior has been validated.
EOF_README

  set +e
  (
    cd "$bundle_stage"
    /usr/bin/zip -qry "$primary_download_bundle" .
  )
  delivery_status=$?
  set -e

  if (( delivery_status == 0 )); then
    /usr/bin/unzip -tq "$primary_download_bundle"
  fi
fi
printf '%s\n' "$delivery_status" > "$artifact_dir/ios-artifact-delivery-exit-code.txt"

if [[ -n "${OUTPUT_DIRECTORY:-}" ]]; then
  output_extra="$OUTPUT_DIRECTORY/extra_data/daggerfall-ios-bootstrap"
  mkdir -p "$output_extra"
  cp -R "$artifact_dir/." "$output_extra/"
  printf 'Copied full unsigned iOS evidence to: %s\n' "$output_extra"

  if (( delivery_status == 0 )); then
    primary_bundle_destination="$OUTPUT_DIRECTORY/$(basename "$primary_download_bundle")"
    cp "$primary_download_bundle" "$primary_bundle_destination"
    test -f "$primary_bundle_destination"
    printf 'Copied user-downloadable unsigned iOS package beside the primary carrier artifact: %s\n' "$primary_bundle_destination"
    printf 'Primary output directory now contains:\n'
    find "$OUTPUT_DIRECTORY" -maxdepth 1 -mindepth 1 -print | sort
  fi
else
  echo 'Build Automation did not provide OUTPUT_DIRECTORY; evidence remains under Build/uba-ios-bootstrap.' >&2
  if (( delivery_status == 0 )); then
    delivery_status=126
    printf '%s\n' "$delivery_status" > "$artifact_dir/ios-artifact-delivery-exit-code.txt"
  fi
fi

if (( xcode_status != 0 )); then
  echo "Unsigned iphoneos compile failed with exit code $xcode_status; evidence was packaged before stopping." >&2
  exit "$xcode_status"
fi

if (( package_status != 0 )); then
  echo "Unsigned iOS packaging failed with exit code $package_status; evidence was packaged before stopping." >&2
  exit "$package_status"
fi

if (( delivery_status != 0 )); then
  echo "Primary artifact delivery failed with exit code $delivery_status." >&2
  exit "$delivery_status"
fi

printf 'UBA unsigned iOS bootstrap, packaging, and primary artifact delivery completed successfully.\n'
