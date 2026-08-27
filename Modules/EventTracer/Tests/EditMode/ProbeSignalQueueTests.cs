using System.Threading.Tasks;
using NUnit.Framework;

namespace AudioToolbox.EventTracer.Tests
{
    [TestFixture]
    public sealed class ProbeSignalQueueTests
    {
        [Test]
        public void CapacityIsRoundedUpToAPowerOfTwo()
        {
            Assert.That(new ProbeSignalQueue(100).Capacity, Is.EqualTo(128));
        }

        [Test]
        public void SignalsComeBackInTheOrderTheyWentIn()
        {
            var queue = new ProbeSignalQueue(8);

            queue.TryEnqueue(1, ProbeSignal.CreateOk, 0, 100);
            queue.TryEnqueue(1, ProbeSignal.Started, 0, 200);

            Assert.That(queue.TryDequeue(out var first), Is.True);
            Assert.That(first.Signal, Is.EqualTo(ProbeSignal.CreateOk));

            Assert.That(queue.TryDequeue(out var second), Is.True);
            Assert.That(second.Signal, Is.EqualTo(ProbeSignal.Started));
            Assert.That(second.Timestamp, Is.EqualTo(200));

            Assert.That(queue.TryDequeue(out _), Is.False);
        }

        [Test]
        public void OnceFull_TheNewestIsDroppedAndCounted()
        {
            // The early signals of a voice are what an outcome is built from; a late one
            // usually only refines it. So the queue keeps what it has rather than making
            // room.
            var queue = new ProbeSignalQueue(2);

            Assert.That(queue.TryEnqueue(0, ProbeSignal.CreateOk, 0, 1), Is.True);
            Assert.That(queue.TryEnqueue(0, ProbeSignal.Started, 0, 2), Is.True);
            Assert.That(queue.TryEnqueue(0, ProbeSignal.Stopped, 0, 3), Is.False);

            Assert.That(queue.DroppedCount, Is.EqualTo(1));

            queue.TryDequeue(out var kept);
            Assert.That(kept.Signal, Is.EqualTo(ProbeSignal.CreateOk));
        }

        [Test]
        public void DrainingMakesRoomAgain()
        {
            var queue = new ProbeSignalQueue(2);

            queue.TryEnqueue(0, ProbeSignal.CreateOk, 0, 1);
            queue.TryEnqueue(0, ProbeSignal.Started, 0, 2);
            queue.TryDequeue(out _);

            Assert.That(queue.TryEnqueue(0, ProbeSignal.Stopped, 0, 3), Is.True);
        }

        [Test]
        public void ManyProducersLoseNothingWhileThereIsRoom()
        {
            // FMOD dispatches from its own update thread and Unity's audio from another;
            // a queue that lost signals under contention would produce outcomes that are
            // wrong rather than merely incomplete.
            const int producers = 4;
            const int perProducer = 500;

            var queue = new ProbeSignalQueue(producers * perProducer * 2);
            var tasks = new Task[producers];

            for (var p = 0; p < producers; p++)
            {
                var voiceId = p;

                tasks[p] = Task.Run(() =>
                {
                    for (var i = 0; i < perProducer; i++)
                    {
                        queue.TryEnqueue(voiceId, ProbeSignal.Started, i, i);
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.That(queue.DroppedCount, Is.Zero);

            var counts = new int[producers];
            var total = 0;

            // A claimed-but-unfinished slot reads as empty, so a single pass can stop
            // early. Retry while any producer might still be mid-write.
            for (var attempt = 0; attempt < 100 && total < producers * perProducer; attempt++)
            {
                while (queue.TryDequeue(out var signal))
                {
                    counts[signal.VoiceId]++;
                    total++;
                }
            }

            Assert.That(total, Is.EqualTo(producers * perProducer));

            foreach (var count in counts)
            {
                Assert.That(count, Is.EqualTo(perProducer));
            }
        }
    }
}
