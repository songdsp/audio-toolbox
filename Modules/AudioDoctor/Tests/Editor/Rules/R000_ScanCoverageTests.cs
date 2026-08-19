using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using AudioToolbox.AudioDoctor.Editor.Rules;
using NUnit.Framework;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Tests.Rules
{
    [TestFixture]
    public sealed class R000_ScanCoverageTests
    {
        private RuleSetAsset _ruleSet;

        [SetUp]
        public void SetUp() => _ruleSet = RuleSetAsset.CreateDefault();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_ruleSet);

        [Test]
        public void ReportsEveryScanNoteAsAnInfoIssue()
        {
            var context = new RuleContext(
                SnapshotBuilder.New()
                    .Note("Event name is built from a variable.", "Assets/Player.cs", 42)
                    .Note("Scenes were skipped.")
                    .Build(),
                _ruleSet);

            var issues = new R000_ScanCoverage().Evaluate(context).ToList();

            Assert.That(issues.Count, Is.EqualTo(2));
            Assert.That(issues.All(i => i.Severity == Severity.Info));
            Assert.That(issues[0].PrimaryAssetPath, Is.EqualTo("Assets/Player.cs"));
            Assert.That(issues[0].Line, Is.EqualTo(42));
        }

        [Test]
        public void ReportsNothingWhenTheScanCoveredEverything()
        {
            var context = new RuleContext(SnapshotBuilder.New().Build(), _ruleSet);

            Assert.That(new R000_ScanCoverage().Evaluate(context), Is.Empty);
        }
    }
}
