# EventTracer — progress and what is left

Working notes for module A. Not documentation; `Documentation~/EventTracer.md` is
the documentation. This file exists so the next session can pick up without
re-deriving anything.

Last updated **2026-08-28**, after the demo sample landed.

---

## Where it stands

| phase | what it is | state |
|---|---|---|
| 0 | Facade, record model, `IAudioRuntimeProbe`, native backend | **done** |
| 1 | Ring buffer, intern table, background writer, `.adtrace` | **done** |
| 2 | FMOD normalisation, all seven outcomes | **done** |
| 3 | Context capture — emitter path, call site, distance, parameter snapshots | **done** |
| 4 | Timeline window | **done** |
| 5 | Wwise backend | **not started** |
| 6 | Blind-spot detection with AudioDoctor + troubleshooting handbook | **not started** |
| §8 | Demo scene | **done** (sample) · demo video is yours |

Everything above is committed. `git log` calls the last one "phase 4 + UI
skeleton"; it also contains Phase 3 fixes and the whole demo sample.

### Verification as of now

| | |
|---|---|
| EditMode, whole project | 203 / 203 |
| EditMode, EventTracer only, `AUDIOTOOLBOX_TRACE` **on** | 117 / 117 |
| EditMode, EventTracer only, `AUDIOTOOLBOX_TRACE` **off** | 32 / 32 |
| PlayMode | 17 / 17 — 5 budget, 8 FMOD outcome, 4 FMOD parameter |
| Zero GC | 2000 posts with emitter + parameters, **0 bytes** |
| Collection layer absent with the define off | 8 types confirmed absent by reflection |

The define-off numbers matter and are easy to forget: the reader, the timeline
window and `TraceTimeline` are deliberately **not** behind `AUDIOTOOLBOX_TRACE`,
because logs come from other people's builds. 21 of those 32 tests are the ones
that prove it.

---

## Do these first

Small, known-wrong, and cheap. None is blocking, all are the kind of thing that
gets embarrassing later.

### 1. `GetOutcome` ignores the handle's generation — latent bug

`AudioTraceRuntime.GetOutcome(handle)` reads `_recorder.GetOutcome(handle.VoiceId)`
with no generation check. Voice slots are recycled, so once a sound has finished
this answers for **whatever now owns that slot**. A caller polling a finished
handle silently starts reading somebody else's outcome.

`AudioTraceHandle` carries a generation precisely so stale handles are inert, and
`Stop` / `SetParameter` / `IsAlive` all honour it. This one does not.

The demo works around it by stopping the moment the voice is not alive
(`TraceDemoEmitter.LateUpdate`), and the FMOD tests never hit it because they poll
while alive. Neither is a fix.

**Fix:** give `TraceRecorder` a `_generation[]` alongside `_state[]`, set it in
`BeginVoice` (the generation is available at the `Post` site, so it has to be
threaded through), and have `GetOutcome` return `NotCalled` when it does not
match. Then simplify the demo's polling and drop the comment explaining the
hazard.

### 2. The fixture script's Spatial3D note is now known to be false

`Tools/TraceFixture~/build-trace-fixture.js`, the `Spatial3D` spec note says:

> "Placed past 10m it is inaudible, which is how the virtual voice system is
> provoked without touching instance limits."

**This is wrong.** The demo established that distance alone does not virtualise a
voice — FMOD goes virtual when it needs the channel back, not when a sound is
inaudible. A sound 49 m past a 10 m max distance comes back `Started`.

The event itself is still correct and still used, by
`A3DEventFarFromTheListener_RecordsTheDistance`, which asserts the distance rather
than virtualisation. Only the note needs rewriting to say what the event is
actually for.

### 3. `README.md` image path uses a backslash

`![tracer](Documentation~\tracer.gif)` — resolves on Windows, breaks on GitHub and
everywhere else. Should be `Documentation~/tracer.gif`, like the line above it.

---

## Phase 5 · Wwise backend

Spec estimate 4 days. Nothing started.

- `AKRESULT` / `AkCallbackType` → `ProbeSignal`. The `OutcomeStateMachine` does not
  change; that separation is the whole reason this phase is mostly translation.
- Expected coverage: `HandleInvalid`, `Started`, `StoppedEarly` cleanly.
  `Rejected` and `Virtualized` best-effort — Wwise's callbacks carry less than
  FMOD's.
- **Update the support matrix to what is measured, not what is hoped.** The
  matrix in `Documentation~/EventTracer.md` currently has a `—` column for Wwise
  and a known-limits entry promising this.
- A Wwise equivalent of `Tools/TraceFixture~` will be needed for the PlayMode
  tests, and there is no headless authoring CLI for Wwise the way there is for
  FMOD. Expect this to be the expensive part, and decide early whether to ship a
  checked-in `.wproj` fixture instead.

## Phase 6 · Blind spots and the handbook

Spec estimate 2 days.

- AudioDoctor already walks code for event-key literals. Extend it to find direct
  middleware calls that bypass the facade — `RuntimeManager.PlayOneShot`,
  `CreateInstance`, `AkSoundEngine.PostEvent` — and report them at Info.
- Surface the count at the top of the timeline window: "this session has N tracing
  blind spots". The window already has a warning strip built for exactly this
  shape of message.
- 《音频问题排查手册》 — the troubleshooting handbook. Most of its content already
  exists scattered across `Documentation~/EventTracer.md` (the outcome table, the
  seven causes) and wants collecting into a symptom-first document: *"I hear
  nothing"* → what to check, in order.

