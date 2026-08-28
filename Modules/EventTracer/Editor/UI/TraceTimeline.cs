using System;
using System.Collections.Generic;

namespace AudioToolbox.EventTracer.Editor
{
    /// <summary>What a lane stands for.</summary>
    public enum TraceGrouping
    {
        /// <summary>One lane per event key. Answers "which sound is going wrong".</summary>
        Event,

        /// <summary>One lane per emitter path. Answers "which object is going wrong".</summary>
        Emitter,
    }

    /// <summary>
    /// What the window is currently showing, as a value.
    /// </summary>
    /// <remarks>
    /// A struct passed into <see cref="TraceTimeline.Build"/> rather than state the model
    /// carries, so that "what does this filter produce" is a question with one answer and
    /// no setup. The filtering is where the module's stated goal lives — a session is
    /// tens of thousands of records and exactly one filter setting turns it into the
    /// short list of sounds nobody heard — so it is worth being able to test on its own.
    /// </remarks>
    public struct TraceFilter
    {
        /// <summary>One bit per <see cref="PlaybackOutcome"/>. Zero shows nothing.</summary>
        public int OutcomeMask;

        /// <summary>
        /// Matched against the event key, the emitter path and the call site at once.
        /// </summary>
        /// <remarks>
        /// One box rather than three. The grouping already separates events from emitters,
        /// and someone typing "Rifleman" wants the sounds that object made whichever of
        /// the two fields the name happens to live in.
        /// </remarks>
        public string Search;

        public double StartSeconds;
        public double EndSeconds;

        public static TraceFilter Everything => new TraceFilter
        {
            OutcomeMask = ~0,
            Search = string.Empty,
            StartSeconds = double.NegativeInfinity,
            EndSeconds = double.PositiveInfinity,
        };

        /// <summary>The filter the window opens on: everything that did not simply play.</summary>
        /// <remarks>
        /// The default is opinionated on purpose. A session is mostly sounds that worked,
        /// and nobody opens a tracer to look at those. Starting on the failures is the
        /// difference between the window answering the question and the window being a
        /// place to start looking for it.
        /// </remarks>
        public static TraceFilter Failures
        {
            get
            {
                var filter = Everything;
                filter.OutcomeMask = ~0 & ~(1 << (int)PlaybackOutcome.Started);
                return filter;
            }
        }

        public bool Includes(PlaybackOutcome outcome) => (OutcomeMask & (1 << (int)outcome)) != 0;

        public TraceFilter With(PlaybackOutcome outcome, bool shown)
        {
            var result = this;
            var bit = 1 << (int)outcome;

            result.OutcomeMask = shown ? result.OutcomeMask | bit : result.OutcomeMask & ~bit;
            return result;
        }
    }

    /// <summary>One row of the timeline: a name, and every record that belongs under it.</summary>
    public sealed class TraceLane
    {
        public string Label = string.Empty;

        /// <summary>Indices into the session's record list, ascending in time.</summary>
        public readonly List<int> Records = new List<int>();

        /// <summary>How many of this lane's visible records ended in each outcome.</summary>
        public readonly int[] OutcomeCounts = new int[TraceTimeline.OutcomeCount];

        public double FirstSeconds;
        public double LastSeconds;

        /// <summary>The outcome worth naming on the row: the worst one present.</summary>
        public PlaybackOutcome WorstOutcome = PlaybackOutcome.Started;
    }

    /// <summary>
    /// A session arranged into lanes, filtered — everything the timeline window draws,
    /// worked out without touching a single visual element.
    /// </summary>
    /// <remarks>
    /// Separated from the window for the same reason
    /// <see cref="Recording.OutcomeStateMachine"/> is separated from the FMOD backend: the
    /// part that decides what you are looking at should be answerable in a test, and a
    /// question about grouping and filtering should not need an editor window open to
    /// ask. What is left in the window is layout, painting and input.
    /// </remarks>
    public sealed class TraceTimeline
    {
        internal const int OutcomeCount = 7;

        private static readonly PlaybackOutcome[] AllOutcomes =
            (PlaybackOutcome[])Enum.GetValues(typeof(PlaybackOutcome));

