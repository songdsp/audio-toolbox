using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor.Rules;
using NUnit.Framework;

namespace AudioToolbox.AudioDoctor.Tests.Rules
{
    [TestFixture]
    internal sealed class R004_OrphanEventTests : RuleTestBase
    {
        private readonly R004_OrphanEvent _rule = new R004_OrphanEvent();

        [Test]
        public void ReportsAPackedEventNothingReferences()
        {
            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/UI/Click", banks: new[] { "UI" })
                .Event("event:/UI/Hover", banks: new[] { "UI" })
                .Bank("UI", "Desktop", 2048, "event:/UI/Click", "event:/UI/Hover")
                .Reference("event:/UI/Click"));

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo(Severity.Warning));
            Assert.That(issues[0].Message, Does.Contain("event:/UI/Hover"));
            Assert.That(issues[0].Detail, Does.Contain("UI"), "The detail must name the bank paying for it.");
        }

        [Test]
        public void SaysNothingWhenEveryPackedEventIsUsed()
        {
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click", banks: new[] { "UI" })
                    .Bank("UI", "Desktop", 1024, "event:/UI/Click")
                    .Reference("event:/UI/Click"),
                "The only packed event is referenced.");
        }

        [Test]
        public void SaysNothingWhenTheScanFoundNoReferencesAtAll()
        {
            // Every packed event would qualify, which is true and useless. A scan that
            // found nothing is a broken scan and is reported as such by R000.
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click", banks: new[] { "UI" })
                    .Event("event:/UI/Hover", banks: new[] { "UI" })
                    .Bank("UI", "Desktop", 2048, "event:/UI/Click", "event:/UI/Hover"),
                "With zero references found, orphan detection has no signal to work from.");
        }

        [Test]
        public void SaysNothingAboutAnEventThatIsNotPacked()
        {
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/WorkInProgress/Idea")
                    .Event("event:/UI/Click", banks: new[] { "UI" })
                    .Bank("UI", "Desktop", 1024, "event:/UI/Click")
                    .Reference("event:/UI/Click"),
                "An unpacked event costs nothing at runtime, so it is not an orphan.");
        }

        [Test]
        public void RepeatsTheScansOwnCaveatWhenReferencesCouldNotAllBeResolved()
        {
            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/UI/Click", banks: new[] { "UI" })
                .Event("event:/UI/Hover", banks: new[] { "UI" })
                .Bank("UI", "Desktop", 2048, "event:/UI/Click", "event:/UI/Hover")
                .Reference("event:/UI/Click")
                .Note("An event name is assembled from parts here.", "Assets/Player.cs", 12));

            Assert.That(issues[0].Detail, Does.Contain("confirm"),
                "Suggesting deletion on incomplete evidence, without saying the evidence is " +
                "incomplete, is how a validator gets someone to delete audio that is in use.");
        }
    }
}
