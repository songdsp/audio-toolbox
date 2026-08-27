# Changelog

All notable changes to this package are documented here.
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Second module: **EventTracer**. FMOD only for now; Wwise is planned.

### EventTracer
Runtime tracing that tells the seven ways a sound can fail to be heard apart
from each other, verified against an FMOD project built to provoke each one.

- **Facade** — `AudioTrace.Post` plays the sound and records the call, capturing
  the call site from `[CallerFilePath]` / `[CallerLineNumber]` at no runtime
  cost. `FmodAudioTrace.Attach` brings instances the game already created into a
  session without moving their call sites.
- **Outcomes** — HandleInvalid, Rejected, Started, Virtualized, Stolen,
  StoppedEarly, plus the distance to the listener on every record. The raw
  middleware result code is kept alongside the normalised outcome.
- **Collection** — a fixed ring buffer of struct records, a string intern table,
  and a background writer. Zero allocation per post, asserted in a PlayMode test
  rather than claimed.
- **Sessions** — `.adtrace` files under `Application.persistentDataPath`,
  readable in the editor whether or not this project has tracing switched on.
- **FMOD backend** — instance lifetime through FMOD's own callbacks, with the
  voice id carried in user data rather than a pinned managed object.
- **Native backend** — AudioClips stand in for events, so the facade and its
  tests work with no middleware installed.
- **Test fixture** — `Tools/TraceFixture~` authors the FMOD events each outcome
  needs and builds their bank, headlessly and repeatably.
- **Toolbox** — `AUDIOTOOLBOX_TRACE` toggle under **Window → Audio Toolbox →
  EventTracer**.

### Design decisions worth knowing
- **Outcome mapping lives in the core, not the backends.** Backends translate
  their callbacks into a neutral `ProbeSignal`; a pure state machine decides what
  a sequence of those means. That is what lets the riskiest code in the module be
  driven from a table on a machine with no middleware installed.
- **`Stolen` and `StoppedEarly` differ by one bit the middleware does not carry**
  — whether the game asked for the stop. The facade is where that is known, which
  is most of why there is a facade.
- **`Virtualized` is sticky.** A sound that came back from virtual keeps the
  outcome; reverting it would erase the evidence of the dropout being reported.
- **Distance is `-1`, not `0`, when there is nothing to measure.** Zero reads as
  "on top of the listener", which is plausible and wrong.
- **The trace define is opt-in.** Middleware presence is a fact about a project
  and is detected; whether a build carries a tracer is a decision and is not.

### Known limits
- **Direct middleware calls are invisible.** The tracer only sees instances it
  holds. AudioDoctor can find the call sites that bypass the facade; nothing can
  say what they did at runtime.
- **`NotCalled` is never produced here.** A tracer records calls that happened.
- **The Native backend cannot report `Rejected` or `Virtualized`** — Unity has no
  virtual voice system. Absent from the support matrix rather than approximated.
- **A voice outliving the ring buffer loses its outcome**, counted in the session
  header rather than dropped silently.
- No timeline window yet; sessions are read through the console dump and
  `TraceLogReader`. Emitter hierarchy paths and parameter snapshots are not
  captured yet — their record fields are present and unset.

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