---

## Smaller things worth doing sometime

- **`Samples~` is never compiled by anything.** The tilde keeps it out of the
  importer, so a broken demo script ships unnoticed. Either add a CI step that
  copies it into a throwaway project, or accept it and test by importing before
  each release.
- **`MaxTrackedParameters` clamps to a minimum of 1.** A test that builds
  `AudioTraceSettings` by hand and forgets the field gets exactly one parameter
  slot and a confusing failure. Consider having `Sanitized()` fill unset fields
  from `Default` rather than clamping to a floor.
- **No round-trip test from a real format-version-1 file.** `TraceFormat` claims v1
  logs still read back; nothing proves it. Check in a small v1 `.adtrace` fixture
  and read it.
- **Timeline window is untested above a few dozen records.** The lane element
  collapses per pixel and the ListView virtualises, so it should hold at 50k, but
  "should" is doing work in that sentence. Generate a large session and look.
- **Versioning is inconsistent.** `package.json` says `0.1.0`; the README module
  table says `v0.2 · FMOD`; `CHANGELOG.md` has everything under `[Unreleased]`.
  Decide the number before tagging anything.
- **Wwise define exists but nothing uses it.** `AUDIOTOOLBOX_WWISE` is detected and
  set by `MiddlewareDetector` and no assembly constrains on it yet.

---

## Facts worth not rediscovering

### FMOD

**Event paths resolve case-insensitively.** `event:/…/Basic2d` finds `Basic2D` and
plays it. Only a real transposition fails. A rename that changes case alone will
not break FMOD, and will break anything comparing those strings itself.

**Distance alone does not virtualise.** See the correction above.

**"Max Instances" and "Stealing" are not `EventMixerMaster.maxInstances` /
`instanceStealing`.** They are `maxVoices` and `voiceStealing` on the event's
`EventAutomatableProperties`. The mixer-bus properties set and save without
complaint and the built bank behaves as though nothing was configured.

**The stored stealing enum is not in the dropdown's order.** Studio lists Oldest,
Furthest, Quietest, Virtualize, None; the file stores Oldest 0, Quietest 1,
Virtualize 2, None 3, Furthest 4.

**FMOD virtualises the quietest**, which with two identical sounds is the one
already playing — so `MaxOneVirtualize` makes the *first* post go virtual and the
second stay `Started`.

**Callbacks arrive 8–15 frames after the post** (async Studio update plus two
thread hand-offs). Tests poll up to 150 frames rather than waiting a fixed count.

**Global parameters** are a `ParameterPreset` owning a `GameParameter` with
`isGlobal` set; every property that matters lives on the `GameParameter`.

### Unity / this project

- **Unity 6.2+ made `GetInstanceID()` an error** (`CS0619`, renamed `GetEntityId`),
  and the `EntityId → int` cast is *also* obsolete-as-error. `EmitterPathCache`
  therefore keys on reference identity via a custom `IEqualityComparer`, which
  does not depend on the engine's id at all.
- **This project is New Input System only** (`activeInputHandler: 1`), so
  `UnityEngine.Input.*` throws at runtime. The demo uses IMGUI for that reason.
- **URP.** Runtime tinting sets both `_BaseColor` and `_Color`.

### Driving the editor from the CLI

- Relaunch after a crash: `unity open --args "-automated" .` — there is no
  `--automated` flag on `open`.
- **Scripting defines do not apply until the next compile**, and a plain recompile
  is not always enough. Use
  `CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)`
  and expect to need it twice if a DLL is locked.
- **Files added to the package do not import on their own.** `AssetDatabase.Refresh(ForceUpdate)`
  first, or `recompile_status` reports `up_to_date` while the new types do not exist.
- `eval_file` **cannot use `using` directives** — the harness wraps the code in a
  method body. Fully qualify everything.
- `Q` / `Query` are extension methods; from `eval` call
  `UnityEngine.UIElements.UQueryExtensions.Query<T>(root, null, (string[])null)`.
- **`VisualElement.Children()` does not reach into a `ListView`'s rows** (it
  follows `contentContainer`). Walk `hierarchy` instead. This cost a wrong "the
  window built no rows" conclusion once.
- `capture_game_view --save_path` must be **inside the project**; it rejects
  absolute paths elsewhere. Write to `Temp/…` and delete afterwards.
- `run_tests` takes `--mode`, not `--platform`.

---

## Where things live

```
Modules/EventTracer/
  Runtime/            facade, voice slots, model            AudioToolbox.EventTracer.Core
    Recording/        everything behind AUDIOTOOLBOX_TRACE
  Backends/Native/    fallback, no constraint
  Backends/Fmod/      define constraint AUDIOTOOLBOX_FMOD
  Editor/             TraceLogReader (not behind the define)
    UI/               timeline window, TraceTimeline, TraceLaneElement, menu
  Tests/EditMode/     · PlayMode/ · PlayModeFmod/ · Support/
Tools/TraceFixture~/  headless FMOD fixture authoring
Samples~/EventTracerDemo/   the demo, imported via Package Manager
```

The test project is `E:\AudioProgramming`, which references the package by
`file:../../audio-toolbox` and lists it under `testables`. The demo is imported
there at `Assets/Samples/Audio Toolbox/0.1.0/EventTracer Demo/` — that copy is a
build artefact of the import, not source.
