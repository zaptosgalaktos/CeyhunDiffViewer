# CeyhunDiffViewer

Semantic diff for Unity YAML assets (prefabs, scenes, `.asset`). It reads two
commits from your **local** git clone and reports what actually changed —
objects added/removed, field changes, and prefab overrides compared by
`(target + propertyPath)` so a reordered/shifted `m_Modifications` list never
shows up as phantom add/remove noise.

Read-only by construction: it only runs `git show`, `git diff`, and `git grep`.
It never writes, never checks out, never touches your working tree, and needs no
GitHub token, app, or Action.

## Layout

```
src/CeyhunDiffViewer.Core   → the reusable engine (no CLI/UI dependencies)
  Git/       GitClient           read-only git access
  Yaml/      YamlAsset/Document   parse documents, modifications, hierarchy
  Diff/      AssetDiffEngine      set-based semantic diff + ReferenceResolver
  Scan/      CommitScanner        commit-level changed-asset list
src/CeyhunDiffViewer.Cli    → thin CLI over the engine (pretty + JSON output)
samples/                  → make-sample-repo.sh to try it end to end
```

The engine is deliberately UI-agnostic: the same `Core` can back this CLI, a
git difftool, or an optional Unity Editor window later.

## Build & run (macOS, .NET 8 SDK)

```bash
dotnet build
dotnet run --project src/CeyhunDiffViewer.Cli -- <command> ...
```

Or open `CeyhunDiffViewer.sln` in Rider and run the `CeyhunDiffViewer.Cli` project.

### Try it on the sample repo

```bash
./samples/make-sample-repo.sh /tmp/uadiff-sample
dotnet run --project src/CeyhunDiffViewer.Cli -- scan /tmp/uadiff-sample HEAD~1 HEAD
dotnet run --project src/CeyhunDiffViewer.Cli -- diff /tmp/uadiff-sample HEAD~1 HEAD \
    --path Assets/Card.prefab
```

## Commands

```
scan <repo> <baseRef> <targetRef> [--filter '*.prefab,*.asset'] [--json]
diff <repo> <baseRef> <targetRef> (--path <assetPath> | --guid <guid>) [--json]
```

- **scan** — commit-level entry point. Lists every changed asset with a status
  of added / deleted / modified / renamed / replaced (identity by `.meta` guid).
- **diff** — drills into one asset. `--path` is the path as seen in the target
  commit; the tool resolves its guid and finds the matching path in the base
  commit, so renames/moves are handled transparently. `--guid` skips straight to
  identity.

Refs are anything git understands: SHA, branch, tag, or `FETCH_HEAD`.

## Reviewing a PR without switching branches

You don't need your Unity project to be on the PR's branch — a diff is just two
refs read from history:

```bash
git fetch origin pull/123/head        # read-only, affects no one
dotnet run --project src/CeyhunDiffViewer.Cli -- \
    scan . origin/main FETCH_HEAD --filter '*.prefab,*.asset,*.unity'
```

## Status semantics

| status    | meaning                                                        |
|-----------|----------------------------------------------------------------|
| modified  | same guid, same path, content changed                          |
| renamed   | same guid, different path (`contentChanged` says if body also) |
| replaced  | path present both sides but **guid differs** — references broke |
| added     | guid only in target                                            |
| deleted   | guid only in base                                              |

## Known limitations (v1)

- Field-level diff uses top-level `key: value` pairs; deeply nested structures
  are compared as their raw one-line value. Overrides (`m_Modifications`) get the
  full set-based treatment.
- The override change reports which PrefabInstance and property changed; drilling
  into the exact sub-object inside the source prefab is a later enhancement.
- guid → path resolution greps `.meta` per ref (cached per run). Fine for normal
  repos; could be pre-indexed for very large ones.

## Verification done so far

The git adapter's command contract was validated against a real throwaway repo
(name-status for modify/rename, blob reads, guid grep across a rename, missing
path). Compile the solution with `dotnet build` on your machine to run the full
pipeline — the sandbox this was authored in had no .NET SDK.
