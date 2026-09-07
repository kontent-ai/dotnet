#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
export PUBLISH_SCRIPT="$root/eng/scripts/publish.sh"
test_root=$(mktemp -d)
trap 'rm -rf "$test_root"' EXIT
export GITHUB_REPOSITORY=fixture/repo NUGET_KEY=fixture-only
export FAIL_PUSH=false FAIL_SYMBOLS=false FAIL_CREATE=false FAIL_UPLOAD=false FAIL_EDIT=false

# No remote services: Git is real, while gh and dotnet write to a local fake feed/release.
gh() {
  printf '%s\n' "$*" >> "$STATE/calls"
  if [ "$1" = api ]; then
    [ "$2" = --method ] && [ "$3" = POST ] && [ "$4" = "repos/$GITHUB_REPOSITORY/git/refs" ] || return 90
    git --git-dir="$REMOTE" update-ref "${6#ref=}" "${8#sha=}" 0000000000000000000000000000000000000000
    return
  fi
  [ "$1" = release ] || return 91
  case "$2" in
    create)
      [ "$FAIL_CREATE" = false ] || return 18
      case " $* " in *' --verify-tag --draft '*) ;; *) return 92 ;; esac
      printf '[[{"tag_name":"%s","draft":true}]]\n' "$3" > "$STATE/releases.json"
      ;;
    upload)
      [ "$FAIL_UPLOAD" = false ] || return 19
      shift 3
      for file in "$@"; do
        [ "$file" = --clobber ] || cp "$file" "$STATE/assets/"
      done
      ;;
    edit)
      [ "$FAIL_EDIT" = false ] || return 20
      [ "$4" = --draft=false ] || return 93
      jq '.[0][0].draft = false' "$STATE/releases.json" > "$STATE/edited.json"
      mv "$STATE/edited.json" "$STATE/releases.json"
      ;;
    *) return 94 ;;
  esac
}

dotnet() {
  [ "$1 $2" = 'nuget push' ] || return 95
  printf '%s\n' "$*" >> "$STATE/calls"
  local file name count=0
  case "$3" in
    *.snupkg) [ "$FAIL_SYMBOLS" = false ] || return 21 ;;
    *.nupkg) case " $* " in *' --no-symbols '*) ;; *) return 96 ;; esac ;;
    *) return 97 ;;
  esac
  for file in $3; do
    name=$(basename "$file")
    if [ -f "$STATE/feed/$name" ]; then continue; fi
    cp "$file" "$STATE/feed/$name"
    count=$((count + 1))
    if [ "$FAIL_PUSH" = true ] && [ "$count" -eq 1 ]; then return 17; fi
  done
}
export -f gh dotnet

fixture() {
  local name="$1"
  mkdir -p "$test_root/$name"
  cd "$test_root/$name"
  export STATE="$PWD/state" REMOTE="$PWD/remote.git" PACK_OUTPUT="$PWD/pack"
  export PRODUCTS='' GITHUB_REF_NAME=vnext
  export FAIL_PUSH=false FAIL_SYMBOLS=false FAIL_CREATE=false FAIL_UPLOAD=false FAIL_EDIT=false
  mkdir -p "$STATE/feed" "$STATE/assets" "$PACK_OUTPUT/delivery" eng
  cp "$root/eng/products.json" eng/products.json
  printf '[[]]\n' > "$STATE/releases.json"
  git init --quiet --bare "$REMOTE"
  git init --quiet .
  git config user.name fixture
  git config user.email fixture@example.invalid
  git -c commit.gpgsign=false commit --quiet --allow-empty -m A
  export SHA_A=$(git rev-parse HEAD)
  git -c commit.gpgsign=false commit --quiet --allow-empty -m B
  export SHA_B=$(git rev-parse HEAD)
  export GITHUB_SHA="$SHA_A"
  git remote add origin "$REMOTE"
  git push --quiet origin HEAD:refs/heads/fixture
  printf 'A\n' > "$PACK_OUTPUT/delivery/A.1.0.0.nupkg"
  printf 'A\n' > "$PACK_OUTPUT/delivery/B.1.0.0.nupkg"
  printf 'symbols\n' > "$PACK_OUTPUT/delivery/A.1.0.0.snupkg"
  printf 'notes\n' > "$PACK_OUTPUT/delivery/notes.md"
  row 'PREPARED, NOT PUBLISHED'
}

