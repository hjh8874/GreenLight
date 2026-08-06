using CityFlow.UI;
using NUnit.Framework;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class FloatingHudLevelControllerTests
    {
        [Test]
        public void MiniHudClick_DoesNotToggleWhenPointerIsOverUi()
        {
            Assert.IsFalse(
                FloatingHudLevelController.ShouldToggleOnClick(pointerOverUi: true));
            Assert.IsTrue(
                FloatingHudLevelController.ShouldToggleOnClick(pointerOverUi: false));
        }

        [Test]
        public void DockMenu_RevealsRequestedPanels_InSmallFloatingPreset()
        {
            Assert.IsTrue(
                FloatingHudLevelController.ShouldShowLargeLevel(
                    isFloating: true,
                    isRevealed: true,
                    presetIndex: 0,
                    UIDockController.MenuType.Build));
            Assert.IsTrue(
                FloatingHudLevelController.ShouldShowMediumLevel(
                    isFloating: true,
                    isRevealed: true,
                    presetIndex: 0,
                    UIDockController.MenuType.Settings));
        }
    }
}
