# AudioDoctor

**Static validation for game audio pipelines.** Part of
[Audio Toolbox](../README.md).

Three things drift apart on every project, and none of them announces it when
they do:

| | maintained by | lives in |
|---|---|---|
| what the middleware **declares** | audio designers | the FMOD Studio project |
| what the banks **contain** | the build | `.bank` files |
| what the game **references** | programmers | prefabs, scenes, code, Timeline |

Almost every consequence is a **silent failure**. A sound that never plays. A
parameter that never takes effect. A bank missing on one platform. No exception,
no log line — just "why isn't the ducking kicking in", and half a day in the
mixer before anyone suspects a typo in a string.

AudioDoctor reconciles all three without running the game, and reports the gaps
as a locatable, fixable list. **It never modifies an asset.**

## Contents

- [Using it](#using-it)
- [The rules](#the-rules) — summary, then a handbook entry for each
- [Backend × rule support](#backend--rule-support)
- [What it will not do](#what-it-will-not-do)
- [Architecture](#architecture)
- [Configuration](#configuration) · [Reports](#reports) · [Known limits](#known-limits)

---

## Using it

**Window → Audio Toolbox → AudioDoctor → Diagnostics** → **Run Validation**.

- Severity counts double as filters — click one to hide or show it.
- Group by **severity**, **rule** or **asset**.
- Double-click a finding to select the asset; code findings open at the line.
- **Copy** or `Cmd/Ctrl+C` puts a finding on the clipboard as plain text.
- **Export** writes `AudioDoctorReports/` as JSON (for CI) and Markdown (for people).

From the command line:

```bash
Unity -batchmode -nographics -projectPath <project> \
      -executeMethod AudioToolbox.AudioDoctor.Editor.AudioDoctorMenu.RunValidation -quit
```

A CI run needs **two** invocations: the first lets the middleware detector write
its scripting defines, the second compiles the matching backend and scans.
Defines only take effect on the next compile.

---

## The rules

| | | Level | Catches |
|---|---|---|---|
| **R001** | Dangling reference | Error | The game asks for an event the middleware does not declare. Tells a typo apart from a case-only mismatch. |
| **R003** | Bank never loaded | Error | A bank is referenced but nothing anywhere loads it. Its events cannot play. |
| **R004** | Orphan event | Warning | An event ships in a bank that nothing references. |
| **R005** | Loading strategy | Warning | A long track that is not streamed, or a short one that is. |
| **R006** | 3D played unpositioned | Warning | A spatialized event played through a call with no position, so it plays at the world origin. |
| **R007** | Unknown parameter | Error | Code sets a parameter the event does not have. **Completely silent at runtime.** |
| **R008** | Naming convention | Info | Names breaking the project's pattern, and pairs differing only by case. |
| **R009** | Cross-platform banks | Error | A bank not built for every platform, or bank names differing only by case. |
| **R000** | Scan coverage | Info | What the scan itself could not resolve. Not a defect — an admission. |

Symptom, cause, fix and how to verify for each:
[**Documentation~/AudioDoctor.md**](Documentation~/AudioDoctor.md).

Thresholds, severities and the naming pattern are configurable —
**Assets → Create → Audio Toolbox → AudioDoctor → Rule Set**.

---

## Backend × rule support

Generated from the capability flags each backend declares, not hand-maintained.

| Rule | FMOD | Native | Wwise |
|---|---|---|---|
| R000 Scan coverage | ✅ | ✅ | — |
| R001 Dangling reference | ✅ | ✅ | — |
| R002 Not in any bank | ❌ | ✅ | — |
| R003 Bank never loaded | ✅ | ❌ | — |
| R004 Orphan event | ✅ | if bundles | — |
| R005 Loading strategy | ✅ | ✅ | — |
| R006 3D unpositioned | ✅ | ❌ | — |
| R007 Unknown parameter | ✅ | ❌ | — |
| R008 Naming convention | ✅ | ✅ | — |
| R009 Cross-platform banks | ✅ | ❌ | — |

**FMOD is optional.** With no middleware installed the Native backend runs
against Unity's own AudioClips, and the rules whose data it cannot supply are
skipped *and listed* rather than approximated.

**R002 cannot run on FMOD, and the reason is structural.** The integration builds
its event list by loading each built bank and enumerating its contents, so an
event assigned to no bank never reaches Unity at all — which is exactly the
event R002 looks for. It is reported as a *skipped check*, never as a pass.

The Wwise column is empty because that backend is a placeholder. Marking the
boundary honestly is worth more than a filled-in matrix.

---

## What it will not do

Every entry here is a decision, not an oversight.

**A rule whose data the backend cannot supply is skipped and listed.** Reports
carry a permanent *"Checks that did not run"* section, because "nothing is wrong"
and "nothing was checked" are different results.

**Event names built at runtime cannot be resolved.** They become coverage notes,
and every R004 finding then carries a "confirm before deleting" caveat — getting
someone to delete audio that is in use is worse than missing an orphan.

**R003 reports a bank nothing loads *anywhere*, not per scene.** A bank loader on
a runtime-instantiated prefab is invisible to a static scan.

**R006 reports only 3D events played without a position.** Every emitter is
positioned by definition, so flagging the reverse would fire on every correctly
wired 2D UI sound.

**R007 stays silent when it cannot resolve a call with certainty.** At Error
level a wrong finding costs more than a missing one, because a validator that
cries wolf gets switched off and then catches nothing at all.

---

---

## Architecture

### Three layers

```
   FMOD Studio project          Unity project
          │                          │
          ▼                          ▼
   ┌────────────────────────────────────────┐
   │  Backends   IAudioProjectSource        │  middleware adapters,
   │  FMOD · Native · (Wwise, v0.2)         │  found reflectively
   └──────────────────┬─────────────────────┘
                      ▼
   ┌────────────────────────────────────────┐
   │  Normalized model  AudioProjectSnapshot│  the only interchange format
   │  EventDef · BankDef · EventRefUsage    │
   │  ParameterUsage · BankLoadUsage        │
   └──────────────────┬─────────────────────┘
                      ▼
   ┌────────────────────────────────────────┐
   │  Rules   IValidationRule × 9           │  never touch a middleware
   └──────────────────┬─────────────────────┘
                      ▼
             window · JSON · Markdown
```

**A rule may only read the snapshot.** No AssetDatabase, no middleware, no
filesystem. That is not tidiness for its own sake — it is the entire test
strategy. 87 EditMode tests feed hand-written `EventDef[]` arrays to the rules
and assert the output, which means **the whole rule set can be proved correct on
a machine with no middleware installed at all.**

### Assemblies

```
AudioToolbox.AudioToolbox.AudioDoctor.Core    Runtime  no dependencies, data model only
AudioToolbox.AudioToolbox.AudioDoctor.Editor  Editor   backend seam, scanners, rule engine, UI, reports
AudioToolbox.AudioToolbox.AudioDoctor.Backend.Native   fallback, no constraint
AudioToolbox.AudioDoctor.Backend.Fmod    Editor   define constraint: AUDIOTOOLBOX_FMOD
AudioToolbox.AudioDoctor.Backend.Wwise   Editor   define constraint: AUDIOTOOLBOX_WWISE (placeholder)
AudioToolbox.AudioToolbox.AudioDoctor.Tests.Editor     Core + Editor only
```

`AudioToolbox.AudioDoctor.Editor` references **no backend**. They are discovered with
`TypeCache.GetTypesDerivedFrom<IAudioProjectSource>()`. This is a hard
constraint rather than a preference: the moment the editor assembly referenced the FMOD
backend, the test assembly's dependency graph would be contaminated and
"the tests pass without middleware installed" would stop being true.

`TypeDiscovery` filters discovery to public, top-level types in assemblies that
do not reference NUnit. Test assemblies are compiled into the editor and
`TypeCache` does not distinguish them — the first end-to-end run reported the
unit tests' fake rules in a real scan.

### Middleware detection

`MiddlewareDetector` runs `[InitializeOnLoad]`, probes the `AppDomain` for
`FMODUnity.RuntimeManager` and `AkUnitySoundEngine`, and adds or removes
`AUDIOTOOLBOX_FMOD` / `AUDIOTOOLBOX_WWISE` to match. Three details decide whether
it is usable:

- **It preserves the project's own defines** and only touches its two.
- **It writes only when the set actually changed.** Otherwise every domain
  reload triggers a recompile and it never converges.
- **It runs synchronously under `-batchmode`.** `delayCall` never fires before
  `-quit`, so a CI run would compile with whatever defines it happened to
  inherit — the exact failure this class exists to prevent.

### Capability flags

```csharp
[Flags] public enum BackendCapability {
    EventLength, StreamingFlag, SpatialFlag, Parameters,
    BankMembership, PlatformBanks, BankLoadInfo,
    GlobalParameters, UnpackedEvents
}
```

Each backend declares what it can genuinely supply. Each rule declares what it
needs. The engine takes the difference: anything missing means the rule is
skipped and the reason is written into the report.

> This is the pivot the whole tool's credibility turns on. A report saying
> "0 errors" means something entirely different when six rules never ran.
> **The tool never guesses.**

The support matrix in the README is generated from these flags rather than
maintained by hand, so it cannot drift from behaviour.

---

## Rule handbook

Symptom, cause, fix, and how to verify — for every rule.

### R000 · Scan coverage `Info`

**Symptom** — the report contains notes about things the scan could not resolve.

**Cause** — the inherent boundary of static analysis. Most often an event name
assembled at runtime:

```csharp
RuntimeManager.PlayOneShot("event:/SFX/" + weaponType);   // unresolvable
```

**Fix** — usually nothing. This is not a defect; it is the scanner admitting
what it could not see. If there are many, R004's orphan verdicts are
correspondingly less reliable.

**Verify** — the Notes section names the file and line.

> The existence of this rule is itself a position: put the blind spots in the
> deliverable, rather than letting a clean report look like a clean project.

---

### R001 · Dangling reference `Error`

**Symptom** — a sound never plays, and nothing appears in the console.

**Cause** — the project references an event the middleware does not declare.
Either the event was renamed or deleted after the reference was authored, or
the reference has a typo.

**Fix** — open the asset and line the report points to and repoint it, or
restore the event.

**Case-only mismatches are called out separately**, because they are a
different bug with a different fix:

> The middleware project declares 'event:/UI/Click', which differs from the
> reference only by letter case. This may still play in the editor, but bank
> lookups are case-sensitive on some platforms.

On macOS it will usually play — FMOD's event lookup is case-insensitive. It
breaks on a Linux build machine.

**Granularity** — one finding per usage site. Three prefabs referencing the same
broken event produce three findings, because there are three places to fix and
the report has to take you to each.

**Verify** — `RuntimeManager.PlayOneShot("event:/DoesNotExist")` should produce
exactly one Error.

---

### R003 · Bank never loaded `Error`

**Symptom** — every event in one bank fails to play.

**Cause** — the bank is referenced, but nothing in the project loads it: no
loader component, no `LoadBank` call, and no setting that loads it at startup.

**Fix** — add the bank to the middleware's load list, or place a bank loader in
the scene that needs it.

**Prerequisite** — the rule reads the middleware's bank-load settings *first*.
FMOD's default configuration loads every bank at startup, and under that
configuration "this scene has no loading logic" is not a defect at all. A rule
blind to the setting would report every bank of every scene.

**Deliberate narrowing** — it reports a bank nothing loads *anywhere*, not
"this scene has no loading logic". A loader on a runtime-instantiated prefab is
invisible to a static scan, and per-scene judgement would report working
projects as broken. Zero false positives beats maximum coverage.

**Verify** — remove one referenced bank from the load list; exactly that one
should be reported.

---

### R004 · Orphan event `Warning`

**Symptom** — download size and memory spent on audio nobody plays.

**Cause** — the event is packed into a bank, and nothing in the project
references it.

**Fix** — wire it up, or remove it from the bank.

**⚠️ Confirm before deleting.** This is the only rule that concludes from an
*absence*. A static scan cannot see an event fetched by a name built at runtime,
so `PlayOneShot("event:/SFX/" + type)` makes `event:/SFX/Pistol` look like an
orphan. Whenever the scan produced any R000 note, every R004 finding carries
that caveat automatically. **Getting someone to delete audio that is in use is
far worse than missing an orphan.**

**False-positive guard** — if the scan found *zero* references anywhere, R004
returns nothing at all. With no references every packed event qualifies, which
would be technically true and completely useless.

**Verify** — pack an event, reference it from nowhere.

---

### R005 · Loading strategy `Warning`

**Symptom** — a memory spike, or a hitch when a short sound fires.

**Cause** — two opposite mistakes, neither audible in the editor:

| | cost |
|---|---|
| long audio **not streamed** | decoded whole into memory when its bank loads, and stays there — the easiest way to blow a console memory budget |
| short audio **streamed** | a file handle and a seek on every trigger; a sound firing ten times a second becomes a hitch |

**Fix** — toggle **Stream** on the audio asset inside the event. Note that
streaming is a property of the *audio instrument*, not of the event.

**Thresholds** — long > **15s**, short < **2s**, both configurable. Nothing is
reported between them; in that band either strategy is defensible.

**Verify** — one event over the long threshold that is not streamed, and one
under the short threshold that is.

---

### R006 · 3D event played without a position `Warning`

**Symptom** — a 3D sound always seems to come from the same place.

**Cause**

```csharp
RuntimeManager.PlayOneShot("event:/Footsteps");   // position defaults to Vector3.zero
```

`PlayOneShot`'s position argument defaults to `Vector3.zero` — the **world
origin**, not the listener and not the object that triggered it.

What makes this one slippery is that **it is not silent**. It plays, from the
wrong place, and in a small test scene the origin is often close enough that
nobody notices until the level gets big.

**Fix**

```csharp
RuntimeManager.PlayOneShot("event:/Footsteps", transform.position);   // positioned
RuntimeManager.PlayOneShotAttached("event:/Footsteps", gameObject);   // follows
// or put the event on an emitter component
```

**Deliberate asymmetry** — the reverse direction (a 2D event played through a
positioned call) is **not** reported. Every emitter is positioned by definition,
so flagging it would fire on every correctly wired UI sound in the project. One
direction is an audible bug; the other is how 2D audio is normally wired.

**Verify** — a 3D event played with the single-argument overload should produce
exactly one finding, and all three correct forms none.

---

### R007 · Unknown parameter `Error`

> **The highest-value rule in the set.**

**Symptom** — a parameter-driven effect never takes hold. No exception, no
warning, no log line.

**Cause** — code sets a parameter the event does not declare. The middleware
looks the name up, does not find it, and returns.

```csharp
_music.setParameterByName("Intensty", 0.8f);   // the event declares "Intensity"
```

What the team experiences is "the ducking never kicks in", and they search the
mixer, the trigger logic and the automation before anyone suspects a string.
**Reading the source does not help either** — `Intensty` and `Intensity` sitting
next to each other look identical to a human eye.

**Fix** — correct the name, or add the parameter. The report lists the
parameters the event actually declares, and calls out any that differ only by
case.

**How calls are tied to events** — this decides what can and cannot be checked.
The scanner tracks variable assignment **within a single file**:

```csharp
// ✅ checked
var inst = RuntimeManager.CreateInstance("event:/Music/Level_01");
inst.setParameterByName("Intensity", 0.5f);

// ⚠️ not checked — reported as an R000 note instead
public EventReference musicEvent;
var inst = RuntimeManager.CreateInstance(musicEvent);
inst.setParameterByName("Intensity", 0.5f);
```

When the link cannot be made the tool reports **"this could not be checked, and
here is why"** rather than a verdict that might be wrong. R007 is Error-level,
and **a wrong Error is how a validator gets switched off — after which it
catches nothing at all.**

**Verify** — misspell a parameter on an event that has one. The correctly
spelled call on the next line must produce nothing.

---

### R008 · Naming convention `Info`

**Symptom** — event names that break the project's convention, or two events
whose names differ only by case.

**Default pattern** — `^event:/[A-Za-z0-9_]+(/[A-Za-z0-9_]+)*$`: letters,
digits, underscores, and `/` as a separator. No spaces, hyphens or punctuation.

**Fix** — rename, or change `Event Naming Pattern` on the rule set asset.
Leaving it empty disables the pattern check.

**The case-collision half is not cosmetic.** macOS and Windows are
case-insensitive by default; Linux is not. Two events differing only in case
coexist on the authoring machine and collide on a build server, where
references resolve to whichever wins. Teams that care can raise R008 to Error
on the rule set.

> Events must be assigned to a bank to be visible at all — see
> [below](#why-r002-is-deferred).

---

### R009 · Cross-platform banks `Error`

**Symptom** — a whole bank of audio missing on one platform.

**Three checks:**

| check | level | |
|---|---|---|
| missing platform | Error | a bank not built for a platform its siblings were built for |
| name case collision | Error | bank names become filenames, and collide across filesystems |
| size deviation | — | **off by default** |

**How platforms are determined** — the middleware records a bank's size once per
platform it was built for, so the set of platforms a bank appears under *is* the
record of which builds exist. The model expands that into one entry per bank per
platform and groups by name.

**Single-platform projects are not reported.** With one platform there is
nothing to be inconsistent with, and inventing an expectation would report every
desktop-only project. Set `Required Platforms` on the rule set to state the
expectation explicitly.

**Size deviation defaults to off.** Platforms legitimately use different
encodings, so a bank being half the size on mobile is normally correct
configuration rather than a defect.

**Fix** — rebuild with the missing platform selected.

**Verify** — build for two platforms, delete one bank file from one platform's
build folder, **touch the remaining bank files**, then refresh.

> Why touch: FMOD's cache refresh decides whether it is current by comparing the
> newest bank write time. Deleting a file changes no other file's timestamp, so
> the cache considers itself up to date and skips the refresh.

---

## Configuration

**Assets → Create → Audio Toolbox → AudioDoctor → Rule Set**. Put it anywhere under `Assets/`;
the tool finds it.

| Setting | Default | |
|---|---|---|
| **Rules** | empty = all enabled | per-rule enable and severity override |
| **Long Event Seconds** | 15 | R005 upper threshold |
| **Short Event Seconds** | 2 | R005 lower threshold |
| **Event Naming Pattern** | `^event:/[A-Za-z0-9_]+(/[A-Za-z0-9_]+)*$` | R008; **empty disables** |
| **Required Platforms** | empty = platforms found | R009 expectation |
| **Bank Size Deviation Ratio** | **0 = off** | R009 size check |

---

## Reports

Written to `<project>/AudioDoctorReports/`.

| File | Audience |
|---|---|
| `audiodoctor-report.md` | people |
| `audiodoctor-report.json` | CI |

`skippedRules` is part of the JSON payload rather than a footnote: a CI job
seeing zero errors must be able to tell "nothing is wrong" from "half the rules
never ran". `schemaVersion` is bumped whenever the shape changes.

---

## Known limits

### Why R002 is deferred

FMOD's Unity integration builds its event cache by loading each `.bank` file and
calling `bank.getEventList()` — it enumerates events **inside banks**.

**An event assigned to no bank therefore does not exist as far as Unity is
concerned.** That is exactly the event R002 looks for, so on the FMOD backend the
rule is not "finding nothing" — it is *structurally incapable* of finding
anything.

Two consequences:

1. **R002 is reported as skipped**, with the reason `Backend 'fmod' does not
   provide: UnpackedEvents`. It never pretends to have run.
2. **R001 is imprecise here.** A reference to an unpacked event is reported as
   "does not exist", when in fact it exists and is simply not packed — a
   different fix.

**The v0.2 solution**: when the source is a Studio project, parse
`Metadata/Event/*.xml` and the `EventFolder` relationships directly to recover
the true authored set. Prototyped and confirmed to work.

### Everything else

| Limit | Effect | Handling |
|---|---|---|
| event names built at runtime | unresolvable | R000 note; R004 findings gain a "confirm first" caveat |
| parameter calls whose receiver crosses files | cannot be tied to an event | R000 note — **never a guess** |
| bank loaders on runtime-instantiated prefabs | invisible | R003 narrowed to project-wide |
| unsaved scene changes | scenes skipped | reported explicitly; losing someone's in-progress scene would be worse than anything the tool could find |
| third-party plugin sources | scanned | path exclusions planned for v0.2 |
| Wwise | not implemented | placeholder backend reports why |

---

## Verification

AudioDoctor was developed against a deliberately broken FMOD Studio project with
one planted defect per rule **and a negative case for each**. The negative cases
are what make the number below mean anything:

> 14 findings, every one planted on purpose. **Zero false positives.**

Run the package's own tests via **Window → General → Test Runner** (EditMode).

---

## Contributing

Issues and pull requests: <https://github.com/songdsp/audio-toolbox>.

**Every bug fix adds a regression case.** Already recorded under that
convention:

| Bug | Regression test |
|---|---|
| unit-test fake rules appeared in real scans | `RuleEngineTests.DiscoveryDoesNotPickUpTestDoubles` |
| `PlayOneShot("event:/" + kind)` reported the literal fragment as a missing event | `CodeLiteralTests` ×7 |
| events but zero references printed "No issues found" | `ProjectScannerTests.SaysSoWhenItFoundEventsButNoReferencesAtAll` |
| R002 silently found nothing on FMOD | `R002_UnpackedEventTests.IsSkippedByABackendThatCannotSeeUnpackedEventsAtAll` |
