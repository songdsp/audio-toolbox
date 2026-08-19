# Changelog

All notable changes to this package are documented here.
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-08-19

First release. One module: **AudioDoctor**.

### AudioDoctor
Static validation for game audio pipelines. Eight rules, verified against a
deliberately broken FMOD Studio project with one planted defect per rule and a
negative case for each.

- **Rules** — R000 scan coverage, R001 dangling reference, R003 bank never
  loaded, R004 orphan event, R005 loading strategy, R006 3D played without a
  position, R007 unknown parameter, R008 naming convention, R009 cross-platform
  banks. Per-rule enable, severity override and thresholds via a `RuleSet` asset.
- **FMOD backend** — authored events, banks expanded one entry per platform,
  global parameters, bank-load configuration, and four reference-collection
  paths: serialized fields, code literals, Timeline clips and AnimationEvents.
- **Native backend** — no middleware required. AudioClips stand in for events
  and AssetBundles for banks.
- **Diagnostics window** — severity counts that double as filters, grouping by
  severity / rule / asset over a virtualized list, double-click to select the
  asset or open code at its line, clipboard copy, and a permanent strip for the
  checks that did not run.
- **Reports** — JSON for CI and Markdown for people.

### Toolbox
- Middleware detection driving `AUDIOTOOLBOX_FMOD` / `AUDIOTOOLBOX_WWISE`, so a
  fresh clone compiles with both middlewares, one, or neither.

### Design decisions worth knowing
- A rule whose data the active backend cannot supply is **skipped and listed**,
  never approximated. Reports distinguish "nothing is wrong" from "nothing was
  checked".
- **R003** reports a bank nothing loads *anywhere* rather than judging per scene;
  a loader on a runtime-instantiated prefab cannot be seen statically.
- **R006** reports only 3D events played without a position. Every emitter is
  positioned by definition, so the reverse direction would fire on every
  correctly wired 2D sound.
- **R009**'s size-deviation check is off by default; platforms legitimately use
  different encodings.
- **R007** stays silent when it cannot resolve a call with certainty. At Error
  level a wrong finding costs more than a missing one.

### Known limits
- **R002 cannot run on the FMOD backend.** The integration enumerates events by
  loading each built bank, so an event in no bank never reaches Unity — exactly
  the event R002 looks for. Reported as a skipped check. Reading the Studio
  project metadata directly is planned for 0.2.
- Event names assembled at runtime cannot be resolved; they are reported as
  coverage notes rather than dropped.
- The Wwise backend is a placeholder that reports why it cannot help.

[0.1.0]: https://github.com/songdsp/audio-toolbox/releases/tag/v0.1.0
