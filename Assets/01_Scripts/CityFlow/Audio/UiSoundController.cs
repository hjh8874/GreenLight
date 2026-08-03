using System.Collections.Generic;
using CityFlow.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace CityFlow.Audio
{
    public sealed class UiSoundController : MonoBehaviour
    {
        [SerializeField] private SoundManager soundManager;
        [SerializeField] private AudioMixerGroup uiOutput;

        private readonly List<RaycastResult> raycastResults = new();
        private AudioSource uiSource;

        private void Awake()
        {
            soundManager ??= GetComponent<SoundManager>();
            GameObject child = new GameObject("UI SFX");
            child.transform.SetParent(transform, false);
            uiSource = child.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.loop = false;
            uiSource.spatialBlend = 0f;
            uiSource.outputAudioMixerGroup = uiOutput;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            EventSystem eventSystem = EventSystem.current;
            if (mouse == null ||
                eventSystem == null ||
                !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            PointerEventData pointer = new(eventSystem)
            {
                position = mouse.position.ReadValue()
            };
            raycastResults.Clear();
            eventSystem.RaycastAll(pointer, raycastResults);

            for (int index = 0; index < raycastResults.Count; index++)
            {
                GameObject target = raycastResults[index].gameObject;
                Selectable selectable = target != null
                    ? target.GetComponentInParent<Selectable>()
                    : null;
                if (selectable == null || !selectable.IsInteractable())
                {
                    continue;
                }

                if (selectable is Button || selectable is Toggle)
                {
                    if (soundManager != null &&
                        soundManager.TryGetSfx(
                            SoundIds.UiClick,
                            out AudioClip clip,
                            out float volume))
                    {
                        uiSource.PlayOneShot(clip, volume);
                    }
                    return;
                }
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            SoundManager manager,
            AudioMixerGroup output)
        {
            soundManager = manager;
            uiOutput = output;
        }
#endif

        // Unity setup:
        // No per-button wiring is required. The baked prefab observes EventSystem clicks.
    }
}
