using System.Collections;
using System.Collections.Generic;
using GPUSkin;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering.VirtualTexturing;

public class GPUSkinAssetsBake : MonoBehaviour
{
    public GPUSkinAsset GPUSkinAsset;

    public class GPUSkinAssetsBaker: Baker<GPUSkinAssetsBake>
    {
        public override void Bake(GPUSkinAssetsBake authoring)
        {
            GPUSkinSystem system = this._State.World.GetOrCreateSystemManaged<GPUSkinSystem>();
            GPUSkinUtility.SetupEntity(GetEntity(TransformUsageFlags.Default), system, this._State.World, authoring.GPUSkinAsset, authoring.GetComponent<MeshRenderer>().sharedMaterial);
        }

    }
}
