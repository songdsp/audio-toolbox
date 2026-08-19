using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AudioToolbox.AudioDoctor.Tests
{
    /// <summary>
    /// Covers the engine's contract with rules: gating, severity resolution and
    /// isolation from a rule that throws.
    /// </summary>
    [TestFixture]
    public sealed class RuleEngineTests
    {
        private RuleSetAsset _ruleSet;

        [SetUp]
        public void SetUp() => _ruleSet = RuleSetAsset.CreateDefault();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_ruleSet);

        [Test]
        public void SkipsRuleWhoseCapabilitiesTheBackendCannotSupply()
        {
            var context = Context(SnapshotBuilder.New().WithCapabilities(BackendCapability.None).Build());

            RuleEngine.Run(context, new IValidationRule[] { new NeedsLengthRule() }, out var issues, out var skipped);

            Assert.That(issues, Is.Empty, "A rule without its data must report nothing rather than guess.");
            Assert.That(skipped.Select(s => s.RuleId), Is.EquivalentTo(new[] { "RTEST_LEN" }));
            Assert.That(skipped[0].Reason, Does.Contain("EventLength"));
        }

        [Test]
        public void RunsRuleWhenTheBackendSuppliesItsCapabilities()
        {
            var context = Context(SnapshotBuilder.New().WithCapabilities(BackendCapability.EventLength).Build());

            RuleEngine.Run(context, new IValidationRule[] { new NeedsLengthRule() }, out var issues, out var skipped);

            Assert.That(skipped, Is.Empty);
            Assert.That(issues.Select(i => i.RuleId), Is.EquivalentTo(new[] { "RTEST_LEN" }));
        }

        [Test]
        public void DisabledRuleIsSkippedAndSaysSo()
        {
            _ruleSet.Rules.Add(new RuleSetting { RuleId = "RTEST_LEN", Enabled = false });
            var context = Context(SnapshotBuilder.New().WithCapabilities(BackendCapability.EventLength).Build());

            RuleEngine.Run(context, new IValidationRule[] { new NeedsLengthRule() }, out var issues, out var skipped);

            Assert.That(issues, Is.Empty);
            Assert.That(skipped[0].Reason, Does.Contain("Disabled"));
        }

        [Test]
        public void SeverityOverrideFromTheRuleSetWins()
        {
            _ruleSet.Rules.Add(new RuleSetting
            {
                RuleId = "RTEST_LEN",
                Enabled = true,
                OverrideSeverity = true,
                Severity = Severity.Error,
            });

            var context = Context(SnapshotBuilder.New().WithCapabilities(BackendCapability.EventLength).Build());

            RuleEngine.Run(context, new IValidationRule[] { new NeedsLengthRule() }, out var issues, out _);

            Assert.That(issues.Single().Severity, Is.EqualTo(Severity.Error),
                "The rule's default is Warning; the asset must be able to raise it.");
        }

        [Test]
        public void OneThrowingRuleDoesNotStopTheOthers()
        {
            var context = Context(SnapshotBuilder.New().WithCapabilities(BackendCapability.EventLength).Build());

            LogAssert.ignoreFailingMessages = true;
            try
            {
                RuleEngine.Run(
                    context,
                    new IValidationRule[] { new ThrowingRule(), new NeedsLengthRule() },
                    out var issues,
                    out var skipped);

                Assert.That(issues.Select(i => i.RuleId), Is.EquivalentTo(new[] { "RTEST_LEN" }));
                Assert.That(skipped.Single().RuleId, Is.EqualTo("RTEST_THROW"));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void IssuesAreSortedWorstFirst()
        {
            var unsorted = new[]
            {
                new ValidationIssue { RuleId = "R002", Severity = Severity.Info, Message = "c" },
                new ValidationIssue { RuleId = "R001", Severity = Severity.Error, Message = "a" },
                new ValidationIssue { RuleId = "R003", Severity = Severity.Warning, Message = "b" },
            };

            var sorted = RuleEngine.Sort(unsorted);

            Assert.That(
                sorted.Select(i => i.Severity),
                Is.EqualTo(new[] { Severity.Error, Severity.Warning, Severity.Info }));
        }

        [Test]
        public void DiscoveryDoesNotPickUpTestDoubles()
        {
            // Regression: the first end-to-end run reported RTEST_LEN and RTEST_THROW
            // in a real scan, because test assemblies are compiled into the editor and
            // TypeCache does not care which assembly a type came from.
            var discovered = RuleEngine.DiscoverRules().Select(r => r.RuleId).ToList();

            Assert.That(discovered, Has.None.StartsWith("RTEST"),
                "Rules defined in a test assembly must never appear in a production scan.");
            Assert.That(discovered, Does.Contain("R000"),
                "Real rules must still be discovered.");
        }

        private RuleContext Context(AudioProjectSnapshot snapshot) => new RuleContext(snapshot, _ruleSet);

        private sealed class NeedsLengthRule : IValidationRule
        {
            public string RuleId => "RTEST_LEN";
            public string Title => "Needs event length";
            public Severity DefaultSeverity => Severity.Warning;
            public BackendCapability RequiredCapabilities => BackendCapability.EventLength;

            public IEnumerable<ValidationIssue> Evaluate(RuleContext context)
            {
                yield return context.Issue(this, "ran", "Assets/Anything.asset", "detail");
            }
        }

        private sealed class ThrowingRule : IValidationRule
        {
            public string RuleId => "RTEST_THROW";
            public string Title => "Always throws";
            public Severity DefaultSeverity => Severity.Error;
            public BackendCapability RequiredCapabilities => BackendCapability.None;

            public IEnumerable<ValidationIssue> Evaluate(RuleContext context) =>
                throw new System.InvalidOperationException("deliberate");
        }
    }
}
