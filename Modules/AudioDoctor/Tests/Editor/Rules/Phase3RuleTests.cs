using System.Linq;
using AudioToolbox.AudioDoctor.Core;
using AudioToolbox.AudioDoctor.Editor.Rules;
using NUnit.Framework;

namespace AudioToolbox.AudioDoctor.Tests.Rules
{
    [TestFixture]
    internal sealed class R003_BankLoadGapTests : RuleTestBase
    {
        private readonly R003_BankLoadGap _rule = new R003_BankLoadGap();

        private static SnapshotBuilder Project() => SnapshotBuilder.New()
            .WithCapabilities(BackendCapability.BankMembership | BackendCapability.BankLoadInfo);

        [Test]
        public void ReportsABankThatIsUsedButNeverLoaded()
        {
            var issues = Run(_rule, Project()
                .Event("event:/Ambience/Wind", banks: new[] { "Ambience" })
                .Reference("event:/Ambience/Wind", "Assets/Scenes/Level.unity"));

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo(Severity.Error));
            Assert.That(issues[0].Message, Does.Contain("Ambience"));
            Assert.That(issues[0].PrimaryAssetPath, Is.EqualTo("Assets/Scenes/Level.unity"));
        }

        [Test]
        public void SaysNothingWhenALoaderComponentLoadsTheBank()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/Weather/Rain", banks: new[] { "Weather" })
                    .Reference("event:/Weather/Rain", "Assets/Scenes/Level.unity")
                    .BankLoad("Weather", "Assets/Scenes/Level.unity"),
                "A StudioBankLoader in the scene loads it.");
        }

        [Test]
        public void SaysNothingWhenTheMiddlewareLoadsItAutomatically()
        {
            // The single most important negative case: FMOD's default configuration
            // loads every bank at startup, and a rule blind to that would report every
            // bank of every scene in a perfectly healthy project.
            AssertSilent(_rule, Project()
                    .Event("event:/UI/Click", banks: new[] { "UI" })
                    .Reference("event:/UI/Click", "Assets/Scenes/Level.unity")
                    .BankLoad("UI", string.Empty, BankLoadSource.SettingsAutoLoad),
                "The middleware's own settings load this bank at startup.");
        }

        [Test]
        public void ACodeLoadAnywhereCountsAsLoaded()
        {
            // A LoadBank call cannot be attributed to a scene by static analysis, and a
            // loader on a runtime-instantiated prefab cannot be seen at all - so any load
            // found anywhere suppresses the finding. Narrower and always right beats
            // broader and sometimes wrong.
            AssertSilent(_rule, Project()
                    .Event("event:/Music/Theme", banks: new[] { "Music" })
                    .Reference("event:/Music/Theme", "Assets/Scenes/Level.unity")
                    .BankLoad("Music", "Assets/Scripts/Boot.cs", BankLoadSource.CodeCall),
                "Something in the project loads the bank, even if not in this scene.");
        }

        [Test]
        public void SaysNothingAboutABankNobodyReferences()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/Unused/Thing", banks: new[] { "Unused" }),
                "An unreferenced bank needs no loading; that is R004's territory, not this rule's.");
        }

        [Test]
        public void IgnoresReferencesToEventsThatDoNotExist()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/UI/Click", banks: new[] { "UI" })
                    .BankLoad("UI", string.Empty, BankLoadSource.SettingsAutoLoad)
                    .Reference("event:/Does/Not/Exist", "Assets/Scenes/Level.unity"),
                "A dangling reference is R001's finding; it names no bank to load.");
        }
    }

    [TestFixture]
    internal sealed class R005_LoadingStrategyTests : RuleTestBase
    {
        private readonly R005_LoadingStrategy _rule = new R005_LoadingStrategy();

        private static SnapshotBuilder Project() => SnapshotBuilder.New()
            .WithCapabilities(BackendCapability.EventLength | BackendCapability.StreamingFlag);

        [Test]
        public void ReportsALongEventThatIsNotStreamed()
        {
            var issues = Run(_rule, Project()
                .Event("event:/Music/Theme", lengthSeconds: 54f, isStreaming: false));

            Assert.That(issues.Single().Message, Does.Contain("not streamed"));
            Assert.That(issues.Single().Severity, Is.EqualTo(Severity.Warning));
        }

        [Test]
        public void ReportsAShortEventThatIsStreamed()
        {
            var issues = Run(_rule, Project()
                .Event("event:/UI/Blip", lengthSeconds: 0.11f, isStreaming: true));

            Assert.That(issues.Single().Message, Does.Contain("is streamed"));
        }

        [Test]
        public void SaysNothingAboutCorrectlyConfiguredAudio()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/Music/Theme", lengthSeconds: 54f, isStreaming: true)
                    .Event("event:/UI/Blip", lengthSeconds: 0.11f, isStreaming: false),
                "A long streamed track and a short resident one are both right.");
        }

        [Test]
        public void SaysNothingInTheBandBetweenTheThresholds()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/Mid/A", lengthSeconds: 9.6f, isStreaming: false)
                    .Event("event:/Mid/B", lengthSeconds: 9.6f, isStreaming: true),
                "Between the two thresholds either strategy is defensible.");
        }

        [Test]
        public void ThresholdsComeFromTheRuleSet()
        {
            RuleSet.LongEventSeconds = 5f;

            var issues = Run(_rule, Project()
                .Event("event:/Music/Theme", lengthSeconds: 9.6f, isStreaming: false));

            Assert.That(issues.Count, Is.EqualTo(1),
                "A 9.6s event passes the default 15s threshold but not a configured 5s one.");
        }

        [Test]
        public void SkipsEventsWhoseLengthOrModeIsUnknown()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/Unknown/A", lengthSeconds: null, isStreaming: false)
                    .Event("event:/Unknown/B", lengthSeconds: 54f, isStreaming: null),
                "Missing data must be skipped, not defaulted and then judged.");
        }
    }

    [TestFixture]
    internal sealed class R006_SpatializationMismatchTests : RuleTestBase
    {
        private readonly R006_SpatializationMismatch _rule = new R006_SpatializationMismatch();

        private static SnapshotBuilder Project() => SnapshotBuilder.New()
            .WithCapabilities(BackendCapability.SpatialFlag);

        [Test]
        public void ReportsA3DEventPlayedWithNoPosition()
        {
            var issues = Run(_rule, Project()
                .Event("event:/SFX/Footsteps", is3D: true)
                .Reference("event:/SFX/Footsteps", "Assets/Scripts/Player.cs",
                    RefSource.CodeLiteral, line: 12, spatialized: false));

            Assert.That(issues.Single().Line, Is.EqualTo(12));
            Assert.That(issues.Single().Message, Does.Contain("3D"));
        }

        [Test]
        public void SaysNothingWhenThePositionIsGiven()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/SFX/Footsteps", is3D: true)
                    .Reference("event:/SFX/Footsteps", spatialized: true),
                "The call site carries a position.");
        }

        [Test]
        public void SaysNothingAboutA2DEventPlayedFromAPositionedCall()
        {
            // Every emitter is positioned by definition, so reporting this direction
            // would fire on every correctly-wired UI sound in the project.
            AssertSilent(_rule, Project()
                    .Event("event:/UI/Click", is3D: false)
                    .Reference("event:/UI/Click", spatialized: true),
                "2D audio on a positioned call site is how 2D audio is normally wired.");
        }

        [Test]
        public void SaysNothingWhenTheScannerCouldNotTell()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/SFX/Footsteps", is3D: true)
                    .Reference("event:/SFX/Footsteps", spatialized: null),
                "Unknown must not be read as 'no position'.");
        }

        [Test]
        public void SaysNothingWhenTheEventDoesNotExist()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/SFX/Footsteps", is3D: true)
                    .Reference("event:/SFX/Typo", spatialized: false),
                "A dangling reference is R001's finding.");
        }
    }

    [TestFixture]
    internal sealed class R007_UnknownParameterTests : RuleTestBase
    {
        private readonly R007_UnknownParameter _rule = new R007_UnknownParameter();

        private static SnapshotBuilder Project() => SnapshotBuilder.New()
            .WithCapabilities(BackendCapability.Parameters | BackendCapability.GlobalParameters);

        [Test]
        public void ReportsAParameterTheEventDoesNotDeclare()
        {
            var issues = Run(_rule, Project()
                .Event("event:/Music/Theme", parameters: new[] { "Intensity" })
                .Parameter("event:/Music/Theme", "Intensty", "Assets/Scripts/Music.cs", line: 22));

            Assert.That(issues.Single().Severity, Is.EqualTo(Severity.Error));
            Assert.That(issues.Single().Line, Is.EqualTo(22));
            Assert.That(issues.Single().Detail, Does.Contain("Intensity"),
                "The finding must name the parameters that do exist.");
        }

        [Test]
        public void SaysNothingWhenTheParameterExists()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/Music/Theme", parameters: new[] { "Intensity" })
                    .Parameter("event:/Music/Theme", "Intensity"),
                "The parameter is declared on the event.");
        }

        [Test]
        public void PointsOutAParameterThatDiffersOnlyByCase()
        {
            var issues = Run(_rule, Project()
                .Event("event:/Music/Theme", parameters: new[] { "Intensity" })
                .Parameter("event:/Music/Theme", "intensity"));

            Assert.That(issues.Single().Detail, Does.Contain("differs only by letter case"));
        }

        [Test]
        public void AGlobalParameterSatisfiesAnEventScopedCall()
        {
            AssertSilent(_rule, Project()
                    .Event("event:/Music/Theme")
                    .GlobalParameter("TimeOfDay")
                    .Parameter("event:/Music/Theme", "TimeOfDay"),
                "Global parameters are settable through an event instance too.");
        }

        [Test]
        public void ReportsAnUndeclaredGlobalParameter()
        {
            var issues = Run(_rule, Project()
                .GlobalParameter("TimeOfDay")
                .Parameter(null, "Weather", isGlobal: true));

            Assert.That(issues.Single().Message, Does.Contain("Global parameter"));
        }

        [Test]
        public void SaysNothingWhenTheEventItselfIsMissing()
        {
            // R001 already reports the missing event; a second error on the same cause
            // would just make the real one harder to find.
            AssertSilent(_rule, Project()
                    .Event("event:/Music/Theme", parameters: new[] { "Intensity" })
                    .Parameter("event:/Music/Typo", "Intensity"),
                "The event is missing, which is R001's finding, not this rule's.");
        }
    }

    [TestFixture]
    internal sealed class R009_CrossPlatformBanksTests : RuleTestBase
    {
        private readonly R009_CrossPlatformBanks _rule = new R009_CrossPlatformBanks();

        private static SnapshotBuilder Project() => SnapshotBuilder.New()
            .WithCapabilities(BackendCapability.PlatformBanks);

        [Test]
        public void ReportsABankMissingFromOnePlatform()
        {
            var issues = Run(_rule, Project()
                .Bank("UI", "Desktop").Bank("UI", "Mobile")
                .Bank("Music", "Desktop"));

            Assert.That(issues.Single().Message, Does.Contain("Music"));
            Assert.That(issues.Single().Message, Does.Contain("Mobile"));
        }

        [Test]
        public void SaysNothingWhenEveryBankCoversEveryPlatform()
        {
            AssertSilent(_rule, Project()
                    .Bank("UI", "Desktop").Bank("UI", "Mobile")
                    .Bank("Music", "Desktop").Bank("Music", "Mobile"),
                "Every bank is built for both platforms.");
        }

        [Test]
        public void SaysNothingOnASinglePlatformProject()
        {
            // With one platform there is nothing to be inconsistent with, and inventing
            // an expectation would report every bank of every desktop-only project.
            AssertSilent(_rule, Project()
                    .Bank("UI", "Desktop").Bank("Music", "Desktop"),
                "A single-platform project cannot be cross-platform inconsistent.");
        }

        [Test]
        public void RequiredPlatformsFromTheRuleSetOverrideWhatWasFound()
        {
            RuleSet.RequiredPlatforms.Add("Desktop");
            RuleSet.RequiredPlatforms.Add("iOS");

            var issues = Run(_rule, Project().Bank("UI", "Desktop").Bank("Music", "Desktop"));

            Assert.That(issues.Count, Is.EqualTo(2),
                "Both banks are missing iOS once the project states it expects iOS.");
        }

        [Test]
        public void ReportsBankNamesThatDifferOnlyByCase()
        {
            var issues = Run(_rule, Project()
                .Bank("Ambience", "Desktop")
                .Bank("ambience", "Desktop"));

            Assert.That(issues.Single().Message, Does.Contain("differ only by letter case"));
            Assert.That(issues.Single().Detail, Does.Contain("Linux"));
        }

        [Test]
        public void ReportsABankThatWasNeverBuilt()
        {
            var issues = Run(_rule, Project().Bank("Ghost", "(not built)"));

            Assert.That(issues.Single().Message, Does.Contain("never been built"));
        }

        [Test]
        public void SizeDeviationIsOffUntilItIsConfigured()
        {
            AssertSilent(_rule, Project()
                    .Bank("Music", "Desktop", 1_000_000)
                    .Bank("Music", "Mobile", 100_000),
                "Platforms legitimately use different encodings, so this check is opt-in.");
        }

        [Test]
        public void SizeDeviationIsReportedOnceConfigured()
        {
            RuleSet.BankSizeDeviationRatio = 0.5f;

            var issues = Run(_rule, Project()
                .Bank("Music", "Desktop", 1_000_000)
                .Bank("Music", "Mobile", 100_000));

            Assert.That(issues.Count, Is.EqualTo(2), "Both sides deviate from the median.");
            Assert.That(issues.All(i => i.Message.Contains("deviates")));
        }
    }
}
