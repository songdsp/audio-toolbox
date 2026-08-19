using System;
using System.Globalization;
using System.Text;

namespace AudioToolbox.AudioDoctor.Editor
{
    /// <summary>
    /// A small hand-rolled JSON emitter.
    /// </summary>
    /// <remarks>
    /// Unity's JsonUtility cannot serialize dictionaries or IReadOnlyList, and pulling
    /// in Newtonsoft would add a package dependency to a tool whose whole point is to
    /// drop into someone else's project without friction. The report schema is small
    /// and stable enough that writing it out is cheaper than either.
    /// </remarks>
    internal sealed class JsonWriter
    {
        private readonly StringBuilder _builder = new StringBuilder();
        private int _depth;
        private bool _needsComma;

        public JsonWriter BeginObject()
        {
            Separator();
            _builder.Append('{');
            _depth++;
            _needsComma = false;
            return this;
        }

        public JsonWriter EndObject()
        {
            _depth--;
            NewLine();
            _builder.Append('}');
            _needsComma = true;
            return this;
        }

        public JsonWriter BeginArray()
        {
            Separator();
            _builder.Append('[');
            _depth++;
            _needsComma = false;
            return this;
        }

        public JsonWriter EndArray()
        {
            _depth--;
            NewLine();
            _builder.Append(']');
            _needsComma = true;
            return this;
        }

        public JsonWriter Name(string name)
        {
            Separator();
            _builder.Append(Quote(name)).Append(": ");
            _needsComma = false;
            return this;
        }

        public JsonWriter Value(string value)
        {
            Separator();
            _builder.Append(value == null ? "null" : Quote(value));
            _needsComma = true;
            return this;
        }

        public JsonWriter Value(long value)
        {
            Separator();
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
            _needsComma = true;
            return this;
        }

        public JsonWriter Value(double value)
        {
            Separator();
            _builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            _needsComma = true;
            return this;
        }

        public JsonWriter Value(bool value)
        {
            Separator();
            _builder.Append(value ? "true" : "false");
            _needsComma = true;
            return this;
        }

        public JsonWriter Property(string name, string value) => Name(name).Value(value);

        public JsonWriter Property(string name, long value) => Name(name).Value(value);

        public JsonWriter Property(string name, double value) => Name(name).Value(value);

        public JsonWriter Property(string name, bool value) => Name(name).Value(value);

        public override string ToString() => _builder.ToString();

        private void Separator()
        {
            if (_needsComma)
            {
                _builder.Append(',');
            }

            if (_builder.Length > 0 && (_needsComma || EndsWithBracket()))
            {
                NewLine();
            }
        }

        private bool EndsWithBracket()
        {
            if (_builder.Length == 0)
            {
                return false;
            }

            var last = _builder[_builder.Length - 1];
            return last == '{' || last == '[';
        }

        private void NewLine()
        {
            _builder.Append('\n');
            _builder.Append(' ', _depth * 2);
        }

        private static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.Append('"').ToString();
        }
    }
}
