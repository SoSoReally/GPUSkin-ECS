using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DOTS;
using GPUSkin;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEditor.Animations;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.VirtualTexturing;

namespace GPUSkin
{


    public static partial class GPUSkinUtility
    {
        public static void SetupEntity(Entity entity, GPUSkinSystem gpuSkinSystem, World world, GPUSkinAsset gPUSkinAsset, Material material)
        {
            GPUSkinSystem system = gpuSkinSystem;

            if (!system.TryGet(gPUSkinAsset.GetHashCode(), out BlobAssetReference<GPUAnimator> animtor))
            {
                animtor = GPUAnimator.Build(gPUSkinAsset);
                system.Register(gPUSkinAsset.GetHashCode(), animtor);
            }
            var entityManager = world.EntityManager;
            entityManager.AddComponent(entity, new ComponentTypeSet(system.RqueristComponentType));
            var shaderName = material.shader.name;

            var ac = new AnimationController()
            {
                currentState = new AnimationState()
                {
                    clip = new GPUAnimationClipData(animtor.Value.Clips[0]),
                    currentFrame = 0,
                    index = 3,
                },
                speed = 1f,
                isLerp = false,
                CanTranition = false,
            };
            switch (shaderName)
            {
                case GPUSkin: break;
                case GPUSkinLerp:
                    entityManager.AddComponent(entity, new ComponentTypeSet(system.RqueristComponentTypeLerp));
                    ac.isLerp = true;
                    break;
                case GPUSkinTranition:
                    ac.CanTranition = true;
                    entityManager.AddComponent(entity, new ComponentTypeSet(system.RqueristComponentTypeTransition));
                    break;
                case GPUSkinLerpAndTranition:
                    entityManager.AddComponent(entity, new ComponentTypeSet(system.RqueristComponentTypeLerp));
                    entityManager.AddComponent(entity, new ComponentTypeSet(system.RqueristComponentTypeTransition));
                    ac.CanTranition = true;
                    ac.isLerp = true;
                    break;
                default: break;
            }
            entityManager.SetComponentData<BlobAssertReferenceGPUAnimator>(entity, new BlobAssertReferenceGPUAnimator() { Animator = animtor });
            entityManager.SetComponentData(entity, ac);
        }

        public static AnimationState ClipConvertState(GPUAnimationClip clip)
        {

            return new AnimationState()
            {
                clip = new GPUAnimationClipData()
                {
                    frameRate = clip.frameRate,
                    length = clip.length,
                    isLoop = clip.isLoop,
                    start = clip.startFrame,
                    normalizeLength = clip.normalizeLength,
                },
                currentFrame = 0,
                index = clip.GetHashCode(),
                travelTime = 0,
            };
        }
    }


    public partial class GPUSkinSystem : SystemBase
    {
        private struct GPUAnimatorMap : IDisposable
        {
            public NativeHashMap<int, BlobAssetReference<GPUAnimator>> AnimatorMap;

            public GPUAnimatorMap(int capactiy)
            {
                AnimatorMap = new NativeHashMap<int, BlobAssetReference<GPUAnimator>>(capactiy, Allocator.Persistent);
            }

            public void Dispose()
            {
                AnimatorMap.Dispose();
            }
        }


        public ComponentType[] RqueristComponentType = new ComponentType[] { ComponentType.ReadOnly<CurrentFrame>(), ComponentType.ReadOnly<Tag>(), ComponentType.ReadOnly<AnimationController>(), ComponentType.ReadOnly<BlobAssertReferenceGPUAnimator>() };
        public ComponentType[] RqueristComponentTypeLerp = new ComponentType[] { ComponentType.ReadOnly<LerpFrame>() };
        public ComponentType[] RqueristComponentTypeTransition = new ComponentType[] { ComponentType.ReadOnly<Transition>(), ComponentType.ReadOnly<TransitionFrame>() };


        //public BlobAssetUtility
        private GPUAnimatorMap AnimationMapInstance;

        protected override void OnCreate()
        {
            AnimationMapInstance = new GPUAnimatorMap(10);
            // QueryAnimationPlayer = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp).WithAll<Tag>().WithAllRW<AniamtionPlay>().WithAll<CurrentFrame>());

        }


