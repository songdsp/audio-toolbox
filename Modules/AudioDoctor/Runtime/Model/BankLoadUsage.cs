using System;

namespace AudioToolbox.AudioDoctor.Core
{
    /// <summary>Where a bank gets loaded. The input to R003.</summary>
    public enum BankLoadSource
    {
        /// <summary>A bank-loader component placed in a scene or prefab.</summary>
        LoaderComponent = 0,

        /// <summary>An explicit load call in a .cs file.</summary>
        CodeCall = 1,

        /// <summary>
        /// The middleware's own settings load this bank automatically at startup.
        /// Without this, R003 would fire on every scene of a project using the
        /// default "load all banks" configuration.
        /// </summary>
        SettingsAutoLoad = 2,
    }

    [Serializable]
    public sealed class BankLoadUsage
    {
        public string BankName;

        /// <summary>Scene or .cs asset path. Empty for <see cref="BankLoadSource.SettingsAutoLoad"/>.</summary>
        public string AssetPath;

        public string ObjectPath;

        public BankLoadSource Source;

        public int Line;

        public override string ToString() => $"{BankName} <- {Source} @ {AssetPath}";
    }
}
