using System;
using System.Collections.Generic;
using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor;
using NUnit.Framework;

namespace AudioToolbox.AudioDoctor.Tests
{
    /// <summary>
    /// Covers what the scanner says about its own coverage, which is the part that
    /// decides whether a clean report can be trusted.
    /// </summary>
    [TestFixture]
    public sealed class ProjectScannerTests
    {
        [Test]
        public void SaysSoWhenItFoundEventsButNoReferencesAtAll()
        {
            // Regression: a project with banks built but nothing wired up yet reported
            // "No issues found" with zero skipped rules. Every reconciling rule had
            // silently bailed for lack of anything to reconcile, so the report read as
            // a clean bill of health for checks that never ran.
            var snapshot = ProjectScanner.Scan(
                new StubBackend(events: new[] { "event:/UI/Click" }, references: Array.Empty<string>()),
                ScanContext.Silent);

            Assert.That(snapshot.Notes.Select(n => n.Message),
                Has.Some.Contains("nothing was checked"));
        }

        [Test]
        public void StaysQuietWhenReferencesWereFound()
        {
            var snapshot = ProjectScanner.Scan(
                new StubBackend(
                    events: new[] { "event:/UI/Click" },
                    references: new[] { "event:/UI/Click" }),
                ScanContext.Silent);

            Assert.That(snapshot.Notes, Is.Empty);
        }

        [Test]
        public void StaysQuietWhenThereWasNothingAuthoredEither()
        {
            // No events and no references is an unconfigured backend, which reports its
            // own reason. A second note about reconciliation would just be noise.
            var snapshot = ProjectScanner.Scan(
                new StubBackend(events: Array.Empty<string>(), references: Array.Empty<string>()),
                ScanContext.Silent);

            Assert.That(snapshot.Notes, Is.Empty);
        }

        [Test]
        public void AnUnavailableBackendReportsItsReasonInsteadOfThrowing()
        {
            var snapshot = ProjectScanner.Scan(new UnavailableBackend(), ScanContext.Silent);

            Assert.That(snapshot.Events, Is.Empty);
            Assert.That(snapshot.Notes.Single().Message, Does.Contain("banks were never built"));
            Assert.That(snapshot.Capabilities, Is.EqualTo(BackendCapability.None),
                "An unusable backend must not claim capabilities, or rules would run on empty data.");
        }

        private sealed class StubBackend : IAudioProjectSource
        {
            private readonly string[] _events;
            private readonly string[] _references;

            public StubBackend(string[] events, string[] references)
            {
                _events = events;
                _references = references;
            }

            public string BackendId => "stub";
            public string DisplayName => "Stub";
            public int Priority => -1;
            public bool IsAvailable => true;
            public string GetUnavailableReason() => string.Empty;
            public BackendCapability Capabilities => BackendCapability.BankMembership;

            public IReadOnlyList<EventDef> GetAuthoredEvents(ScanContext context) =>
                _events.Select(k => new EventDef { Key = k }).ToList();

            public IReadOnlyList<BankDef> GetBanks(ScanContext context) => Array.Empty<BankDef>();

            public IReadOnlyList<string> GetGlobalParameters(ScanContext context) => Array.Empty<string>();

            public void FindReferences(ScanContext context, ReferenceSink sink)
            {
                foreach (var key in _references)
                {
                    sink.Add(new EventRefUsage { EventKey = key, AssetPath = "Assets/Stub.prefab" });
                }
            }
        }

        private sealed class UnavailableBackend : IAudioProjectSource
        {
            public string BackendId => "unavailable";
            public string DisplayName => "Unavailable";
            public int Priority => -1;
            public bool IsAvailable => false;
            public string GetUnavailableReason() => "its banks were never built.";
            public BackendCapability Capabilities => BackendCapability.BankMembership;

            public IReadOnlyList<EventDef> GetAuthoredEvents(ScanContext context) =>
                throw new InvalidOperationException("must not be called");

            public IReadOnlyList<BankDef> GetBanks(ScanContext context) =>
                throw new InvalidOperationException("must not be called");

            public IReadOnlyList<string> GetGlobalParameters(ScanContext context) =>
                throw new InvalidOperationException("must not be called");

            public void FindReferences(ScanContext context, ReferenceSink sink) =>
                throw new InvalidOperationException("must not be called");
        }
    }
}
