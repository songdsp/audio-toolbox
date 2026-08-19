using System.Threading;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>Everything a backend needs to know while it is scanning.</summary>
    public sealed class ScanContext
    {
        public ScanContext(IProgressSink progress, CancellationToken token)
        {
            Progress = progress ?? NullProgressSink.Instance;
            Token = token;
        }

        public IProgressSink Progress { get; }

        public CancellationToken Token { get; }

        public static ScanContext Silent => new ScanContext(NullProgressSink.Instance, CancellationToken.None);

        /// <summary>Throws <see cref="System.OperationCanceledException"/> if the user cancelled.</summary>
        public void ThrowIfCancelled() => Token.ThrowIfCancellationRequested();
    }
}