row() {
  printf 'delivery\t1.0.0\tdelivery-v1.0.0\tDelivery 1.0.0\t%s\t\n' "$1" > "$STATE/candidates.tsv"
}

plan() {
  bash "$PUBLISH_SCRIPT" plan "$STATE/candidates.tsv" "$STATE/releases.json" "$STATE/order.tsv"
}

reserve() { bash "$PUBLISH_SCRIPT" reserve "$STATE/order.tsv"; }
push() { bash "$PUBLISH_SCRIPT" push "$STATE/order.tsv" "$STATE/releases.json"; }
fails() {
  if "$@" > "$STATE/failure.log" 2>&1; then
    echo "Expected failure: $*" >&2
    exit 1
  fi
}
complete() { jq -e '.[0][0].draft == false' "$STATE/releases.json" >/dev/null; }

fixture partial
plan
fails push
reserve
[ "$(git --git-dir="$REMOTE" rev-parse refs/tags/delivery-v1.0.0)" = "$SHA_A" ]
export FAIL_PUSH=true
fails push
[ -f "$STATE/feed/A.1.0.0.nupkg" ] && [ ! -f "$STATE/feed/B.1.0.0.nupkg" ]
row 'PARTIALLY PUBLISHED'
plan
export GITHUB_SHA="$SHA_B" FAIL_PUSH=false
fails reserve
fails push
[ ! -f "$STATE/feed/B.1.0.0.nupkg" ]
export GITHUB_SHA="$SHA_A"
reserve
push
complete
cmp "$STATE/feed/A.1.0.0.nupkg" "$PACK_OUTPUT/delivery/A.1.0.0.nupkg"
echo 'PASS: partial retry is bound to the original commit'

for failure in FAIL_CREATE FAIL_UPLOAD FAIL_EDIT FAIL_SYMBOLS; do
  fixture "$failure"
  plan
  reserve
  export "$failure=true"
  fails push
  row published
  plan
  [ -s "$STATE/order.tsv" ]
  export "$failure=false"
  reserve
  push
  complete
  [ -f "$STATE/assets/A.1.0.0.nupkg" ] && [ -f "$STATE/assets/A.1.0.0.snupkg" ]
  [ -f "$STATE/feed/A.1.0.0.snupkg" ]
  plan
  [ ! -s "$STATE/order.tsv" ]
  echo "PASS: automatic recovery after $failure, including fully published NuGet state"
done

fixture complete
row published
printf '[[{"tag_name":"delivery-v1.0.0","draft":false}]]\n' > "$STATE/releases.json"
plan
[ ! -s "$STATE/order.tsv" ]
export PRODUCTS=$' delivery\t\n'
plan > "$STATE/plan.log"
[ ! -s "$STATE/order.tsv" ]
grep -q '::notice::delivery 1.0.0 is already published' "$STATE/plan.log"
echo 'PASS: a complete release is skipped, with a notice when named'

fixture annotated
git -c tag.gpgsign=false tag -a delivery-v1.0.0 "$SHA_A" -m original
git push --quiet origin refs/tags/delivery-v1.0.0
plan
reserve
push
complete
export GITHUB_SHA="$SHA_B"
fails push
echo 'PASS: an annotated tag verifies by its peeled commit'

fixture guards
export GITHUB_REF_NAME=maintenance/delivery
fails plan
export PRODUCTS=unknown
fails plan
export PRODUCTS=delivery
row 'PARTIALLY PUBLISHED'
plan
fails reserve
row 'PREPARED, NOT PUBLISHED'
plan
reserve
printf 'sync\t2.0.0\tsync-v2.0.0\tSync 2.0.0\tPREPARED, NOT PUBLISHED\t\n' >> "$STATE/order.tsv"
git --git-dir="$REMOTE" update-ref refs/tags/sync-v2.0.0 "$SHA_B"
fails push
[ ! -f "$STATE/feed/A.1.0.0.nupkg" ]
git remote set-url origin "$STATE/no-such-remote"
fails reserve
echo 'PASS: missing provenance, invalid selection, batch conflicts, and remote failures stop publishing'

echo 'All publish regression tests passed.'
