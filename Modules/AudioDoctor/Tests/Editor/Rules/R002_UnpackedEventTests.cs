using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor.Rules;
using NUnit.Framework;

namespace AudioToolbox.AudioDoctor.Tests.Rules
{
    [TestFixture]
    internal sealed class R002_UnpackedEventTests : RuleTestBase
    {
        private readonly R002_UnpackedEvent _rule = new R002_UnpackedEvent();

        [Test]
        public void ReportsAUsedEventThatNoBankContains()
        {
            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/UI/Click")
                .Bank("Master", "Desktop", 1024)
                .Reference("event:/UI/Click", "Assets/Menu.prefab"));

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo(Severity.Error));
            Assert.That(issues[0].PrimaryAssetPath, Is.EqualTo("Assets/Menu.prefab"),
                "The report must open on a place the event is used.");
        }

        [Test]
        public void SaysNothingWhenTheEventDeclaresABank()
        {
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click", banks: new[] { "UI" })
                    .Bank("UI", "Desktop", 1024, "event:/UI/Click")
                    .Reference("event:/UI/Click"),
                "The event is packed.");
        }

        [Test]
        public void SaysNothingWhenABankListsTheEventEvenIfTheEventDoesNotListTheBank()
        {
            // Backends can express membership from either side; trusting only one of
            // them would report properly packed events as missing.
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click")
                    .Bank("UI", "Desktop", 1024, "event:/UI/Click")
                    .Reference("event:/UI/Click"),
                "The bank lists the event, which is membership just as much as the reverse.");
        }

        [Test]
        public void SaysNothingAboutAnUnpackedEventNobodyUses()
        {
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/WorkInProgress/Idea")
                    .Bank("Master", "Desktop", 1024),
                "An unassigned event that nothing references is work in progress, not a defect.");
        }

        [Test]
        public void IsSkippedByABackendThatCannotSeeUnpackedEventsAtAll()
        {
            // FMOD's integration enumerates events by loading each built bank, so an
            // event in no bank never reaches Unity - exactly the event this rule looks
            // for. Running anyway would report a clean result for a check that was
            // structurally incapable of finding anything.
            var snapshot = SnapshotBuilder.New()
                .WithCapabilities(BackendCapability.BankMembership)
                .Event("event:/UI/Click")
                .Reference("event:/UI/Click")
                .Build();

            AudioToolbox.AudioDoctor.Editor.RuleEngine.Run(
                new AudioToolbox.AudioDoctor.Editor.RuleContext(snapshot, RuleSet),
                new AudioToolbox.AudioDoctor.Editor.IValidationRule[] { _rule },
                out var issues,
                out var skipped);

            Assert.That(issues, Is.Empty);
            Assert.That(skipped.Count, Is.EqualTo(1));
            Assert.That(skipped[0].Reason, Does.Contain("UnpackedEvents"));
        }

        [Test]
        public void CollectsEveryUsageSiteSoTheScopeOfTheBreakageIsVisible()
        {
            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/UI/Click")
                .Reference("event:/UI/Click", "Assets/A.prefab")
                .Reference("event:/UI/Click", "Assets/B.prefab")
                .Reference("event:/UI/Click", "Assets/C.prefab"));

            Assert.That(issues.Count, Is.EqualTo(1), "One event, one finding.");
            Assert.That(issues[0].Detail, Does.Contain("3 place(s)"));
            Assert.That(issues[0].SecondaryAssetPaths,
                Is.EquivalentTo(new[] { "Assets/B.prefab", "Assets/C.prefab" }));
        }
    }
}
