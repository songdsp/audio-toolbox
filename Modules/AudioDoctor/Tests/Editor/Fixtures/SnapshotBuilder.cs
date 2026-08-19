using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Tests
{
    /// <summary>
    /// Builds snapshots by hand for the rule unit tests.
    /// </summary>
    /// <remarks>
    /// No AssetDatabase, no middleware, no Unity assets — the whole point of the
    /// normalized model is that a rule can be proved correct from arrays like these.
    /// If a rule ever needs something this builder cannot express, that rule has
    /// reached outside the model and should be reconsidered.
    /// </remarks>
    internal sealed class SnapshotBuilder
    {
        private readonly List<EventDef> _events = new List<EventDef>();
        private readonly List<BankDef> _banks = new List<BankDef>();
        private readonly List<EventRefUsage> _references = new List<EventRefUsage>();
        private readonly List<ParameterUsage> _parameterUsages = new List<ParameterUsage>();
        private readonly List<BankLoadUsage> _bankLoads = new List<BankLoadUsage>();
        private readonly List<ScanNote> _notes = new List<ScanNote>();
        private readonly List<string> _globalParameters = new List<string>();
        private readonly List<string> _scenePaths = new List<string>();

        private BackendCapability _capabilities = BackendCapability.None;

        public static SnapshotBuilder New() => new SnapshotBuilder();

        public SnapshotBuilder WithCapabilities(BackendCapability capabilities)
        {
            _capabilities = capabilities;
            return this;
        }

        public SnapshotBuilder Event(
            string key,
            IEnumerable<string> banks = null,
            IEnumerable<string> parameters = null,
            bool? is3D = null,
            bool? isStreaming = null,
            float? lengthSeconds = null)
        {
            _events.Add(new EventDef
            {
                Key = key,
                BackendId = key,
                Is3D = is3D,
                IsStreaming = isStreaming,
                LengthSeconds = lengthSeconds,
                Parameters = parameters?.ToList() ?? (IReadOnlyList<string>)new string[0],
                BankNames = banks?.ToList() ?? (IReadOnlyList<string>)new string[0],
            });

            return this;
        }

        public SnapshotBuilder Bank(string name, string platform = "Desktop", long sizeBytes = 1024, params string[] eventKeys)
        {
            _banks.Add(new BankDef
            {
                Name = name,
                Platform = platform,
                SizeBytes = sizeBytes,
                EventKeys = eventKeys.ToList(),
            });

            return this;
        }

        public SnapshotBuilder Reference(
            string eventKey,
            string assetPath = "Assets/Test.prefab",
            RefSource source = RefSource.SerializedField,
            int line = 0,
            string objectPath = null,
            bool? spatialized = null)
        {
            _references.Add(new EventRefUsage
            {
                EventKey = eventKey,
                AssetPath = assetPath,
                ObjectPath = objectPath,
                Source = source,
                Line = line,
                IsSpatializedCallSite = spatialized,
            });

            return this;
        }

        public SnapshotBuilder Parameter(
            string eventKey,
            string parameterName,
            string assetPath = "Assets/Test.cs",
            int line = 1,
            bool isGlobal = false)
        {
            _parameterUsages.Add(new ParameterUsage
            {
                EventKey = eventKey,
                ParameterName = parameterName,
                AssetPath = assetPath,
                Line = line,
                IsGlobal = isGlobal,
            });

            return this;
        }

        public SnapshotBuilder BankLoad(
            string bankName,
            string assetPath = "Assets/Test.unity",
            BankLoadSource source = BankLoadSource.LoaderComponent)
        {
            _bankLoads.Add(new BankLoadUsage
            {
                BankName = bankName,
                AssetPath = assetPath,
                Source = source,
            });

            return this;
        }

        public SnapshotBuilder GlobalParameter(string name)
        {
            _globalParameters.Add(name);
            return this;
        }

        public SnapshotBuilder Scene(string scenePath)
        {
            _scenePaths.Add(scenePath);
            return this;
        }

        public SnapshotBuilder Note(string message, string assetPath = null, int line = 0)
        {
            _notes.Add(new ScanNote { Message = message, AssetPath = assetPath, Line = line });
            return this;
        }

        public AudioProjectSnapshot Build() => new AudioProjectSnapshot
        {
            BackendId = "test",
            Events = _events,
            Banks = _banks,
            References = _references,
            ParameterUsages = _parameterUsages,
            BankLoads = _bankLoads,
            GlobalParameters = _globalParameters,
            ScenePaths = _scenePaths,
            Capabilities = _capabilities,
            Notes = _notes,
        };
    }
}
