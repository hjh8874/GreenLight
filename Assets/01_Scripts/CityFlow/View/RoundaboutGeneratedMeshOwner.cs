using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.View
{
    [ExecuteAlways]
    internal sealed class RoundaboutGeneratedMeshOwner : MonoBehaviour
    {
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials =
            new List<Material>();

        public void Track(Mesh mesh)
        {
            if (mesh != null)
            {
                meshes.Add(mesh);
            }
        }

        public void Track(Material material)
        {
            if (material != null)
            {
                materials.Add(material);
            }
        }

        internal void Release()
        {
            Mesh[] ownedMeshes = meshes.ToArray();
            meshes.Clear();
            for (int i = 0; i < ownedMeshes.Length; i++)
            {
                Mesh mesh = ownedMeshes[i];
                if (mesh == null)
                {
                    continue;
                }

                if (Application.IsPlaying(gameObject))
                {
                    Destroy(mesh);
                }
                else
                {
                    DestroyImmediate(mesh);
                }
            }

            Material[] ownedMaterials = materials.ToArray();
            materials.Clear();
            for (int i = 0; i < ownedMaterials.Length; i++)
            {
                Material material = ownedMaterials[i];
                if (material == null)
                {
                    continue;
                }

                if (Application.IsPlaying(gameObject))
                {
                    Destroy(material);
                }
                else
                {
                    DestroyImmediate(material);
                }
            }
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}
