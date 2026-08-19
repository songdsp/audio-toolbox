using System;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>Where a long scan reports what it is doing.</summary>
    public interface IProgressSink
    {
        /// <summary>Called often. <paramref name="normalized"/> is 0..1 within the stage.</summary>
        void Report(string stage, string detail, float normalized);
    }

    /// <summary>Discards everything. The default for tests and batch runs.</summary>
    public sealed class NullProgressSink : IProgressSink
    {
        public static readonly NullProgressSink Instance = new NullProgressSink();

        private NullProgressSink() { }

        public void Report(string stage, string detail, float normalized) { }
    }

    /// <summary>Forwards to a delegate. Used by the editor window and the CLI.</summary>
    public sealed class DelegateProgressSink : IProgressSink
    {
        private readonly Action<string, string, float> _onReport;

        public DelegateProgressSink(Action<string, string, float> onReport)
        {
            _onReport = onReport ?? throw new ArgumentNullException(nameof(onReport));
        }

        public void Report(string stage, string detail, float normalized) =>
            _onReport(stage, detail, normalized);
    }
}
