using UnityEngine;
using UnityEngine.UIElements;

namespace Cosmimera.Bootstrap.Demo.UI
{
    /// <summary>Creates and manages runtime UI Toolkit UIDocuments and PanelSettings.</summary>
    internal static class UitkRuntimeHost
    {
        private static PanelSettings _sharedPanelSettings;

        internal static PanelSettings SharedPanelSettings()
        {
            if (_sharedPanelSettings != null)
            {
                return _sharedPanelSettings;
            }

            _sharedPanelSettings = Resources.Load<PanelSettings>("UI/CosmimeraPanelSettings");
            if (_sharedPanelSettings == null)
            {
                _sharedPanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _sharedPanelSettings.name = "CosmimeraRuntimePanelSettings";
                _sharedPanelSettings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                _sharedPanelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _sharedPanelSettings.referenceResolution = new Vector2Int(1280, 720);
                _sharedPanelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                _sharedPanelSettings.match = 0.5f;
            }

            return _sharedPanelSettings;
        }

        internal static UIDocument CreateDocument(string objectName, string resourcePath, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(objectName);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            UIDocument doc = go.AddComponent<UIDocument>();
            doc.panelSettings = SharedPanelSettings();
            doc.sortingOrder = sortingOrder;
            VisualTreeAsset tree = Resources.Load<VisualTreeAsset>(resourcePath);
            if (tree != null)
            {
                doc.visualTreeAsset = tree;
            }
            else
            {
                Debug.LogError($"UitkRuntimeHost: missing VisualTreeAsset at Resources/{resourcePath}");
            }

            return doc;
        }
    }
}
