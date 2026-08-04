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
    }
}
