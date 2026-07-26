#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository="${GITHUB_RELEASE_REPOSITORY:-arjunyerevan95-dot/DaggerfallUnityiOS}"
source_commit="${GIT_COMMIT:-$(git -C "$repo_root" rev-parse HEAD)}"
short_commit="${source_commit:0:12}"
raw_build_number="${BUILD_NUMBER:-${CLOUD_BUILD_NUMBER:-${UNITY_CLOUD_BUILD_NUMBER:-${BUILD_ID:-unknown}}}}"
safe_build_number="$(printf '%s' "$raw_build_number" | tr -c '[:alnum:]._- ' '-' | tr ' ' '-')"
[[ -n "$safe_build_number" ]] || safe_build_number="unknown"
release_tag="${GITHUB_RELEASE_TAG:-ios-build-${safe_build_number}-${short_commit}}"
release_name="${GITHUB_RELEASE_NAME:-Daggerfall Unity iOS - Build ${raw_build_number} (${short_commit})}"
api_root="https://api.github.com/repos/$repository"

ipa_name="DaggerfallUnity-ios-build-${safe_build_number}-${short_commit}-arm64-unsigned.ipa"
evidence_name="DaggerfallUnity-iOS-build-${safe_build_number}-${short_commit}-evidence.zip"
checksums_name="SHA256SUMS-${safe_build_number}-${short_commit}.txt"
manifest_name="unsigned-package-manifest-${safe_build_number}-${short_commit}.txt"

: "${GITHUB_RELEASE_TOKEN:?GITHUB_RELEASE_TOKEN is required for GitHub release publishing}"
: "${GITHUB_RELEASE_IPA:?GITHUB_RELEASE_IPA must point to the unsigned IPA}"
: "${GITHUB_RELEASE_EVIDENCE:?GITHUB_RELEASE_EVIDENCE must point to the build evidence ZIP}"
: "${GITHUB_RELEASE_CHECKSUMS:?GITHUB_RELEASE_CHECKSUMS must point to SHA256SUMS}"
: "${GITHUB_RELEASE_MANIFEST:?GITHUB_RELEASE_MANIFEST must point to the package manifest}"

for required_file in \
  "$GITHUB_RELEASE_IPA" \
  "$GITHUB_RELEASE_EVIDENCE" \
  "$GITHUB_RELEASE_CHECKSUMS" \
  "$GITHUB_RELEASE_MANIFEST"; do
  if [[ ! -f "$required_file" ]]; then
    echo "Required release asset is missing: $required_file" >&2
    exit 2
  fi
done

printf '::mask-value::%s\n' "$GITHUB_RELEASE_TOKEN"

work_dir="$(mktemp -d "${TMPDIR:-/tmp}/daggerfall-github-release.XXXXXX")"
trap 'rm -rf "$work_dir"' EXIT

api_request() {
  local method="$1"
  local url="$2"
  local output="$3"
  local data_file="${4:-}"
  local status

  if [[ -n "$data_file" ]]; then
    status="$(curl --silent --show-error --location \
      --request "$method" \
      --header "Accept: application/vnd.github+json" \
      --header "Authorization: Bearer $GITHUB_RELEASE_TOKEN" \
      --header "X-GitHub-Api-Version: 2022-11-28" \
      --header "Content-Type: application/json" \
      --data-binary "@$data_file" \
      --output "$output" \
      --write-out '%{http_code}' \
      "$url")"
  else
    status="$(curl --silent --show-error --location \
      --request "$method" \
      --header "Accept: application/vnd.github+json" \
      --header "Authorization: Bearer $GITHUB_RELEASE_TOKEN" \
      --header "X-GitHub-Api-Version: 2022-11-28" \
      --output "$output" \
      --write-out '%{http_code}' \
      "$url")"
  fi

  printf '%s' "$status"
}

json_value() {
  local file="$1"
  local expression="$2"
  python3 - "$file" "$expression" <<'PY'
import json
import sys

path, expression = sys.argv[1], sys.argv[2]
with open(path, "r", encoding="utf-8") as handle:
    value = json.load(handle)
for part in expression.split("."):
    if part:
        value = value[part]
print(value)
PY
}

write_release_payload() {
  local output="$1"
  python3 - \
    "$output" \
    "$release_tag" \
    "$release_name" \
    "$source_commit" \
    "$raw_build_number" \
    "$ipa_name" <<'PY'
import json
import sys

output, tag, name, commit, build_number, ipa_name = sys.argv[1:]
body = f"""Automatically published from Unity Build Automation.

Unity build: `{build_number}`
Source commit: `{commit}`
IPA asset: `{ipa_name}`

This is an unsigned ARM64 iOS build. SideStore or another development-signing workflow must sign it before installation.
No commercial Daggerfall data is included. This release does not claim gameplay validation.
"""
payload = {
    "tag_name": tag,
    "target_commitish": commit,
    "name": name,
    "body": body,
    "draft": False,
    "prerelease": True,
}
with open(output, "w", encoding="utf-8") as handle:
    json.dump(payload, handle)
PY
}

release_response="$work_dir/release.json"
release_payload="$work_dir/release-payload.json"
write_release_payload "$release_payload"

tag_response="$work_dir/tag.json"
status="$(api_request GET "$api_root/git/ref/tags/$release_tag" "$tag_response")"
if [[ "$status" == "200" ]]; then
  echo "Refusing to overwrite existing Git tag: $release_tag" >&2
  cat "$tag_response" >&2
  exit 3
