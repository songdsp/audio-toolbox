using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using NUnit.Framework;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Tests
{
    [TestFixture]
    public sealed class RuleContextTests
    {
        private RuleSetAsset _ruleSet;

        [SetUp]
        public void SetUp() => _ruleSet = RuleSetAsset.CreateDefault();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_ruleSet);

        [Test]
        public void IndexesEventsByKey()
        {
            var context = new RuleContext(
                SnapshotBuilder.New()
                    .Event("event:/UI/Click")
                    .Event("event:/Music/Menu")
                    .Build(),
                _ruleSet);

            Assert.That(context.EventsByKey.Keys, Is.EquivalentTo(new[] { "event:/UI/Click", "event:/Music/Menu" }));
        }

        [Test]
        public void EventLookupIsCaseSensitive()
        {
            var context = new RuleContext(
                SnapshotBuilder.New().Event("event:/UI/Click").Build(),
                _ruleSet);

            Assert.That(context.EventsByKey.ContainsKey("event:/ui/click"), Is.False,
                "Folding case here would hide exactly the mismatch R009 exists to catch.");
        }

        [Test]
        public void PackedEventKeysUnionsEveryBank()
        {
            var context = new RuleContext(
                SnapshotBuilder.New()
                    .Bank("Music", "Desktop", 100, "event:/Music/Menu")
                    .Bank("Music", "iOS", 90, "event:/Music/Menu")
                    .Bank("SFX", "Desktop", 50, "event:/UI/Click")
                    .Build(),
                _ruleSet);

            Assert.That(context.PackedEventKeys, Is.EquivalentTo(new[] { "event:/Music/Menu", "event:/UI/Click" }));
        }

        [Test]
        public void BanksAreGroupedByNameAcrossPlatforms()
        {
            var context = new RuleContext(
                SnapshotBuilder.New()
                    .Bank("Music", "Desktop")
                    .Bank("Music", "iOS")
                    .Bank("SFX", "Desktop")
                    .Build(),
                _ruleSet);

            Assert.That(context.BanksByName["Music"].Select(b => b.Platform),
                Is.EquivalentTo(new[] { "Desktop", "iOS" }));
            Assert.That(context.BanksByName["SFX"].Count(), Is.EqualTo(1));
        }

        [Test]
        public void DuplicateEventKeysKeepTheFirstRatherThanThrowing()
        {
            // A middleware project can end up with two events resolving to the same
            // key. Indexing must survive it; R008 is the rule that reports it.
            var context = new RuleContext(
                SnapshotBuilder.New()
                    .Event("event:/UI/Click", banks: new[] { "First" })
                    .Event("event:/UI/Click", banks: new[] { "Second" })
                    .Build(),
                _ruleSet);

            Assert.That(context.EventsByKey["event:/UI/Click"].BankNames, Is.EquivalentTo(new[] { "First" }));
        }

        [Test]
        public void ReferencesAreGroupedByEventKey()
        {
            var context = new RuleContext(
                SnapshotBuilder.New()
                    .Reference("event:/UI/Click", "Assets/A.prefab")
                    .Reference("event:/UI/Click", "Assets/B.prefab")
                    .Reference("event:/Music/Menu", "Assets/C.prefab")
                    .Build(),
                _ruleSet);

            Assert.That(context.ReferencesByKey["event:/UI/Click"].Count(), Is.EqualTo(2));
            Assert.That(context.ReferencesByKey["event:/Nothing"], Is.Empty);
        }
    }

    [TestFixture]
    public sealed class EventKeyComparerTests
    {
        [Test]
        public void ExactMatchIsOrdinal()
        {
            Assert.That(EventKeyComparer.Equals("event:/A", "event:/A"), Is.True);
            Assert.That(EventKeyComparer.Equals("event:/A", "event:/a"), Is.False);
        }

        [Test]
        public void DetectsKeysThatDifferOnlyByCase()
        {
            Assert.That(EventKeyComparer.DiffersOnlyByCase("event:/UI/Click", "event:/ui/click"), Is.True);
        }

        [Test]
        public void IdenticalKeysDoNotCountAsACaseDifference()
        {
            Assert.That(EventKeyComparer.DiffersOnlyByCase("event:/UI/Click", "event:/UI/Click"), Is.False);
        }

        [Test]
        public void GenuinelyDifferentKeysAreNotACaseDifference()
        {
            Assert.That(EventKeyComparer.DiffersOnlyByCase("event:/UI/Click", "event:/UI/Hover"), Is.False);
        }
    }
}
