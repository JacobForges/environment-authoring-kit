# Documentation index — Environment Authoring Kit

Package **`com.cursor.environment-authoring-kit`** — start at the [package README](../README.md).

**On GitHub:** read the Hub repo [PUBLIC_REPO_SCOPE.md](../../../../docs/PUBLIC_REPO_SCOPE.md) first.

---

## Start here

| Document | Audience | Content |
|----------|----------|---------|
| [../README.md](../README.md) | Everyone | Install, menus, quick start, pipeline overview |
| [REQUIREMENTS.md](REQUIREMENTS.md) | PM / leads | Functional requirements, 9-tile prop contract, acceptance |
| [../CHANGELOG.md](../CHANGELOG.md) | Maintainers | Version history |

---

## Pipeline & architecture

| Document | Content |
|----------|---------|
| [WORLD-GENERATION-PIPELINE-LADDER.md](WORLD-GENERATION-PIPELINE-LADDER.md) | Global rung order, invalidation, Far Cry / UE PCG principles |
| [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md) | Rung I/O table + queued step mapping (120 steps) |
| [SURFACE-WORLD-BUILD.md](SURFACE-WORLD-BUILD.md) | Surface menus, scopes, generated hierarchy |
| [AAA-PROCEDURAL-CAVE-PIPELINE.md](AAA-PROCEDURAL-CAVE-PIPELINE.md) | Autonomous Unity + Cursor design |
| [CAVE-BUILD-WORKFLOW-HARMONY.md](CAVE-BUILD-WORKFLOW-HARMONY.md) | Coordinator rules (nav, ground lock, meat loop) |
| [PRODUCT_BOUNDARY.md](PRODUCT_BOUNDARY.md) | In scope / out of scope, Florida policy |

---

## Grading, Cursor, quality

| Document | Content |
|----------|---------|
| [CaveGradingAndCursor.md](CaveGradingAndCursor.md) | JSON outputs, API setup, pre/post workflows |
| [FLOW-AUDIT-2026-05-27.md](FLOW-AUDIT-2026-05-27.md) | Hub/provider flow audit; 63→120 doc drift closed 2026-05-28 |
| [PUBLIC_REPO_SCOPE.md](../../../../docs/PUBLIC_REPO_SCOPE.md) | What GitHub contains vs local-only |
| [COMMERCIAL-PRODUCTION-GRADING.md](COMMERCIAL-PRODUCTION-GRADING.md) | Ship / Beta / Alpha tiers |
| [SURFACE_BUILD_RESPONSIVENESS.md](SURFACE_BUILD_RESPONSIVENESS.md) | Editor freeze avoidance on surface passes |

---

## CI & security

| Document | Content |
|----------|---------|
| [CODEQL_SETUP_AND_USE.md](../../../../docs/CODEQL_SETUP_AND_USE.md) | CodeQL on GitHub — setup, run, results (Hub) |
| [CODEQL_SELFHOSTED_INSTALL.md](CODEQL_SELFHOSTED_INSTALL.md) | Install checklist, legal, troubleshooting depth |

---

## Data & legal

| Document | Content |
|----------|---------|
| [RESEARCH_DATA_ATTRIBUTION.md](RESEARCH_DATA_ATTRIBUTION.md) | USGS, NOAA, FGS/FDEP credits |
| [PUBLISHING.md](PUBLISHING.md) | Release checklist for GitHub / UPM |

---

## Session notes (archive)

| Document | Content |
|----------|---------|
| [SESSION-SUMMARY-2026-05-21.md](SESSION-SUMMARY-2026-05-21.md) | Point-in-time notes — prefer [CHANGELOG.md](../CHANGELOG.md) for current history |

---

## When to update

| You changed… | Update |
|--------------|--------|
| Menu path or build scope | [../README.md](../README.md) + [CHANGELOG.md](../CHANGELOG.md) |
| Acceptance bar or prop/cave contract | [REQUIREMENTS.md](REQUIREMENTS.md) |
| Ladder rung I/O | [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md) + `CaveBuildPhaseContractRegistry` |
| Grade JSON or Cursor env | [CaveGradingAndCursor.md](CaveGradingAndCursor.md) |
| Research / attribution | [RESEARCH_DATA_ATTRIBUTION.md](RESEARCH_DATA_ATTRIBUTION.md) |
| CodeQL workflows or Unity prep scripts | [CODEQL_SETUP_AND_USE.md](../../../../docs/CODEQL_SETUP_AND_USE.md) + [CHANGELOG.md](../CHANGELOG.md) |
