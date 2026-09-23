# CY Shared Visual References

> Canonical source: `simonliu1118-byte/AITeam`
>
> Status: shared design/reference material. This package is **not** a fourth governance layer and does not replace `REPOSITORY_RULES.md`, `REPO_POLICY.md`, or project `PROJECT_RULES.md`.

## Purpose

The canonical upstream copy of these CY cross-repository visual references lives in `simonliu1118-byte/AITeam` under `shared/cy-visual/`. `CYapps` and `CYapps_pvt` keep synchronized downstream copies under `docs/cy-visual/`.

The intended model mirrors the existing common-rules synchronization pattern, but remains a separate **design-reference sync lane**:

1. Author and review the canonical reference in AITeam.
2. Merge the accepted canonical version to AITeam `main`.
3. Downstream repositories fetch the canonical files and create synchronization PRs.
4. Downstream copies are not independently edited.

## Current package

`icon-family/` contains the shared CY application icon-family reference and the handoff brief for future AI/design conversations.

## Planned scope

When the CY Desktop Visual Guide finishes its current prototype/DPI validation and the user approves promotion, the same upstream/downstream model may be used for the broader shared visual guide.

Until then, only the icon-family package is canonicalized here.

## Important governance boundary

These files document visual direction and production references. They do not override repository or project governance. If a visual document conflicts with a higher-priority user instruction, project rule, repo policy, or shared repository rule, the higher-priority source wins.
