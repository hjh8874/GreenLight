using System.Collections.Generic;
using CityFlow.View;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.Audio
{
    [DefaultExecutionOrder(-10000)]
    [RequireComponent(typeof(AudioListener))]
    public sealed class AudioListenerFollower : MonoBehaviour
    {
        [SerializeField] private AudioListener managedListener;

        private readonly List<AudioListener> disabledListeners = new();
        private MainCityView cityView;
        private float nextResolveTime;

        private void Awake()
        {
            ResolveManagedListener();
            DisableCompetingListeners();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnEnable()
        {
            ResolveManagedListener();
        }

        private void LateUpdate()
        {
            ResolveManagedListener();

            if (cityView == null && Time.unscaledTime >= nextResolveTime)
            {
                cityView = FindAnyObjectByType<MainCityView>();
                nextResolveTime = Time.unscaledTime + 1f;
            }

            Camera activeCamera = cityView != null
                ? cityView.ActiveViewCamera
                : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            transform.SetPositionAndRotation(
                activeCamera.transform.position,
                activeCamera.transform.rotation);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            for (int index = 0; index < disabledListeners.Count; index++)
            {
                AudioListener listener = disabledListeners[index];
                if (listener != null && listener != managedListener)
                {
                    listener.enabled = true;
                }
            }
        }

        private void OnSceneLoaded(Scene _, LoadSceneMode __)
        {
            cityView = null;
            nextResolveTime = 0f;
            DisableCompetingListeners();
        }

        private void DisableCompetingListeners()
        {
            ResolveManagedListener();
            AudioListener[] listeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include);

            for (int index = 0; index < listeners.Length; index++)
            {
                AudioListener listener = listeners[index];
                if (listener == null ||
                    ReferenceEquals(listener, managedListener) ||
                    !listener.enabled)
                {
                    continue;
                }

                listener.enabled = false;
                if (!disabledListeners.Contains(listener))
                {
                    disabledListeners.Add(listener);
                }
            }
        }

        private void ResolveManagedListener()
        {
            if (managedListener == null)
            {
                managedListener = GetComponent<AudioListener>();
            }

            if (managedListener == null)
            {
                managedListener = gameObject.AddComponent<AudioListener>();
                Debug.LogWarning(
                    "[AudioListenerFollower] Missing managed listener was restored.",
                    this);
            }

            if (!managedListener.enabled)
            {
                managedListener.enabled = true;
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(AudioListener listener)
        {
            managedListener = listener;
        }
#endif

        // Unity setup:
        // This component is included in the baked SoundSystem prefab.
        // It follows the active city or drive-view camera automatically.
    }
}
