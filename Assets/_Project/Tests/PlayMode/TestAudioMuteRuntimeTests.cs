using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Tests.PlayMode
{
    public sealed class TestAudioMuteRuntimeTests
    {
        [UnityTest]
        public IEnumerator AudioListener_volume_is_zero_by_default_during_playmode_tests()
        {
            yield return null;
            Assert.That(AudioListener.volume, Is.EqualTo(0f));
        }
    }
}
