using System.Collections.Generic;
using System.Globalization;
using AudioToolbox.AudioDoctor.Core;

namespace AudioToolbox.AudioDoctor.Editor.Rules
{
    /// <summary>
    /// Audio whose loading mode does not match its length.
    /// </summary>
    /// <remarks>
    /// Two opposite mistakes, both costly and neither audible in the editor. A long
    /// track that is not streamed is decoded whole into memory when its bank loads —
    /// the single easiest way to blow a memory budget on a console. A one-shot that is
    /// streamed opens a file handle and seeks on every trigger, which turns a free
    /// sound into a hitch when it fires ten times a second.
    /// </remarks>
    public sealed class R005_LoadingStrategy : IValidationRule
    {
        public string RuleId => "R005";

        public string Title => "Loading strategy does not match length";

        public Severity DefaultSeverity => Severity.Warning;

        public BackendCapability RequiredCapabilities =>
            BackendCapability.EventLength | BackendCapability.StreamingFlag;

        public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
        {
            var longSeconds = context.Settings.LongEventSeconds;
            var shortSeconds = context.Settings.ShortEventSeconds;

            foreach (var authored in context.Events)
            {
                // A backend may know the length of some events and not others; skipping
                // the unknowns beats assuming a default and reporting on the assumption.
                if (string.IsNullOrEmpty(authored.Key) ||
                    !authored.LengthSeconds.HasValue ||
                    !authored.IsStreaming.HasValue)
                {
                    continue;
                }

                var length = authored.LengthSeconds.Value;
                var streaming = authored.IsStreaming.Value;

                if (length > longSeconds && !streaming)
                {
                    yield return context.Issue(
                        this,
                        $"'{authored.Key}' is {Format(length)} long but is not streamed",
                        primaryAssetPath: null,
                        detail:
                        $"Anything longer than {Format(longSeconds)} is expected to stream. This one " +
                        "is decoded in full into memory the moment its bank loads, and stays there. " +
                        "Enable streaming on the audio asset inside the event, or shorten it. " +
                        "The threshold is configurable on the rule set asset.");
                }
                else if (length < shortSeconds && streaming)
                {
                    yield return context.Issue(
                        this,
                        $"'{authored.Key}' is only {Format(length)} long but is streamed",
                        primaryAssetPath: null,
                        detail:
                        $"Anything shorter than {Format(shortSeconds)} is expected to be resident. " +
                        "Streaming opens a file handle and seeks on every trigger, so a short sound " +
                        "fired often costs far more than the memory it saves. Turn streaming off on " +
                        "the audio asset inside the event.");
                }
            }
        }

        private static string Format(float seconds) =>
            seconds.ToString("0.##", CultureInfo.InvariantCulture) + "s";
    }
}
