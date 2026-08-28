using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AudioToolbox.EventTracer.Editor
{
    /// <summary>
    /// A session, on a time axis, filtered down to the sounds nobody heard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window opens showing everything that was <em>not</em> a plain
    /// <see cref="PlaybackOutcome.Started"/>, because that is the question people have. A
    /// session is mostly sounds that worked; a tool that made you filter those out
    /// yourself before it said anything useful would be a log viewer.
    /// </para>
    /// <para>
    /// A timeline rather than a list, because the interesting failures are relational. A
    /// gunshot that was stolen at 12.4s means one thing on its own and another when forty
    /// footsteps fired in the same tenth of a second, and only the horizontal axis shows
    /// you the second reading.
    /// </para>
    /// <para>
    /// It reads sessions from disk, and deliberately does not require this project to have
    /// <c>AUDIOTOOLBOX_TRACE</c> switched on — the logs worth opening come from a QA
    /// machine, a console or a player. Capturing from the running editor is an extra when
    /// the define happens to be on, not the way in.
    /// </para>
    /// </remarks>
    public sealed class EventTracerWindow : EditorWindow
    {
        private const string LayoutAssetName = "EventTracerWindow";

        /// <summary>Ticks the ruler aims for. Enough to read a span, few enough to not crowd it.</summary>
        private const int RulerTickTarget = 8;

        // Survives a domain reload so a recompile does not throw away what you were
        // looking at. The session itself cannot survive one, and is re-read from the path.
        [SerializeField] private string _sourcePath = string.Empty;
        [SerializeField] private TraceGrouping _grouping = TraceGrouping.Event;
        [SerializeField] private string _search = string.Empty;
        [SerializeField] private int _outcomeMask = -1;
        [SerializeField] private double _rangeStart;
        [SerializeField] private double _rangeEnd;
        [SerializeField] private bool _rangeCustom;
        [SerializeField] private int _selectedRecord = -1;

        private TraceSession _session;
        private TraceTimeline _timeline = TraceTimeline.Empty();

        private readonly List<TraceLane> _lanes = new List<TraceLane>();
        private readonly Dictionary<PlaybackOutcome, Button> _chips =
            new Dictionary<PlaybackOutcome, Button>();

        private VisualElement _root;
        private ToolbarButton _latestButton;
        private ToolbarButton _openButton;
        private ToolbarButton _reloadButton;
        private ToolbarButton _captureButton;
        private ToolbarMenu _groupMenu;
        private ToolbarSearchField _searchField;
        private VisualElement _chipRow;
        private Label _summarySource;
        private Label _summaryDetail;
        private VisualElement _warningStrip;
        private VisualElement _emptyState;
        private Label _emptyTitle;
        private Label _emptyBody;
        private VisualElement _timelineRoot;
        private DoubleField _rangeStartField;
        private DoubleField _rangeEndField;
        private Button _rangeFit;
        private VisualElement _ruler;
        private ListView _laneList;
        private Label _detailPlaceholder;
        private VisualElement _detailBody;
        private VisualElement _dropHint;

        [MenuItem("Window/Audio Toolbox/EventTracer/Timeline", priority = 118)]
        public static void Open()
        {
            var window = GetWindow<EventTracerWindow>();
            window.titleContent = new GUIContent("EventTracer");
            window.minSize = new Vector2(760, 340);
            window.Show();
        }

        /// <summary>Opens the window on one file. Used by the menu and by tests.</summary>
        public static void OpenSession(string path)
        {
            var window = GetWindow<EventTracerWindow>();
            window.titleContent = new GUIContent("EventTracer");
            window.Show();
            window.Load(path);
        }

        public void CreateGUI()
        {
            var tree = FindPackageAsset<VisualTreeAsset>();
            var style = FindPackageAsset<StyleSheet>();

            if (tree == null)
            {
                rootVisualElement.Add(new Label(
                    $"EventTracer's window layout ({LayoutAssetName}.uxml) could not be found. " +
                    "Reimport the package."));
                return;
            }

            tree.CloneTree(rootVisualElement);
            _root = rootVisualElement.Q<VisualElement>("root");

            if (style != null)
            {
                rootVisualElement.styleSheets.Add(style);
            }

            // USS cannot see the editor skin, so the skin becomes a class.
            _root.AddToClassList(EditorGUIUtility.isProSkin ? "dark" : "light");

            QueryElements();
            BuildChips();
            WireToolbar();
            WireRange();
            WireLaneList();
            WireDragAndDrop();

            _ruler.RegisterCallback<GeometryChangedEvent>(_ => RefreshRuler());

            if (!string.IsNullOrEmpty(_sourcePath) && File.Exists(_sourcePath))
            {
                Load(_sourcePath);
            }
            else
            {
                ShowNoSession(
                    "No session open",
                    "Open a .adtrace log, or drop one onto this window. Sessions a build writes " +
                    "are under Application.persistentDataPath/AudioToolboxTraces.");
            }
        }

        private static T FindPackageAsset<T>() where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets($"{LayoutAssetName} t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

                if (asset != null && asset.name == LayoutAssetName)
                {
                    return asset;
                }
            }

            return null;
        }

        private void QueryElements()
        {
            _latestButton = _root.Q<ToolbarButton>("latest-button");
            _openButton = _root.Q<ToolbarButton>("open-button");
            _reloadButton = _root.Q<ToolbarButton>("reload-button");
            _captureButton = _root.Q<ToolbarButton>("capture-button");
            _groupMenu = _root.Q<ToolbarMenu>("group-menu");
            _searchField = _root.Q<ToolbarSearchField>("search-field");
            _chipRow = _root.Q<VisualElement>("chips");
            _summarySource = _root.Q<Label>("summary-source");
            _summaryDetail = _root.Q<Label>("summary-detail");
            _warningStrip = _root.Q<VisualElement>("warning-strip");
            _emptyState = _root.Q<VisualElement>("empty-state");
            _emptyTitle = _root.Q<Label>("empty-title");
            _emptyBody = _root.Q<Label>("empty-body");
            _timelineRoot = _root.Q<VisualElement>("timeline");
            _rangeStartField = _root.Q<DoubleField>("range-start");
            _rangeEndField = _root.Q<DoubleField>("range-end");
            _rangeFit = _root.Q<Button>("range-fit");
            _ruler = _root.Q<VisualElement>("ruler");
            _laneList = _root.Q<ListView>("lane-list");
            _detailPlaceholder = _root.Q<Label>("detail-placeholder");
            _detailBody = _root.Q<VisualElement>("detail-body");
            _dropHint = _root.Q<VisualElement>("drop-hint");
        }

        // ------------------------------------------------------------------- toolbar

        private void WireToolbar()
        {
            _latestButton.clicked += LoadLatest;
            _openButton.clicked += OpenDialog;
            _reloadButton.clicked += () => Load(_sourcePath);
            _captureButton.clicked += CaptureLive;

            _searchField.value = _search;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                Rebuild();
            });

            RefreshGroupMenu();
            RefreshCaptureButton();
        }

        private void RefreshGroupMenu()
        {
            _groupMenu.menu.ClearItems();

            foreach (TraceGrouping grouping in Enum.GetValues(typeof(TraceGrouping)))
            {
                var captured = grouping;

                _groupMenu.menu.AppendAction(
                    grouping.ToString(),
                    _ =>
                    {
                        _grouping = captured;
                        RefreshGroupMenu();
                        Rebuild();
                    },
                    _ => _grouping == captured
                        ? DropdownMenuAction.Status.Checked
                        : DropdownMenuAction.Status.Normal);
            }

            _groupMenu.text = $"Rows: {_grouping}";
        }

        /// <summary>
        /// Enables capture only when there is something to capture.
        /// </summary>
        /// <remarks>
        /// Live capture works by flushing the running session and reading the file back,
        /// rather than by reaching into the recorder's buffers. That way the window has
        /// one way of reading a session instead of two, and what you see in the editor is
        /// exactly what a build would have written — including the fact that a sound still
        /// playing has not been written yet.
        /// </remarks>
        private void RefreshCaptureButton()
        {
            if (_captureButton == null)
            {
                return;
            }

#if AUDIOTOOLBOX_TRACE
            var recording = EditorApplication.isPlaying && AudioTrace.IsRecording;

            _captureButton.SetEnabled(recording);
            _captureButton.tooltip = recording
                ? "Flush the running session to disk and open it."
                : "Enter Play mode with a backend registered to capture a live session.";
#else
            _captureButton.SetEnabled(false);
            _captureButton.tooltip =
                "AUDIOTOOLBOX_TRACE is off in this project, so nothing is being recorded here. " +
                "Logs from other builds still open normally.";
#endif
        }

        private void CaptureLive()
        {
#if AUDIOTOOLBOX_TRACE
            AudioTraceRuntime.Flush();

            var path = AudioTrace.SessionPath;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ShowNoSession(
                    "Nothing has been written yet",
                    "The running session is memory-only, or no sound has finished. " +
                    "AudioTraceSettings.WriteToDisk has to be on for a session to have a file.");
                return;
            }

            Load(path);
