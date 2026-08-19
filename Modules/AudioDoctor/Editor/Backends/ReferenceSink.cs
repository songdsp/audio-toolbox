using System.Collections.Generic;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// Collects what a reference scan finds. Backends push into this rather than
    /// returning four parallel lists, so adding a fifth kind of usage later does
    /// not change every backend's signature.
    /// </summary>
    public sealed class ReferenceSink
    {
        private readonly List<EventRefUsage> _references = new List<EventRefUsage>();
        private readonly List<ParameterUsage> _parameterUsages = new List<ParameterUsage>();
        private readonly List<BankLoadUsage> _bankLoads = new List<BankLoadUsage>();
        private readonly List<ScanNote> _notes = new List<ScanNote>();
        private readonly List<string> _scenePaths = new List<string>();

        public IReadOnlyList<EventRefUsage> References => _references;
        public IReadOnlyList<ParameterUsage> ParameterUsages => _parameterUsages;
        public IReadOnlyList<BankLoadUsage> BankLoads => _bankLoads;
        public IReadOnlyList<ScanNote> Notes => _notes;
        public IReadOnlyList<string> ScenePaths => _scenePaths;

        public void Add(EventRefUsage usage) => _references.Add(usage);

        public void Add(ParameterUsage usage) => _parameterUsages.Add(usage);

        public void Add(BankLoadUsage usage) => _bankLoads.Add(usage);

        public void Add(ScanNote note) => _notes.Add(note);

        /// <summary>
        /// Records that a scene was considered. R003 needs the full list of scenes,
        /// including the ones that turned out to reference nothing.
        /// </summary>
        public void AddScene(string scenePath)
        {
            if (!string.IsNullOrEmpty(scenePath) && !_scenePaths.Contains(scenePath))
            {
                _scenePaths.Add(scenePath);
            }
        }

        public void Note(string message, string assetPath = null, int line = 0) =>
            _notes.Add(new ScanNote { Message = message, AssetPath = assetPath, Line = line });
    }
}
