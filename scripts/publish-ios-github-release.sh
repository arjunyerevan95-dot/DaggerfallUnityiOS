#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository="${GITHUB_RELEASE_REPOSITORY:-arjunyerevan95-dot/DaggerfallUnityiOS}"
release_tag="${GITHUB_RELEASE_TAG:-ios-latest}"
release_name="${GITHUB_RELEASE_NAME:-Daggerfall Unity iOS - Latest Device Build}"
source_commit="${GIT_COMMIT:-$(git -C "$repo_root" rev-parse HEAD)}"
short_commit="${source_commit:0:12}"
api_root="https://api.github.com/repos/$repository"

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

# Unity Build Automation understands this directive and redacts the token from logs.
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
  python3 - "$output" "$release_tag" "$release_name" "$source_commit" "$short_commit" <<'PY'
import json
import sys

output, tag, name, commit, short_commit = sys.argv[1:]
body = f"""Automatically published from Unity Build Automation.

Source commit: `{commit}`

Assets include the unsigned ARM64 iOS IPA, package evidence, checksums, and manifest.
The IPA must be signed separately before installation. This release does not claim gameplay validation.
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

status="$(api_request GET "$api_root/releases/tags/$release_tag" "$release_response")"
if [[ "$status" == "404" ]]; then
  status="$(api_request POST "$api_root/releases" "$release_response" "$release_payload")"
  if [[ "$status" != "201" ]]; then
    echo "GitHub release creation failed with HTTP $status." >&2
    cat "$release_response" >&2
    exit 3
  fi
elif [[ "$status" == "200" ]]; then
  release_id="$(json_value "$release_response" id)"
  status="$(api_request PATCH "$api_root/releases/$release_id" "$release_response" "$release_payload")"
  if [[ "$status" != "200" ]]; then
    echo "GitHub release update failed with HTTP $status." >&2
    cat "$release_response" >&2
    exit 4
  fi
else
  echo "GitHub release lookup failed with HTTP $status." >&2
  cat "$release_response" >&2
  exit 5
fi

release_id="$(json_value "$release_response" id)"
release_url="$(json_value "$release_response" html_url)"

assets_response="$work_dir/assets.json"
status="$(api_request GET "$api_root/releases/$release_id/assets?per_page=100" "$assets_response")"
if [[ "$status" != "200" ]]; then
  echo "GitHub release asset listing failed with HTTP $status." >&2
  cat "$assets_response" >&2
  exit 6
fi

ipa_asset="$work_dir/DaggerfallUnity-ios-arm64-unsigned.ipa"
evidence_asset="$work_dir/DaggerfallUnity-iOS-build-evidence.zip"
checksums_asset="$work_dir/SHA256SUMS"
manifest_asset="$work_dir/unsigned-package-manifest.txt"
cp "$GITHUB_RELEASE_IPA" "$ipa_asset"
cp "$GITHUB_RELEASE_EVIDENCE" "$evidence_asset"
cp "$GITHUB_RELEASE_CHECKSUMS" "$checksums_asset"
cp "$GITHUB_RELEASE_MANIFEST" "$manifest_asset"

python3 - "$assets_response" > "$work_dir/delete-assets.tsv" <<'PY'
import json
import sys

wanted = {
    "DaggerfallUnity-ios-arm64-unsigned.ipa",
    "DaggerfallUnity-iOS-build-evidence.zip",
    "SHA256SUMS",
    "unsigned-package-manifest.txt",
}
with open(sys.argv[1], "r", encoding="utf-8") as handle:
    assets = json.load(handle)
for asset in assets:
    if asset.get("name") in wanted:
        print(f"{asset['id']}\t{asset['name']}")
PY

while IFS=$'\t' read -r asset_id asset_name; do
  [[ -z "$asset_id" ]] && continue
  delete_response="$work_dir/delete-$asset_id.json"
  status="$(api_request DELETE "$api_root/releases/assets/$asset_id" "$delete_response")"
  if [[ "$status" != "204" ]]; then
    echo "Could not delete existing release asset $asset_name (HTTP $status)." >&2
    cat "$delete_response" >&2
    exit 7
  fi
done < "$work_dir/delete-assets.tsv"

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

printf 'Published rolling GitHub prerelease: %s\n' "$release_url"
printf 'Release tag: %s\n' "$release_tag"
printf 'Source commit: %s\n' "$source_commit"