        /// <summary>
        /// Outcomes ordered by how badly they answer "why did I not hear that", worst
        /// first.
        /// </summary>
        /// <remarks>
        /// Used where several records fall on the same pixel and only one mark can be
        /// drawn, and to label a lane by its worst moment. The order is a judgement and
        /// worth stating: nothing was created at all beats a voice that was refused,
        /// which beats a sound that played inaudibly, which beats one something else cut
        /// short, which beats one the game itself stopped — that last is usually not a
        /// fault at all. A summary that hid a <c>HandleInvalid</c> behind a
        /// <c>Started</c> in the same millisecond would hide the only record that
        /// mattered.
        /// </remarks>
        private static readonly PlaybackOutcome[] BySeverity =
        {
            PlaybackOutcome.HandleInvalid,
            PlaybackOutcome.Rejected,
            PlaybackOutcome.Virtualized,
            PlaybackOutcome.Stolen,
            PlaybackOutcome.StoppedEarly,
            PlaybackOutcome.NotCalled,
            PlaybackOutcome.Started,
        };

        private static readonly int[] SeverityRank = BuildSeverityRanks();

        private readonly List<TraceLane> _lanes = new List<TraceLane>();

        public TraceSession Session { get; private set; }

        public IReadOnlyList<TraceLane> Lanes => _lanes;

        /// <summary>Records that passed every filter.</summary>
        public int VisibleRecordCount { get; private set; }

        /// <summary>
        /// Records per outcome after the search and time filters but <em>before</em> the
        /// outcome filter, which is what the outcome chips have to show — a count that
        /// vanished when you switched its own chip off would be no use for deciding
        /// whether to switch it back on.
        /// </summary>
        public int[] OutcomeCounts { get; } = new int[OutcomeCount];

        /// <summary>The full extent of the session, ignoring the time filter.</summary>
        public double SessionStart { get; private set; }

        public double SessionEnd { get; private set; }

        public static int Severity(PlaybackOutcome outcome)
        {
            var index = (int)outcome;
            return index >= 0 && index < SeverityRank.Length ? SeverityRank[index] : int.MaxValue;
        }

        /// <summary>True when <paramref name="candidate"/> should win a shared pixel.</summary>
        public static bool IsWorse(PlaybackOutcome candidate, PlaybackOutcome incumbent) =>
            Severity(candidate) < Severity(incumbent);

        public static TraceTimeline Empty() => new TraceTimeline { Session = null };

        public static TraceTimeline Build(TraceSession session, TraceGrouping grouping, in TraceFilter filter)
        {
            var timeline = new TraceTimeline { Session = session };

            if (session == null)
            {
                return timeline;
            }

            timeline.MeasureSession(session);
            timeline.Fill(session, grouping, filter);
            return timeline;
        }

        private void MeasureSession(TraceSession session)
        {
            if (session.Records.Count == 0)
            {
                return;
            }

            // Records are written in the order they were appended, and the ring buffer
            // drains contiguously, so the file is in ascending time order end to end.
            SessionStart = session.Records[0].TimeSeconds;
            SessionEnd = session.Records[session.Records.Count - 1].TimeSeconds;
        }

        private void Fill(TraceSession session, TraceGrouping grouping, in TraceFilter filter)
        {
            var laneByLabel = new Dictionary<string, TraceLane>(StringComparer.Ordinal);

            for (var i = 0; i < session.Records.Count; i++)
            {
                var record = session.Records[i];

                if (record.TimeSeconds < filter.StartSeconds || record.TimeSeconds > filter.EndSeconds)
                {
                    continue;
                }

                if (!MatchesSearch(session, in record, filter.Search))
                {
                    continue;
                }

                // Counted before the outcome filter, so the chips keep saying what is
                // being hidden.
                var outcomeIndex = (int)record.Outcome;

                if (outcomeIndex >= 0 && outcomeIndex < OutcomeCounts.Length)
                {
                    OutcomeCounts[outcomeIndex]++;
                }

                if (!filter.Includes(record.Outcome))
                {
                    continue;
                }

                var label = LabelFor(session, in record, grouping);

                if (!laneByLabel.TryGetValue(label, out var lane))
                {
                    lane = new TraceLane { Label = label, FirstSeconds = record.TimeSeconds };
                    laneByLabel.Add(label, lane);
                    _lanes.Add(lane);
                }

                lane.Records.Add(i);
                lane.LastSeconds = record.TimeSeconds;

                if (outcomeIndex >= 0 && outcomeIndex < lane.OutcomeCounts.Length)
                {
                    lane.OutcomeCounts[outcomeIndex]++;
                }

                if (lane.Records.Count == 1 || IsWorse(record.Outcome, lane.WorstOutcome))
                {
                    lane.WorstOutcome = record.Outcome;
                }

                VisibleRecordCount++;
            }

            SortLanes();
        }

