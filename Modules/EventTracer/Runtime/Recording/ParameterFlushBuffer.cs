#if AUDIOTOOLBOX_TRACE

using System.Collections.Generic;

namespace AudioToolbox.EventTracer.Recording
{
    /// <summary>A parameter slot, declared once before any snapshot refers to it.</summary>
    internal readonly struct ParameterSlotEntry
    {
        public readonly int Slot;
        public readonly int NameStringId;

        public ParameterSlotEntry(int slot, int nameStringId)
        {
            Slot = slot;
            NameStringId = nameStringId;
        }
    }

    /// <summary>One snapshot's identity and where its changes sit in <see cref="ParameterFlushBuffer.Deltas"/>.</summary>
    internal readonly struct SnapshotHeaderEntry
    {
        public readonly int Id;
        public readonly int ParentId;
        public readonly int Offset;
        public readonly int Count;

        public SnapshotHeaderEntry(int id, int parentId, int offset, int count)
        {
            Id = id;
            ParentId = parentId;
            Offset = offset;
            Count = count;
        }
    }

    /// <summary>One parameter's value, as of one snapshot.</summary>
    internal readonly struct SnapshotDeltaEntry
    {
        public readonly int Slot;
        public readonly float Value;

        public SnapshotDeltaEntry(int slot, float value)
        {
            Slot = slot;
            Value = value;
        }
    }

    /// <summary>
    /// What one flush hands the writer about parameters, in wire order.
    /// </summary>
    /// <remarks>
    /// A container rather than five more arguments on <c>TrySubmit</c>, and it exists in
    /// two copies for the same reason the record buffer does: the main thread refills its
    /// own while the writer thread drains the other. The lists are reused, so after the
    /// first few flushes they are at their high-water mark and a flush allocates nothing.
    /// </remarks>
    internal sealed class ParameterFlushBuffer
    {
        public readonly List<ParameterSlotEntry> Slots = new List<ParameterSlotEntry>();
        public readonly List<SnapshotHeaderEntry> Snapshots = new List<SnapshotHeaderEntry>();
        public readonly List<SnapshotDeltaEntry> Deltas = new List<SnapshotDeltaEntry>();

        public bool IsEmpty => Slots.Count == 0 && Snapshots.Count == 0;

        public void Clear()
        {
            Slots.Clear();
            Snapshots.Clear();
            Deltas.Clear();
        }

        public void CopyFrom(ParameterFlushBuffer source)
        {
            Clear();

            if (source == null)
            {
                return;
            }

            Slots.AddRange(source.Slots);
            Snapshots.AddRange(source.Snapshots);
            Deltas.AddRange(source.Deltas);
        }
    }
}

#endif
