using System.Runtime.CompilerServices;

// The ring buffer, intern table, recorder and voice registry are internal: they are
// mechanism, not API, and a game has no business reaching into them. Their behaviour
// under overflow and wraparound is exactly what has to be tested, though, and testing
// it through the facade only would mean asserting on symptoms rather than on the thing
// that produces them.
[assembly: InternalsVisibleTo("AudioToolbox.EventTracer.Tests.EditMode")]
[assembly: InternalsVisibleTo("AudioToolbox.EventTracer.Tests.PlayMode")]
