namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>
    /// How badly a <see cref="ValidationIssue"/> should be taken.
    /// Ordered ascending so that <c>severity &gt;= threshold</c> works for CI's --fail-on.
    /// </summary>
    public enum Severity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }
}
