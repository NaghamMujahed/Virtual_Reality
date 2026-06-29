using UnityEngine;

namespace Simulation
{
    public sealed partial class RopeSimulation
    {
        // ════════════════════════════════════════════════════════════════════
        //  HELPERS & UTILITIES
        // ════════════════════════════════════════════════════════════════════

        static int Groups(int count) => Mathf.Max(1, (count + 63) / 64);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void SimLog(LogLevel lvl, string msg)
        {
            if (lvl > _logLevel) return;
            switch (lvl)
            {
                case LogLevel.Error:   Debug.LogError($"[Rope] {msg}");   break;
                case LogLevel.Warn:    Debug.LogWarning($"[Rope] {msg}"); break;
                default:               Debug.Log($"[Rope] {msg}");        break;
            }
        }
    }
}