#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="${PROJECT_PATH:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
build_root="$repo_root/Build"
log_dir="$build_root/logs"
artifact_dir="$build_root/uba-ios-bootstrap"
unity_version="${UNITY_VERSION:-2022.3.62f3}"

mkdir -p "$log_dir" "$artifact_dir"

export IOS_EXPORT_PATH="$build_root/iOS"
export IOS_DERIVED_DATA_PATH="$build_root/DerivedData-iOS"
export IOS_EXPORT_LOG="$log_dir/ios-unity-export.log"
export IOS_XCODE_LOG="$log_dir/ios-xcodebuild.log"

exec > >(tee "$log_dir/uba-prebuild-ios.log") 2>&1

printf 'Daggerfall Unity iOS UBA bootstrap\n'
printf 'Project: %s\n' "$repo_root"
printf 'Unity version: %s\n' "$unity_version"
printf 'Builder OS: %s\n' "${BUILDER_OS:-unknown}"
printf 'Output directory: %s\n' "${OUTPUT_DIRECTORY:-unset}"

if [[ "${BUILDER_OS:-MAC}" != "MAC" ]]; then
  echo 'This experiment requires a macOS Build Automation machine.' >&2
  exit 2
fi

find_editor() {
  local candidate
  local candidates=(
    "${UNITY_EDITOR:-}"
    "/Applications/Unity/Hub/Editor/$unity_version/Unity.app/Contents/MacOS/Unity"
  )

  for candidate in "${candidates[@]}"; do
    if [[ -n "$candidate" && -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  find /Applications "$HOME" /opt \
    -type f \
    -path "*/$unity_version/Unity.app/Contents/MacOS/Unity" \
    -perm -111 \
    2>/dev/null \
    | head -n 1
}

unity_editor="$(find_editor || true)"
if [[ -z "$unity_editor" || ! -x "$unity_editor" ]]; then
  echo "Unable to locate an executable Unity $unity_version Editor on the Build Automation machine." >&2
  exit 3
fi

editor_root="$(dirname "$(dirname "$unity_editor")")"
if [[ ! -d "$editor_root/PlaybackEngines/iOSSupport" ]]; then
  echo "Unity Editor is licensed and present, but iOS Build Support is missing: $editor_root" >&2
  echo 'This is a bounded stop condition for the macOS-carrier experiment.' >&2
  exit 4
fi

printf 'Using Unity Editor: %s\n' "$unity_editor"
"$unity_editor" -version
xcodebuild -version

UNITY_EDITOR="$unity_editor" bash "$repo_root/scripts/export-ios.sh"
bash "$repo_root/scripts/build-ios-xcode.sh"

tar -czf "$artifact_dir/daggerfall-unity-ios-xcode-project.tar.gz" -C "$build_root" iOS
cp -R "$log_dir" "$artifact_dir/logs"

if [[ -n "${OUTPUT_DIRECTORY:-}" ]]; then
  output_extra="$OUTPUT_DIRECTORY/extra_data/daggerfall-ios-bootstrap"
  mkdir -p "$output_extra"
  cp -R "$artifact_dir/." "$output_extra/"
fi

printf 'UBA unsigned iOS bootstrap completed successfully.\n'
