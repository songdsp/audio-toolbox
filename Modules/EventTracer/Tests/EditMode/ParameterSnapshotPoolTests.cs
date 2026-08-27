#if AUDIOTOOLBOX_TRACE

using AudioToolbox.EventTracer.Recording;
using NUnit.Framework;

namespace AudioToolbox.EventTracer.Tests
{
    /// <summary>
    /// The other half of a record's context: the state of the world it was posted under.
    /// </summary>
    /// <remarks>
    /// Two claims are being tested, and they pull against each other. The first is that a
    /// snapshot is complete — someone reading a record must be able to reconstruct every
    /// parameter as it stood, not just the ones that happened to change recently. The
    /// second is that it is cheap — a burst of identical posts must not write the same
    /// state over and over. The differential storage is what satisfies both, and these
    /// tests are what stop a future simplification from quietly dropping one of them.
    /// </remarks>
    [TestFixture]
    public sealed class ParameterSnapshotPoolTests
    {
        private StringInternTable _strings;
        private ParameterSnapshotPool _pool;
        private ParameterFlushBuffer _flush;

        [SetUp]
        public void SetUp()
        {
            _strings = new StringInternTable(64);
            _pool = new ParameterSnapshotPool(_strings, maxParameters: 4, pendingSnapshotCapacity: 8);
            _flush = new ParameterFlushBuffer();
        }

        [Test]
        public void WithNothingKnownThereIsNoSnapshotToPointAt()
        {
            // Not snapshot zero, which would claim an empty world was observed. A project
            // with no parameters at all should produce records that say so.
            Assert.That(_pool.Capture(), Is.EqualTo(TraceFormat.NoSnapshotId));
        }

        [Test]
        public void CapturesTakenUnderUnchangedStateShareOneSnapshot()
        {
            // The whole reason this is not one snapshot per post. Forty footsteps in a
            // second happen under one state, and they cost one snapshot between them.
            _pool.Set("Health", 1f);

            var first = _pool.Capture();
            var second = _pool.Capture();
            var third = _pool.Capture();

            Assert.That(first, Is.Not.EqualTo(TraceFormat.NoSnapshotId));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(third, Is.EqualTo(first));

            _pool.Drain(_flush);
            Assert.That(_flush.Snapshots.Count, Is.EqualTo(1));
        }

        [Test]
        public void AChangedValueMakesANewSnapshotCarryingOnlyWhatChanged()
        {
            _pool.Set("Health", 1f);
            _pool.Set("Tension", 0f);
            var before = _pool.Capture();

            _pool.Set("Tension", 0.75f);
            var after = _pool.Capture();

            Assert.That(after, Is.Not.EqualTo(before));

            _pool.Drain(_flush);

            Assert.That(_flush.Snapshots.Count, Is.EqualTo(2));
            Assert.That(_flush.Snapshots[0].Count, Is.EqualTo(2), "the first snapshot should carry both parameters");
            Assert.That(_flush.Snapshots[1].Count, Is.EqualTo(1), "the second should carry only the one that moved");
            Assert.That(_flush.Snapshots[1].ParentId, Is.EqualTo(before), "the chain back to the full state is broken");
        }

        [Test]
        public void SettingTheSameValueAgainIsNotAChange()
        {
            _pool.Set("Health", 1f);
            var first = _pool.Capture();

            _pool.Set("Health", 1f);

            Assert.That(_pool.Capture(), Is.EqualTo(first));
        }

        [Test]
        public void AValueThatMovesAwayAndBackWithinOneCaptureCountsAsUnchanged()
        {
            _pool.Set("Tension", 0f);
            var first = _pool.Capture();

            _pool.Set("Tension", 1f);
            _pool.Set("Tension", 0f);

            Assert.That(_pool.Capture(), Is.EqualTo(first), "a state identical to the last one got a new snapshot");
        }

        [Test]
        public void AParameterWhoseFirstValueIsZeroIsStillRecorded()
        {
            // The trap in any difference-based scheme: a fresh slot in a zeroed array
            // looks unchanged when its real value happens to be zero, and the parameter
            // vanishes from the log. Zero is a value like any other and often the
            // interesting one.
            _pool.Set("Alarm", 0f);

            Assert.That(_pool.Capture(), Is.Not.EqualTo(TraceFormat.NoSnapshotId));

            _pool.Drain(_flush);

            Assert.That(_flush.Snapshots.Count, Is.EqualTo(1));
            Assert.That(_flush.Snapshots[0].Count, Is.EqualTo(1));
            Assert.That(_flush.Deltas[0].Value, Is.EqualTo(0f));
        }