        protected override void OnUpdate()
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            Dependency = new TransitionJob() { deltaTime = deltaTime }.Schedule(Dependency);
            Dependency = new PlayAnimationJobNoLerp() { deltaTime = deltaTime }.Schedule(Dependency);
            Dependency = new PlayAnimationJobLerp() { deltaTime = deltaTime }.Schedule(Dependency);
        }

        protected override void OnDestroy()
        {
            AnimationMapInstance.Dispose();
            base.OnDestroy();
        }
        public bool Register(int hashCode, BlobAssetReference<GPUAnimator> blobAssetReference)
        {
           return AnimationMapInstance.AnimatorMap.TryAdd(hashCode, blobAssetReference);
        }
        public BlobAssetReference<GPUAnimator> Get(int hashcode)
        {
            return AnimationMapInstance.AnimatorMap[hashcode];
        }
        public bool TryGet(int index,out BlobAssetReference<GPUAnimator> result)
        {
            return  AnimationMapInstance.AnimatorMap.TryGetValue(index, out result);
        }


        [WithAll(typeof(Tag))]
        public partial struct TransitionJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            [ReadOnly]
            public float deltaTime;
            [NativeDisableUnsafePtrRestriction]
            public EnabledMask EnabledMask;
            public void Execute(
                [EntityIndexInChunk]int entityIndex,
                ref TransitionFrame nextFrame,
                ref Transition transition,
                ref AnimationController animation,
                ref AnimationTransition animationTransition)
            {
                animationTransition.trvael -= deltaTime * animation.speed;
                if (animationTransition.trvael >= animationTransition.normalizeLength)
                {
                    var enableMask = EnabledMask.GetEnabledRefRW<AnimationTransition>(entityIndex);
                    animation.currentState = animationTransition.nextState;
                    animation.currentState.currentFrame = nextFrame.value;
                    animation.currentState.travelTime = (nextFrame.value - animation.currentState.clip.start) / animationTransition.nextState.clip.frameRate;
                    nextFrame.value = 0;
                    transition.value = new half(0);
                    animationTransition.trvael = 0;
                    enableMask.ValueRW = false;
                }
                else
                {
                    transition.value = Mathf.Clamp01(animationTransition.trvael);
                }
            }

            public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                EnabledMask = chunk.GetEnabledMask(ref this.__GPUSkin_AnimationTransitionComponentTypeHandle);
                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
                return;
            }
        }

        [WithAll(typeof(Tag))]
        [WithOptions(EntityQueryOptions.FilterWriteGroup)]
        public partial struct PlayAnimationJobNoLerp : IJobEntity
        {
            [ReadOnly]
            public float deltaTime;

            public void Execute(
                ref CurrentFrame currentFrame,
                ref AnimationController animation)
            {
                ref readonly var clip = ref animation.currentState.clip;
                ref var state = ref animation.currentState;
                currentFrame.value = state.currentFrame;

                state.travelTime += (deltaTime * animation.speed);



                if (clip.isLoop)
                {
                    state.travelTime %= clip.normalizeLength;
                }
                else
                {
                    state.travelTime = math.min(state.travelTime, clip.normalizeLength);
                }
                var frame = state.travelTime * clip.frameRate;
                state.currentFrame = clip.start + Mathf.FloorToInt(frame);
                state.currentFrame = math.clamp(state.currentFrame, clip.start, clip.start + clip.length - 1);
            }

        }
        [WithAll(typeof(Tag))]
        public partial struct PlayAnimationJobLerp : IJobEntity
        {
            [ReadOnly]
            public float deltaTime;

            public void Execute(
                ref CurrentFrame currentFrame,
                ref LerpFrame lerpFrame,
                ref AnimationController animation)
            {
                ref readonly var clip = ref animation.currentState.clip;
                ref var state = ref animation.currentState;

                float cliplength = clip.length;

                state.travelTime += (deltaTime * animation.speed);
                var travelFrame = state.travelTime * clip.frameRate;
                if (clip.isLoop)
                {
                    lerpFrame.value = travelFrame + 1;
                    travelFrame %= cliplength;
                    lerpFrame.value %= cliplength;
                }
                else
                {
                    travelFrame = math.min(travelFrame, clip.length - 0.0001f);
                    lerpFrame.value = 0;
                }
                state.currentFrame = clip.start + travelFrame;
                currentFrame.value = state.currentFrame;
                lerpFrame.value += clip.start;

            }
        }
    }


}