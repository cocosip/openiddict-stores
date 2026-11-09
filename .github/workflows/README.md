# GitHub Actions CI/CD

## Triggers

| Event | Ref | What runs |
| --- | --- | --- |
| `push` | `main` / `master` | Build + test (the local `scripts/verify.ps1` gate; packing runs because the package asset tests read the nupkgs) |
| `pull_request` | targeting `main` / `master` | Build + test |
| `push` | version tag `v*.*.*` | Build + test → upload the verified packages → publish to NuGet.org → create the GitHub release |
| `workflow_dispatch` | manual | Build + test |

Other branches and non-version tags do not trigger anything.

## Jobs

### build-and-test

1. Checkout with `fetch-depth: 0` (release notes need the tag history).
2. Install the .NET 10 SDK, restore the NuGet cache.
3. **Resolve the version and validate the tag.** `<VersionPrefix>` (plus `<VersionSuffix>` for a
   pre-release) in `Directory.Build.props` is the single version source. A tag must equal
   `v<VersionPrefix>[-<VersionSuffix>]` and must point to HEAD, so the published package version cannot
   drift from the tagged source.
4. Run `scripts/verify.ps1`: restore → Release build for every target framework → pack the eight packages →
   the package/asset tests (which read the freshly packed nupkgs).
5. On a tag: generate `artifacts/RELEASE_NOTES.md` and upload the nupkgs plus the notes as the
   `nuget-packages` artifact.

### publish

Runs only for a validated version tag, and only consumes the build artifact: no checkout, no restore, no
build, so the pushed bytes are exactly the ones the gate accepted.

1. Download `nuget-packages`.
2. Verify there are exactly eight `.nupkg` files.
3. Exchange this workflow's GitHub OIDC token for a one-hour nuget.org API key (`NuGet/login@v1`).
4. Push each package with `--skip-duplicate`.
5. Create the GitHub release with `gh release create --verify-tag`, attaching the packages and the release
   notes. A version with a pre-release suffix is published as a pre-release.

## Trusted publishing setup (one-time, on nuget.org)

The workflow uses [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
so no long-lived API key is stored in GitHub.

1. Sign in to [NuGet.org](https://www.nuget.org/).
2. Open the account menu → **Trusted Publishing** → **Add new policy**.
3. Fill in:
   - **Repository owner**: `cocosip`
   - **Repository**: `openiddict-stores`
   - **Workflow file**: `ci-cd.yml` (file name only, without `.github/workflows/`)
   - **Environment**: leave blank (the workflow does not use a GitHub Environment)
4. The workflow logs in as `user: virtual` — that is the nuget.org profile name (not an email) that owns
   the `Cocosip.*` packages, and it is public, so it is not a secret.
5. Do **not** create a `NUGET_API_KEY` secret; the `publish` job requests `id-token: write` instead.

With trusted publishing the API key is temporary: it is valid for one hour and only for this workflow.

## Release process

1. Bump `<VersionPrefix>` in `Directory.Build.props` (for example `1.1.0`) and push to `main`/`master`.
   For a pre-release, also set `<VersionSuffix>` (for example `rc.1`), which makes the version
   `1.1.0-rc.1`.
2. Wait for the branch build and tests to pass (the packages were already packed and inspected there).
3. Create and push the matching tag:
   ```bash
   git tag v1.1.0
   git push origin v1.1.0
   ```
   A tag that does not match the composed version or does not point to HEAD fails before the build starts.
4. The workflow builds once, publishes the eight packages to NuGet.org, and creates the GitHub release.

## Package versions

Versions are declared once in `Directory.Packages.props`:

- `OpenIddictVersionRange` covers the OpenIddict packages (`[7.7.2,8.0.0)`).
- `MicrosoftVersionRange` is set per target framework (8.0.x for netstandard2.0/2.1 and net8.0, 9.0.x for
  net9.0, 10.0.x for net10.0) and is reused by every Microsoft.Extensions.* package and by the transitive
  `System.Security.Cryptography.Pkcs` pin.
- Dependency versions are bounded ranges, so the packed nuspec lets consumers take compatible servicing
  updates inside the same major line. Central package management restores the lowest version in the range,
  so builds use the reviewed lower bound.
- Test, sample and tooling versions stay pinned because they never ship.

## Troubleshooting

- **Version/tag validation fails**: the tag must be `v<VersionPrefix>` and must point to HEAD. Delete and
  re-create the tag after fixing the version; NuGet package versions are permanent, so never re-tag a
  published version.
- **`NuGet/login` fails**: confirm the trusted publishing policy is `cocosip` / `openiddict-stores` /
  `ci-cd.yml` with a blank environment, and that the `publish` job still has `id-token: write`.
- **Push says the version already exists**: `--skip-duplicate` makes reruns safe; a new version needs a new
  `VersionPrefix` and tag.
