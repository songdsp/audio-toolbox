using System.Collections.Generic;
using AudioToolbox.EventTracer.Editor;
using NUnit.Framework;

namespace AudioToolbox.EventTracer.Tests
{
    /// <summary>
    /// What the timeline window is showing, asked without opening one.
    /// </summary>
    /// <remarks>
    /// The window's claim is that one filter setting turns tens of thousands of records
    /// into the short list of sounds nobody heard. That claim lives entirely in
    /// <see cref="TraceTimeline"/>, so it is settled here — grouping, filtering and the
    /// ordering that decides what lands on the first screen. What is left in the window
    /// is layout, painting and input, which a test of this kind could not judge anyway.
    /// <para>
    /// Sessions are built by hand rather than round-tripped through a file. The reading
    /// and writing of logs is covered by <see cref="TraceLogRoundTripTests"/>; mixing the
    /// two would mean a format change broke these too, and the failure would say the
    /// wrong thing.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class TraceTimelineTests
    {
        private TraceSession _session;
        private int _nextStringId;

        [SetUp]
        public void SetUp()
        {
            _session = new TraceSession();
            _nextStringId = 0;
        }

        private int Intern(string value)
        {
            var id = _nextStringId++;
            _session.Strings[id] = value;
            return id;
        }

        private void Add(
            string eventKey,
            PlaybackOutcome outcome,
            double seconds,
            string emitter = null,
            string callSite = null)
        {
            _session.Records.Add(new AudioTraceRecord
            {
                TimeSeconds = seconds,
                Frame = (long)(seconds * 60),
                EventKeyId = Intern(eventKey),
                EmitterPathId = emitter == null ? TraceFormat.NoStringId : Intern(emitter),
                CallSiteId = callSite == null ? TraceFormat.NoStringId : Intern(callSite),
                Outcome = outcome,
                ParamSnapshotId = TraceFormat.NoSnapshotId,
                DistanceToListener = -1f,
            });
        }

        private TraceTimeline Build(TraceFilter filter, TraceGrouping grouping = TraceGrouping.Event) =>
            TraceTimeline.Build(_session, grouping, filter);

        private static List<string> LabelsOf(TraceTimeline timeline)
        {
            var labels = new List<string>();

            foreach (var lane in timeline.Lanes)
            {
                labels.Add(lane.Label);
            }

            return labels;
        }

        [Test]
        public void AnEmptySessionProducesNoLanesRatherThanThrowing()
        {
            var timeline = Build(TraceFilter.Everything);

            Assert.That(timeline.Lanes, Is.Empty);
            Assert.That(timeline.VisibleRecordCount, Is.Zero);
        }

        [Test]
        public void ANullSessionIsAnEmptyTimeline()
        {
            var timeline = TraceTimeline.Build(null, TraceGrouping.Event, TraceFilter.Everything);

            Assert.That(timeline.Lanes, Is.Empty);
        }

        [Test]
        public void GroupingByEventPutsEveryPostOfOneEventOnOneLane()
        {
            Add("event:/SFX/Footstep", PlaybackOutcome.Started, 1.0);
            Add("event:/SFX/Footstep", PlaybackOutcome.Started, 1.5);
            Add("event:/SFX/Gunshot", PlaybackOutcome.Started, 2.0);

            var timeline = Build(TraceFilter.Everything);

            Assert.That(timeline.Lanes.Count, Is.EqualTo(2));
            Assert.That(LabelsOf(timeline), Does.Contain("event:/SFX/Footstep"));
            Assert.That(timeline.VisibleRecordCount, Is.EqualTo(3));
        }

        [Test]
        public void GroupingByEmitterSplitsOneEventAcrossTheObjectsThatPlayedIt()
        {
            // The reading that matters when one sound is fine everywhere except on one
            // object — which a per-event lane would average away.
            Add("event:/SFX/Footstep", PlaybackOutcome.Started, 1.0, emitter: "/Level/Guard");
            Add("event:/SFX/Footstep", PlaybackOutcome.Started, 1.5, emitter: "/Level/Player");

            var timeline = Build(TraceFilter.Everything, TraceGrouping.Emitter);

            Assert.That(LabelsOf(timeline), Is.EquivalentTo(new[] { "/Level/Guard", "/Level/Player" }));
        }

        [Test]
        public void SoundsWithNoEmitterShareANamedLaneRatherThanABlankOne()
        {
            // Music and UI genuinely have no object. That is a fact about them, not a gap
            // in the data, and a blank row would read as the second thing.
            Add("event:/Music/Menu", PlaybackOutcome.Started, 1.0);

            var timeline = Build(TraceFilter.Everything, TraceGrouping.Emitter);

            Assert.That(LabelsOf(timeline), Is.EqualTo(new[] { "(no emitter)" }));
        }

        [Test]
        public void TheDefaultFilterShowsEverythingThatDidNotSimplyPlay()
        {
            // The module's stated exit condition: one setting, and what is left on screen
            // is every silent failure.
            Add("event:/SFX/Quiet", PlaybackOutcome.Started, 1.0);
            Add("event:/SFX/Quiet", PlaybackOutcome.Started, 1.1);
            Add("event:/SFX/Missing", PlaybackOutcome.HandleInvalid, 2.0);
            Add("event:/SFX/Busy", PlaybackOutcome.Rejected, 3.0);
            Add("event:/SFX/Far", PlaybackOutcome.Virtualized, 4.0);

            var timeline = Build(TraceFilter.Failures);

            Assert.That(timeline.VisibleRecordCount, Is.EqualTo(3));
            Assert.That(LabelsOf(timeline), Does.Not.Contain("event:/SFX/Quiet"));
        }

        [Test]
        public void TheOutcomeCountsKeepReportingWhatTheFilterIsHiding()
        {
            // A count that disappeared along with its records would be no use for deciding
            // whether to switch that outcome back on.
            Add("event:/SFX/Quiet", PlaybackOutcome.Started, 1.0);
            Add("event:/SFX/Quiet", PlaybackOutcome.Started, 1.1);
            Add("event:/SFX/Missing", PlaybackOutcome.HandleInvalid, 2.0);

            var timeline = Build(TraceFilter.Failures);

            Assert.That(timeline.OutcomeCounts[(int)PlaybackOutcome.Started], Is.EqualTo(2));
            Assert.That(timeline.OutcomeCounts[(int)PlaybackOutcome.HandleInvalid], Is.EqualTo(1));
            Assert.That(timeline.VisibleRecordCount, Is.EqualTo(1));
        }

        [Test]
        public void SearchMatchesTheEventTheEmitterAndTheCallSiteAlike()
        {
            Add("event:/SFX/Gunshot", PlaybackOutcome.Started, 1.0, emitter: "/Level/Guard");
            Add("event:/SFX/Footstep", PlaybackOutcome.Started, 2.0, emitter: "/Level/Rifleman");
            Add("event:/Music/Menu", PlaybackOutcome.Started, 3.0, callSite: "Assets/UI/Menu.cs:12");

            var byEvent = TraceFilter.Everything;
            byEvent.Search = "gunshot";

            var byEmitter = TraceFilter.Everything;
            byEmitter.Search = "Rifleman";

            var byCallSite = TraceFilter.Everything;
            byCallSite.Search = "Menu.cs";

            Assert.That(Build(byEvent).VisibleRecordCount, Is.EqualTo(1), "event key");
            Assert.That(Build(byEmitter).VisibleRecordCount, Is.EqualTo(1), "emitter path");
            Assert.That(Build(byCallSite).VisibleRecordCount, Is.EqualTo(1), "call site");
        }

        [Test]
        public void SearchIsCaseInsensitive()
        {
            Add("event:/SFX/Gunshot", PlaybackOutcome.Started, 1.0);

            var filter = TraceFilter.Everything;
            filter.Search = "GUNSHOT";

            Assert.That(Build(filter).VisibleRecordCount, Is.EqualTo(1));
        }

        [Test]
        public void TheTimeRangeExcludesRecordsOutsideIt()
        {
            Add("event:/SFX/Early", PlaybackOutcome.Started, 1.0);
            Add("event:/SFX/Middle", PlaybackOutcome.Started, 5.0);
            Add("event:/SFX/Late", PlaybackOutcome.Started, 9.0);

            var filter = TraceFilter.Everything;
            filter.StartSeconds = 4.0;
            filter.EndSeconds = 6.0;

            Assert.That(LabelsOf(Build(filter)), Is.EqualTo(new[] { "event:/SFX/Middle" }));
        }

        [Test]
        public void TheSessionExtentIgnoresTheTimeFilter()
        {
            // The extent is what the "Fit" button goes back to, so narrowing the range
            // must not narrow the thing you would use to widen it again.
            Add("event:/SFX/Early", PlaybackOutcome.Started, 1.0);
            Add("event:/SFX/Late", PlaybackOutcome.Started, 9.0);

            var filter = TraceFilter.Everything;
            filter.StartSeconds = 4.0;
            filter.EndSeconds = 6.0;

            var timeline = Build(filter);

            Assert.That(timeline.SessionStart, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(timeline.SessionEnd, Is.EqualTo(9.0).Within(1e-9));
        }

        [Test]
        public void TheWorstLaneComesFirstWhateverItIsCalled()
        {
            // Alphabetical order would be stable and useless. Someone opened this window
            // because something is wrong; the lane holding the answer goes on top.
            Add("event:/A/Fine", PlaybackOutcome.Started, 1.0);
            Add("event:/A/Fine", PlaybackOutcome.Started, 1.1);
            Add("event:/A/Fine", PlaybackOutcome.Started, 1.2);
            Add("event:/Z/Broken", PlaybackOutcome.HandleInvalid, 2.0);

            Assert.That(LabelsOf(Build(TraceFilter.Everything))[0], Is.EqualTo("event:/Z/Broken"));
        }

        [Test]
        public void AmongEquallyBadLanesTheBusiestComesFirst()
        {
            Add("event:/Rare", PlaybackOutcome.Stolen, 1.0);
            Add("event:/Common", PlaybackOutcome.Stolen, 2.0);
            Add("event:/Common", PlaybackOutcome.Stolen, 2.1);

            Assert.That(LabelsOf(Build(TraceFilter.Everything))[0], Is.EqualTo("event:/Common"));
        }

        [Test]
        public void ALaneIsLabelledByItsWorstMomentNotItsLast()
        {
            Add("event:/SFX/Gun", PlaybackOutcome.HandleInvalid, 1.0);
            Add("event:/SFX/Gun", PlaybackOutcome.Started, 2.0);
            Add("event:/SFX/Gun", PlaybackOutcome.Started, 3.0);

            Assert.That(Build(TraceFilter.Everything).Lanes[0].WorstOutcome,
                Is.EqualTo(PlaybackOutcome.HandleInvalid));
        }

        [Test]
        public void NothingCreatedOutranksSomethingThatPlayedInaudibly()
        {
            // The severity order decides which mark survives when several records land on
            // the same pixel. A HandleInvalid hidden behind a Started would hide the only
            // record that mattered.
            Assert.That(TraceTimeline.IsWorse(PlaybackOutcome.HandleInvalid, PlaybackOutcome.Rejected), Is.True);
            Assert.That(TraceTimeline.IsWorse(PlaybackOutcome.Rejected, PlaybackOutcome.Virtualized), Is.True);
            Assert.That(TraceTimeline.IsWorse(PlaybackOutcome.Virtualized, PlaybackOutcome.Stolen), Is.True);
            Assert.That(TraceTimeline.IsWorse(PlaybackOutcome.Stolen, PlaybackOutcome.StoppedEarly), Is.True);
            Assert.That(TraceTimeline.IsWorse(PlaybackOutcome.StoppedEarly, PlaybackOutcome.Started), Is.True);
            Assert.That(TraceTimeline.IsWorse(PlaybackOutcome.Started, PlaybackOutcome.HandleInvalid), Is.False);
        }

        [Test]
        public void LaneRecordsStayInTimeOrder()
        {
            // The lane element walks them assuming ascending time, and stops early on that
            // basis when picking a mark.
            Add("event:/SFX/Gun", PlaybackOutcome.Started, 1.0);
            Add("event:/SFX/Gun", PlaybackOutcome.Started, 2.0);
            Add("event:/SFX/Gun", PlaybackOutcome.Started, 3.0);

            var lane = Build(TraceFilter.Everything).Lanes[0];
            var previous = double.NegativeInfinity;

            foreach (var index in lane.Records)
            {
                var time = _session.Records[index].TimeSeconds;
                Assert.That(time, Is.GreaterThanOrEqualTo(previous));
                previous = time;
            }

            Assert.That(lane.FirstSeconds, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(lane.LastSeconds, Is.EqualTo(3.0).Within(1e-9));
        }

        [Test]
        public void SwitchingEveryOutcomeOffLeavesNothing()
        {
            Add("event:/SFX/Gun", PlaybackOutcome.Started, 1.0);

            var filter = TraceFilter.Everything;
            filter.OutcomeMask = 0;

            Assert.That(Build(filter).Lanes, Is.Empty);
        }

        [Test]
        public void TheRulerStepsOnRoundNumbers()
        {
            Assert.That(TraceTimeline.NiceTimeStep(0.07), Is.EqualTo(0.1).Within(1e-9));
            Assert.That(TraceTimeline.NiceTimeStep(0.15), Is.EqualTo(0.2).Within(1e-9));
            Assert.That(TraceTimeline.NiceTimeStep(3.0), Is.EqualTo(5.0).Within(1e-9));
            Assert.That(TraceTimeline.NiceTimeStep(7.0), Is.EqualTo(10.0).Within(1e-9));
        }

        [Test]
        public void AStepJustOverTwoDoesNotJumpAllTheWayToFive()
        {
            // The case that turned an eight-tick axis into a three-tick one: a two second
            // session wants a spacing a hair over 0.2, and without the 2.5 rung the ladder
            // hands back 0.5.
            Assert.That(TraceTimeline.NiceTimeStep(0.204), Is.EqualTo(0.25).Within(1e-9));
        }

        [Test]
        public void ATickLabelHasEnoughDecimalsToBeWhereItSays()
        {
            // 0.25 written to one decimal is "0.2", which is not where the tick is.
            Assert.That(TraceTimeline.DecimalsFor(1.0), Is.EqualTo(0));
            Assert.That(TraceTimeline.DecimalsFor(0.5), Is.EqualTo(1));
            Assert.That(TraceTimeline.DecimalsFor(0.2), Is.EqualTo(1));
            Assert.That(TraceTimeline.DecimalsFor(0.25), Is.EqualTo(2));
            Assert.That(TraceTimeline.DecimalsFor(0.025), Is.EqualTo(3));
        }

        [Test]
        public void TogglingOneOutcomeLeavesTheOthersAlone()
        {
            var filter = TraceFilter.Everything.With(PlaybackOutcome.Started, false);

            Assert.That(filter.Includes(PlaybackOutcome.Started), Is.False);
            Assert.That(filter.Includes(PlaybackOutcome.Stolen), Is.True);

            Assert.That(filter.With(PlaybackOutcome.Started, true).Includes(PlaybackOutcome.Started), Is.True);
        }
    }
}
