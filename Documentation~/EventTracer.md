# EventTracer

**Runtime tracing for game audio.** Part of [Audio Toolbox](../README.md).

"There's no sound here" is not one bug. It is seven, and they need seven
different fixes:

| | what actually happened |
|---|---|
| the code never ran | a branch, a null, a disabled component |
| the handle was invalid | the bank is not loaded, or the event name is misspelt |
| the instance was refused | max instances reached, stealing set to None |
| it went virtual | out of range, too quiet, over the voice limit |
| something stole it | a newer instance took the voice |
| your own code stopped it | a state change, a fade, an early `Stop` |
| it played somewhere else | posted with no position, or the wrong one |

From the game they are indistinguishable: silence, no exception, no log line.
EventTracer records every call through its facade and tells them apart.

## Contents

- [Known limits](#known-limits) — read first
- [Using it](#using-it)
- [What a record carries](#what-a-record-carries)
- [The outcomes](#the-outcomes)
- [Backend support](#backend-support)
- [Architecture](#architecture) · [Performance](#performance) · [The log format](#the-log-format)
- [The test fixture](#the-test-fixture)

---

## Known limits

Ahead of everything else, because each one changes how you read a session.

**Code that calls the middleware directly is invisible.** The tracer only sees
instances it holds, so a `RuntimeManager.PlayOneShot` elsewhere in the codebase
leaves no record at all — and a missing record looks exactly like a sound that
was never posted. `FmodAudioTrace.Attach` brings existing instances in without
moving their call sites; AudioDoctor can list the call sites that bypass the
facade. Neither can tell you what an untraced call did.

**`NotCalled` is never produced by this module.** A tracer records calls that
happened. "The code never reached the line" needs static analysis or a
breakpoint. The value exists in the enum so a report can carry a finding from
AudioDoctor beside the runtime ones — not because the tracer can detect it.

**Wwise is not implemented.** Planned, and its virtualisation coverage will be
weaker than FMOD's because the callbacks carry less. That will be stated in this
table rather than papered over.

**A sound that outlives the buffer loses its outcome.** Records are patched in
place as callbacks arrive, so a voice still playing when its record scrolls out
of the ring buffer can no longer be updated. Counted in the session header, never
silently dropped.

**An emitter's path is fixed the first time it is seen.** Building it walks the
hierarchy and allocates a string, so it happens once per object and is cached.
An object renamed or reparented afterwards keeps the path it had at first
sighting. Two consequences worth knowing: a pooled emitter reused for a different
purpose still reads under its original path, and the cache holds a reference to
every emitter it has seen — bounded by `EmitterPathCapacity`, but that many
managed wrappers stay reachable for the session.

**Only global parameters are captured, and only ones the backend admits to.** Per
instance values are not recorded — the snapshot is taken at the post, before they
exist. On the native backend nothing is captured at all: Unity's audio has no
global parameter concept, and inventing one would put values in a log that no
engine ever acted on.

---

## Using it

Post sounds through the facade instead of calling the middleware:

```csharp
using AudioToolbox.EventTracer;

// Follows the transform, records the call site automatically.
var engine = AudioTrace.Post("event:/Vehicle/Engine", transform);

AudioTrace.SetParameter(engine, "RPM", rpm);
AudioTrace.Stop(engine);

// No emitter — UI, music, narration.
AudioTrace.Post("event:/UI/Click");

// A fixed world position.
AudioTrace.Post("event:/SFX/Explosion", impactPoint);
```

Already have FMOD code you are not ready to move?

```csharp
using AudioToolbox.EventTracer.Backends.Fmod;

var instance = RuntimeManager.CreateInstance(reference);
instance.start();
FmodAudioTrace.Attach(instance, "event:/Vehicle/Engine", transform);
```

Attaching traces the rest of the sound's life. What it cannot recover is anything
before the attach — an instance refused a voice is already gone, so `Rejected`
and `HandleInvalid` stay invisible on this path.

### Switching recording on

**Window → Audio Toolbox → EventTracer → Record Traces** adds
`AUDIOTOOLBOX_TRACE` to the active build target.

This is a decision, not a detection, which is why it is not automatic the way
`AUDIOTOOLBOX_FMOD` is: whether FMOD is installed is a fact about the project,
whether a build should carry a tracer is a call about the build. Note that
scripting defines are per build target — turning it on for the editor does not
turn it on for a console build.

With it off, `AudioTrace.Post` still plays sounds. The collection layer is not
compiled at all.

### Reading a session

Sessions are written to
`Application.persistentDataPath/AudioToolboxTraces/session-<timestamp>.adtrace`,
which is where you tell QA to look.

**Window → Audio Toolbox → EventTracer → Dump Latest Session** prints a summary
and every record that was not a plain `Started` — that filter is the whole
workflow in one line. **Open Trace Folder** reveals the directory.

The timeline window arrives in a later version; until then the console dump and
`TraceLogReader` are the way in. The reader is not behind `AUDIOTOOLBOX_TRACE`,
on purpose: the sessions worth reading come from someone else's build, and
whether *your* project has tracing on has nothing to do with whether you can open
one.

### Settings

```csharp
AudioTrace.Configure(new AudioTraceSettings
{
    RecordCapacity = 50_000,        // ~3.4 MB
    MaxConcurrentVoices = 512,
    SignalQueueCapacity = 4096,
    InternCapacity = 8192,
    EmitterPathCapacity = 4096,     // distinct emitters whose scene path is kept
    MaxTrackedParameters = 256,
    PendingSnapshotCapacity = 1024, // parameter states staged between flushes
    GlobalParameterSampleIntervalSeconds = 0.25f,
    WriteToDisk = true,
    FlushIntervalSeconds = 2f,
    NaturalEndToleranceSeconds = 0.1,
});
```

Before the first post. Afterwards the buffers exist and resizing them would mean
discarding a session in progress, so the call is ignored and says so.

`GlobalParameterSampleIntervalSeconds` reads oddly at its edges and the reading is
deliberate: **zero means no interval** — a poll every frame — and a **negative**
value turns polling off, leaving the facade as the only source of parameter
values.

---

## What a record carries

An outcome on its own rarely settles anything. "A footstep was `Virtualized`" is
the start of a question, not the end of one — which footstep, triggered from
where, with the game in what state. A record answers all four so that reading one
is enough:

| field | what it answers | where it comes from |
|---|---|---|
| `EventKeyId` | which event | the key you posted |
| `EmitterPathId` | which object | `/Level/Enemies/Rifleman/Muzzle`, walked once per emitter and cached |
| `CallSiteId` | which line | `[CallerFilePath]` / `[CallerLineNumber]`, a compile-time constant |
| `EmitterPos` · `ListenerPos` · `DistanceToListener` | how far away | sampled at the post; `-1` when there is nothing to measure |
| `ParamSnapshotId` | what the world was doing | the global parameters in force at the post |
| `Outcome` · `BackendResultCode` | what became of it | the normalised outcome and the middleware's raw code |

The console dump prints all of it:

```
f5    0.000s  StoppedEarly   event:/SFX/Gunshot  [5.0m]  Assets/Combat/Rifle.cs:88
  on /Level/Rifleman/Muzzle
  Tension=0.2  Weather=3
```

### Parameter snapshots

A snapshot is the set of **global** parameters — the ones describing the world
rather than one sound. They reach the tracer two ways:

- **Through the facade.** `AudioTrace.SetGlobalParameter` records the value as it
  is set: exact, immediate, no polling lag.
- **By asking the backend.** Every
  `GlobalParameterSampleIntervalSeconds`, the tracer reads the middleware's own
  parameter list. This is what catches values set by code that never goes through
  the facade, which are exactly the ones that explain a sound nobody can account
  for.

Storage is differential, and that is what makes it affordable. A capture taken
while nothing has changed hands back **the same id the previous one got**, so a
burst of forty footsteps under one unchanging state shares a single snapshot; a
capture after a change writes only what changed. Reading is the reverse walk, and
the reader always hands back the whole set.

Per-instance parameters are deliberately not captured. A snapshot is taken at the
post, before any of them could have been set, so recording them would say nothing.

---

## The outcomes

| | means | look at |
|---|---|---|
| `HandleInvalid` | No instance could be obtained | `BackendResultCode`, and the event name for typos |
| `Rejected` | Created, never granted a voice | the event's max instances and stealing mode |
| `Started` | Played, or is playing | nothing — this one worked |
| `Virtualized` | Playing, producing no output | `DistanceToListener`, the event's range, the voice limit |
| `Stolen` | Cut off by the engine | what else is posting the same event |
| `StoppedEarly` | Your code stopped it | the call site that called `Stop` |
| `NotCalled` | Never produced here — see [Known limits](#known-limits) | |

Two of these are worth spelling out.

**`Stolen` versus `StoppedEarly`.** The middleware reports both as a stop before
the event's length; nothing in the callback says who asked. The facade knows,
because `AudioTrace.Stop` went through it. That single bit is the difference
between "the engine took your voice" and "your own state machine cut it", which
are entirely different bugs.

**`Virtualized` is sticky.** A sound that came back from virtual keeps the
outcome, because a record that reverted to `Started` would erase the only
evidence of the dropout somebody is reporting. If a virtualised sound is then cut
short, the cut wins — it is what explains the silence at the end.

**Distance is `-1`, not `0`, when there is nothing to measure** — a post with no
position, or no listener yet. Zero reads as "right on top of the listener", which
is plausible and wrong.

---

## Backend support

| | FMOD | Native (Unity audio) | Wwise |
|---|---|---|---|
| HandleInvalid | ✅ | ✅ | — |
| Rejected | ✅ | ❌ | — |
| Started | ✅ | ✅ | — |
| Virtualized | ✅ | ❌ | — |
| Stolen | ✅ | ✅ | — |
| StoppedEarly | ✅ | ✅ | — |
| Distance to listener | ✅ | ✅ | — |
| Emitter scene path | ✅ | ✅ | — |
| Call site | ✅ | ✅ | — |
| Global parameter snapshots | ✅ | ❌ | — |
| Attach an existing instance | ✅ | ❌ | — |

**The Native backend is a real backend, not a null object.** Clips are loaded
from `Resources` by event key, sounds are heard, and a pool of 64 AudioSources
runs out and steals the oldest — which is a genuine `Stolen`. What it cannot
report is `Rejected` and `Virtualized`, because those belong to a virtual voice
system Unity does not have, and global parameters, which Unity's audio has no
notion of at all. They are absent from this table rather than approximated.

**FMOD is optional.** The backend assembly carries a define constraint, so a
project with no middleware compiles and falls back to Native.

---

## Architecture

### The seam that makes the mapping testable

```
FMOD RESULT / EVENT_CALLBACK_TYPE
          │  FmodSignalMap — translation only, no judgement
          ▼
      ProbeSignal            backend-neutral: CreateOk, Started, WentVirtual,
          │                  Stopped, Destroyed, StopRequested …
          ▼
  OutcomeStateMachine        pure; no Unity, no middleware, no state outside
          │                  the struct handed in
          ▼
    PlaybackOutcome
```

Deciding that a particular sequence of callbacks means "stolen" is the riskiest
code in the module — callback ordering is where middleware documentation and
middleware behaviour part company. So it lives where it can be driven from a
table in an EditMode test on a machine with no middleware installed, and the
backends only translate.

The PlayMode fixture then answers the other half: does FMOD *actually* emit that
sequence? Keeping the two questions apart is what makes a red test mean something
specific.

### Assemblies

```
AudioToolbox.EventTracer.Core            Runtime  facade, voice slots, recording, format
AudioToolbox.EventTracer.Backend.Native  Runtime  fallback, no constraint
AudioToolbox.EventTracer.Backend.Fmod    Runtime  define constraint: AUDIOTOOLBOX_FMOD
AudioToolbox.EventTracer.Editor          Editor   log reader, menus
AudioToolbox.EventTracer.TestSupport     Runtime  the fake probe (UNITY_INCLUDE_TESTS)
AudioToolbox.EventTracer.Tests.EditMode
AudioToolbox.EventTracer.Tests.PlayMode
AudioToolbox.EventTracer.Tests.PlayMode.Fmod      constraint: AUDIOTOOLBOX_FMOD
```

The namespace is `AudioToolbox.EventTracer` even though the assembly is
`…​.EventTracer.Core`: the facade is the package's most-typed line of code, and
`using AudioToolbox.EventTracer;` is what it should be.

**Backends register themselves** with `AudioTrace.RegisterProbe` from a
`[RuntimeInitializeOnLoadMethod]`, so the core assembly never names one. Unlike
AudioDoctor, which discovers its backends reflectively, this is a runtime path:
sweeping every loaded assembly at startup costs real time on a console, and
reflection needs keeping alive by hand under IL2CPP. A backend assembly only
compiles when its middleware is present, so having each one announce itself is
both cheaper and harder to get wrong.

### What compiles out

With `AUDIOTOOLBOX_TRACE` off, everything under `Runtime/Recording/` is gone:
ring buffer, intern table, outcome state machine, session writer, writer thread.

What remains is the facade, the voice registry and the signal queue. Those are
playback infrastructure, not collection: voice slots have to be recycled when
sounds end whether or not anyone is recording, and a handle has to stay safe to
hold. The data types in `Runtime/Model/` also remain — declarations with no code,
which the log reader shares with the writer.

---

## Performance

The collection layer ships in the player, so these are constraints, not targets.
Each is a test in `TraceBudgetTests`.

| | budget | how |
|---|---|---|
| GC per post | **0 B** | struct records in one flat array; every string is an interned `int` |
| 200 concurrent sounds | < 0.5 ms/frame | dense voice ids index plain arrays — no dictionaries on the hot path |
| Ring buffer | < 8 MB | 50,000 × 68 bytes ≈ 3.4 MB |
| Callback thread | no locks, no allocation | a lock-free queue; the callback resolves a voice and pushes |
| Disk | never on the main thread | double-buffered, background writer; a busy writer means the batch waits, not the game |

Zero allocation is not an aspiration here. A tracer that allocated once per sound
would trigger a collection every few seconds in a busy scene, and the frame hitch
would be indistinguishable from the audio problems it was installed to find.

The one place the main thread waits on the disk is shutdown, where there is no
next frame to try again on.

---

## The log format

`.adtrace` is a magic number, a version, and then a stream of tagged chunks:
interned strings, parameter slots, parameter snapshots, records, and the session
header as JSON.

Chunked rather than sectioned, because the sessions that matter most are the ones
that ended badly. A layout with a table of contents at the end is unreadable
after a crash — precisely when someone most wants to see what the audio system
was doing. Here a truncated file loses its last chunk and nothing else, and
because **every chunk is written after everything it depends on** — strings, then
the parameter slots that name them, then the snapshots that use those slots, then
the records that point at those snapshots — what survives still resolves.

A snapshot chunk carries only the parameters that differ from the snapshot before
it, plus the id of that earlier one. A reader walks the chain back to the first
snapshot to rebuild the full set; `TraceSession.TryResolveParameters` does that
and returns false rather than a partial set when the chain is not all there.

Format version 2 added the parameter and snapshot chunks. The record layout is
unchanged, so version 1 logs still read back — every record in one simply carries
no snapshot, which is the truth about a session recorded before this existed.

The header carries the Unity version, platform, backend and its version, buffer
capacity, and the counts of anything dropped. A truncated session that presented
itself as complete would have someone conclude a sound was never posted when in
fact the evidence was overwritten.

Records are written field by field rather than blitted: a memory copy would make
the file depend on field packing and host endianness, so a log from a console
would read as garbage on a desktop — the one case the format exists for.

---

## The test fixture

The seven outcomes need FMOD events built to provoke them: max instances of one
with stealing set to None for a refusal, to Oldest for a steal, to Virtualize for
a virtualisation, and a 3D event with a narrow range to fall out of.

`Tools/TraceFixture~/build-trace-fixture.ps1` authors them into an FMOD Studio
project and builds the bank, driving `fmodstudiocl` over a scripting API script.
It generates its own audio asset, so nothing has to be supplied, and re-running
reconfigures the existing events rather than duplicating them.

It also authors one global parameter, `AudioToolboxTraceTension`, for the
parameter-capture tests. Global parameters are project-wide and cannot live in a
folder, so the name carries its own prefix to stay out of the way of whatever the
project already has.

```powershell
./Tools/TraceFixture~/build-trace-fixture.ps1 -Project path/to/Project.fspro
```

Two things it knows that cost an afternoon to find out, both recorded in the
script:

**"Max Instances" and "Stealing" are not `EventMixerMaster.maxInstances` and
`instanceStealing`.** They are `maxVoices` and `voiceStealing` on the event's
`EventAutomatableProperties`. The similarly-named mixer bus properties set and
save without complaint, and the built bank then behaves as though nothing was
configured.

**The stored stealing enum is not in the dropdown's order.** Studio lists Oldest,
Furthest, Quietest, Virtualize, None; the file stores Oldest 0, Quietest 1,
Virtualize 2, None 3, Furthest 4. Trusting the visible order silently authors the
wrong behaviour — 4 reads as None but steals, 3 reads as Virtualize but refuses.

---

## Roadmap

Shipped: the facade, zero-allocation collection, the on-disk format, the FMOD
backend, all seven outcomes, and the context on each record — emitter path, call
site, distance and parameter snapshot.

Next: the timeline window with filtering and `.adtrace` drag-and-drop; the Wwise
backend; and the blind-spot notice driven by AudioDoctor's static scan.
