using UnityEngine;

namespace CityFlow.Environment
{
    public abstract class EnvironmentVisualModule : MonoBehaviour
    {
        [SerializeField] private bool visibleAtStartup = true;

        public bool IsVisualEnabled { get; private set; }
        protected EnvironmentVisualSystem VisualSystem { get; private set; }

        internal void InitializeModule(EnvironmentVisualSystem visualSystem)
        {
            if (VisualSystem != null)
            {
                return;
            }

            VisualSystem = visualSystem;
            IsVisualEnabled = visibleAtStartup;
            OnModuleInitialized();
            OnVisualStateChanged(IsVisualEnabled);
        }

        internal void TickModule(float unscaledDeltaTime)
        {
            if (!IsVisualEnabled || !isActiveAndEnabled)
            {
                return;
            }

            OnVisualTick(unscaledDeltaTime);
        }

        internal void ShutdownModule()
        {
            if (VisualSystem == null)
            {
                return;
            }

            OnModuleShutdown();
            VisualSystem = null;
        }

        public void SetVisualEnabled(bool isEnabled)
        {
            if (IsVisualEnabled == isEnabled)
            {
                return;
            }

            IsVisualEnabled = isEnabled;
            OnVisualStateChanged(isEnabled);
        }

        protected abstract void OnModuleInitialized();
        protected abstract void OnVisualTick(float unscaledDeltaTime);
        protected abstract void OnVisualStateChanged(bool isEnabled);
        protected abstract void OnModuleShutdown();

        // Unity setup:
        // Add derived visual modules below EnvironmentVisualSystem.prefab.
    }
}
