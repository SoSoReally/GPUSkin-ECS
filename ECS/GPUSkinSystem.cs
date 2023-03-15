using System;
using System.Linq;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;

namespace GPUSkin
{


    public static partial class GPUSkinUtility
    {
        public static void SetupEntity(NativeArray<Entity> entitys,World world, GPUSkinAsset gPUSkinAsset, Material material)
        {
            BlobAssetReference<GPUAnimator> animtor = GPUAnimator.Build(gPUSkinAsset);
            var entityManager = world.EntityManager;
            entityManager.AddComponent(entitys, new ComponentTypeSet(GPUSkinSystem.RqueristComponentType));
            var shaderName = material.shader.name;

            var ac = new AnimationController()
            {
                currentState = new AnimationState()
                {
                    clip = new GPUAnimationClipData(animtor.Value.Clips[0]),
                    currentFrame = 0,
                    index = 0,
                },
                speed = 1f,
                isLerp = false,
                CanTranition = false,
            };
           
            switch (shaderName)
            {
                case GPUSkin: break;
                case GPUSkinLerp:
                    entityManager.AddComponent(entitys, new ComponentTypeSet(GPUSkinSystem.RqueristComponentTypeLerp));
                    ac.isLerp = true;
                    break;
                case GPUSkinTranition:
                    ac.CanTranition = true;
                    entityManager.AddComponent(entitys, new ComponentTypeSet(GPUSkinSystem.RqueristComponentTypeTransition));
                    break;
                case GPUSkinLerpAndTranition:
                    entityManager.AddComponent(entitys, new ComponentTypeSet(GPUSkinSystem.RqueristComponentTypeLerp));
                    entityManager.AddComponent(entitys, new ComponentTypeSet(GPUSkinSystem.RqueristComponentTypeTransition));
                    ac.CanTranition = true;
                    ac.isLerp = true;
                    break;
                default: break;
            }
            for (int i = 0; i < entitys.Length; i++)
            {
                var entity = entitys[i];
                entityManager.SetComponentData<BlobAssertReferenceGPUAnimator>(entity, new BlobAssertReferenceGPUAnimator() { Animator = animtor });
                entityManager.SetComponentData(entity, ac);
            }
        }
        public static void SetAnimationByIndex(int index,Entity entity ,EntityManager entityManager)
        {
            entityManager.SetComponentData(entity, GetAnimationController(index,entity,entityManager));
        }

        public static AnimationController GetAnimationController(int index,Entity entity, EntityManager entityManager) 
        {
            var Animator = entityManager.GetComponentData<BlobAssertReferenceGPUAnimator>(entity);
            return GetAnimationController(index, Animator);
        }

        public static AnimationController GetAnimationController(int index, BlobAssertReferenceGPUAnimator blobAssertReferenceGPUAnimator)
        {
            var ac = new AnimationController()
            {
                currentState = new AnimationState()
                {
                    clip = new GPUAnimationClipData(blobAssertReferenceGPUAnimator.Animator.Value.Clips[index]),
                    currentFrame = 0,
                    index = index,
                },
                speed = 1f,
                isLerp = false,
                CanTranition = false,
            };
            return ac;
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

    public readonly partial struct AnimationTransitionAspect : IAspect
    {
        public readonly RefRW<TransitionFrame> nextFrame;
        public readonly RefRW<Transition> transition;
        public readonly RefRW<AnimationController> animation;
        public readonly RefRW<AnimationTransition> animationTransition;
        public readonly EnabledRefRW<AnimationTransition> enabledRefRWanimationTransition;
    }

    public partial class GPUSkinSystem : SystemBase
    {
        public readonly static ComponentType[] RqueristComponentType = new ComponentType[] { ComponentType.ReadOnly<CurrentFrame>(), ComponentType.ReadOnly<Tag>(), ComponentType.ReadOnly<AnimationController>(), ComponentType.ReadOnly<BlobAssertReferenceGPUAnimator>() };
        public readonly static ComponentType[] RqueristComponentTypeLerp = new ComponentType[] { ComponentType.ReadOnly<LerpFrame>() };
        public readonly static ComponentType[] RqueristComponentTypeTransition = new ComponentType[] { ComponentType.ReadOnly<Transition>(), ComponentType.ReadOnly<TransitionFrame>() };


        public AnimationTransitionAspect.TypeHandle typeHandle;
        public EntityQuery AnimationTransitionQuery;
        protected override void OnCreate()
        {
            AnimationTransitionQuery = SystemAPI.QueryBuilder().WithAspect<AnimationTransitionAspect>().WithAll<Tag>().WithAll<PerInstanceCullingTag>().Build();
            typeHandle = new AnimationTransitionAspect.TypeHandle(ref this.CheckedStateRef, false);
        }
        protected override void OnUpdate()
        {
            typeHandle.Update(ref this.CheckedStateRef);
            var deltaTime = SystemAPI.Time.DeltaTime;
            Dependency = new TransitionJobx() { TypeHandle = typeHandle, deltaTime = deltaTime }.Schedule(AnimationTransitionQuery,Dependency);
            Dependency = new PlayAnimationJobNoLerp() { deltaTime = deltaTime }.Schedule(Dependency);
            Dependency = new PlayAnimationJobLerp() { deltaTime = deltaTime }.Schedule(Dependency);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        public partial struct TransitionJobx : IJobChunk
        {
            [ReadOnly]
            public float deltaTime;
            public AnimationTransitionAspect.TypeHandle TypeHandle;
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var valueArray = TypeHandle.Resolve(chunk);
                ChunkEntityEnumerator chunkEntityEnumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (chunkEntityEnumerator.NextEntityIndex(out int nextIndex))
                {
                    var c = valueArray[nextIndex];
                    c.enabledRefRWanimationTransition.ValueRW = false;
                    RefExecute(ref c.nextFrame.ValueRW,
                        ref c.transition.ValueRW,
                        ref c.animation.ValueRW,
                        ref c.animationTransition.ValueRW,
                        c.enabledRefRWanimationTransition,
                        deltaTime);
                 }


            }
            public void RefExecute(
                ref TransitionFrame nextFrame,
                ref Transition transition,
                ref AnimationController animation,
                ref AnimationTransition animationTransition,
                EnabledRefRW<AnimationTransition> enabledRefRW,
                float deltaTime)
            {
                    animationTransition.trvael -= deltaTime * animation.speed;
                    if (animationTransition.trvael >= animationTransition.normalizeLength)
                    {
                        animation.currentState = animationTransition.nextState;
                        animation.currentState.currentFrame = nextFrame.value;
                        animation.currentState.travelTime = (nextFrame.value - animation.currentState.clip.start) / animationTransition.nextState.clip.frameRate;
                        nextFrame.value = 0;
                        transition.value = new half(0);
                        animationTransition.trvael = 0;
                        enabledRefRW.ValueRW = false;
                    }
                    else
                    {
                        transition.value = Mathf.Clamp01(animationTransition.trvael);
                    }
                }
            }
        }
        


        [WithAll(typeof(Tag))]
        [WithAll(typeof(PerInstanceCullingTag))]
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

    [   WithAll(typeof(PerInstanceCullingTag))]
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


