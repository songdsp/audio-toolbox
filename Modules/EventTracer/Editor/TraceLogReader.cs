using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AudioToolbox.EventTracer.Editor
{
    /// <summary>A session read back from a .adtrace file.</summary>
    public sealed class TraceSession
    {
        public TraceSessionHeader Header = new TraceSessionHeader();

        public readonly List<AudioTraceRecord> Records = new List<AudioTraceRecord>();

        public readonly Dictionary<int, string> Strings = new Dictionary<int, string>();

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

                if (version != TraceFormat.Version)
                {
                    // Refused rather than read on a best-effort basis. A record layout
                    // read at the wrong version does not fail, it produces plausible
                    // nonsense - and someone will act on it.
                    throw new InvalidDataException(
                        $"{Path.GetFileName(path)} is format version {version}; this editor reads version {TraceFormat.Version}.");
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
