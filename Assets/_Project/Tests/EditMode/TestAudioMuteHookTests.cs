using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Architecture.Tests
{
    public sealed class TestAudioMuteHookTests
    {
        [Test]
        public void Editor_audio_is_muted_by_default_during_test_run()
        {
            Assert.That(SessionState.GetBool(TestAudioMuteHook.IsTestRunActiveKey, false), Is.True);
            Assert.That(EditorUtility.audioMasterMute, Is.True);
            Assert.That(AudioListener.volume, Is.EqualTo(0f));
        }

        [Test]
        public void EndTestRunMute_restores_previous_mute_state()
        {
            bool wasActive = SessionState.GetBool(TestAudioMuteHook.IsTestRunActiveKey, false);
            bool originalMute = EditorUtility.audioMasterMute;
            float originalVolume = AudioListener.volume;

            try
            {
                SessionState.SetBool(TestAudioMuteHook.IsTestRunActiveKey, false);
                EditorUtility.audioMasterMute = false;
                AudioListener.volume = 0.75f;

                TestAudioMuteHook.BeginTestRunMute();
                Assert.That(EditorUtility.audioMasterMute, Is.True);
                Assert.That(AudioListener.volume, Is.EqualTo(0f));

                TestAudioMuteHook.EndTestRunMute();
                Assert.That(EditorUtility.audioMasterMute, Is.False);
                Assert.That(AudioListener.volume, Is.EqualTo(0.75f));
                Assert.That(SessionState.GetBool(TestAudioMuteHook.IsTestRunActiveKey, false), Is.False);
            }
            finally
            {
                SessionState.SetBool(TestAudioMuteHook.IsTestRunActiveKey, wasActive);
                EditorUtility.audioMasterMute = originalMute;
                AudioListener.volume = originalVolume;
            }
        }
    }
}
