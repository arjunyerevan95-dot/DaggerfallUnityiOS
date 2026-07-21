#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="${PROJECT_DIRECTORY:-${PROJECT_PATH:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}}"
build_root="$repo_root/Build"
log_dir="$build_root/logs"
artifact_dir="$build_root/uba-ios-bootstrap"
unity_version_raw="${UNITY_VERSION:-2022.3.62f3}"
unity_version="${unity_version_raw//_/.}"

mkdir -p "$log_dir" "$artifact_dir"

exec > >(tee "$log_dir/uba-prebuild-ios.log") 2>&1

printf 'Daggerfall Unity iOS UBA bootstrap preflight\n'
printf 'Project: %s\n' "$repo_root"
printf 'Unity version: %s\n' "$unity_version"
printf 'Builder OS: %s\n' "${BUILDER_OS:-unknown}"
printf 'Output directory: %s\n' "${OUTPUT_DIRECTORY:-unset}"
printf 'UBA Unity executable: %s\n' "${UNITY_EXE:-unset}"

if [[ "${BUILDER_OS:-MAC}" != "MAC" ]]; then
  echo 'This experiment requires a macOS Build Automation machine.' >&2
  exit 2
fi

find_editor() {
  local candidate
  local candidates=(
    "${UNITY_EXE:-}"
    "${UNITY_EDITOR:-}"
    "/Applications/Unity/Hub/Editor/$unity_version/Unity.app/Contents/MacOS/Unity"
  )

  for candidate in "${candidates[@]}"; do
    if [[ -n "$candidate" && -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  find /Applications "$HOME" /opt /BUILD_PATH /Volumes \
    -type f \
    -path '*/Unity.app/Contents/MacOS/Unity' \
    -perm -111 \
    2>/dev/null \
    | head -n 1
}

unity_editor="$(find_editor || true)"
if [[ -z "$unity_editor" || ! -x "$unity_editor" ]]; then
  echo "Unable to locate an executable Unity $unity_version Editor on the Build Automation machine." >&2
  echo "UNITY_EXE=${UNITY_EXE:-unset}" >&2
  exit 3
fi

editor_root="$(dirname "$(dirname "$unity_editor")")"
if [[ ! -d "$editor_root/PlaybackEngines/iOSSupport" ]]; then
  echo "Unity Editor is licensed and present, but iOS Build Support is missing: $editor_root" >&2
  echo 'This is a bounded stop condition for the macOS-carrier experiment.' >&2
  exit 4
fi

printf 'Using Unity Editor: %s\n' "$unity_editor"
xcodebuild -version

cat > "$artifact_dir/preflight-environment.txt" <<EOF
Unity version: $unity_version
Unity executable: $unity_editor
Builder OS: ${BUILDER_OS:-unknown}
Xcode: $(xcodebuild -version | tr '\n' ' ')
EOF

printf 'UBA pre-build checks completed. The configured Unity pre-export method will perform the iOS Xcode export.\n'
