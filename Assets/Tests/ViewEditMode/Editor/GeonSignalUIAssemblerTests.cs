using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using CityFlow.UI.Editor;

namespace CityFlow.UI.Tests
{
    public class GeonSignalUIAssemblerTests
    {
        [Test]
        public void SignalControlPanelPrefab_ExistsInResourcesPath()
        {
            // Arrange
            string expectedPath = "Assets/Resources/CityFlow/UI/UI_SignalControlPanel.prefab";

            // Act
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(expectedPath);

            // Assert
            Assert.IsNotNull(prefab, $"Prefab should exist at path: {expectedPath}");
            Assert.IsNotNull(prefab.GetComponent<CityFlow.UI.SignalControlPanelView>(), "Prefab should have SignalControlPanelView attached.");
        }
    }
}
