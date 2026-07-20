#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="${PROJECT_PATH:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
artifact_dir="$repo_root/Build/uba-ios-bootstrap"

if [[ ! -d "$artifact_dir" ]]; then
  echo "Unsigned iOS bootstrap evidence was not produced: $artifact_dir" >&2
  exit 2
fi

if [[ -z "${OUTPUT_DIRECTORY:-}" ]]; then
  echo 'Build Automation did not provide OUTPUT_DIRECTORY.' >&2
  exit 3
fi

output_extra="$OUTPUT_DIRECTORY/extra_data/daggerfall-ios-bootstrap"
mkdir -p "$output_extra"
cp -R "$artifact_dir/." "$output_extra/"

printf 'Copied unsigned iOS bootstrap evidence to: %s\n' "$output_extra"