        [Test]
        public void EveryParameterASnapshotNamesIsDeclaredInTheSameDrain()
        {
            // Slot indices are meaningless without the declarations that name them, and
            // the writer emits whatever the drain hands it in order.
            _pool.Set("Health", 1f);
            _pool.Set("Tension", 0.5f);
            _pool.Capture();

            _pool.Drain(_flush);

            foreach (var delta in _flush.Deltas)
            {
                Assert.That(
                    _flush.Slots.Exists(slot => slot.Slot == delta.Slot),
                    Is.True,
                    $"slot {delta.Slot} was used before it was declared");
            }

            Assert.That(_flush.Slots.Count, Is.EqualTo(2));
            Assert.That(_strings.Resolve(_flush.Slots[0].NameStringId), Is.EqualTo("Health"));
            Assert.That(_strings.Resolve(_flush.Slots[1].NameStringId), Is.EqualTo("Tension"));
        }

        [Test]
        public void TwoDrainsMergedIntoOneBufferStillPointAtTheirOwnDeltas()
        {
            // What happens when the writer refuses a batch: the next flush appends into
            // the same buffer. Offsets recorded against the staging arena rather than the
            // destination would silently pair a snapshot with somebody else's values.
            _pool.Set("Health", 1f);
            _pool.Capture();
            _pool.Drain(_flush);

            _pool.Set("Tension", 0.5f);
            _pool.Capture();
            _pool.Drain(_flush);

            Assert.That(_flush.Snapshots.Count, Is.EqualTo(2));

            var second = _flush.Snapshots[1];
            var slotOfTension = _flush.Slots.Find(slot => _strings.Resolve(slot.NameStringId) == "Tension").Slot;

            Assert.That(_flush.Deltas[second.Offset].Slot, Is.EqualTo(slotOfTension));
            Assert.That(_flush.Deltas[second.Offset].Value, Is.EqualTo(0.5f));
        }

        [Test]
        public void ParametersBeyondTheTrackedLimitAreCountedRatherThanSwallowed()
        {
            for (var i = 0; i < 4; i++)
            {
                _pool.Set("Parameter" + i, i);
            }

            _pool.Set("OneTooMany", 1f);

            Assert.That(_pool.ParameterCount, Is.EqualTo(4));
            Assert.That(_pool.DroppedCount, Is.EqualTo(1));
        }

        [Test]
        public void OnceStagingIsFullARecordGetsNoSnapshotRatherThanAWrongOne()
        {
            for (var i = 0; i < 8; i++)
            {
                _pool.Set("Health", i);
                Assert.That(_pool.Capture(), Is.Not.EqualTo(TraceFormat.NoSnapshotId), $"capture {i}");
            }

            _pool.Set("Health", 99f);

            Assert.That(_pool.Capture(), Is.EqualTo(TraceFormat.NoSnapshotId));
            Assert.That(_pool.DroppedCount, Is.EqualTo(1));
        }

        [Test]
        public void AfterADrainStagingIsAvailableAgain()
        {
            for (var i = 0; i < 8; i++)
            {
                _pool.Set("Health", i);
                _pool.Capture();
            }

            _pool.Drain(_flush);
            _flush.Clear();

            _pool.Set("Health", 99f);

            Assert.That(_pool.Capture(), Is.Not.EqualTo(TraceFormat.NoSnapshotId));
        }

        [Test]
        public void SnapshotIdsStayUniqueAcrossDrains()
        {
            // Ids are session-global while the arenas holding them are per-flush. A pool
            // that restarted numbering on drain would make two different states share an
            // id, and every record pointing at the older one would read as the newer.
            _pool.Set("Health", 1f);
            var first = _pool.Capture();

            _pool.Drain(_flush);
            _flush.Clear();

            _pool.Set("Health", 2f);
            var second = _pool.Capture();

            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(_flush.Snapshots, Is.Empty);
        }
    }
}

#endif
