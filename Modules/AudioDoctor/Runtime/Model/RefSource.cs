namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>Where a reference to an audio event was found.</summary>
    public enum RefSource
    {
        /// <summary>A serialized field on a component in a prefab or scene.</summary>
        SerializedField = 0,

        /// <summary>A string literal passed to a middleware API in a .cs file.</summary>
        CodeLiteral = 1,

        /// <summary>A clip on a Timeline asset.</summary>
        Timeline = 2,

        /// <summary>An AnimationEvent with a string parameter on an AnimationClip.</summary>
        AnimationEvent = 3,
    }
}
