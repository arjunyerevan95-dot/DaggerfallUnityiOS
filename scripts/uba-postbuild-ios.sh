#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="${PROJECT_DIRECTORY:-${PROJECT_PATH:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}}"
build_root="$repo_root/Build"
export_path="$build_root/iOS"
log_dir="$build_root/logs"
artifact_dir="$build_root/uba-ios-bootstrap"
xcode_project="$export_path/Unity-iPhone.xcodeproj"

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

if [[ -n "${OUTPUT_DIRECTORY:-}" ]]; then
  output_extra="$OUTPUT_DIRECTORY/extra_data/daggerfall-ios-bootstrap"
  mkdir -p "$output_extra"
  cp -R "$artifact_dir/." "$output_extra/"
  printf 'Copied unsigned iOS bootstrap evidence and package artifacts to: %s\n' "$output_extra"
else
  echo 'Build Automation did not provide OUTPUT_DIRECTORY; evidence remains under Build/uba-ios-bootstrap.' >&2
fi

if (( xcode_status != 0 )); then
  echo "Unsigned iphoneos compile failed with exit code $xcode_status; evidence was packaged before stopping." >&2
  exit "$xcode_status"
fi

if (( package_status != 0 )); then
  echo "Unsigned iOS packaging failed with exit code $package_status; evidence was packaged before stopping." >&2
  exit "$package_status"
fi

printf 'UBA unsigned iOS bootstrap and packaging completed successfully.\n'
