using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CityFlow.Managers
{
    public sealed class SoundHandleCache_cmt
    {
        private readonly Dictionary<string, AsyncOperationHandle<AudioClip>> loadedHandles = new();
        private readonly Dictionary<string, Task<AudioClip>> loadingTasks = new();

        public Task<AudioClip> LoadAsync(SoundCatalog_cmt.SoundEntry sound)
        {
            if (sound == null || string.IsNullOrWhiteSpace(sound.Id))
            {
                return Task.FromResult<AudioClip>(null);
            }

            if (loadedHandles.TryGetValue(sound.Id, out AsyncOperationHandle<AudioClip> loadedHandle)
                && loadedHandle.IsValid()
                && loadedHandle.Status == AsyncOperationStatus.Succeeded)
            {
                return Task.FromResult(loadedHandle.Result);
            }

            if (loadingTasks.TryGetValue(sound.Id, out Task<AudioClip> loadingTask))
            {
                return loadingTask;
            }

            Task<AudioClip> task = LoadInternalAsync(sound);
            loadingTasks.Add(sound.Id, task);
            return task;
        }

        public void Release(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            loadingTasks.Remove(id);

            if (!loadedHandles.TryGetValue(id, out AsyncOperationHandle<AudioClip> handle))
            {
                return;
            }

            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }

            loadedHandles.Remove(id);
        }

        public void ReleaseAll()
        {
            foreach (AsyncOperationHandle<AudioClip> handle in loadedHandles.Values)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            loadedHandles.Clear();
            loadingTasks.Clear();
        }

        private async Task<AudioClip> LoadInternalAsync(SoundCatalog_cmt.SoundEntry sound)
        {
            AssetReferenceT<AudioClip> reference = sound.Clip;

            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                loadingTasks.Remove(sound.Id);
                return null;
            }

            AsyncOperationHandle<AudioClip> handle = reference.LoadAssetAsync();
            loadedHandles[sound.Id] = handle;

            await handle.Task;
            loadingTasks.Remove(sound.Id);

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Release(sound.Id);
                return null;
            }

            return handle.Result;
        }
    }
}
