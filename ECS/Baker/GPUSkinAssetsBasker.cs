using Unity.Collections;
using Unity.Entities;
using UnityEngine;
namespace GPUSkin
{
    public class GPUSkinAssetsBake : MonoBehaviour
    {
        public GPUSkinAsset GPUSkinAsset;

        public class GPUSkinAssetsBaker : Baker<GPUSkinAssetsBake>
        {
            public override void Bake(GPUSkinAssetsBake authoring)
            {
                NativeArray<Entity> entities = new NativeArray<Entity>(1, Allocator.Temp);
                entities[0] = GetEntity(TransformUsageFlags.Default);
                GPUSkinUtility.SetupEntity(entities, this._State.World, authoring.GPUSkinAsset, authoring.GetComponent<MeshRenderer>().sharedMaterial);
            }

        }
    }
}