using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEditor.Animations;
using UnityEngine;
using static Unity.Collections.AllocatorManager;

namespace GPUSkin
{
    public readonly ref struct PlayAnimation
    {
        public PlayAnimation(int index, float transitionTime, in Entity entity, EntityManager entityManager )
        {
            var ac = entityManager.GetComponentData<AnimationController>(entity);
            var map = entityManager.GetComponentData<BlobAssertReferenceGPUAnimator>(entity);
            var next = GPUSkinUtility.ClipConvertState(map.Animator.Value.Clips[index]);
            if (ac.CanTranition)
            {

                entityManager.SetComponentEnabled<AnimationTransition>(entity, true);
                var animationTransition = new AnimationTransition() { nextState = next, normalizeLength = transitionTime };
                var noramllength = (transitionTime % next.clip.normalizeLength) * next.clip.frameRate;
                var transitionFrame = new TransitionFrame()
                {
                    value = next.clip.start + math.ceil(noramllength)
                };

                entityManager.SetComponentData<AnimationTransition>(entity, animationTransition);
                entityManager.SetComponentData<TransitionFrame>(entity, transitionFrame);
            }
            else
            {
                ac.currentState = next;
                entityManager.SetComponentData<AnimationController>(entity, ac);
            }
        }
        public PlayAnimation(float speed, in Entity entity, EntityManager entityManager)
        {
            var ac = entityManager.GetComponentData<AnimationController>(entity);
            ac.speed = speed;
            entityManager.SetComponentData<AnimationController>(entity, ac);
        }
    }
    public struct GPUAnimator
    {
        public BlobArray<GPUAnimationClip> Clips;
        public static BlobAssetReference<GPUAnimator> Build(Span<GPUAnimationClip> gPUAnimationClips)
        {
            using var bb = new BlobBuilder(Allocator.Temp);

            ref var array = ref bb.ConstructRoot<BlobArray<GPUAnimationClip>>();
            var blodarray = bb.Allocate(ref array, gPUAnimationClips.Length);
            for (int i = 0; i < blodarray.Length; i++)
            {
                blodarray[i] = gPUAnimationClips[i];
            }
            return bb.CreateBlobAssetReference<GPUAnimator>(Allocator.Persistent);
        }
        public static BlobAssetReference<GPUAnimator> Build(GPUSkinAsset gPUSkinAsset)
        {
            return Build(gPUSkinAsset.Clips.Select((a) => { return a.GPUClip; }).ToArray());
        }

    }
    [WriteGroup(typeof(CurrentFrame))]
    [MaterialProperty("_CurrentFrame")]
    public partial struct CurrentFrame : IComponentData
    {
    }
    [MaterialProperty("_LerpFrame")]
    [WriteGroup(typeof(CurrentFrame))]
    public partial struct LerpFrame : IComponentData
    {
    }
    [MaterialProperty("_TransitionFrame")]
    public partial struct TransitionFrame : IComponentData
    {
    }
    [MaterialProperty("_Transition")]
    public partial struct Transition : IComponentData
    {

    }

    public partial struct AnimationController : IComponentData
    {

    }
    public partial struct AnimationTransition : IComponentData, IEnableableComponent
    {

    }




    public struct Tag : IComponentData { }

    public struct BlobAssertReferenceGPUAnimator : IComponentData
    {
        public BlobAssetReference<GPUAnimator> Animator;
    }
}

