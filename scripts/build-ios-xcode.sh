#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export_path="${IOS_EXPORT_PATH:-$repo_root/Build/iOS}"
derived_data="${IOS_DERIVED_DATA_PATH:-$repo_root/Build/DerivedData-iOS}"
log_path="${IOS_XCODE_LOG:-$repo_root/Build/logs/ios-xcodebuild.log}"
project="$export_path/Unity-iPhone.xcodeproj"

if [[ ! -d "$project" ]]; then
  echo "Missing Unity Xcode project: $project" >&2
  echo "Run ./scripts/export-ios.sh first." >&2
  exit 2
fi

mkdir -p "$(dirname "$log_path")"

set -o pipefail
xcodebuild \
  -project "$project" \
  -scheme Unity-iPhone \
  -configuration Release \
  -sdk iphoneos \
  -destination 'generic/platform=iOS' \
  -derivedDataPath "$derived_data" \
  CODE_SIGNING_ALLOWED=NO \
  CODE_SIGNING_REQUIRED=NO \
  build 2>&1 | tee "$log_path"

printf 'Unsigned iphoneos compile succeeded. Log: %s\n' "$log_path"
