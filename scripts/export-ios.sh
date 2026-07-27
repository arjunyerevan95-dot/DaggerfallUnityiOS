#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity}"
export_path="${IOS_EXPORT_PATH:-$repo_root/Build/iOS}"
log_path="${IOS_EXPORT_LOG:-$repo_root/Build/logs/ios-unity-export.log}"

if [[ ! -x "$unity_editor" ]]; then
  echo "Unity editor not found or not executable: $unity_editor" >&2
  echo "Set UNITY_EDITOR to the Unity 2022.3.62f3 executable." >&2
  exit 2
fi

mkdir -p "$(dirname "$log_path")"

export IOS_EXPORT_PATH="$export_path"

"$unity_editor" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$repo_root" \
  -executeMethod DaggerfallUnityIOS.Editor.IOSBuild.BuildFromCommandLine \
  -logFile "$log_path"

xcode_project="$export_path/Unity-iPhone.xcodeproj"
if [[ ! -d "$xcode_project" ]]; then
  echo "Unity completed without producing $xcode_project" >&2
  echo "Review: $log_path" >&2
  exit 3
fi

printf 'Created iOS Xcode project: %s\n' "$xcode_project"
printf 'Unity export log: %s\n' "$log_path"
