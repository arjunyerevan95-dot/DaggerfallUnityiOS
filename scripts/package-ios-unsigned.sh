#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
derived_data="${IOS_DERIVED_DATA_PATH:-$repo_root/Build/DerivedData-iOS}"
products_dir="${IOS_PRODUCTS_DIR:-$derived_data/Build/Products/Release-iphoneos}"
output_dir="${IOS_PACKAGE_OUTPUT_DIR:-$repo_root/Build/uba-ios-bootstrap/unsigned-ios-package}"
expected_bundle_id="${IOS_EXPECTED_BUNDLE_ID:-com.arjukstudios.daggerfallunityios}"
expected_minimum_os="${IOS_EXPECTED_MINIMUM_OS:-15.0}"
expected_platform="iPhoneOS"

app_path="${IOS_APP_PATH:-$products_dir/DaggerfallUnity.app}"
if [[ ! -d "$app_path" ]]; then
  app_candidates=()
  while IFS= read -r candidate; do
    app_candidates+=("$candidate")
  done < <(find "$products_dir" -maxdepth 1 -type d -name '*.app' -print 2>/dev/null | sort)

  if (( ${#app_candidates[@]} != 1 )); then
    echo "Expected exactly one unsigned iOS app under $products_dir; found ${#app_candidates[@]}." >&2
    if (( ${#app_candidates[@]} == 0 )); then
      echo 'Candidate: <none>' >&2
    else
      printf 'Candidate: %s\n' "${app_candidates[@]}" >&2
    fi
    exit 2
  fi
  app_path="${app_candidates[0]}"
fi

info_plist="$app_path/Info.plist"
if [[ ! -f "$info_plist" ]]; then
  echo "Missing generated app Info.plist: $info_plist" >&2
  exit 3
fi

plist_value() {
  /usr/libexec/PlistBuddy -c "Print :$1" "$info_plist"
}

bundle_id="$(plist_value CFBundleIdentifier)"
minimum_os="$(plist_value MinimumOSVersion)"
executable_name="$(plist_value CFBundleExecutable)"
supported_platform="$(plist_value CFBundleSupportedPlatforms:0)"
app_binary="$app_path/$executable_name"

if [[ "$bundle_id" != "$expected_bundle_id" ]]; then
  echo "Unexpected bundle identifier: $bundle_id (expected $expected_bundle_id)." >&2
  exit 4
fi

if [[ "$minimum_os" != "$expected_minimum_os" ]]; then
  echo "Unexpected minimum iOS version: $minimum_os (expected $expected_minimum_os)." >&2
  exit 5
fi

if [[ "$supported_platform" != "$expected_platform" ]]; then
  echo "Unexpected supported platform: $supported_platform (expected $expected_platform)." >&2
  exit 6
fi

if [[ ! -f "$app_binary" ]]; then
  echo "Missing generated app executable: $app_binary" >&2
  exit 7
fi

architectures="$(/usr/bin/lipo -archs "$app_binary")"
if [[ " $architectures " != *" arm64 "* ]]; then
  echo "Generated app is missing ARM64: $architectures" >&2
  exit 8
fi

if [[ " $architectures " == *" x86_64 "* || " $architectures " == *" i386 "* ]]; then
  echo "Generated device app unexpectedly contains simulator architecture(s): $architectures" >&2
  exit 9
fi

codesign_log="$(mktemp)"
trap 'rm -f "$codesign_log"' EXIT
if [[ -d "$app_path/_CodeSignature" ]] || /usr/bin/codesign -dv --verbose=4 "$app_path" >"$codesign_log" 2>&1; then
  echo "Generated app is signed, but this milestone requires a strictly unsigned product." >&2
  cat "$codesign_log" >&2 || true
  exit 10
fi

rm -rf "$output_dir"
mkdir -p "$output_dir"
staging_dir="$(mktemp -d "${TMPDIR:-/tmp}/daggerfall-ios-package.XXXXXX")"
trap 'rm -f "$codesign_log"; rm -rf "$staging_dir"' EXIT

app_name="$(basename "$app_path")"
artifact_stem="DaggerfallUnity-ios-arm64-unsigned"
app_archive="$output_dir/$artifact_stem.app.tar.gz"
ipa_path="$output_dir/$artifact_stem.ipa"
manifest_path="$output_dir/unsigned-package-manifest.txt"
checksums_path="$output_dir/SHA256SUMS"

/usr/bin/tar -czf "$app_archive" -C "$(dirname "$app_path")" "$app_name"
mkdir -p "$staging_dir/Payload"
/usr/bin/ditto --norsrc "$app_path" "$staging_dir/Payload/$app_name"
/usr/bin/ditto -c -k --norsrc --keepParent "$staging_dir/Payload" "$ipa_path"

if ! /usr/bin/unzip -Z1 "$ipa_path" | /usr/bin/grep -Fxq "Payload/$app_name/Info.plist"; then
  echo "Unsigned IPA does not contain the expected Payload/$app_name/Info.plist entry." >&2
  exit 11
fi

source_commit="${GIT_COMMIT:-$(git -C "$repo_root" rev-parse HEAD)}"
unity_build_number="${BUILD_NUMBER:-${CLOUD_BUILD_NUMBER:-${UNITY_CLOUD_BUILD_NUMBER:-${BUILD_ID:-unknown}}}}"
binary_description="$(/usr/bin/file "$app_binary")"
xcode_version="$(xcodebuild -version | tr '\n' ' ' | sed 's/[[:space:]]*$//')"

cat > "$manifest_path" <<EOF_MANIFEST
artifact_kind=unsigned-ios-package
source_commit=$source_commit
unity_version=${UNITY_VERSION:-2022.3.62f3}
unity_build_number=$unity_build_number
xcode=$xcode_version
app=$app_name
bundle_identifier=$bundle_id
minimum_os=$minimum_os
supported_platform=$supported_platform
architectures=$architectures
code_signature=absent
binary=$binary_description
ipa_payload=Payload/$app_name
warning=This IPA is unsigned and is not installable until separately signed outside this milestone.
EOF_MANIFEST

dsym_path="$products_dir/$app_name.dSYM"
if [[ -d "$dsym_path" ]]; then
  /usr/bin/tar -czf "$output_dir/$artifact_stem.app.dSYM.tar.gz" -C "$products_dir" "$app_name.dSYM"
fi

(
  cd "$output_dir"
  /usr/bin/shasum -a 256 ./* > "$(basename "$checksums_path").tmp"
  mv "$(basename "$checksums_path").tmp" "$(basename "$checksums_path")"
)

printf 'Unsigned iOS package created.\n'
printf 'App archive: %s\n' "$app_archive"
printf 'IPA: %s\n' "$ipa_path"
printf 'Manifest: %s\n' "$manifest_path"
printf 'Architectures: %s\n' "$architectures"
printf 'Bundle identifier: %s\n' "$bundle_id"
printf 'Minimum iOS: %s\n' "$minimum_os"