elif [[ "$status" != "404" ]]; then
  echo "Git tag lookup failed with HTTP $status." >&2
  cat "$tag_response" >&2
  exit 4
fi

status="$(api_request GET "$api_root/releases/tags/$release_tag" "$release_response")"
if [[ "$status" == "200" ]]; then
  echo "Refusing to overwrite existing GitHub release: $release_tag" >&2
  cat "$release_response" >&2
  exit 5
elif [[ "$status" != "404" ]]; then
  echo "GitHub release lookup failed with HTTP $status." >&2
  cat "$release_response" >&2
  exit 6
fi

status="$(api_request POST "$api_root/releases" "$release_response" "$release_payload")"
if [[ "$status" != "201" ]]; then
  echo "GitHub release creation failed with HTTP $status." >&2
  cat "$release_response" >&2
  exit 7
fi

status="$(api_request GET "$api_root/git/ref/tags/$release_tag" "$tag_response")"
if [[ "$status" != "200" ]]; then
  echo "Created release did not produce the expected Git tag (HTTP $status)." >&2
  cat "$tag_response" >&2
  exit 8
fi

python3 - "$tag_response" "$source_commit" <<'PY'
import json
import sys

path, expected_commit = sys.argv[1:]
with open(path, "r", encoding="utf-8") as handle:
    tag = json.load(handle)
target = tag.get("object", {})
if target.get("type") != "commit" or target.get("sha") != expected_commit:
    raise SystemExit(
        "Created Git tag does not point directly to the expected commit: "
        f"expected={expected_commit!r}, actual={target!r}"
    )
PY

release_id="$(json_value "$release_response" id)"
release_url="$(json_value "$release_response" html_url)"

ipa_asset="$work_dir/$ipa_name"
evidence_asset="$work_dir/$evidence_name"
checksums_asset="$work_dir/$checksums_name"
manifest_asset="$work_dir/$manifest_name"
cp "$GITHUB_RELEASE_IPA" "$ipa_asset"
cp "$GITHUB_RELEASE_EVIDENCE" "$evidence_asset"
cp "$GITHUB_RELEASE_CHECKSUMS" "$checksums_asset"
cp "$GITHUB_RELEASE_MANIFEST" "$manifest_asset"

upload_asset() {
  local file="$1"
  local name
  local encoded_name
  local response="$work_dir/upload-$(basename "$file").json"
  local status

  name="$(basename "$file")"
  encoded_name="$(python3 - "$name" <<'PY'
import sys
from urllib.parse import quote
print(quote(sys.argv[1], safe=""))
PY
)"

  status="$(curl --silent --show-error --location \
    --request POST \
    --header "Accept: application/vnd.github+json" \
    --header "Authorization: Bearer $GITHUB_RELEASE_TOKEN" \
    --header "X-GitHub-Api-Version: 2022-11-28" \
    --header "Content-Type: application/octet-stream" \
    --data-binary "@$file" \
    --output "$response" \
    --write-out '%{http_code}' \
    "https://uploads.github.com/repos/$repository/releases/$release_id/assets?name=$encoded_name")"

  if [[ "$status" != "201" ]]; then
    echo "GitHub release asset upload failed for $name with HTTP $status." >&2
    cat "$response" >&2
    exit 8
  fi

  printf 'Uploaded GitHub release asset: %s\n' "$name"
}

upload_asset "$ipa_asset"
upload_asset "$evidence_asset"
upload_asset "$checksums_asset"
upload_asset "$manifest_asset"

assets_response="$work_dir/assets.json"
status="$(api_request GET "$api_root/releases/$release_id/assets?per_page=100" "$assets_response")"
if [[ "$status" != "200" ]]; then
  echo "GitHub release asset verification failed with HTTP $status." >&2
  cat "$assets_response" >&2
  exit 9
fi

python3 - \
  "$assets_response" \
  "$ipa_name" \
  "$evidence_name" \
  "$checksums_name" \
  "$manifest_name" <<'PY'
import json
import sys

path, *expected = sys.argv[1:]
with open(path, "r", encoding="utf-8") as handle:
    assets = json.load(handle)
actual = [asset["name"] for asset in assets]
if sorted(actual) != sorted(expected):
    raise SystemExit(
        "Published release assets do not match the immutable expected set: "
        f"expected={sorted(expected)!r}, actual={sorted(actual)!r}"
    )
PY

result_dir="$(dirname "$GITHUB_RELEASE_MANIFEST")"
direct_ipa_url="https://github.com/$repository/releases/download/$release_tag/$ipa_name"
printf '%s\n' "$release_url" > "$result_dir/github-release-url.txt"
printf '%s\n' "$direct_ipa_url" > "$result_dir/github-release-ipa-url.txt"
printf '%s\n' "$release_tag" > "$result_dir/github-release-tag.txt"
printf '%s\n' "$source_commit" > "$result_dir/github-release-source-commit.txt"
printf '%s\n' "$raw_build_number" > "$result_dir/github-release-build-number.txt"

printf 'Published immutable GitHub prerelease: %s\n' "$release_url"
printf 'Direct IPA URL: %s\n' "$direct_ipa_url"
printf 'Release tag: %s\n' "$release_tag"
printf 'Source commit: %s\n' "$source_commit"
printf 'Unity build number: %s\n' "$raw_build_number"
