using System;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// String analysis shared by the backends' code scanners.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in a backend so it can be unit-tested: the test assembly
    /// is forbidden from referencing any backend, and this is exactly the kind of
    /// fiddly text handling that needs tests more than the code around it does.
    /// </remarks>
    public static class CodeLiteral
    {
        /// <summary>
        /// True when the literal at the given position is glued to something else with
        /// <c>+</c>, so its text is only a fragment of the real value.
        /// </summary>
        /// <remarks>
        /// <c>PlayOneShot("event:/" + kind)</c> contains a perfectly well-formed literal
        /// that is not an event name. Reading it as one produced an Error-level finding
        /// claiming 'event:/' did not exist - true, useless, and wrong in the way that
        /// gets a validator switched off. A fragment must be reported as a coverage note
        /// instead, which is what the scanner already did for the same line.
        /// </remarks>
        /// <param name="line">The source line.</param>
        /// <param name="contentStart">Index of the first character inside the quotes.</param>
        /// <param name="contentLength">Length of the text inside the quotes.</param>
        public static bool IsConcatenated(string line, int contentStart, int contentLength)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            // contentStart - 1 is the opening quote; look at what precedes it.
            var before = PreviousNonWhitespace(line, contentStart - 2);

            if (before == '+')
            {
                return true;
            }

            // contentStart + contentLength is the closing quote; look at what follows.
            var after = NextNonWhitespace(line, contentStart + contentLength + 1);

            return after == '+';
        }

        private static char PreviousNonWhitespace(string line, int index)
        {
            for (var i = Math.Min(index, line.Length - 1); i >= 0; i--)
            {
                if (!char.IsWhiteSpace(line[i]))
                {
                    return line[i];
                }
            }

            return '\0';
        }

        private static char NextNonWhitespace(string line, int index)
        {
            for (var i = Math.Max(index, 0); i < line.Length; i++)
            {
                if (!char.IsWhiteSpace(line[i]))
                {
                    return line[i];
                }
            }

            return '\0';
        }
    }
}
