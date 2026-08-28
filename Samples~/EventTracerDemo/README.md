# EventTracer Demo

Seven sounds that go missing in seven different ways, each on a button.

From the call site they are indistinguishable — `AudioTrace.Post` returns, no
exception, no log line, and six of the seven produce silence or something wrong.
The point of the demo is to fire them on demand and then show the trace telling
them apart.

## Setup

1. **FMOD is required.** The demo's events come from `Tools/TraceFixture~`; run
   `build-trace-fixture.ps1` against your FMOD Studio project and let Unity
   reimport the banks.
2. **Turn recording on**: Window ▸ Audio Toolbox ▸ EventTracer ▸ **Record Traces**.
   Without it the sounds still play and nothing is collected — the demo says so in
   a banner rather than looking broken.
3. **Window ▸ Audio Toolbox ▸ EventTracer ▸ Create Demo Scene.** The scene is
   generated rather than shipped as an asset, so it is built against whatever
   render pipeline and Unity version you actually have.

## Recording it

FMOD draws its own debug overlay in the top-left corner; the panel leaves room for
it, and switching it off under **FMOD ▸ Edit Settings ▸ Logging** gives a cleaner
capture.

Press Play, then **Run all**. The cases fire in order with a gap between them,
which is what gives the session a readable time axis afterwards — seven distinct
columns rather than one pile.

Each object turns the colour of its outcome as the middleware's callbacks arrive,
and stays dimmed until that outcome is final. Then open **Window ▸ Audio Toolbox ▸
EventTracer ▸ Timeline** and press **Latest Session**.

## The seven

| | what the designer did | what you hear | what the trace says |
|---|---|---|---|
| Plays fine | nothing wrong | the tone | `Started` |
| Event name has a typo | renamed an event, missed a reference | nothing | `HandleInvalid` |
| Instance limit, stealing None | capped at one voice | only the first | `Rejected` |
| Instance limit, stealing Oldest | capped at one voice | the tail gets cut | `Stolen` |
| Instance limit, stealing Virtualize | capped at one voice | nothing, but it *is* playing | `Virtualized` |
| Posted out of earshot | emitter 40 m away, max distance 10 m | nothing | `Started`, at 49 m |
| The game stopped it | your own `Stop` call | it cuts out | `StoppedEarly` |

**The out-of-earshot row is the one to pause on.** Its outcome is `Started`, and
that is correct: distance alone does not virtualise a voice, so the sound really is
playing, at nothing. The outcome is not the answer there — the `49 m` on the record
is. It is the clearest argument for why a trace keeps the emitter, the position and
the distance rather than an outcome alone.

`StoppedEarly` and `Stolen` are the opposite case: different outcomes that look
identical in a middleware callback, told apart only because the stop went through
the facade.

`NotCalled` — the code never ran — has no button, because a tracer records calls
that happened. Finding those is AudioDoctor's job.
