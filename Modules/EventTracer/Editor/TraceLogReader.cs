using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AudioToolbox.EventTracer.Editor
{
    /// <summary>
    /// One parameter snapshot, as it sits in the file: what changed, and what it changed
    /// from.
    /// </summary>
    public sealed class TraceParameterSnapshot
    {
        public int Id;

        /// <summary>The snapshot this one differs from, or <see cref="TraceFormat.NoSnapshotId"/>.</summary>
        public int ParentId = TraceFormat.NoSnapshotId;

        /// <summary>Slot index to value, for the parameters that changed at this point.</summary>
        public readonly List<KeyValuePair<int, float>> Changes = new List<KeyValuePair<int, float>>();
    }

    /// <summary>A session read back from a .adtrace file.</summary>
    public sealed class TraceSession
    {
        public TraceSessionHeader Header = new TraceSessionHeader();

        public readonly List<AudioTraceRecord> Records = new List<AudioTraceRecord>();

        public readonly Dictionary<int, string> Strings = new Dictionary<int, string>();

        /// <summary>Parameter slot index to the intern id of its name.</summary>
        public readonly Dictionary<int, int> ParameterSlots = new Dictionary<int, int>();

        /// <summary>Every snapshot in the file, by id.</summary>
        public readonly Dictionary<int, TraceParameterSnapshot> Snapshots =
            new Dictionary<int, TraceParameterSnapshot>();

        /// <summary>
        /// True when the file ended mid-chunk — almost always a build that crashed or
        /// was killed. The records before that point are still good.
        /// </summary>
        public bool EndedAbruptly;

        public string SourcePath = string.Empty;

        /// <summary>The text behind an intern id, or a description of why there is none.</summary>
        public string Resolve(int id)
        {
            if (id == TraceFormat.NoStringId)
            {
                return string.Empty;
            }

            if (id == TraceFormat.OverflowStringId)
            {
                return TraceFormat.OverflowStringText;
            }

            return Strings.TryGetValue(id, out var value) ? value : $"<unknown string {id}>";
        }

        /// <summary>The name of a parameter slot, or a placeholder when the log lost it.</summary>
        public string ResolveParameterName(int slot) =>
            ParameterSlots.TryGetValue(slot, out var nameId) ? Resolve(nameId) : $"<slot {slot}>";

        /// <summary>
        /// Rebuilds the full set of parameters a record was posted under, by walking the
        /// snapshot back to the first one and replaying the changes forward.
        /// </summary>
        /// <remarks>
        /// Returns false for a record that carries no snapshot, and for one whose chain
        /// the log does not contain — the second happens when a session dropped a batch,
        /// and answering with a partial set that looks complete would be worse than
        /// saying nothing. <paramref name="destination"/> is cleared either way.
        /// </remarks>
        public bool TryResolveParameters(int snapshotId, Dictionary<string, float> destination)
        {
            destination.Clear();

            if (snapshotId == TraceFormat.NoSnapshotId)
            {
                return false;
            }

            var chain = new List<TraceParameterSnapshot>();
            var id = snapshotId;

            while (id != TraceFormat.NoSnapshotId)
            {
                if (!Snapshots.TryGetValue(id, out var snapshot))
                {
                    return false;
                }

                chain.Add(snapshot);

                // A chain longer than the whole table means the file is self-referential;
                // a reader must not hang on a bad log.
                if (chain.Count > Snapshots.Count)
                {
                    return false;
                }

                id = snapshot.ParentId;
            }

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var change in chain[i].Changes)
                {
                    destination[ResolveParameterName(change.Key)] = change.Value;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Reads a .adtrace file written by any build, on any platform.
    /// </summary>
    /// <remarks>
    /// Deliberately in the editor assembly and deliberately not behind
    /// <c>AUDIOTOOLBOX_TRACE</c>. The logs worth reading come from somebody else's
    /// build — a QA machine, a console, a player — and whether tracing happens to be
    /// switched on in <em>this</em> project has nothing to do with whether you can open
    /// one. It shares only <see cref="TraceFormat"/> with the writer, which is what
    /// keeps the two from drifting.
    /// </remarks>
    public static class TraceLogReader
    {
        public static TraceSession Read(string path)
        {
            var session = new TraceSession { SourcePath = path };

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (stream.Length < 8)
                {
                    throw new InvalidDataException($"{Path.GetFileName(path)} is too short to be a trace log.");
                }

                var magic = reader.ReadUInt32();

                if (magic != TraceFormat.Magic)
                {
                    throw new InvalidDataException($"{Path.GetFileName(path)} is not a trace log.");
                }

                var version = reader.ReadInt32();

                if (version < TraceFormat.MinReadableVersion || version > TraceFormat.Version)
                {
                    // Refused rather than read on a best-effort basis. A record layout
                    // read at the wrong version does not fail, it produces plausible
                    // nonsense - and someone will act on it. The accepted range is
                    // deliberately narrow: it holds only the older versions whose record
                    // layout is known to be identical.
                    throw new InvalidDataException(
                        $"{Path.GetFileName(path)} is format version {version}; this editor reads " +
                        $"versions {TraceFormat.MinReadableVersion} to {TraceFormat.Version}.");
                }

                ReadChunks(reader, session);
            }

            return session;
        }

        private static void ReadChunks(BinaryReader reader, TraceSession session)
        {
            var stream = reader.BaseStream;

            while (stream.Position < stream.Length)
            {
                var chunkStart = stream.Position;

                try
                {
                    switch (reader.ReadByte())
                    {
                        case TraceFormat.ChunkTag.String:
                            var id = reader.ReadInt32();
                            session.Strings[id] = reader.ReadString();
                            break;

                        case TraceFormat.ChunkTag.Record:
                            session.Records.Add(ReadRecord(reader));
                            break;

                        case TraceFormat.ChunkTag.Parameter:
                            var slot = reader.ReadInt32();
                            session.ParameterSlots[slot] = reader.ReadInt32();
                            break;

                        case TraceFormat.ChunkTag.Snapshot:
                            var snapshot = new TraceParameterSnapshot
                            {
                                Id = reader.ReadInt32(),
                                ParentId = reader.ReadInt32(),
                            };

                            var changeCount = reader.ReadInt32();

                            for (var i = 0; i < changeCount; i++)
                            {
                                snapshot.Changes.Add(
                                    new KeyValuePair<int, float>(reader.ReadInt32(), reader.ReadSingle()));
                            }

                            session.Snapshots[snapshot.Id] = snapshot;
                            break;

                        case TraceFormat.ChunkTag.Session:
                            // The last header in the file wins: the writer emits one at
                            // the start and another on a clean shutdown with the final
                            // counts.
                            session.Header = JsonUtility.FromJson<TraceSessionHeader>(reader.ReadString())
                                             ?? session.Header;
                            break;

                        default:
                            // An unknown tag means the stream is no longer aligned, and
                            // there is no way back. Everything before here is sound.
                            session.EndedAbruptly = true;
                            return;
                    }
                }
                catch (Exception e) when (e is EndOfStreamException || e is ArgumentException)
                {
                    // A half-written chunk at the end of a log from a process that died.
                    stream.Position = chunkStart;
                    session.EndedAbruptly = true;
                    return;
                }
            }
        }

        private static AudioTraceRecord ReadRecord(BinaryReader reader) => new AudioTraceRecord
        {
            Frame = reader.ReadInt64(),
            TimeSeconds = reader.ReadDouble(),
            EventKeyId = reader.ReadInt32(),
            EmitterPathId = reader.ReadInt32(),
            CallSiteId = reader.ReadInt32(),
            EmitterPos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            ListenerPos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            DistanceToListener = reader.ReadSingle(),
            Outcome = (PlaybackOutcome)reader.ReadInt32(),
            BackendResultCode = reader.ReadInt32(),
            ParamSnapshotId = reader.ReadInt32(),
        };

        /// <summary>The folder a build writes its sessions to, for this machine.</summary>
        public static string SessionFolder =>
            Path.Combine(Application.persistentDataPath, "AudioToolboxTraces");

        /// <summary>The newest session on this machine, or null when there is none.</summary>
        public static string FindLatestSession()
        {
            if (!Directory.Exists(SessionFolder))
            {
                return null;
            }

            string newest = null;
            var newestTime = DateTime.MinValue;

            foreach (var file in Directory.GetFiles(SessionFolder, "*" + TraceFormat.FileExtension))
            {
                var written = File.GetLastWriteTimeUtc(file);

                if (written > newestTime)
                {
                    newestTime = written;
                    newest = file;
                }
            }

            return newest;
        }
    }
}
