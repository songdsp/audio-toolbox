#if AUDIOTOOLBOX_TRACE

using System.Collections.Generic;
using AudioToolbox.EventTracer.Recording;
using NUnit.Framework;
using UnityEngine;

namespace AudioToolbox.EventTracer.Tests
{
    /// <summary>
    /// The half of a record that says <em>which</em> object made the sound.
    /// </summary>
    /// <remarks>
    /// A log full of "event:/SFX/Footstep, Started" answers nothing anyone asks. The
    /// question is always which of the twelve NPCs walking around produced it, and the
    /// scene path is the whole of the answer.
    /// </remarks>
    [TestFixture]
    public sealed class EmitterPathCacheTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        private StringInternTable _strings;
        private EmitterPathCache _cache;

        [SetUp]
        public void SetUp()
        {
            _strings = new StringInternTable(64);
            _cache = new EmitterPathCache(_strings, 4);
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        private Transform MakeChain(params string[] names)
        {
            Transform parent = null;

            foreach (var name in names)
            {
                var go = new GameObject(name);
                _created.Add(go);
                go.transform.SetParent(parent);
                parent = go.transform;
            }

            return parent;
        }

        [Test]
        public void APathReadsFromTheSceneRootDown()
        {
            var muzzle = MakeChain("Level", "Enemies", "Rifleman", "Muzzle");

            var path = _strings.Resolve(_cache.GetPathId(muzzle));

            Assert.That(path, Is.EqualTo("/Level/Enemies/Rifleman/Muzzle"));
        }

        [Test]
        public void ARootObjectStillGetsALeadingSlash()
        {
            // So that a path is always a path, and a reader never has to special-case the
            // one-segment form.
            var solo = MakeChain("Jukebox");

            Assert.That(_strings.Resolve(_cache.GetPathId(solo)), Is.EqualTo("/Jukebox"));
        }

        [Test]
        public void TheSameEmitterResolvesToTheSameIdWithoutInterningAgain()
        {
            // The point of the cache. Interning twice would mean the path was rebuilt,
            // which is a string allocation on the collection path.
            var emitter = MakeChain("Level", "Speaker");

            var first = _cache.GetPathId(emitter);
            var countAfterFirst = _strings.Count;
            var second = _cache.GetPathId(emitter);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(_strings.Count, Is.EqualTo(countAfterFirst), "the path was built a second time");
        }

        [Test]
        public void TwoObjectsWithTheSameNameUnderDifferentParentsAreDistinguished()
        {
            var left = MakeChain("Level", "Left", "Speaker");
            var right = MakeChain("Level", "Right", "Speaker");

            Assert.That(_strings.Resolve(_cache.GetPathId(left)), Is.EqualTo("/Level/Left/Speaker"));
            Assert.That(_strings.Resolve(_cache.GetPathId(right)), Is.EqualTo("/Level/Right/Speaker"));
        }

        [Test]
        public void ASoundWithNoEmitterHasNoPathRatherThanAnEmptyOne()
        {
            // Distinct from "the path was lost": UI and music genuinely have no emitter,
            // and a reader has to be able to tell those two apart.
            Assert.That(_cache.GetPathId(null), Is.EqualTo(TraceFormat.NoStringId));
        }

        [Test]
        public void PastCapacityThePathIsReportedLostRatherThanRebuiltEveryTime()
        {
            for (var i = 0; i < 4; i++)
            {
                _cache.GetPathId(MakeChain("Emitter" + i));
            }

            var overflow = _cache.GetPathId(MakeChain("OneTooMany"));

            Assert.That(overflow, Is.EqualTo(TraceFormat.OverflowStringId));
            Assert.That(_cache.DroppedCount, Is.EqualTo(1), "the loss was not counted");
        }

        [Test]
        public void ADestroyedEmitterReadsAsAbsentInsteadOfThrowing()
        {
            // A sound can outlive the object that started it, and asking a destroyed
            // Transform for its parent throws. The tracer must not be what crashes a game
            // that was already despawning something.
            var emitter = MakeChain("Level", "Doomed");
            Object.DestroyImmediate(emitter.gameObject);

            Assert.That(_cache.GetPathId(emitter), Is.EqualTo(TraceFormat.NoStringId));
        }
    }
}

#endif
