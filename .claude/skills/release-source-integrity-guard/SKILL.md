---
name: release-source-integrity-guard
description: >-
  Guard a release so its git tag can always rebuild its own artifact. Many release
  scripts make a "Release vX.Y.Z" commit that stages ONLY the bumped version files
  plus the built installer/binary — so if the feature SOURCE was not committed
  first, it is orphaned: the tag and the shipped binary exist, but the source
  behind them is missing from history and the tag cannot be rebuilt. Use when
  cutting a release, when writing or reviewing a release/publish script, or when
  a tag's tree does not match what shipped. Covers the clean-tracked-tree
  pre-flight check (allow untracked strays, block modified/staged tracked files),
  the commit-source-then-release ordering, and how to recover an already-orphaned
  release by committing the source as-is before fixing forward. Triggers on release
  integrity, orphaned source, tag cannot rebuild, release commit only version
  files, commit before release, reproducible release, release script guard.
---

# Release Source-Integrity Guard

A release tag is a promise: *check out this tag and you can rebuild exactly what
shipped.* That promise breaks silently when a release script commits the build
output and the version bump **without** the feature source that produced them —
because the source was still sitting uncommitted in the working tree when the
script ran. The binary ships, the tag is created, users are happy — and the tag's
tree can't reproduce the binary. Nobody notices until someone tries to patch from
the tag and finds the feature isn't there.

## Core principle

**Never start a release with uncommitted tracked source.** The release commit
should add *only* the things the release itself produces (the version bump, the
changelog promotion, the built artifact). Everything else it needs must already be
in history. So the invariant is: **the tracked working tree is clean before the
release script runs.** Untracked strays (scratch files, local notes) are fine;
modified or staged tracked files are not — those are the ones that get orphaned.

## The failure mode (why this happens)

Release scripts commonly do, in one "Release vX.Y.Z" commit:

```
git add <csproj/package.json/version file>   # the bump
git add <changelog>                          # [Unreleased] -> [X.Y.Z]
git add <dist/installer/binary>              # the built artifact
git commit -m "Release vX.Y.Z"
git tag vX.Y.Z && git push --tags
```

Notice it stages files **by name** — not `git add -A`. That is deliberate (it
keeps the release commit clean and avoids committing junk). But it means any
feature source you forgot to commit is **not** in the release commit, **not** in
the tag, and — if the script built the artifact from your dirty working tree —
the shipped binary contains code that exists nowhere in history. The build is
real; its source is a ghost.

This is easy to hit when the workflow is "run the release script" and you assume
it commits everything, or when an earlier "commit the source" step got skipped
under time pressure.

## The guard: a clean-tracked-tree pre-flight check

Add this to the top of the release script, after it computes the new version and
before it bumps/builds anything. It aborts unless the tracked tree is clean,
allowing untracked (`??`) files through and offering an explicit override.

PowerShell:

```powershell
if (-not $AllowDirty) {
    $dirty = @(git -C $root status --porcelain | Where-Object { $_ -and ($_ -notmatch '^\?\?') })
    if ($dirty.Count -gt 0) {
        $dirty | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "Release aborted: commit your source first (the release commit stages only " +
              "version files + artifact, so these would be orphaned from the vNEW tag). " +
              "Override with -AllowDirty."
    }
}
```

Bash:

```bash
if [ -z "${ALLOW_DIRTY:-}" ]; then
  dirty="$(git status --porcelain | grep -v '^??' || true)"
  if [ -n "$dirty" ]; then
    echo "$dirty"
    echo "Release aborted: commit your source first (the release commit stages only version" >&2
    echo "files + artifact, so these would be orphaned from the tag). Set ALLOW_DIRTY=1 to override." >&2
    exit 1
  fi
fi
```

The `grep -v '^??'` / `-notmatch '^\?\?'` is the whole trick: porcelain marks
untracked files with `??`, so excluding those leaves exactly the modified, added,
deleted, staged, and renamed *tracked* changes — the ones that would be orphaned.

## The correct ordering

1. **Commit the feature source** (and tests + docs) as normal commits.
2. **Run the release script.** The guard confirms the tree is clean, then it bumps
   the version, promotes the changelog, builds + signs the artifact, and makes the
   single "Release vX.Y.Z" commit + tag from an already-complete history.
3. The tag now points at a tree that fully rebuilds the artifact.

If your release runs in CI off a pushed tag, the same rule applies: the tag must
contain the source, so push the source commit *before* (or as part of) the tag.

## Recovering an already-orphaned release

If a release already shipped without its source (the tag builds, the binary is
live, but the source is missing from history):

1. **Do not move the tag.** It was built from and points at a real commit; CI and
   anyone who fetched it depend on it. Rewriting a published tag is worse than the
   gap.
2. **Commit the orphaned source as-is**, matching the shipped binary (do not fix
   bugs in this commit). Message it clearly: "feat(vX.Y.Z): source for the
   already-released vX.Y.Z (omitted from the release commit)." Now history
   *contains* the source even though the tag doesn't point exactly at it.
3. **Fix forward.** Make the corrections as normal commits toward the next patch,
   then cut vX.Y.(Z+1) — which, with the guard in place, will contain everything.

This keeps history honest and bisectable: a reader sees what vX.Y.Z actually was,
then the fixes layered on top.

## Anti-patterns

- **Assuming the release script commits everything.** Most stage by name and
  won't pick up your uncommitted feature files. Read what it stages.
- **`git add -A` in the release commit** to "fix" this — now the release commit is
  a grab-bag (stray files, half-finished work) and you've traded one integrity
  problem for another. Keep the release commit minimal; commit source separately,
  first.
- **Moving/force-pushing a published tag** to retrofit the source. Breaks fetchers
  and CI; commit-forward instead.
- **Building the artifact from a dirty tree.** Even with a guard, if you bypass it
  the binary and the tag's source silently diverge. The guard exists precisely so
  this can't happen by accident.
- **No override.** Provide an `-AllowDirty` / `ALLOW_DIRTY` escape hatch for the
  rare legitimate case, so people don't delete the guard the first time it's
  inconvenient.