        /// <summary>
        /// Worst lane first, then busiest, then by name.
        /// </summary>
        /// <remarks>
        /// Not alphabetical, and not chronological. Someone opens this window because
        /// something is wrong, and the lane with a <c>HandleInvalid</c> in it is the
        /// answer whether it happens to start with an A or a Z. Alphabetical order would
        /// be stable and useless; this is stable within a severity and puts the answer on
        /// the first screen, which is what the module promised.
        /// </remarks>
        private void SortLanes()
        {
            _lanes.Sort((left, right) =>
            {
                var bySeverity = Severity(left.WorstOutcome).CompareTo(Severity(right.WorstOutcome));

                if (bySeverity != 0)
                {
                    return bySeverity;
                }

                var byCount = right.Records.Count.CompareTo(left.Records.Count);

                return byCount != 0
                    ? byCount
                    : string.Compare(left.Label, right.Label, StringComparison.Ordinal);
            });
        }

        private static string LabelFor(TraceSession session, in AudioTraceRecord record, TraceGrouping grouping)
        {
            if (grouping == TraceGrouping.Event)
            {
                var key = session.Resolve(record.EventKeyId);
                return string.IsNullOrEmpty(key) ? "(no event key)" : key;
            }

            if (record.EmitterPathId == TraceFormat.NoStringId)
            {
                // Not a gap in the data: music, UI and narration genuinely have no
                // emitter, and they belong together rather than under a blank name.
                return "(no emitter)";
            }

            var path = session.Resolve(record.EmitterPathId);
            return string.IsNullOrEmpty(path) ? "(no emitter)" : path;
        }

        private static bool MatchesSearch(TraceSession session, in AudioTraceRecord record, string search)
        {
            if (string.IsNullOrEmpty(search))
            {
                return true;
            }

            return Contains(session.Resolve(record.EventKeyId), search)
                   || Contains(session.Resolve(record.EmitterPathId), search)
                   || Contains(session.Resolve(record.CallSiteId), search);
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static int[] BuildSeverityRanks()
        {
            var ranks = new int[OutcomeCount];

            for (var i = 0; i < ranks.Length; i++)
            {
                ranks[i] = int.MaxValue;
            }

            for (var rank = 0; rank < BySeverity.Length; rank++)
            {
                ranks[(int)BySeverity[rank]] = rank;
            }

            return ranks;
        }

        /// <summary>
        /// Rounds a tick spacing up to the next round number: 1, 2, 2.5 or 5 times a power
        /// of ten.
        /// </summary>
        /// <remarks>
        /// So a time axis reads 0.5s, 1s, 2s rather than 0.4713s. A ruler whose labels are
        /// exact and unreadable is worse than one whose labels are round.
        /// <para>
        /// The 2.5 rung earns its place: without it the ladder jumps straight from 2 to 5,
        /// and a spacing that needed to be a hair over 2 becomes 5 — which on a two second
        /// session is the difference between eight ticks and three.
        /// </para>
        /// </remarks>
        public static double NiceTimeStep(double raw)
        {
            if (raw <= 0d || double.IsNaN(raw) || double.IsInfinity(raw))
            {
                return 1d;
            }

            var magnitude = Math.Pow(10d, Math.Floor(Math.Log10(raw)));
            var normalized = raw / magnitude;

            if (normalized <= 1d)
            {
                return magnitude;
            }

            if (normalized <= 2d)
            {
                return 2d * magnitude;
            }

            if (normalized <= 2.5d)
            {
                return 2.5d * magnitude;
            }

            return normalized <= 5d ? 5d * magnitude : 10d * magnitude;
        }

        /// <summary>
        /// Decimal places enough to write <paramref name="step"/> and its multiples
        /// exactly.
        /// </summary>
        /// <remarks>
        /// Derived from the step rather than from its order of magnitude, because a step
        /// of 0.25 needs two places while a step of 0.2 needs one, and they sit in the
        /// same decade. Rounding 0.25 to "0.2" would put a label on the axis that is not
        /// where the tick is.
        /// </remarks>
        public static int DecimalsFor(double step)
        {
            var decimals = 0;
            var probe = Math.Abs(step);

            while (decimals < 4 && Math.Abs(probe - Math.Round(probe)) > 1e-9d)
            {
                probe *= 10d;
                decimals++;
            }

            return decimals;
        }

        /// <summary>Every outcome, in the order the filter chips show them.</summary>
        public static IReadOnlyList<PlaybackOutcome> OutcomesInSeverityOrder => BySeverity;

        /// <summary>Every outcome, in declaration order.</summary>
        public static IReadOnlyList<PlaybackOutcome> AllOutcomeValues => AllOutcomes;
    }
}