#endif
        }

        private void OpenDialog()
        {
            var start = Directory.Exists(TraceLogReader.SessionFolder)
                ? TraceLogReader.SessionFolder
                : Application.persistentDataPath;

            var path = EditorUtility.OpenFilePanel(
                "Open trace session", start, TraceFormat.FileExtension.TrimStart('.'));

            if (!string.IsNullOrEmpty(path))
            {
                Load(path);
            }
        }

        private void LoadLatest()
        {
            var path = TraceLogReader.FindLatestSession();

            if (string.IsNullOrEmpty(path))
            {
                ShowNoSession(
                    "No sessions on this machine",
                    $"Nothing under {TraceLogReader.SessionFolder}. Enter Play mode with tracing on " +
                    "and post a sound, or open a log from another build.");
                return;
            }

            Load(path);
        }

        // ------------------------------------------------------------------- loading

        private void Load(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                _session = TraceLogReader.Read(path);
            }
            catch (Exception e)
            {
                _session = null;
                ShowNoSession($"Could not read {Path.GetFileName(path)}", e.Message);
                return;
            }

            _sourcePath = path;
            _selectedRecord = -1;
            _rangeCustom = false;

            // A first look at an unfamiliar session should already be the answer, so the
            // window starts on the failures. Every outcome is one click away.
            if (_outcomeMask == -1)
            {
                _outcomeMask = TraceFilter.Failures.OutcomeMask;
            }

            ClearDetail();
            Rebuild();
        }

        // -------------------------------------------------------------------- filter

        private TraceFilter CurrentFilter()
        {
            var filter = TraceFilter.Everything;

            filter.OutcomeMask = _outcomeMask;
            filter.Search = _search;

            if (_rangeCustom)
            {
                filter.StartSeconds = _rangeStart;
                filter.EndSeconds = _rangeEnd;
            }

            return filter;
        }

        private void Rebuild()
        {
            if (_session == null)
            {
                return;
            }

            _timeline = TraceTimeline.Build(_session, _grouping, CurrentFilter());

            if (!_rangeCustom)
            {
                _rangeStart = _timeline.SessionStart;
                _rangeEnd = _timeline.SessionEnd;
            }

            _lanes.Clear();
            _lanes.AddRange(_timeline.Lanes);

            RefreshChips();
            RefreshSummary();
            RefreshWarnings();
            RefreshRangeFields();
            RefreshRuler();

            _laneList.itemsSource = _lanes;
            _laneList.Rebuild();

            if (_session.Records.Count == 0)
            {
                // A file with a header and no records is a real and confusing thing: a
                // session that was flushed before any sound finished, or one where nothing
                // was posted at all. An empty grid would read as a broken window.
                ShowEmptyState(
                    "This session has no records",
                    "The log opened correctly — it just has nothing in it. A record is only written " +
                    "once its sound can no longer change, so a session flushed while everything was " +
                    "still playing looks like this, as does one where nothing was posted.");
                RefreshSummary();
                return;
            }

            if (_lanes.Count == 0)
            {
                ShowEmptyState(
                    "Nothing matches the filter",
                    $"{_session.Records.Count} record(s) in this session, all filtered out. " +
                    "The outcome chips above keep their counts while they are off — switch one back " +
                    "on, or clear the search and the time range.");
                RefreshSummary();
                return;
            }

            ShowTimeline();
        }

        // --------------------------------------------------------------------- chips

        private void BuildChips()
        {
            _chipRow.Clear();
            _chips.Clear();

            foreach (var outcome in TraceTimeline.OutcomesInSeverityOrder)
            {
                var chip = new Button { name = $"chip-{outcome}" };
                chip.AddToClassList("chip");

                var swatch = new VisualElement();
                swatch.AddToClassList("chip-swatch");
                swatch.AddToClassList($"chip-swatch--{outcome.ToString().ToLowerInvariant()}");
                chip.Add(swatch);

                var label = new Label(outcome.ToString());
                label.AddToClassList("chip-label");
                chip.Add(label);

                var count = new Label("0") { name = "count" };
                count.AddToClassList("chip-count");
                chip.Add(count);

                var captured = outcome;

                chip.clicked += () =>
                {
                    _outcomeMask ^= 1 << (int)captured;
                    Rebuild();
                };

                _chipRow.Add(chip);
                _chips.Add(outcome, chip);
            }
        }

        private void RefreshChips()
        {
            foreach (var pair in _chips)
            {
                var outcome = pair.Key;
                var chip = pair.Value;
                var count = _timeline.OutcomeCounts[(int)outcome];
                var shown = (_outcomeMask & (1 << (int)outcome)) != 0;

                chip.Q<Label>("count").text = count.ToString();
                chip.EnableInClassList("chip--off", !shown);
                chip.EnableInClassList("chip--empty", count == 0);

                chip.tooltip = shown
                    ? $"{count} record(s). Click to hide {outcome}."
                    : $"{count} record(s), hidden. Click to show {outcome}.";
            }
        }

        // ------------------------------------------------------------------- summary

        private void RefreshSummary()
        {
            _summarySource.text = Path.GetFileName(_sourcePath);

            var header = _session.Header;
            var lanes = _lanes.Count;

            _summaryDetail.text =
                $"{_timeline.VisibleRecordCount} of {_session.Records.Count} record(s) · " +
                $"{lanes} {(_grouping == TraceGrouping.Event ? "event" : "emitter")}{(lanes == 1 ? string.Empty : "s")} · " +
                $"{header.BackendId} {header.BackendVersion} · {header.Platform}";
        }

        private void RefreshWarnings()
        {
            _warningStrip.Clear();

            var header = _session.Header;

            if (header.DroppedRecordCount > 0)
            {
                // First and loudest. Every count on screen is a lower bound while this is
                // non-zero, and someone reading a filtered timeline as "these are all the
                // failures" would be wrong in the one direction that matters.
                AddWarning(
                    $"{header.DroppedRecordCount} record(s) were overwritten before they could be written. " +
                    "This session is incomplete — raise AudioTraceSettings.RecordCapacity.");
            }

            if (_session.EndedAbruptly)
            {
                AddWarning("The log ends mid-chunk, so the process did not exit cleanly. Everything before that point is sound.");
            }

            if (header.DroppedStringCount > 0)
            {
                AddWarning($"{header.DroppedStringCount} name(s) did not fit the intern table and read as \"{TraceFormat.OverflowStringText}\".");
            }

            if (header.DroppedEmitterPathCount > 0)
            {
                AddWarning($"{header.DroppedEmitterPathCount} emitter path(s) were lost to a full cache. Raise AudioTraceSettings.EmitterPathCapacity.");
            }

            if (header.DroppedSnapshotCount > 0)
            {
                AddWarning($"{header.DroppedSnapshotCount} parameter snapshot(s) were dropped, so some records carry no state.");
            }

            if (header.DroppedSignalCount > 0)
            {
                AddWarning($"{header.DroppedSignalCount} backend signal(s) were dropped, so some outcomes may be stuck at an earlier stage.");
            }

            _warningStrip.EnableInClassList("visible", _warningStrip.childCount > 0);
        }

        private void AddWarning(string text)
        {
            var label = new Label(text);
            label.AddToClassList("warning-line");
            _warningStrip.Add(label);
        }

        // --------------------------------------------------------------------- range

        private void WireRange()
        {
            _rangeStartField.RegisterValueChangedCallback(evt =>
            {
                _rangeStart = evt.newValue;
                _rangeCustom = true;
                Rebuild();
            });

            _rangeEndField.RegisterValueChangedCallback(evt =>
            {
                _rangeEnd = evt.newValue;
                _rangeCustom = true;
                Rebuild();
            });

            _rangeFit.clicked += () =>
            {
                _rangeCustom = false;
                Rebuild();
            };

            _rangeFit.tooltip = "Back to the whole session.";
        }

        private void RefreshRangeFields()
        {
            _rangeStartField.SetValueWithoutNotify(Math.Round(_rangeStart, 3));
            _rangeEndField.SetValueWithoutNotify(Math.Round(_rangeEnd, 3));
            _rangeFit.SetEnabled(_rangeCustom);
        }

        private void RefreshRuler()
        {
            if (_ruler == null)
            {
                return;
            }

            _ruler.Clear();

            var width = _ruler.contentRect.width;
            var span = _rangeEnd - _rangeStart;

            if (width <= 1f || span <= 0d || double.IsNaN(span))
            {
                return;
            }

            var step = TraceTimeline.NiceTimeStep(span / RulerTickTarget);
            var first = Math.Ceiling(_rangeStart / step) * step;
            var decimals = TraceTimeline.DecimalsFor(step);

            for (var t = first; t <= _rangeEnd + step * 0.001d; t += step)
            {
                var x = (float)((t - _rangeStart) / span) * width;

                if (x < 0f || x > width)
                {
                    continue;
                }

                var tick = new Label(t.ToString("F" + decimals) + "s");
                tick.AddToClassList("ruler-tick");
                tick.style.left = x;
                _ruler.Add(tick);
            }
        }

        // --------------------------------------------------------------------- lanes

        private void WireLaneList()
        {
            _laneList.fixedItemHeight = 34;
            _laneList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            _laneList.selectionType = SelectionType.Single;
            _laneList.itemsSource = _lanes;
            _laneList.makeItem = MakeLaneRow;
            _laneList.bindItem = BindLaneRow;
        }

        private VisualElement MakeLaneRow()
        {
            var row = new VisualElement();
            row.AddToClassList("lane-row");

            var gutter = new VisualElement();
            gutter.AddToClassList("lane-gutter");

            var label = new Label { name = "lane-label" };
            label.AddToClassList("lane-label");
            gutter.Add(label);

            var summary = new Label { name = "lane-summary" };
            summary.AddToClassList("lane-summary");
            gutter.Add(summary);

            row.Add(gutter);

            var track = new TraceLaneElement { name = "lane-track" };
            track.AddToClassList("lane-track");

            // Subscribed once per recycled row rather than per lane: the element knows
            // which records it is currently showing, so this stays correct as the ListView
            // reuses it across lanes.
            track.RecordPicked += SelectRecord;

            row.Add(track);
            return row;
        }

        private void BindLaneRow(VisualElement element, int index)
        {
            var lane = _lanes[index];

            element.Q<Label>("lane-label").text = lane.Label;
            element.Q<Label>("lane-label").tooltip = lane.Label;
            element.Q<Label>("lane-summary").text = SummaryOf(lane);

            var track = element.Q<TraceLaneElement>("lane-track");
            track.SetData(_session, lane, _rangeStart, _rangeEnd, _selectedRecord);
        }

        private static string SummaryOf(TraceLane lane)
        {
            var text = string.Empty;

            // Named in severity order and capped, so the worst thing about a lane is the
            // first thing on the row rather than whichever outcome sorts first.
            foreach (var outcome in TraceTimeline.OutcomesInSeverityOrder)
            {
                var count = lane.OutcomeCounts[(int)outcome];

                if (count == 0)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text += "  ";
                }

                text += $"{outcome} {count}";
            }

            return text;
        }

        private void SelectRecord(int recordIndex)
        {
            _selectedRecord = recordIndex;

            foreach (var track in _root.Query<TraceLaneElement>().ToList())
            {
                track.SetSelection(recordIndex);
            }

            ShowDetail(recordIndex);
        }

        // -------------------------------------------------------------------- detail

        private void ClearDetail()
        {
            _detailBody?.Clear();

            if (_detailPlaceholder != null)
            {
                _detailPlaceholder.style.display = DisplayStyle.Flex;
            }
        }

        private void ShowDetail(int recordIndex)
        {
            if (_session == null || recordIndex < 0 || recordIndex >= _session.Records.Count)
            {
                ClearDetail();
                return;
            }

            var record = _session.Records[recordIndex];

            _detailPlaceholder.style.display = DisplayStyle.None;
            _detailBody.Clear();

            var heading = new VisualElement();
            heading.AddToClassList("detail-outcome");

            var swatch = new VisualElement();
            swatch.AddToClassList("detail-outcome-swatch");
            swatch.AddToClassList($"chip-swatch--{record.Outcome.ToString().ToLowerInvariant()}");
            heading.Add(swatch);

            var outcomeLabel = new Label(record.Outcome.ToString());
            outcomeLabel.AddToClassList("detail-outcome-label");
            heading.Add(outcomeLabel);

            _detailBody.Add(heading);

            var eventLabel = new Label(_session.Resolve(record.EventKeyId));
            eventLabel.AddToClassList("detail-event");
            _detailBody.Add(eventLabel);

            var meaning = new Label(MeaningOf(record.Outcome));
            meaning.AddToClassList("detail-meaning");
            _detailBody.Add(meaning);

            AddSection("When");
            AddField("Time", $"{record.TimeSeconds:0.000}s");
            AddField("Frame", record.Frame.ToString());

            AddSection("Where");
            AddField("Emitter", DescribeEmitter(record));
            AddField("Distance", DescribeDistance(record));
            AddField("Emitter pos", Format(record.EmitterPos));
            AddField("Listener pos", Format(record.ListenerPos));

            AddSection("Who asked");
            AddCallSite(record);

            AddSection("Game state");
            AddParameters(record);

            if (record.BackendResultCode != 0)
            {
                AddSection("Backend");
                AddField("Result code", record.BackendResultCode.ToString());
                AddNote(
                    "The middleware's own code, kept unmapped. The normalised outcome above is " +
                    "deliberately lossy, and this is the number a support thread will ask for.");
            }
        }

        private void AddSection(string title)
        {
            var label = new Label(title);
            label.AddToClassList("detail-section");
            _detailBody.Add(label);
        }

        private void AddField(string name, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("detail-field");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("detail-field-name");
            row.Add(nameLabel);

            var valueLabel = new Label(value);
            valueLabel.AddToClassList("detail-field-value");
            row.Add(valueLabel);

            _detailBody.Add(row);
        }

        private void AddNote(string text)
        {
            var label = new Label(text);
            label.AddToClassList("detail-note");
            _detailBody.Add(label);
        }

        private string DescribeEmitter(in AudioTraceRecord record)
        {
            if (record.EmitterPathId == TraceFormat.NoStringId)
            {
                return "none — posted without an object";
            }

            var path = _session.Resolve(record.EmitterPathId);
            return string.IsNullOrEmpty(path) ? "none" : path;
        }

        private static string DescribeDistance(in AudioTraceRecord record) =>
            record.DistanceToListener < 0f
                ? "not measurable — no position, or no listener"
                : $"{record.DistanceToListener:0.00} m";

        private static string Format(Vector3 value) => $"({value.x:0.0}, {value.y:0.0}, {value.z:0.0})";

        private void AddCallSite(in AudioTraceRecord record)
        {
            var callSite = _session.Resolve(record.CallSiteId);

            if (string.IsNullOrEmpty(callSite))
            {
                AddField("Call site", "not recorded");
                return;
            }

            var button = new Button(() => OpenCallSite(callSite)) { text = callSite };
            button.AddToClassList("detail-link");
            button.tooltip = "Open this line in your editor.";
            _detailBody.Add(button);
        }

        private void AddParameters(in AudioTraceRecord record)
        {
            var values = new Dictionary<string, float>();

            if (!_session.TryResolveParameters(record.ParamSnapshotId, values))
            {
                AddField(
                    "Parameters",
                    record.ParamSnapshotId == TraceFormat.NoSnapshotId
                        ? "none were known when this was posted"
                        : "the snapshot chain is not complete in this log");
                return;
            }

            if (values.Count == 0)
            {
                AddField("Parameters", "none");
                return;
            }

            var names = new List<string>(values.Keys);
            names.Sort(StringComparer.Ordinal);

            foreach (var name in names)
            {
                AddField(name, values[name].ToString("0.###"));
            }

            AddNote("The global parameters in force at the moment this sound was posted.");
        }

        /// <summary>
        /// Takes the reader to the line that posted the sound.
        /// </summary>
        /// <remarks>
        /// Two routes, because a call site can come from anywhere. A path inside this
        /// project opens through the asset database, which respects the external script
        /// editor and works for a package the project has embedded. Anything else — a log
        /// from a machine whose sources live elsewhere — goes straight to the external
        /// editor, which at worst fails to find the file rather than silently doing
        /// nothing.
        /// </remarks>
        private static void OpenCallSite(string callSite)
        {
            var separator = callSite.LastIndexOf(':');
            var path = separator > 0 ? callSite.Substring(0, separator) : callSite;
            var line = 0;

            if (separator > 0)
            {
                int.TryParse(callSite.Substring(separator + 1), out line);
            }

            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);

            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset, Math.Max(line, 1));
                return;
            }

            if (!UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, Math.Max(line, 1)))
            {
                Debug.LogWarning(
                    $"[EventTracer] Could not open {callSite}. The log came from a machine whose " +
                    "sources are not at that path here.");
            }
        }

        private static string MeaningOf(PlaybackOutcome outcome)
        {
            switch (outcome)
            {
                case PlaybackOutcome.HandleInvalid:
                    return "No instance was created at all. The event does not exist, or its bank was not loaded.";

                case PlaybackOutcome.Rejected:
                    return "An instance was created and refused a voice — an instance limit with stealing set to None.";

                case PlaybackOutcome.Started:
                    return "It played. Nothing to explain.";

                case PlaybackOutcome.Virtualized:
                    return "It started and then went virtual: still playing, producing no output. Out of range, or beaten by louder sounds.";

                case PlaybackOutcome.Stolen:
                    return "It started and something else stopped it early. Nobody in the game asked for that.";

                case PlaybackOutcome.StoppedEarly:
                    return "It started and the game stopped it before it finished. Usually deliberate.";

                default:
                    return "The tracer never saw this call. Only static analysis can produce this.";
            }
        }

        // -------------------------------------------------------------- drag and drop

        private void WireDragAndDrop()
        {
            _root.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!HasTraceFile(DragAndDrop.paths))
                {
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                _dropHint.AddToClassList("visible");
                evt.StopPropagation();
            });

            _root.RegisterCallback<DragLeaveEvent>(_ => _dropHint.RemoveFromClassList("visible"));
            _root.RegisterCallback<DragExitedEvent>(_ => _dropHint.RemoveFromClassList("visible"));

            _root.RegisterCallback<DragPerformEvent>(evt =>
            {
                _dropHint.RemoveFromClassList("visible");

                var path = FirstTraceFile(DragAndDrop.paths);

                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                Load(path);
                evt.StopPropagation();
            });
        }

        private static bool HasTraceFile(string[] paths) => !string.IsNullOrEmpty(FirstTraceFile(paths));

        private static string FirstTraceFile(string[] paths)
        {
            if (paths == null)
            {
                return null;
            }

            foreach (var path in paths)
            {
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(TraceFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        // --------------------------------------------------------------------- state

        private void ShowEmptyState(string title, string body)
        {
            if (_emptyState == null)
            {
                return;
            }

            _emptyTitle.text = title;
            _emptyBody.text = body;
            _emptyState.style.display = DisplayStyle.Flex;
            _timelineRoot.style.display = DisplayStyle.None;

            // The summary and the warnings above are left alone. An empty timeline is
            // often the interesting case — a session that dropped every record still has
            // to say so, and clearing the strip would take that away exactly when it is
            // the explanation.
        }

        /// <summary>The empty state for when there is no session at all, rather than an empty one.</summary>
        private void ShowNoSession(string title, string body)
        {
            ShowEmptyState(title, body);

            if (_warningStrip == null)
            {
                return;
            }

            _warningStrip.Clear();
            _warningStrip.EnableInClassList("visible", false);
            _summarySource.text = string.Empty;
            _summaryDetail.text = string.Empty;

            foreach (var chip in _chips.Values)
            {
                chip.Q<Label>("count").text = "0";
            }
        }

        private void ShowTimeline()
        {
            _emptyState.style.display = DisplayStyle.None;
            _timelineRoot.style.display = DisplayStyle.Flex;
        }

        private void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        private void OnPlayModeChanged(PlayModeStateChange change) => RefreshCaptureButton();
    }
}
