#if AUDIOTOOLBOX_TRACE

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AudioToolbox.EventTracer.Recording
{
    /// <summary>
    /// Writes finished records to a .adtrace file on a thread of its own.
    /// </summary>
    /// <remarks>
    /// The main thread's part of this is a memcpy and a set of a flag; everything that
    /// touches the file system happens on the writer thread. A tracer that made the
    /// audio thread wait on a disk would introduce the very dropouts it exists to
    /// diagnose, and one that stalled the main thread would change the frame timings
    /// being traced.
    /// <para>
    /// When a flush arrives while the previous one is still being written, the new one
    /// is skipped rather than queued or waited on. The records stay in the ring buffer
    /// and go out next time; the only way to lose them is for the buffer to wrap first,
    /// which is counted. Back-pressure that blocks the game is never the right trade
    /// here.
    /// </para>
    /// </remarks>
    internal sealed class TraceSessionWriter : IDisposable
    {
        private readonly string _path;
        private readonly Thread _thread;
        private readonly AutoResetEvent _work = new AutoResetEvent(false);
        private readonly ManualResetEventSlim _idle = new ManualResetEventSlim(true);

        // Two buffers: the main thread fills one while the writer drains the other.
        private readonly AudioTraceRecord[] _recordBuffer;
        private readonly List<KeyValuePair<int, string>> _stringBuffer = new List<KeyValuePair<int, string>>();
        private readonly ParameterFlushBuffer _parameterBuffer = new ParameterFlushBuffer();

        private int _recordCount;
        private string _pendingHeaderJson;

        private volatile bool _busy;
        private volatile bool _stopping;
        private volatile string _failure;

        private long _recordsWritten;

        public TraceSessionWriter(string path, int batchCapacity)
        {
            _path = path;
            _recordBuffer = new AudioTraceRecord[batchCapacity];

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            _thread = new Thread(Run)
            {
                Name = "AudioToolbox.EventTracer writer",
                IsBackground = true,
            };

            _thread.Start();
        }

        public string FilePath => _path;

        public long RecordsWritten => Interlocked.Read(ref _recordsWritten);

        /// <summary>The first write error, if any. Reported once rather than every frame.</summary>
        public string Failure => _failure;

        /// <summary>How many records one flush can carry.</summary>
        public int BatchCapacity => _recordBuffer.Length;

        /// <summary>True when the previous batch is still being written.</summary>
        public bool IsBusy => _busy;

        /// <summary>
        /// Hands a batch to the writer thread. Main thread only, never blocks. Returns
        /// false when the writer was still busy and nothing was taken.
        /// </summary>
        public bool TrySubmit(
            AudioTraceRecord[] records,
            int recordCount,
            List<KeyValuePair<int, string>> newStrings,
            ParameterFlushBuffer parameters,
            string headerJson)
        {
            if (_busy || _stopping)
            {
                return false;
            }

            Array.Copy(records, _recordBuffer, recordCount);
            _recordCount = recordCount;

            _stringBuffer.Clear();
            if (newStrings != null)
            {
                _stringBuffer.AddRange(newStrings);
            }

            _parameterBuffer.CopyFrom(parameters);

            _pendingHeaderJson = headerJson;

            _busy = true;
            _idle.Reset();
            _work.Set();
            return true;
        }

        public void Dispose()
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            _work.Set();

            // A bounded wait: a shutdown must not hang on a disk that has stopped
            // answering, and losing the tail of a log is a better outcome than a build
            // that will not quit.
            if (!_thread.Join(TimeSpan.FromSeconds(2)))
            {
                Debug.LogWarning("[EventTracer] Writer thread did not finish within 2s; the log may be missing its tail.");
            }

            _work.Dispose();
            _idle.Dispose();
        }

        /// <summary>Blocks until any in-flight batch is on disk. For shutdown and tests only.</summary>
        public void WaitForIdle(int millisecondsTimeout = 2000) => _idle.Wait(millisecondsTimeout);

        private void Run()
        {
            FileStream stream = null;
            BinaryWriter writer = null;

            try
            {
                stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024);
                writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

                writer.Write(TraceFormat.Magic);
                writer.Write(TraceFormat.Version);

                while (true)
                {
                    _work.WaitOne();

                    if (_busy)
                    {
                        WriteBatch(writer);
                        _busy = false;
                        _idle.Set();
                    }

                    if (_stopping)
                    {
                        // One last pass: a batch submitted between the check above and
                        // the stop flag being set would otherwise be dropped.
                        if (_busy)
                        {
                            WriteBatch(writer);
                            _busy = false;
                        }

                        writer.Flush();
                        _idle.Set();
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                _failure = e.Message;
                _busy = false;
                _idle.Set();
            }
            finally
            {
                writer?.Dispose();
                stream?.Dispose();
            }
        }

        private void WriteBatch(BinaryWriter writer)
        {
            // Strings first, always. A record referring to an id that was never written
            // is unreadable, and a crash between the two orderings is the difference
            // between a usable log and a wall of numbers.
            for (var i = 0; i < _stringBuffer.Count; i++)
            {
                writer.Write(TraceFormat.ChunkTag.String);
                writer.Write(_stringBuffer[i].Key);
                writer.Write(_stringBuffer[i].Value ?? string.Empty);
            }

            // Then parameter slots, then the snapshots that name them, then the records
            // that point at those snapshots. Same discipline as the strings above, for
            // the same reason: every chunk arrives after everything it depends on, so a
            // log cut short anywhere still resolves everything it does contain.
            for (var i = 0; i < _parameterBuffer.Slots.Count; i++)
            {
                var slot = _parameterBuffer.Slots[i];
                writer.Write(TraceFormat.ChunkTag.Parameter);
                writer.Write(slot.Slot);
                writer.Write(slot.NameStringId);
            }

            for (var i = 0; i < _parameterBuffer.Snapshots.Count; i++)
            {
                var snapshot = _parameterBuffer.Snapshots[i];

                writer.Write(TraceFormat.ChunkTag.Snapshot);
                writer.Write(snapshot.Id);
                writer.Write(snapshot.ParentId);
                writer.Write(snapshot.Count);

                for (var d = 0; d < snapshot.Count; d++)
                {
                    var delta = _parameterBuffer.Deltas[snapshot.Offset + d];
                    writer.Write(delta.Slot);
                    writer.Write(delta.Value);
                }
            }

            for (var i = 0; i < _recordCount; i++)
            {
                writer.Write(TraceFormat.ChunkTag.Record);
                WriteRecord(writer, in _recordBuffer[i]);
            }

            Interlocked.Add(ref _recordsWritten, _recordCount);

            if (!string.IsNullOrEmpty(_pendingHeaderJson))
            {
                writer.Write(TraceFormat.ChunkTag.Session);
                writer.Write(_pendingHeaderJson);
                _pendingHeaderJson = null;
            }

            writer.Flush();
        }

        /// <summary>
        /// Fields in declaration order, written one at a time.
        /// </summary>
        /// <remarks>
        /// Explicit rather than a blit of the struct's memory. A blit would be faster and
        /// would also make the file depend on the runtime's field packing and the host's
        /// endianness — so a log from a console would read as garbage on a desktop, which
        /// is the one case this format exists for. This runs on the writer thread, where
        /// the cost does not matter.
        /// </remarks>
        private static void WriteRecord(BinaryWriter writer, in AudioTraceRecord record)
        {
            writer.Write(record.Frame);
            writer.Write(record.TimeSeconds);
            writer.Write(record.EventKeyId);
            writer.Write(record.EmitterPathId);
            writer.Write(record.CallSiteId);
            writer.Write(record.EmitterPos.x);
            writer.Write(record.EmitterPos.y);
            writer.Write(record.EmitterPos.z);
            writer.Write(record.ListenerPos.x);
            writer.Write(record.ListenerPos.y);
            writer.Write(record.ListenerPos.z);
            writer.Write(record.DistanceToListener);
            writer.Write((int)record.Outcome);
            writer.Write(record.BackendResultCode);
            writer.Write(record.ParamSnapshotId);
        }
    }
}

#endif
