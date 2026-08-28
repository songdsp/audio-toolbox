using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioToolbox.EventTracer.Editor
{
    /// <summary>
    /// One lane's worth of time, painted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Custom-painted rather than built from child elements, and the reason is arithmetic:
    /// a busy session puts tens of thousands of records in a lane, and one
    /// <see cref="VisualElement"/> per record would build a layout tree larger than the
    /// scene being profiled. Here a lane is a single element that draws what fits.
    /// </para>
    /// <para>
    /// <b>Marks collapse per pixel, and the worst one wins.</b> At any useful zoom level
    /// several records land on the same column, and only one mark can be drawn there. The
    /// one kept is the most serious — a <c>HandleInvalid</c> is never hidden behind a
    /// <c>Started</c> that happened in the same millisecond. That is the difference
    /// between a summary and a lie.
    /// </para>
    /// <para>
    /// <b>Outcome is drawn twice: as colour and as height.</b> Failures reach full lane
    /// height, a plain <c>Started</c> is a short tick along the baseline. Colour alone
    /// would fail a colour-blind reader and would not survive a greyscale screenshot in a
    /// bug report, which is where these pictures usually end up.
    /// </para>
    /// </remarks>
    public sealed class TraceLaneElement : VisualElement
    {
        /// <summary>Half-width of a mark, in pixels. Wide enough to see, narrow enough to count.</summary>
        private const float MarkHalfWidth = 1.5f;

        /// <summary>How close a click has to land, in pixels, to select a mark.</summary>
        private const float PickTolerance = 5f;

        private TraceSession _session;
        private TraceLane _lane;
        private double _rangeStart;
        private double _rangeEnd;
        private int _selectedRecord = -1;

        // One entry per pixel column, reused across repaints so that scrolling a long
        // session does not allocate an array per lane per frame.
        private PlaybackOutcome[] _columns = Array.Empty<PlaybackOutcome>();
        private bool[] _columnUsed = Array.Empty<bool>();

        private readonly OutcomePalette _palette = new OutcomePalette();

        public TraceLaneElement()
        {
            generateVisualContent += Paint;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);

            // The lane handles its own input and reports what was chosen. The window then
            // only has to say what to do about it, which is also what makes the choosing
            // half reachable without a pointer.
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (PickAt(evt.localPosition.x))
                {
                    evt.StopPropagation();
                }
            });
        }

        /// <summary>Raised when a click selects a record. The argument is a session record index.</summary>
        public event Action<int> RecordPicked;

        /// <summary>
        /// Selects whatever sits at a horizontal position, as a click would. Returns false
        /// when nothing was close enough.
        /// </summary>
        public bool PickAt(float localX)
        {
            var picked = PickRecord(localX);

            if (picked < 0)
            {
                return false;
            }

            RecordPicked?.Invoke(picked);
            return true;
        }

        public void SetData(TraceSession session, TraceLane lane, double rangeStart, double rangeEnd, int selected)
        {
            _session = session;
            _lane = lane;
            _rangeStart = rangeStart;
            _rangeEnd = rangeEnd;
            _selectedRecord = selected;

            MarkDirtyRepaint();
        }

        public void SetSelection(int recordIndex)
        {
            if (_selectedRecord == recordIndex)
            {
                return;
            }

            _selectedRecord = recordIndex;
            MarkDirtyRepaint();
        }

        /// <summary>
        /// The record nearest a horizontal position, or -1 when the click landed on empty
        /// track rather than on a mark.
        /// </summary>
        public int PickRecord(float localX)
        {
            if (_lane == null || _session == null || _lane.Records.Count == 0)
            {
                return -1;
            }

            var width = contentRect.width;

            if (width <= 0f)
            {
                return -1;
            }

            var best = -1;
            var bestDistance = PickTolerance;

            for (var i = 0; i < _lane.Records.Count; i++)
            {
                var index = _lane.Records[i];
                var x = XFor(_session.Records[index].TimeSeconds, width);
                var distance = Mathf.Abs(x - localX);

                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
                else if (x > localX && best >= 0)
                {
                    // Marks are in ascending time, so once we are past the click and
                    // already have a candidate, nothing later can be closer.
                    break;
                }
            }

            return best;
        }

        private float XFor(double seconds, float width)
        {
            var span = _rangeEnd - _rangeStart;

            if (span <= 0d)
            {
                return width * 0.5f;
            }

            return (float)((seconds - _rangeStart) / span) * width;
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            _palette.Resolve(evt.customStyle);
            MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext context)
        {
            var rect = contentRect;

            if (_lane == null || _session == null || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            var painter = context.painter2D;

            PaintBaseline(painter, rect);

            var columns = Mathf.Max(1, Mathf.CeilToInt(rect.width));
            EnsureColumns(columns);

            CollapseIntoColumns(rect.width, columns);
            PaintColumns(painter, rect, columns);
            PaintSelection(painter, rect);
        }

        private void PaintBaseline(Painter2D painter, Rect rect)
        {
            // A hairline the marks sit on, so an empty stretch of a lane reads as "nothing
            // happened here" rather than as a rendering failure.
            var y = rect.height - 0.5f;

            painter.strokeColor = _palette.Baseline;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, y));
            painter.LineTo(new Vector2(rect.width, y));
            painter.Stroke();
        }

        private void EnsureColumns(int columns)
        {
            if (_columns.Length >= columns)
            {
                return;
            }

            _columns = new PlaybackOutcome[columns];
            _columnUsed = new bool[columns];
        }

        private void CollapseIntoColumns(float width, int columns)
        {
            Array.Clear(_columnUsed, 0, columns);

            for (var i = 0; i < _lane.Records.Count; i++)
            {
                var index = _lane.Records[i];
                var record = _session.Records[index];
                var x = XFor(record.TimeSeconds, width);

                if (x < 0f || x > width)
                {
                    continue;
                }

                var column = Mathf.Clamp(Mathf.RoundToInt(x), 0, columns - 1);

                if (!_columnUsed[column])
                {
                    _columnUsed[column] = true;
                    _columns[column] = record.Outcome;
                }
                else if (TraceTimeline.IsWorse(record.Outcome, _columns[column]))
                {
                    _columns[column] = record.Outcome;
                }
            }
        }

        private void PaintColumns(Painter2D painter, Rect rect, int columns)
        {
            for (var column = 0; column < columns; column++)
            {
                if (!_columnUsed[column])
                {
                    continue;
                }

                var outcome = _columns[column];
                var height = rect.height * HeightFraction(outcome);
                var top = rect.height - height;

                painter.fillColor = _palette.For(outcome);
                painter.BeginPath();
                painter.MoveTo(new Vector2(column - MarkHalfWidth, top));
                painter.LineTo(new Vector2(column + MarkHalfWidth, top));
                painter.LineTo(new Vector2(column + MarkHalfWidth, rect.height));
                painter.LineTo(new Vector2(column - MarkHalfWidth, rect.height));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private void PaintSelection(Painter2D painter, Rect rect)
        {
            if (_selectedRecord < 0 || _selectedRecord >= _session.Records.Count)
            {
                return;
            }

            if (!LaneHolds(_selectedRecord))
            {
                return;
            }

            var x = XFor(_session.Records[_selectedRecord].TimeSeconds, rect.width);

            if (x < 0f || x > rect.width)
            {
                return;
            }

            // A full-height rule rather than a highlighted mark: the selected record has
            // to be findable at a glance among a few hundred others, and it also lines the
            // reader up with what happened in the other lanes at the same instant.
            painter.strokeColor = _palette.Selection;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, 0f));
            painter.LineTo(new Vector2(x, rect.height));
            painter.Stroke();
        }

        private bool LaneHolds(int recordIndex)
        {
            for (var i = 0; i < _lane.Records.Count; i++)
            {
                if (_lane.Records[i] == recordIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// How tall a mark is drawn, as a fraction of the lane. The second channel that
        /// carries the outcome, so the picture survives being printed in grey.
        /// </summary>
        private static float HeightFraction(PlaybackOutcome outcome)
        {
            switch (outcome)
            {
                case PlaybackOutcome.HandleInvalid:
                case PlaybackOutcome.Rejected:
                    return 1f;

                case PlaybackOutcome.Virtualized:
                case PlaybackOutcome.Stolen:
                    return 0.75f;

                case PlaybackOutcome.StoppedEarly:
                    return 0.5f;

                default:
                    return 0.28f;
            }
        }

        /// <summary>
        /// The outcome colours, read out of the stylesheet rather than written here.
        /// </summary>
        /// <remarks>
        /// Custom-painted content cannot inherit a USS colour the way a styled element
        /// can, so without this the palette would exist twice — once in the stylesheet for
        /// the chips and once in C# for the marks — and the two would drift the first time
        /// anyone adjusted a shade. The fallbacks below are only for a stylesheet that
        /// failed to load.
        /// </remarks>
        private sealed class OutcomePalette
        {
            private static readonly CustomStyleProperty<Color> NotCalledProperty =
                new CustomStyleProperty<Color>("--et-outcome-notcalled");

            private static readonly CustomStyleProperty<Color> HandleInvalidProperty =
                new CustomStyleProperty<Color>("--et-outcome-handleinvalid");

            private static readonly CustomStyleProperty<Color> RejectedProperty =
                new CustomStyleProperty<Color>("--et-outcome-rejected");

            private static readonly CustomStyleProperty<Color> StartedProperty =
                new CustomStyleProperty<Color>("--et-outcome-started");

            private static readonly CustomStyleProperty<Color> VirtualizedProperty =
                new CustomStyleProperty<Color>("--et-outcome-virtualized");

            private static readonly CustomStyleProperty<Color> StolenProperty =
                new CustomStyleProperty<Color>("--et-outcome-stolen");

            private static readonly CustomStyleProperty<Color> StoppedEarlyProperty =
                new CustomStyleProperty<Color>("--et-outcome-stoppedearly");

            private static readonly CustomStyleProperty<Color> BaselineProperty =
                new CustomStyleProperty<Color>("--et-lane-baseline");

            private static readonly CustomStyleProperty<Color> SelectionProperty =
                new CustomStyleProperty<Color>("--et-selection");

            private readonly Color[] _colors =
            {
                new Color(0.42f, 0.45f, 0.48f), // NotCalled
                new Color(0.83f, 0.23f, 0.27f), // HandleInvalid
                new Color(0.91f, 0.42f, 0.24f), // Rejected
                new Color(0.36f, 0.58f, 0.44f), // Started
                new Color(0.90f, 0.72f, 0.24f), // Virtualized
                new Color(0.85f, 0.55f, 0.20f), // Stolen
                new Color(0.40f, 0.55f, 0.72f), // StoppedEarly
            };

            public Color Baseline { get; private set; } = new Color(0.5f, 0.5f, 0.5f, 0.35f);

            public Color Selection { get; private set; } = new Color(0.35f, 0.6f, 0.95f);

            public Color For(PlaybackOutcome outcome)
            {
                var index = (int)outcome;
                return index >= 0 && index < _colors.Length ? _colors[index] : _colors[0];
            }

            public void Resolve(ICustomStyle style)
            {
                Read(style, NotCalledProperty, PlaybackOutcome.NotCalled);
                Read(style, HandleInvalidProperty, PlaybackOutcome.HandleInvalid);
                Read(style, RejectedProperty, PlaybackOutcome.Rejected);
                Read(style, StartedProperty, PlaybackOutcome.Started);
                Read(style, VirtualizedProperty, PlaybackOutcome.Virtualized);
                Read(style, StolenProperty, PlaybackOutcome.Stolen);
                Read(style, StoppedEarlyProperty, PlaybackOutcome.StoppedEarly);

                if (style.TryGetValue(BaselineProperty, out var baseline))
                {
                    Baseline = baseline;
                }

                if (style.TryGetValue(SelectionProperty, out var selection))
                {
                    Selection = selection;
                }
            }

            private void Read(ICustomStyle style, CustomStyleProperty<Color> property, PlaybackOutcome outcome)
            {
                if (style.TryGetValue(property, out var color))
                {
                    _colors[(int)outcome] = color;
                }
            }
        }
    }
}
