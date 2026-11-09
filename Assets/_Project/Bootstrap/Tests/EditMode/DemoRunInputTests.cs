using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class DemoRunInputTests
    {
        [Test]
        public void Missing_run_actions_fail_before_configuration_and_shared_actions_stay_disabled()
        {
            Assert.Throws<ContractException>(() => RunSession.RequireRunMap(null));
            var empty = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                Assert.Throws<ContractException>(() => RunSession.RequireRunMap(empty));
                empty.AddActionMap("DemoRun").AddAction("SelectFirst", InputActionType.Button);
                Assert.Throws<ContractException>(() => RunSession.RequireRunMap(empty));
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }

            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/_Project/Settings/Input/InputSystem_Actions.inputactions");
            InputActionMap map = RunSession.RequireRunMap(asset);
            Assert.That(map.enabled, Is.False, "validation does not enable the shared asset");
            Assert.That(map.actions.Count, Is.EqualTo(10));
        }
    }
}
