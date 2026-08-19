using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using AudioToolbox.AudioDoctor.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// The window an audio designer actually works in.
    /// </summary>
    /// <remarks>
    /// A validation report is scanned, not read top to bottom, so this is built as an
    /// information display rather than a document: the counts come first and double as
    /// filters, severity is encoded as colour *and* text so it survives a screenshot or
    /// a colour-blind reader, and the checks that did not run keep a permanent strip at
    /// the bottom instead of being a footnote nobody scrolls to.
    /// </remarks>
    public sealed class AudioDoctorWindow : EditorWindow
    {
        // Looked up by name rather than by a literal path. A hard-coded path is a
        // second place the package layout is recorded, and it breaks silently at
        // runtime the first time a folder is renamed - which is exactly what happened
        // when AudioDoctor became a module inside a toolbox.
        private const string LayoutAssetName = "AudioDoctorWindow";

        private enum GroupMode
        {
            Severity,
            Rule,
            Asset,
        }

        /// <summary>A line in the list: either a group header or one finding.</summary>
        private sealed class Row
        {
            public bool IsHeader;
            public string GroupKey;
            public string Label;
            public int Count;
            public bool Expanded;
            public ValidationIssue Issue;
        }

        // Survives a domain reload so the view does not reset on every recompile.
        [SerializeField] private GroupMode _groupMode = GroupMode.Severity;
        [SerializeField] private string _searchText = string.Empty;
        [SerializeField] private string _requestedBackendId = string.Empty;
        [SerializeField] private bool _showErrors = true;
        [SerializeField] private bool _showWarnings = true;
        [SerializeField] private bool _showInfos = true;

        // The report itself cannot survive a reload; the view falls back to its empty state.
        private ValidationReport _report;
        private ValidationIssue _selectedIssue;

        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<string> _collapsedGroups = new HashSet<string>(StringComparer.Ordinal);

        private ToolbarButton _runButton;
        private ToolbarMenu _backendMenu;
        private ToolbarMenu _groupMenu;
        private ToolbarButton _exportButton;
        private ToolbarSearchField _searchField;
        private Button _errorChip;
        private Button _warningChip;
        private Button _infoChip;
        private Label _summaryScope;
        private Label _summaryBackend;
        private VisualElement _summary;
        private VisualElement _emptyState;
        private Label _emptyTitle;
        private Label _emptyBody;
        private VisualElement _split;
        private ListView _list;
        private Label _detailPlaceholder;
        private VisualElement _detailBody;
        private VisualElement _skippedSection;
        private Foldout _skippedFoldout;
        private VisualElement _skippedList;

        [MenuItem("Window/Audio Toolbox/AudioDoctor/Diagnostics", priority = 100)]
        public static void Open()
        {
            var window = GetWindow<AudioDoctorWindow>();
            window.titleContent = new GUIContent("AudioDoctor");
            window.minSize = new Vector2(680, 320);
            window.Show();
        }

        public void CreateGUI()
        {
            var tree = FindPackageAsset<VisualTreeAsset>();
            var style = FindPackageAsset<StyleSheet>();

            if (tree == null)
            {
                rootVisualElement.Add(new Label(
                    $"AudioDoctor's window layout ({LayoutAssetName}.uxml) could not be found. " +
                    "Reimport the package."));
                return;
            }

            tree.CloneTree(rootVisualElement);

            var root = rootVisualElement.Q<VisualElement>("root");

            if (style != null)
            {
                rootVisualElement.styleSheets.Add(style);
            }

            // USS has no access to the editor skin, so the skin becomes a class.
            root.AddToClassList(EditorGUIUtility.isProSkin ? "dark" : "light");

            QueryElements();
            WireToolbar();
            WireList();
            WireChips();

            ShowEmptyState(
                "No scan yet",
                "Run a validation to reconcile what the middleware project declares, what the " +
                "banks contain, and what this Unity project references.");
        }

        private static T FindPackageAsset<T>() where T : UnityEngine.Object
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
            _runButton = rootVisualElement.Q<ToolbarButton>("run-button");
            _backendMenu = rootVisualElement.Q<ToolbarMenu>("backend-menu");
            _groupMenu = rootVisualElement.Q<ToolbarMenu>("group-menu");
            _exportButton = rootVisualElement.Q<ToolbarButton>("export-button");
            _searchField = rootVisualElement.Q<ToolbarSearchField>("search-field");

            _summary = rootVisualElement.Q<VisualElement>("summary");
            _errorChip = rootVisualElement.Q<Button>("chip-error");
            _warningChip = rootVisualElement.Q<Button>("chip-warning");
            _infoChip = rootVisualElement.Q<Button>("chip-info");
            _summaryScope = rootVisualElement.Q<Label>("summary-scope");
            _summaryBackend = rootVisualElement.Q<Label>("summary-backend");

            _emptyState = rootVisualElement.Q<VisualElement>("empty-state");
            _emptyTitle = rootVisualElement.Q<Label>("empty-title");
            _emptyBody = rootVisualElement.Q<Label>("empty-body");

            _split = rootVisualElement.Q<VisualElement>("split");
            _list = rootVisualElement.Q<ListView>("issue-list");
            _detailPlaceholder = rootVisualElement.Q<Label>("detail-placeholder");
            _detailBody = rootVisualElement.Q<VisualElement>("detail-body");

            _skippedSection = rootVisualElement.Q<VisualElement>("skipped-section");
            _skippedFoldout = rootVisualElement.Q<Foldout>("skipped-foldout");
            _skippedList = rootVisualElement.Q<VisualElement>("skipped-list");
        }

        private void WireToolbar()
        {
            _runButton.clicked += RunValidation;

            _searchField.value = _searchText;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchText = evt.newValue;
                RebuildRows();
            });

            _exportButton.clicked += ExportReports;

            RefreshBackendMenu();
            RefreshGroupMenu();
        }

        private void RefreshBackendMenu()
        {
            _backendMenu.menu.ClearItems();

            _backendMenu.menu.AppendAction(
                "Auto (highest priority available)",
                _ => SetBackend(string.Empty),
                _ => string.IsNullOrEmpty(_requestedBackendId)
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal);

            foreach (var backend in BackendRegistry.All())
            {
                var id = backend.BackendId;
                var available = SafeIsAvailable(backend);

                // An installed-but-unusable backend stays selectable: choosing it is how
                // you find out *why* it is unusable, which is more useful than hiding it.
                _backendMenu.menu.AppendAction(
                    available ? backend.DisplayName : $"{backend.DisplayName} (not ready)",
                    _ => SetBackend(id),
                    _ => string.Equals(_requestedBackendId, id, StringComparison.Ordinal)
                        ? DropdownMenuAction.Status.Checked
                        : DropdownMenuAction.Status.Normal);
            }

            _backendMenu.text = string.IsNullOrEmpty(_requestedBackendId)
                ? "Backend: Auto"
                : $"Backend: {_requestedBackendId}";
        }

        private void RefreshGroupMenu()
        {
            _groupMenu.menu.ClearItems();

            foreach (GroupMode mode in Enum.GetValues(typeof(GroupMode)))
            {
                var captured = mode;

                _groupMenu.menu.AppendAction(
                    mode.ToString(),
                    _ =>
                    {
                        _groupMode = captured;
                        _collapsedGroups.Clear();
                        RefreshGroupMenu();
                        RebuildRows();
                    },
                    _ => _groupMode == captured
                        ? DropdownMenuAction.Status.Checked
                        : DropdownMenuAction.Status.Normal);
            }

            _groupMenu.text = $"Group: {_groupMode}";
        }

        private void SetBackend(string backendId)
        {
            _requestedBackendId = backendId;
            RefreshBackendMenu();
        }

        private void WireChips()
        {
            _errorChip.clicked += () => ToggleSeverity(Severity.Error);
            _warningChip.clicked += () => ToggleSeverity(Severity.Warning);
            _infoChip.clicked += () => ToggleSeverity(Severity.Info);
        }

        private void ToggleSeverity(Severity severity)
        {
            switch (severity)
            {
                case Severity.Error: _showErrors = !_showErrors; break;
                case Severity.Warning: _showWarnings = !_showWarnings; break;
                default: _showInfos = !_showInfos; break;
            }

            RefreshSummary();
            RebuildRows();
        }

        private void WireList()
        {
            _list.fixedItemHeight = 38;
            _list.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            _list.selectionType = SelectionType.Single;
            _list.itemsSource = _rows;
            _list.makeItem = MakeRow;
            _list.bindItem = BindRow;

            _list.selectionChanged += selection =>
            {
                if (selection.FirstOrDefault() is Row row && !row.IsHeader)
                {
                    ShowDetail(row.Issue);
                }
            };

            // Copying a finding is how it gets into a bug report, a chat message or a
            // commit, which is most of what anyone does with one after reading it.
            _list.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.C && (evt.actionKey || evt.commandKey) && _selectedIssue != null)
                {
                    CopyToClipboard(_selectedIssue);
                    evt.StopPropagation();
                }
            });

            // A double-click is what takes you to the broken asset; a header toggles.
            _list.itemsChosen += chosen =>
            {
                if (!(chosen.FirstOrDefault() is Row row))
                {
                    return;
                }

                if (row.IsHeader)
                {
                    ToggleGroup(row.GroupKey);
                }
                else
                {
                    OpenIssueTarget(row.Issue);
                }
            };
        }

        // ------------------------------------------------------------------ running

        private void RunValidation()
        {
            var cancellation = new CancellationTokenSource();

            try
            {
                _report = AudioDoctorRunner.Run(new RunOptions
                {
                    BackendId = _requestedBackendId,
                    RuleSet = AudioDoctorRunner.FindProjectRuleSet(),
                    Token = cancellation.Token,
                    Progress = new DelegateProgressSink((stage, detail, normalized) =>
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "AudioDoctor", $"{stage}: {detail}", normalized))
                        {
                            cancellation.Cancel();
                        }
                    }),
                });
            }
            catch (OperationCanceledException)
            {
                ShowEmptyState("Scan cancelled", "Nothing was checked. Run again when you are ready.");
                return;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowEmptyState("The scan failed", e.Message);
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                cancellation.Dispose();
            }

            _collapsedGroups.Clear();
            RefreshSummary();
            RefreshSkipped();
            RebuildRows();
            ShowResults();
        }

        private void ExportReports()
        {
            if (_report == null)
            {
                EditorUtility.DisplayDialog("AudioDoctor", "Run a validation first.", "OK");
                return;
            }

            var folder = AudioDoctorMenu.WriteReports(_report);
            Debug.Log($"[AudioDoctor] Reports written to {folder}");
            EditorUtility.RevealInFinder(folder);
        }

        // ------------------------------------------------------------------- display

        private void ShowEmptyState(string title, string body)
        {
            _emptyTitle.text = title;
            _emptyBody.text = body;

            _emptyState.style.display = DisplayStyle.Flex;
            _split.style.display = DisplayStyle.None;
            _summary.style.display = _report == null ? DisplayStyle.None : DisplayStyle.Flex;
            _skippedSection.style.display = DisplayStyle.None;
        }

        private void ShowResults()
        {
            if (_report.Issues.Count == 0)
            {
                // "No findings" and "nothing was checked" are different results, and the
                // difference lives in the skipped list - so keep that visible here.
                ShowEmptyState(
                    "No findings",
                    _report.SkippedRules.Count > 0
                        ? $"Every check that ran came back clean, but {_report.SkippedRules.Count} " +
                          "did not run. Expand the strip below before treating this as a pass."
                        : "Every check ran and every one came back clean.");

                _summary.style.display = DisplayStyle.Flex;
                _skippedSection.style.display =
                    _report.SkippedRules.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                return;
            }

            _emptyState.style.display = DisplayStyle.None;
            _split.style.display = DisplayStyle.Flex;
            _summary.style.display = DisplayStyle.Flex;
            _skippedSection.style.display =
                _report.SkippedRules.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshSummary()
        {
            if (_report == null)
            {
                return;
            }

            SetChip(_errorChip, _report.ErrorCount, "Error", _showErrors);
            SetChip(_warningChip, _report.WarningCount, "Warning", _showWarnings);
            SetChip(_infoChip, _report.InfoCount, "Note", _showInfos);

            _summaryScope.text =
                $"{_report.EventCount} events · {_report.BankCount} banks · {_report.ReferenceCount} references";

            _summaryBackend.text =
                $"{_report.BackendDisplayName} · scanned in {_report.ScanSeconds:0.0}s";
        }

        private static void SetChip(Button chip, int count, string noun, bool active)
        {
            chip.text = $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
            chip.EnableInClassList("chip--muted", !active);
            chip.tooltip = active ? $"Hide {noun.ToLowerInvariant()}s" : $"Show {noun.ToLowerInvariant()}s";
        }

        private void RefreshSkipped()
        {
            _skippedList.Clear();

            if (_report == null)
            {
                return;
            }

            _skippedFoldout.text = $"Checks that did not run ({_report.SkippedRules.Count})";

            foreach (var skipped in _report.SkippedRules)
            {
                var row = new VisualElement();
                row.AddToClassList("skipped-row");

                var id = new Label(skipped.RuleId);
                id.AddToClassList("skipped-rule");

                var reason = new Label($"{skipped.Title} — {skipped.Reason}");
                reason.AddToClassList("skipped-reason");

                row.Add(id);
                row.Add(reason);
                _skippedList.Add(row);
            }
        }

        // ---------------------------------------------------------------- row model

        private bool PassesFilter(ValidationIssue issue)
        {
            switch (issue.Severity)
            {
                case Severity.Error when !_showErrors: return false;
                case Severity.Warning when !_showWarnings: return false;
                case Severity.Info when !_showInfos: return false;
            }

            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return true;
            }

            var needle = _searchText.Trim();

            return Contains(issue.Message, needle) ||
                   Contains(issue.PrimaryAssetPath, needle) ||
                   Contains(issue.RuleId, needle) ||
                   Contains(issue.Detail, needle);
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private string GroupKeyOf(ValidationIssue issue)
        {
            switch (_groupMode)
            {
                case GroupMode.Rule:
                    return issue.RuleId;
                case GroupMode.Asset:
                    return string.IsNullOrEmpty(issue.PrimaryAssetPath)
                        ? "(no asset — project-wide)"
                        : issue.PrimaryAssetPath;
                default:
                    return issue.Severity.ToString();
            }
        }

        private void RebuildRows()
        {
            _rows.Clear();

            if (_report != null)
            {
                var groups = _report.Issues
                    .Where(PassesFilter)
                    .GroupBy(GroupKeyOf, StringComparer.Ordinal)
                    .ToList();

                // Severity groups read worst-first; the other modes read alphabetically.
                var ordered = _groupMode == GroupMode.Severity
                    ? groups.OrderByDescending(g => ParseSeverity(g.Key))
                    : groups.OrderBy(g => g.Key, StringComparer.Ordinal);

                foreach (var group in ordered)
                {
                    var collapsed = _collapsedGroups.Contains(group.Key);

                    _rows.Add(new Row
                    {
                        IsHeader = true,
                        GroupKey = group.Key,
                        Label = group.Key,
                        Count = group.Count(),
                        Expanded = !collapsed,
                    });

                    if (collapsed)
                    {
                        continue;
                    }

                    foreach (var issue in group)
                    {
                        _rows.Add(new Row { Issue = issue });
                    }
                }
            }

            _list.itemsSource = _rows;
            _list.Rebuild();
        }

        private static int ParseSeverity(string name) =>
            Enum.TryParse<Severity>(name, out var parsed) ? (int)parsed : -1;

        private void ToggleGroup(string groupKey)
        {
            if (!_collapsedGroups.Remove(groupKey))
            {
                _collapsedGroups.Add(groupKey);
            }

            RebuildRows();
        }

        // ------------------------------------------------------------------ binding

        private static VisualElement MakeRow()
        {
            // One reusable element renders either shape; ListView recycles it either way,
            // which is what keeps a thousand-issue report scrolling smoothly.
            var container = new VisualElement();
            container.style.flexGrow = 1;

            var header = new VisualElement { name = "header" };
            header.AddToClassList("group-header");
            var arrow = new Label { name = "arrow" };
            arrow.AddToClassList("group-arrow");
            var groupLabel = new Label { name = "group-label" };
            groupLabel.AddToClassList("group-label");
            var groupCount = new Label { name = "group-count" };
            groupCount.AddToClassList("group-count");
            header.Add(arrow);
            header.Add(groupLabel);
            header.Add(groupCount);

            var row = new VisualElement { name = "issue" };
            row.AddToClassList("row");
            var stripe = new VisualElement { name = "stripe" };
            stripe.AddToClassList("row-stripe");
            var badge = new Label { name = "badge" };
            badge.AddToClassList("row-badge");
            var text = new VisualElement();
            text.AddToClassList("row-text");
            var message = new Label { name = "message" };
            message.AddToClassList("row-message");
            var path = new Label { name = "path" };
            path.AddToClassList("row-path");
            text.Add(message);
            text.Add(path);
            row.Add(stripe);
            row.Add(badge);
            row.Add(text);

            container.Add(header);
            container.Add(row);
            return container;
        }

        private void BindRow(VisualElement element, int index)
        {
            var row = _rows[index];
            var header = element.Q<VisualElement>("header");
            var issueRow = element.Q<VisualElement>("issue");

            header.style.display = row.IsHeader ? DisplayStyle.Flex : DisplayStyle.None;
            issueRow.style.display = row.IsHeader ? DisplayStyle.None : DisplayStyle.Flex;

            if (row.IsHeader)
            {
                element.Q<Label>("arrow").text = row.Expanded ? "▼" : "▶";
                element.Q<Label>("group-label").text = row.Label;
                element.Q<Label>("group-count").text = row.Count.ToString();
                element.tooltip = "Double-click to collapse or expand";
                return;
            }

            var issue = row.Issue;
            var severity = issue.Severity.ToString().ToLowerInvariant();

            var stripe = element.Q<VisualElement>("stripe");
            SetSeverityClass(stripe, "row-stripe", severity);

            var badge = element.Q<Label>("badge");
            badge.text = issue.RuleId;
            SetSeverityClass(badge, "row-badge", severity);

            element.Q<Label>("message").text = issue.Message;
            element.Q<Label>("path").text = LocationOf(issue);
            element.tooltip = "Double-click to select the asset";
        }

        private static void SetSeverityClass(VisualElement element, string baseClass, string severity)
        {
            element.RemoveFromClassList($"{baseClass}--error");
            element.RemoveFromClassList($"{baseClass}--warning");
            element.RemoveFromClassList($"{baseClass}--info");
            element.AddToClassList($"{baseClass}--{severity}");
        }

        private static string LocationOf(ValidationIssue issue)
        {
            if (string.IsNullOrEmpty(issue.PrimaryAssetPath))
            {
                return "project-wide — no single asset to open";
            }

            return issue.Line > 0 ? $"{issue.PrimaryAssetPath}:{issue.Line}" : issue.PrimaryAssetPath;
        }

        // ------------------------------------------------------------------- detail

        private void ShowDetail(ValidationIssue issue)
        {
            _selectedIssue = issue;
            _detailPlaceholder.style.display = DisplayStyle.None;
            _detailBody.Clear();

            var ruleLabel = new Label(issue.RuleId);
            ruleLabel.AddToClassList("detail-rule");
            ruleLabel.AddToClassList($"row-badge--{issue.Severity.ToString().ToLowerInvariant()}");
            _detailBody.Add(ruleLabel);

            var title = new Label(issue.Message);
            title.AddToClassList("detail-title");
            _detailBody.Add(title);

            AddDetailSection("Where", LocationOf(issue), "detail-path");

            if (!string.IsNullOrEmpty(issue.Detail))
            {
                AddDetailSection("What this means", issue.Detail, "detail-text");
            }

            if (issue.SecondaryAssetPaths.Count > 0)
            {
                AddDetailSection(
                    "Also involves",
                    string.Join("\n", issue.SecondaryAssetPaths),
                    "detail-path");
            }

            var actions = new VisualElement();
            actions.AddToClassList("detail-actions");

            if (!string.IsNullOrEmpty(issue.PrimaryAssetPath))
            {
                actions.Add(new Button(() => OpenIssueTarget(issue)) { text = "Select asset" });
            }

            actions.Add(new Button(() => CopyToClipboard(issue))
            {
                text = "Copy",
                tooltip = "Copy this finding as text (also " + CopyShortcutLabel + ")",
            });

            _detailBody.Add(actions);
        }

        private void AddDetailSection(string label, string body, string bodyClass)
        {
            var sectionLabel = new Label(label.ToUpperInvariant());
            sectionLabel.AddToClassList("detail-section-label");

            var bodyLabel = new Label(body);
            bodyLabel.AddToClassList(bodyClass);
            bodyLabel.selection.isSelectable = true;

            _detailBody.Add(sectionLabel);
            _detailBody.Add(bodyLabel);
        }

        private static string CopyShortcutLabel =>
            Application.platform == RuntimePlatform.OSXEditor ? "Cmd+C" : "Ctrl+C";

        /// <summary>
        /// Puts one finding on the clipboard as plain text.
        /// </summary>
        /// <remarks>
        /// Laid out so it stays readable wherever it is pasted - a bug tracker, a chat
        /// message, a commit body - which means no table syntax and no leading
        /// characters that a Markdown renderer would eat.
        /// </remarks>
        private static void CopyToClipboard(ValidationIssue issue)
        {
            var text = new StringBuilder();

            text.AppendLine($"[{issue.RuleId}/{issue.Severity}] {issue.Message}");
            text.AppendLine(LocationOf(issue));

            if (!string.IsNullOrEmpty(issue.Detail))
            {
                text.AppendLine();
                text.AppendLine(issue.Detail);
            }

            if (issue.SecondaryAssetPaths.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Also involves:");

                foreach (var path in issue.SecondaryAssetPaths)
                {
                    text.AppendLine("  " + path);
                }
            }

            EditorGUIUtility.systemCopyBuffer = text.ToString().TrimEnd();
            Debug.Log($"[AudioDoctor] Copied {issue.RuleId} to the clipboard.");
        }

        private static void OpenIssueTarget(ValidationIssue issue)
        {
            if (string.IsNullOrEmpty(issue.PrimaryAssetPath))
            {
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<Object>(issue.PrimaryAssetPath);

            if (asset == null)
            {
                Debug.LogWarning(
                    $"[AudioDoctor] '{issue.PrimaryAssetPath}' could not be loaded. " +
                    "It may have been moved or deleted since the scan.");
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);

            // A code finding knows its line, so take the reader all the way there.
            if (issue.Line > 0)
            {
                AssetDatabase.OpenAsset(asset, issue.Line);
            }
        }

        private static bool SafeIsAvailable(IAudioProjectSource backend)
        {
            try
            {
                return backend.IsAvailable;
            }
            catch
            {
                return false;
            }
        }
    }
}
