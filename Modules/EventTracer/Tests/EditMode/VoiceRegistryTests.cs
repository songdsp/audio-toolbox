using NUnit.Framework;

namespace AudioToolbox.EventTracer.Tests
{
    [TestFixture]
    public sealed class VoiceRegistryTests
    {
        [Test]
        public void SlotsAreHandedOutUntilTheyRunOut()
        {
            var registry = new VoiceRegistry(2);

            Assert.That(registry.TryAcquire(out _, out _), Is.True);
            Assert.That(registry.TryAcquire(out _, out _), Is.True);
            Assert.That(registry.TryAcquire(out _, out _), Is.False);
            Assert.That(registry.StarvedCount, Is.EqualTo(1));
        }

        [Test]
        public void ReleasedSlotsComeBack()
        {
            var registry = new VoiceRegistry(1);

            registry.TryAcquire(out var voiceId, out _);
            Assert.That(registry.TryAcquire(out _, out _), Is.False);

            registry.Release(voiceId);
            Assert.That(registry.TryAcquire(out _, out _), Is.True);
        }

        [Test]
        public void AHandleToAFinishedSoundStopsMatching()
        {
            // The reason a handle carries a generation. Without it, a handle held past
            // the end of its sound would address whatever took the slot next, and
            // stopping it would silence a stranger.
            var registry = new VoiceRegistry(1);

            registry.TryAcquire(out var voiceId, out var generation);
            Assert.That(registry.IsAlive(voiceId, generation), Is.True);

            registry.Release(voiceId);
            Assert.That(registry.IsAlive(voiceId, generation), Is.False);

            registry.TryAcquire(out var reusedId, out var newGeneration);

            Assert.That(reusedId, Is.EqualTo(voiceId), "the slot should be reused");
            Assert.That(newGeneration, Is.Not.EqualTo(generation));
            Assert.That(registry.IsAlive(voiceId, generation), Is.False, "the old handle matched the new sound");
        }

        [Test]
        public void ReleasingTwiceIsNotAnError()
        {
            // Both a Stopped and a Destroyed can retire the same voice, and which arrives
            // is up to the middleware. Neither should corrupt the free list.
            var registry = new VoiceRegistry(2);

            registry.TryAcquire(out var voiceId, out _);

            Assert.That(registry.Release(voiceId), Is.True);
            Assert.That(registry.Release(voiceId), Is.False);
            Assert.That(registry.ActiveCount, Is.Zero);

            registry.TryAcquire(out _, out _);
            registry.TryAcquire(out _, out _);
            Assert.That(registry.TryAcquire(out _, out _), Is.False, "a slot was handed out twice");
        }

        [Test]
        public void AnOutOfRangeReleaseIsIgnored()
        {
            var registry = new VoiceRegistry(2);

            Assert.That(registry.Release(-1), Is.False);
            Assert.That(registry.Release(99), Is.False);
        }

        [Test]
        public void AnInvalidHandleIsNeverAlive()
        {
            var registry = new VoiceRegistry(2);
            Assert.That(registry.IsAlive(-1, 0), Is.False);
        }
    }
}
