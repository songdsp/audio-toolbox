using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor.Rules;
using NUnit.Framework;

namespace AudioToolbox.AudioDoctor.Tests.Rules
{
    [TestFixture]
    internal sealed class R001_DanglingReferenceTests : RuleTestBase
    {
        private readonly R001_DanglingReference _rule = new R001_DanglingReference();

        [Test]
        public void ReportsAReferenceToAnEventThatDoesNotExist()
        {
            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/UI/Click")
                .Reference("event:/UI/Clik", "Assets/Menu.prefab"));

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo(Severity.Error));
            Assert.That(issues[0].PrimaryAssetPath, Is.EqualTo("Assets/Menu.prefab"));
            Assert.That(issues[0].Message, Does.Contain("event:/UI/Clik"));
        }

        [Test]
        public void SaysNothingWhenEveryReferenceResolves()
        {
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click")
                    .Event("event:/Music/Menu")
                    .Reference("event:/UI/Click")
                    .Reference("event:/Music/Menu"),
                "Every reference points at a declared event.");
        }

        [Test]
        public void ACaseOnlyMismatchIsReportedAndExplainedAsSuch()
        {
            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/UI/Click")
                .Reference("event:/ui/click", "Assets/Menu.prefab"));

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Detail, Does.Contain("case"),
                "A case mismatch has a different fix from a typo and must be told apart from one.");
            Assert.That(issues[0].Detail, Does.Contain("event:/UI/Click"),
                "The detail must name the event the author probably meant.");
        }

        [Test]
        public void ReportsOncePerUsageSiteSoEachOneCanBeOpened()
        {
            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/UI/Click")
                .Reference("event:/Missing", "Assets/A.prefab")
                .Reference("event:/Missing", "Assets/B.prefab")
                .Reference("event:/Missing", "Assets/Player.cs", RefSource.CodeLiteral, line: 88));

            Assert.That(issues.Count, Is.EqualTo(3));
            Assert.That(issues.Select(i => i.PrimaryAssetPath),
                Is.EquivalentTo(new[] { "Assets/A.prefab", "Assets/B.prefab", "Assets/Player.cs" }));
            Assert.That(issues.Single(i => i.PrimaryAssetPath == "Assets/Player.cs").Line, Is.EqualTo(88));
        }

        [Test]
        public void SaysNothingWhenNoEventsWereFoundAtAll()
        {
            // A backend that returned nothing would otherwise make every single
            // reference in the project look dangling.
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Reference("event:/UI/Click")
                    .Reference("event:/Music/Menu"),
                "An empty authored list means a failed scan, not a broken project.");
        }

        [Test]
        public void IgnoresReferencesWithNoKey()
        {
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click")
                    .Reference(null)
                    .Reference(string.Empty),
                "An unassigned reference field has nothing to resolve.");
        }
    }
}
