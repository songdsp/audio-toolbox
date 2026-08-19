using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor.Rules;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AudioToolbox.AudioDoctor.Tests.Rules
{
    [TestFixture]
    internal sealed class R008_NamingConventionTests : RuleTestBase
    {
        private readonly R008_NamingConvention _rule = new R008_NamingConvention();

        [Test]
        public void ReportsAKeyThatDoesNotMatchThePattern()
        {
            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/test 1")
                .Event("event:/UI/Click"));

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo(Severity.Info));
            Assert.That(issues[0].Message, Does.Contain("event:/test 1"));
        }

        [Test]
        public void SaysNothingWhenEveryKeyMatches()
        {
            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click")
                    .Event("event:/Music/Level_01")
                    .Event("event:/Ambience/Forest/Birds"),
                "All three follow the default convention.");
        }

        [Test]
        public void AnEmptyPatternDisablesThePatternCheck()
        {
            RuleSet.EventNamingPattern = string.Empty;

            AssertSilent(_rule, SnapshotBuilder.New().Event("event:/anything at all!"),
                "An empty pattern is how a team opts out of the convention check.");
        }

        [Test]
        public void APatternFromTheRuleSetIsWhatGetsApplied()
        {
            RuleSet.EventNamingPattern = "^event:/SFX/";

            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/SFX/Click")
                .Event("event:/UI/Click"));

            Assert.That(issues.Single().Message, Does.Contain("event:/UI/Click"));
        }

        [Test]
        public void ReportsTwoEventsWhoseKeysDifferOnlyByCase()
        {
            RuleSet.EventNamingPattern = string.Empty;

            var issues = Run(_rule, SnapshotBuilder.New()
                .Event("event:/UI/Click")
                .Event("event:/ui/click"));

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Message, Does.Contain("differ only by letter case"));
            Assert.That(issues[0].Detail, Does.Contain("Linux"),
                "The finding must explain why this is a portability bug and not a style nit.");
        }

        [Test]
        public void TwoGenuinelyDifferentKeysAreNotACaseCollision()
        {
            RuleSet.EventNamingPattern = string.Empty;

            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click")
                    .Event("event:/UI/Hover"),
                "These differ by more than case.");
        }

        [Test]
        public void TheSameKeyListedTwiceIsNotACaseCollision()
        {
            RuleSet.EventNamingPattern = string.Empty;

            AssertSilent(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click")
                    .Event("event:/UI/Click"),
                "A duplicate entry is not two events that differ by case.");
        }

        [Test]
        public void AnInvalidPatternWarnsInsteadOfTakingTheRuleDown()
        {
            RuleSet.EventNamingPattern = "^event:/[unclosed";

            LogAssert.ignoreFailingMessages = true;
            try
            {
                var issues = Run(_rule, SnapshotBuilder.New()
                    .Event("event:/UI/Click")
                    .Event("event:/ui/click"));

                Assert.That(issues.Count, Is.EqualTo(1),
                    "The case-collision check must still run when the pattern is unusable.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
