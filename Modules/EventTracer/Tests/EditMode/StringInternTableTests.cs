#if AUDIOTOOLBOX_TRACE

using System.Collections.Generic;
using AudioToolbox.EventTracer.Recording;
using NUnit.Framework;

namespace AudioToolbox.EventTracer.Tests
{
    [TestFixture]
    public sealed class StringInternTableTests
    {
        [Test]
        public void TheSameStringAlwaysGetsTheSameId()
        {
            var table = new StringInternTable(16);

            var first = table.Intern("event:/SFX/Gunshot");
            var second = table.Intern("event:/SFX/Gunshot");

            Assert.That(second, Is.EqualTo(first));
            Assert.That(table.Count, Is.EqualTo(1));
        }

        [Test]
        public void DistinctStringsGetDistinctIds()
        {
            var table = new StringInternTable(16);

            Assert.That(table.Intern("a"), Is.Not.EqualTo(table.Intern("b")));
            Assert.That(table.Count, Is.EqualTo(2));
        }

        [Test]
        public void NullIsItsOwnId_DistinctFromTheEmptyString()
        {
            // A record with no call site and one posted from an empty path are different
            // facts, and a reader has to be able to tell them apart.
            var table = new StringInternTable(16);

            Assert.That(table.Intern(null), Is.EqualTo(TraceFormat.NoStringId));
            Assert.That(table.Intern(string.Empty), Is.Not.EqualTo(TraceFormat.NoStringId));
        }

        [Test]
        public void OnceFull_NewStringsGetTheOverflowIdAndAreCounted()
        {
            // A project that builds event names at runtime would otherwise grow this
            // without limit. Refusing loudly beats being the reason a build runs out of
            // memory.
            var table = new StringInternTable(2);

            table.Intern("one");
            table.Intern("two");

            Assert.That(table.Intern("three"), Is.EqualTo(TraceFormat.OverflowStringId));
            Assert.That(table.DroppedCount, Is.EqualTo(1));
            Assert.That(table.Count, Is.EqualTo(2));
        }

        [Test]
        public void StringsAlreadyInterned_StillResolveAfterItIsFull()
        {
            var table = new StringInternTable(2);

            var id = table.Intern("one");
            table.Intern("two");
            table.Intern("three");

            Assert.That(table.Resolve(id), Is.EqualTo("one"));
        }

        [Test]
        public void TheOverflowIdResolvesToAnExplanation()
        {
            var table = new StringInternTable(2);
            Assert.That(table.Resolve(TraceFormat.OverflowStringId), Is.EqualTo(TraceFormat.OverflowStringText));
        }

        [Test]
        public void DrainHandsOverEachNewEntryExactlyOnce()
        {
            // The writer emits these before the records that name them. A string handed
            // over twice wastes space; one handed over never leaves records unreadable.
            var table = new StringInternTable(16);
            var drained = new List<KeyValuePair<int, string>>();

            table.Intern("a");
            table.Intern("b");
            table.DrainNewEntries(drained);

            Assert.That(drained.Count, Is.EqualTo(2));

            drained.Clear();
            table.Intern("a");
            table.DrainNewEntries(drained);

            Assert.That(drained, Is.Empty, "an id already emitted was emitted again");

            drained.Clear();
            table.Intern("c");
            table.DrainNewEntries(drained);

            Assert.That(drained.Count, Is.EqualTo(1));
            Assert.That(drained[0].Value, Is.EqualTo("c"));
        }
    }
}

#endif
