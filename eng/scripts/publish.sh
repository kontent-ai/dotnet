#!/usr/bin/env bash
set -euo pipefail

# The workflow owns build/pack and credentials; this script owns selection and retries.
# Release JSON is the paginated/slurped response from GET /repos/{owner}/{repo}/releases.
plan() {
  local candidates="$1" releases="$2" output="$3" product version tag title status prerelease
  local selected=() input="${PRODUCTS:-}"
  input="${input//$'\n'/ }"
  read -r -a selected <<< "$input"
  if [ "${#selected[@]}" -eq 0 ] && [ "$GITHUB_REF_NAME" != main ] && [ "$GITHUB_REF_NAME" != vnext ]; then
    echo "::error::Name the products to publish when running from '$GITHUB_REF_NAME'." >&2
    exit 1
  fi
  if [ "${#selected[@]}" -gt 0 ]; then
    for product in "${selected[@]}"; do
      if ! jq -e --arg p "$product" 'has($p)' eng/products.json >/dev/null; then
        echo "::error::Unknown product '$product'." >&2
        exit 1
      fi
    done
  fi

  : > "$output"
  while IFS=$'\t' read -r product version tag title status prerelease; do
    if [ "${#selected[@]}" -gt 0 ]; then
      case " ${selected[*]} " in *" $product "*) ;; *) continue ;; esac
    fi
    # A release leaves draft only after every package and asset is up, so this is the whole story.
    if [ "$status" = published ] && jq -e --arg tag "$tag" \
      'any(.[][]; .tag_name == $tag and .draft == false)' "$releases" >/dev/null; then
      if [ "${#selected[@]}" -gt 0 ]; then
        echo "::notice::$product $version is already published and released - skipping"
      fi
      continue
    fi
    printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$product" "$version" "$tag" "$title" "$status" "$prerelease" >> "$output"
  done < "$candidates"
}

tag_commit() {
  local tag="$1" refs
  # Query the remote, not checkout's shallow/local tag cache. Prefer an annotated tag's peeled commit.
  refs=$(git ls-remote --tags origin "refs/tags/$tag" "refs/tags/$tag^{}")
  printf '%s\n' "$refs" | awk -v ref="refs/tags/$tag" '
    $2 == ref { direct = $1 } $2 == ref "^{}" { peeled = $1 }
    END { print (peeled != "" ? peeled : direct) }'
}

verify() {
  local product version tag title status prerelease commit
  while IFS=$'\t' read -r product version tag title status prerelease; do
    git check-ref-format "refs/tags/$tag"
    commit=$(tag_commit "$tag")
    if [ -n "$commit" ] && [ "$commit" != "$GITHUB_SHA" ]; then
      echo "::error::$tag belongs to $commit, not $GITHUB_SHA. Re-run the publish run that created it. Only if none of its packages reached nuget.org may the tag be deleted and the publish dispatched again." >&2
      exit 1
    fi
    if [ -z "$commit" ] && [ "$status" != 'PREPARED, NOT PUBLISHED' ]; then
      echo "::error::$tag has packages on NuGet but no source tag. Establish their original commit before recovering this release." >&2
      exit 1
    fi
    if [ -z "$commit" ] && [ "${2:-}" = required ]; then
      echo "::error::$tag must be reserved before pushing packages." >&2
      exit 1
    fi
  done < "$1"
}

reserve() {
  local product version tag title status prerelease commit
  verify "$1"
  while IFS=$'\t' read -r product version tag title status prerelease; do
    commit=$(tag_commit "$tag")
    if [ -z "$commit" ]; then
      # Creating a ref fails if it already exists; never force-update a version's source identity.
      gh api --method POST "repos/$GITHUB_REPOSITORY/git/refs" \
        -f "ref=refs/tags/$tag" -f "sha=$GITHUB_SHA" >/dev/null
    fi
  done < "$1"
  verify "$1"
}

push() {
  local order="$1" releases="$2" product version tag title status prerelease out exists draft
  local flags=() symbols=()
  # Check the entire batch before the first irreversible package upload.
  verify "$order" required
  shopt -s nullglob
  while IFS=$'\t' read -r product version tag title status prerelease; do
    out="$PACK_OUTPUT/$product"
    echo "::group::$tag"
    dotnet nuget push "$out/*.nupkg" -s https://api.nuget.org/v3/index.json \
      -k "$NUGET_KEY" --skip-duplicate --no-symbols
    # Retry symbols separately: an existing primary package must not hide a failed symbol push.
    symbols=("$out"/*.snupkg)
    if [ "${#symbols[@]}" -gt 0 ]; then
      dotnet nuget push "$out/*.snupkg" -s https://api.nuget.org/v3/index.json \
        -k "$NUGET_KEY" --skip-duplicate
    fi

    exists=$(jq --arg tag "$tag" 'any(.[][]; .tag_name == $tag)' "$releases")
    draft=true
    if [ "$exists" = true ]; then
      draft=$(jq -r --arg tag "$tag" '.[][] | select(.tag_name == $tag) | .draft' "$releases")
    else
      flags=(--verify-tag --draft --title "$title" --notes-file "$out/notes.md")
      if [ "$prerelease" = prerelease ]; then flags+=(--prerelease); fi
      gh release create "$tag" "${flags[@]}"
    fi
    gh release upload "$tag" "$out"/*.nupkg "$out"/*.snupkg --clobber
    if [ "$draft" = true ]; then
      gh release edit "$tag" --draft=false
    fi
    echo "::endgroup::"
  done < "$order"
}

case "${1:-}" in
  plan) plan "$2" "$3" "$4" ;;
  verify) verify "$2" ;;
  reserve) reserve "$2" ;;
  push) push "$2" "$3" ;;
  *) echo 'usage: publish.sh <plan candidates releases output | verify order | reserve order | push order releases>' >&2; exit 1 ;;
esac
