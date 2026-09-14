# Releasing

Each product has its own version in [`eng/Versions.props`](./eng/Versions.props) and its own
`CHANGELOG.md`. The [Prepare release](./.github/workflows/prepare-release.yml) workflow bumps
versions and opens a pull request. After that PR is merged, [Publish](./.github/workflows/release.yml)
builds and publishes the packages.

Run the commands below from the repository root.

- [Prepare a release](#prepare-a-release)
- [Write the overview for a new stable major](#releasing-a-new-stable-major)
- [Publish](#publish)
- [Recover a failed publish](#when-a-publish-run-fails)
- [Hotfix an earlier major](#hotfixing-an-earlier-major)
- [Update dependency floors](#cross-product-dependency-floors)
- [Check a prepared but unpublished version](#prepared-but-not-published)

## Prepare a release

Open **Actions → Prepare release** on the branch you want to release. Each product has a field
accepting `prerelease`, `release`, `patch`, `minor`, `major`, or an explicit version such as
`2.1.0-rc.1`. Leave a field empty to skip that product.

One run bumps the selected products, promotes each one's `## Unreleased` changelog section,
and opens one PR for the batch. Review the versions and release notes before merging.
Prepare release publishes nothing.

The version-update script can also be run locally. For example, to promote Delivery's current
prerelease to stable:

```sh
dotnet run eng/scripts/update-version.cs -- delivery release
```

Use the workflow for the release PR; feature PRs must not change `eng/Versions.props`.

## Releasing a new stable major

A release page contains only that version's changelog section. When promoting a release candidate,
the section usually describes changes since the previous candidate. Add an overview for readers
upgrading from the previous stable major.

In the release PR, put the overview between the version heading and the first `###`, without
a heading of its own. It should cover, in order:

1. The new release line and target framework.
2. What users of the previous stable major need to change, separating API changes from behavior
   changes that compile unchanged.
3. A link to the upgrade guide, including earlier guides if users can skip a major.
4. Where to find the detail: the sections below cover changes since the last prerelease;
   earlier prerelease entries retain the rest of the history.

Keep links absolute so they work on the GitHub Release page. Do not copy all the prerelease
entries into the overview; the upgrade guide already brings the migration steps together.

Render the notes from the release branch to check them. For example, for Delivery 20.0.0:

```sh
dotnet run eng/scripts/release-notes.cs -- delivery-v20.0.0
```

Publish rejects a new stable major without an overview, and rejects an empty release entry.
Both checks run during the dry run, before anything is pushed.

## Publish

After merging the release PR, open **Actions → Publish** on the same branch. It publishes products
whose declared version is missing from NuGet. Any product can be released independently.

Publish defaults to a dry run: it builds, packs, and renders the release notes without pushing.
Run it once to inspect the output, then run it again with dry run disabled. The packages and
notes are available as workflow artifacts in both cases.

The workflow refuses to publish a product if its changelog has no entry for the declared version
or a cross-product dependency floor names a version that is not on NuGet. It records each release
with a `<product>-v<version>` tag and a GitHub Release whose notes come from the changelog.
Repository links in those notes are pinned to the tag; links inside the destination guides may
still point to `main`.

The job uses the `nuget.org` GitHub environment. Its settings determine which branches may publish
and whether approval is required. From a branch other than `main` or `vnext`, name the products
to publish explicitly.

## When a publish run fails

NuGet uploads are immutable, and a product may contain several packages. Publish creates the
version's tag before pushing any package, binding that version to one commit. The GitHub Release
stays a draft until every package and asset has been uploaded.

To finish an interrupted publish, **re-run that run** in Actions. It uses the original commit,
skips completed uploads, and finishes the remaining work. A fresh dispatch from a later commit
is refused for that version. Only if none of the version's packages reached NuGet may its tag
be deleted and publishing dispatched again.

A fresh dispatch also finds products whose packages are on NuGet but whose GitHub Release is
missing or still a draft. Completed products are skipped, with a notice if explicitly selected.

## Hotfixing an earlier major

Branch from the last tag of that line as `maintenance/<name>` and cherry-pick the fix. Run both
workflows from that branch: Prepare release opens its PR against the branch, and Publish needs
the product named explicitly. An older branch declares every product at its version from that time.

## Cross-product dependency floors

Releasing a product does not update the version its siblings depend on. Those minimum versions
live in [`Directory.Packages.props`](./Directory.Packages.props). Raise a floor in a separate PR,
after the dependency has been published, and only when the consuming code needs the newer API.
See [Changing an API that another product consumes](./CONTRIBUTING.md#changing-an-api-that-another-product-consumes)
for the sequence.

To check the current floors:

```sh
dotnet run eng/scripts/dependency-floors.cs
```

Prepare release includes the same report in its PR, and **Actions → Dependency floors** runs it
monthly. A floor behind the latest release is reported, not rejected. A floor that names a version
absent from NuGet fails the check.

## Prepared but not published

A merged release PR may declare a version that has not yet been published. Check the status with:

```sh
dotnet run eng/scripts/release-status.cs
```

Do not prepare that product again before publishing or abandoning the prepared version: another
bump would skip the unpublished version. To abandon it, restore the previous version in
`eng/Versions.props` and remove the prepared `## <version> (<date>)` heading so its notes return
to `## Unreleased`.
