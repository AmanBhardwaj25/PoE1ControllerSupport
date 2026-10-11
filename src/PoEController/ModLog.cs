using System.Diagnostics;
using BepInEx.Logging;

namespace PoEController
{
    /// <summary>
    /// The mod's only way to write to the BepInEx log. Every method is compiled only when the build defines
    /// MOD_LOGGING (the normal development build does). The release build (make_release.ps1, NoModLogging=true)
    /// leaves it out, so the compiler removes each call together with its message: the shipped DLL writes nothing.
    /// </summary>
    public sealed class ModLog
    {
        private readonly ManualLogSource m_Source;

        public ModLog(ManualLogSource source)
        {
            m_Source = source;
        }

        [Conditional("MOD_LOGGING")]
        public void LogInfo(object message) => m_Source.LogInfo(message);

        [Conditional("MOD_LOGGING")]
        public void LogMessage(object message) => m_Source.LogMessage(message);

        [Conditional("MOD_LOGGING")]
        public void LogWarning(object message) => m_Source.LogWarning(message);

        [Conditional("MOD_LOGGING")]
        public void LogError(object message) => m_Source.LogError(message);

        [Conditional("MOD_LOGGING")]
        public void LogDebug(object message) => m_Source.LogDebug(message);
    }
}
