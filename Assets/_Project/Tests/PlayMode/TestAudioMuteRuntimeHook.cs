using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Cosmimera.Tests.PlayMode
{
    /// <summary>
    /// Ensures AudioListener.volume is muted by default whenever PlayMode tests execute,
    /// without muting normal Play Mode sessions in the Unity Editor.
    /// </summary>
    internal static class TestAudioMuteRuntimeHook
    {
        private const string IsTestRunActiveKey = "Cosmimera.Tests.IsTestRunActive";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void MuteWhenRunningTests()
        {
#if UNITY_EDITOR
            if (!SessionState.GetBool(IsTestRunActiveKey, false))
            {
                return;
            }
#endif
            AudioListener.volume = 0f;
        }
    }
}
