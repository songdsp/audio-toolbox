using AudioToolbox.AudioDoctor.Editor;
using NUnit.Framework;

namespace AudioToolbox.AudioDoctor.Tests
{
    /// <summary>
    /// Regression cover for the false positive that the fixture project surfaced:
    /// <c>PlayOneShot("event:/" + kind)</c> was reported as an Error claiming the event
    /// 'event:/' did not exist, on the very same line the scanner also admitted it
    /// could not resolve the name. A wrong Error is how a validator gets switched off.
    /// </summary>
    [TestFixture]
    public sealed class CodeLiteralTests
    {
        /// <summary>Finds the literal the way the scanners' regexes capture it.</summary>
        private static bool Check(string line, string literal)
        {
            var quoted = line.IndexOf('"' + literal + '"', System.StringComparison.Ordinal);
            Assert.That(quoted, Is.GreaterThanOrEqualTo(0), "test setup: literal not found in line");
            return CodeLiteral.IsConcatenated(line, quoted + 1, literal.Length);
        }

        [Test]
        public void DetectsALiteralFollowedByConcatenation()
        {
            Assert.That(Check(@"RuntimeManager.PlayOneShot(""event:/"" + kind);", "event:/"), Is.True);
        }

        [Test]
        public void DetectsALiteralPrecededByConcatenation()
        {
            Assert.That(Check(@"RuntimeManager.PlayOneShot(prefix + ""/Click"");", "/Click"), Is.True);
        }

        [Test]
        public void DetectsConcatenationAcrossExtraWhitespace()
        {
            Assert.That(Check(@"PlayOneShot(""event:/""   +   kind);", "event:/"), Is.True);
        }

        [Test]
        public void AStandaloneLiteralIsNotConcatenated()
        {
            Assert.That(Check(@"RuntimeManager.PlayOneShot(""event:/UI/Click"");", "event:/UI/Click"), Is.False);
        }

        [Test]
        public void ALiteralFollowedByAnotherArgumentIsNotConcatenated()
        {
            // The comma before a position argument must not read as a concatenation.
            Assert.That(
                Check(@"RuntimeManager.PlayOneShot(""event:/Footsteps"", transform.position);", "event:/Footsteps"),
                Is.False);
        }

        [Test]
        public void ALiteralAtTheStartOfTheLineIsHandled()
        {
            Assert.That(Check(@"""event:/Click"");", "event:/Click"), Is.False);
        }

        [Test]
        public void AnEmptyLineIsNotConcatenated()
        {
            Assert.That(CodeLiteral.IsConcatenated(string.Empty, 1, 0), Is.False);
            Assert.That(CodeLiteral.IsConcatenated(null, 1, 0), Is.False);
        }
    }
}
