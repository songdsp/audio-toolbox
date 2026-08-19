using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using NUnit.Framework;
using UnityEngine;

namespace AudioToolbox.AudioDoctor.Tests
{
    /// <summary>Shared plumbing for the rule unit tests.</summary>
    internal abstract class RuleTestBase
    {
        protected RuleSetAsset RuleSet { get; private set; }

        [SetUp]
        public void CreateRuleSet() => RuleSet = RuleSetAsset.CreateDefault();

        [TearDown]
        public void DestroyRuleSet() => Object.DestroyImmediate(RuleSet);

        protected List<ValidationIssue> Run(IValidationRule rule, SnapshotBuilder builder) =>
            rule.Evaluate(new RuleContext(builder.Build(), RuleSet)).ToList();

        /// <summary>Asserts the rule found nothing - the false-positive half of every rule's contract.</summary>
        protected void AssertSilent(IValidationRule rule, SnapshotBuilder builder, string because)
        {
            var issues = Run(rule, builder);

            Assert.That(issues, Is.Empty,
                $"{because}\nBut it reported: {string.Join("; ", issues.Select(i => i.Message))}");
        }
    }
}
