#if AUDIOTOOLBOX_TRACE

using AudioToolbox.EventTracer.Recording;
using NUnit.Framework;

namespace AudioToolbox.EventTracer.Tests
{
    [TestFixture]
    public sealed class TraceRingBufferTests
    {
        private static AudioTraceRecord RecordWithFrame(long frame) => new AudioTraceRecord { Frame = frame };

        [Test]
        public void CapacityIsRoundedUpToAPowerOfTwo()
        {
            // Sequences are masked rather than divided, so the caller's number is a
            // minimum, not a promise. Reporting the rounded figure is what lets a reader
            // tell "the buffer was full" from "that is all there was".
            Assert.That(new TraceRingBuffer(100).Capacity, Is.EqualTo(128));
        }

        [Test]
        public void SequencesCountFromZeroAndDoNotRepeat()
        {
            var buffer = new TraceRingBuffer(4);

            Assert.That(buffer.Append(RecordWithFrame(0)), Is.EqualTo(0));
            Assert.That(buffer.Append(RecordWithFrame(1)), Is.EqualTo(1));
            Assert.That(buffer.Append(RecordWithFrame(2)), Is.EqualTo(2));
        }

        [Test]
        public void FillingItExactly_DropsNothing()
        {
            var buffer = new TraceRingBuffer(4);

            for (var i = 0; i < 4; i++)
            {
                buffer.Append(RecordWithFrame(i));
            }

            Assert.That(buffer.DroppedCount, Is.Zero);
            Assert.That(buffer.PendingCount, Is.EqualTo(4));
        }

        [Test]
        public void OverflowingKeepsTheNewestAndCountsWhatItLost()
        {
            var buffer = new TraceRingBuffer(4);

            for (var i = 0; i < 7; i++)
            {
                buffer.Append(RecordWithFrame(i));
            }

            Assert.That(buffer.DroppedCount, Is.EqualTo(3));

            // The three oldest are gone; asking for them says so rather than handing
            // back whatever is in the slot now.
            Assert.That(buffer.TryGet(0, out _), Is.False);
            Assert.That(buffer.TryGet(2, out _), Is.False);

            Assert.That(buffer.TryGet(3, out var oldestSurvivor), Is.True);
            Assert.That(oldestSurvivor.Frame, Is.EqualTo(3));

            Assert.That(buffer.TryGet(6, out var newest), Is.True);
            Assert.That(newest.Frame, Is.EqualTo(6));
        }

        [Test]
        public void PatchingAResidentRecord_Works()
        {
            var buffer = new TraceRingBuffer(4);
            var sequence = buffer.Append(RecordWithFrame(1));

            Assert.That(buffer.TryPatchOutcome(sequence, PlaybackOutcome.Stolen, 17), Is.True);
            Assert.That(buffer.TryGet(sequence, out var patched), Is.True);
            Assert.That(patched.Outcome, Is.EqualTo(PlaybackOutcome.Stolen));
            Assert.That(patched.BackendResultCode, Is.EqualTo(17));
        }

        [Test]
        public void PatchingARecordThatScrolledAway_FailsRatherThanCorruptingItsSlot()
        {
            // The scenario this whole sequence-number design exists for: a sound outlives
            // the buffer, and its late callback must not rewrite whoever took its slot.
            var buffer = new TraceRingBuffer(4);
            var doomed = buffer.Append(RecordWithFrame(0));

            for (var i = 1; i <= 5; i++)
            {
                buffer.Append(RecordWithFrame(i));
            }

            Assert.That(buffer.TryPatchOutcome(doomed, PlaybackOutcome.Stolen, 0), Is.False);

            foreach (var sequence in new long[] { 2, 3, 4, 5 })
            {
                Assert.That(buffer.TryGet(sequence, out var survivor), Is.True);
                Assert.That(survivor.Outcome, Is.EqualTo(PlaybackOutcome.NotCalled), $"sequence {sequence} was overwritten");
            }
        }

        [Test]
        public void PatchingDoesNotEraseAnEarlierErrorCodeWithASuccess()
        {
            var buffer = new TraceRingBuffer(4);
            var sequence = buffer.Append(new AudioTraceRecord { BackendResultCode = 42 });

            buffer.TryPatchOutcome(sequence, PlaybackOutcome.Started, 0);

            buffer.TryGet(sequence, out var record);
            Assert.That(record.BackendResultCode, Is.EqualTo(42));
        }

        [Test]
        public void DrainTakesEverythingBelowTheBarrierAndNoMore()
        {
            var buffer = new TraceRingBuffer(8);

            for (var i = 0; i < 6; i++)
            {
                buffer.Append(RecordWithFrame(i));
            }

            var destination = new AudioTraceRecord[8];

            // Sequence 4 is still live, so nothing from 4 onwards may be written: it
            // could still be patched.
            Assert.That(buffer.Drain(4, destination), Is.EqualTo(4));
            Assert.That(destination[0].Frame, Is.EqualTo(0));
            Assert.That(destination[3].Frame, Is.EqualTo(3));

            Assert.That(buffer.Drain(6, destination), Is.EqualTo(2));
            Assert.That(destination[0].Frame, Is.EqualTo(4));

            Assert.That(buffer.Drain(6, destination), Is.Zero);
        }

        [Test]
        public void DrainedRecordsAreNotCountedAsDroppedWhenOverwritten()
        {
            // Anything already on disk may be overwritten freely. Counting it as lost
            // would make every long session claim to be incomplete.
            var buffer = new TraceRingBuffer(4);
            var destination = new AudioTraceRecord[4];

            for (var i = 0; i < 4; i++)
            {
                buffer.Append(RecordWithFrame(i));
            }

            buffer.Drain(4, destination);

            for (var i = 4; i < 8; i++)
            {
                buffer.Append(RecordWithFrame(i));
            }

            Assert.That(buffer.DroppedCount, Is.Zero);
        }

        [Test]
        public void DrainStopsAtTheDestinationSize()
        {
            var buffer = new TraceRingBuffer(16);

            for (var i = 0; i < 16; i++)
            {
                buffer.Append(RecordWithFrame(i));
            }

            var small = new AudioTraceRecord[4];

            Assert.That(buffer.Drain(16, small), Is.EqualTo(4));
            Assert.That(buffer.Drain(16, small), Is.EqualTo(4));
            Assert.That(small[0].Frame, Is.EqualTo(4));
        }
    }
}

#endif
