using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Cosmimera.Architecture.Tests
{
    /// <summary>
    /// Mutes Unity Editor audio for the duration of any EditMode or PlayMode test run
    /// and restores the previous mute state when the run finishes.
    /// </summary>
    [InitializeOnLoad]
    internal static class TestAudioMuteHook
    {
        // SessionState survives domain reloads when entering/exiting PlayMode during tests.
        internal const string IsTestRunActiveKey = "Cosmimera.Tests.IsTestRunActive";
        private const string PrevMasterMuteKey = "Cosmimera.Tests.PrevMasterMute";
        private const string PrevListenerVolumeKey = "Cosmimera.Tests.PrevListenerVolume";

        static TestAudioMuteHook()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.hideFlags = HideFlags.HideAndDontSave;
            api.RegisterCallbacks(new MuteCallbacks());
        }

        internal static void BeginTestRunMute()
        {
            if (!SessionState.GetBool(IsTestRunActiveKey, false))
            {
                SessionState.SetBool(PrevMasterMuteKey, EditorUtility.audioMasterMute);
                SessionState.SetFloat(PrevListenerVolumeKey, AudioListener.volume);
                SessionState.SetBool(IsTestRunActiveKey, true);
            }

            EditorUtility.audioMasterMute = true;
            AudioListener.volume = 0f;
        }

        internal static void EndTestRunMute()
        {
            if (!SessionState.GetBool(IsTestRunActiveKey, false))
            {
                return;
            }

            bool previousMute = SessionState.GetBool(PrevMasterMuteKey, false);
            float previousVolume = SessionState.GetFloat(PrevListenerVolumeKey, 1f);
            SessionState.SetBool(IsTestRunActiveKey, false);
            EditorUtility.audioMasterMute = previousMute;
            AudioListener.volume = previousVolume;
        }

        private sealed class MuteCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                BeginTestRunMute();
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                EndTestRunMute();
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
