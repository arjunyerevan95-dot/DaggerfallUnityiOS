# Direct iPhone delivery through GitHub Releases

Successful Unity Build Automation runs publish a new immutable GitHub prerelease. The default tag is:

`ios-build-<unity-build-number>-<12-character-commit>`

The release contains:

- `DaggerfallUnity-ios-build-<build>-<commit>-arm64-unsigned.ipa`
- `DaggerfallUnity-iOS-build-<build>-<commit>-evidence.zip`
- `SHA256SUMS-<build>-<commit>.txt`
- `unsigned-package-manifest-<build>-<commit>.txt`

The IPA is unsigned when produced by Unity Build Automation. It must be re-signed by SideStore or another development-signing workflow before installation.

Publishing fails if either the tag or release already exists. Existing releases and assets are never patched, deleted, or replaced. The publisher verifies that the release contains exactly the four expected uniquely named assets before reporting success.

## Unity secret

The `ios-bootstrap` Build Automation configuration must define:

```text
GITHUB_RELEASE_TOKEN
```

The token must be a fine-grained GitHub personal access token restricted to `arjunyerevan95-dot/DaggerfallUnityiOS` with repository permission `Contents: Read and write`.

The publisher masks the token before making API calls and never prints it intentionally.

## Mobile workflow

1. Open the repository's Releases page on iPhone.
2. Open the release whose build number and commit match the build under test.
3. Download its uniquely named unsigned IPA directly into Files.
4. Open the IPA with SideStore for signing and installation.
5. Confirm the same build number and full commit SHA in `DaggerfallUnity-iOS.log`.
