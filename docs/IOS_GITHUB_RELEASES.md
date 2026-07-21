# Direct iPhone delivery through GitHub Releases

Successful Unity Build Automation runs publish a rolling GitHub prerelease tagged `ios-latest`.

The release contains:

- `DaggerfallUnity-ios-arm64-unsigned.ipa`
- `DaggerfallUnity-iOS-build-evidence.zip`
- `SHA256SUMS`
- `unsigned-package-manifest.txt`

The IPA is unsigned when produced by Unity Build Automation. It must be re-signed by SideStore or another development-signing workflow before installation.

## Unity secret

The `ios-bootstrap` Build Automation configuration must define:

```text
GITHUB_RELEASE_TOKEN
```

The token must be a fine-grained GitHub personal access token restricted to `arjunyerevan95-dot/DaggerfallUnityiOS` with repository permission `Contents: Read and write`.

The publisher masks the token before making API calls and never prints it intentionally.

## Mobile workflow

1. Open the repository's Releases page on iPhone.
2. Open **Daggerfall Unity iOS - Latest Device Build**.
3. Download `DaggerfallUnity-ios-arm64-unsigned.ipa` directly into Files.
4. Open the IPA with SideStore for signing and installation.

The rolling prerelease is replaced by each successful authoritative Unity build. The source commit is recorded in the release body and package manifest.
