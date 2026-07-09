using System;
using TMPro;
using UnityEngine;

namespace GreenLight.Radio.Runtime
{
    public sealed class RadioDummyPlayer : MonoBehaviour
    {
        [SerializeField] private RadioService radioService;
        [SerializeField] private TMP_Text outputText;

        public event Action<string> StatusChanged;

        private void Awake()
        {
            if (radioService == null)
            {
                radioService = GetComponent<RadioService>();
            }
        }

        private void OnEnable()
        {
            Bind();
        }

        private void OnDisable()
        {
            Unbind();
        }

        public void Configure(RadioService radioService, TMP_Text outputText)
        {
            Unbind();
            this.radioService = radioService;
            this.outputText = outputText;
            Bind();
        }

        private void OnPlayRequested(RadioPlaybackRequest request)
        {
            PublishStatus(
                $"Dummy play: slot {request.SlotIndex}, videoId={request.YoutubeVideoId}, name={request.DisplayName}");
        }

        private void OnPauseRequested()
        {
            PublishStatus("Dummy pause requested.");
        }

        private void Bind()
        {
            if (radioService == null)
            {
                return;
            }

            radioService.PlayRequested -= OnPlayRequested;
            radioService.PauseRequested -= OnPauseRequested;
            radioService.PlayRequested += OnPlayRequested;
            radioService.PauseRequested += OnPauseRequested;
        }

        private void Unbind()
        {
            if (radioService == null)
            {
                return;
            }

            radioService.PlayRequested -= OnPlayRequested;
            radioService.PauseRequested -= OnPauseRequested;
        }

        private void PublishStatus(string message)
        {
            if (outputText != null)
            {
                outputText.text = message;
            }

            StatusChanged?.Invoke(message);
            Debug.Log($"[RadioDummyPlayer] {message}");
        }

        // Unity setup: Attach this beside RadioService until a real WebView/IFrame player replaces it.
    }
}
