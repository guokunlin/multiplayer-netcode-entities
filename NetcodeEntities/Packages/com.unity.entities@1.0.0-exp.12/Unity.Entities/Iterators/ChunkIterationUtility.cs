using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine.Assertions;

namespace Unity.Entities
{
    /// <summary>
    ///     Enables iteration over chunks belonging to a set of archetypes.
    /// </summary>
    [BurstCompile]
    [GenerateTestsForBurstCompatibility(RequiredUnityDefine = "!NET_DOTS")]
    internal unsafe partial struct ChunkIterationUtility
    {
        /// <summary>
        /// Creates a NativeArray with all the chunks in a given archetype filtered by the provided EntityQueryFilter.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="jobHandle">Handle to the GatherChunks job used to fill the output array.</param>
        /// <param name="filter">Filter used to filter the resulting chunks</param>
        /// <param name="dependsOn">All jobs spawned will depend on this JobHandle</param>
        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        [Obsolete("Remove with CreateArchetypeChunkArrayAsync. (RemovedAfter Entities 1.0)")]
        public static NativeArray<ArchetypeChunk> CreateArchetypeChunkArrayAsync(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator, out JobHandle jobHandle, ref EntityQueryFilter filter,
            JobHandle dependsOn = default(JobHandle))
        {
            var archetypeCount = matchingArchetypes.Length;

            var offsets =
                new NativeArray<int>(archetypeCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var chunkCount = 0;
            {
                var ptrs = matchingArchetypes.Ptr;
                for (int i = 0; i < archetypeCount; ++i)
                {
                    var archetype = ptrs[i]->Archetype;
                    offsets[i] = chunkCount;
                    chunkCount += archetype->Chunks.Count;
                }
            }

            if (!filter.RequiresMatchesFilter)
            {
                var chunks = CollectionHelper.CreateNativeArray<ArchetypeChunk>(chunkCount, allocator, NativeArrayOptions.UninitializedMemory);
                var gatherChunksJob = new GatherChunksToArrayJob
                {
                    MatchingArchetypes = matchingArchetypes.Ptr,
                    entityComponentStore = matchingArchetypes.entityComponentStore,
                    Offsets = offsets,
                    Chunks = chunks
                };
                jobHandle = gatherChunksJob.Schedule(archetypeCount, 1, dependsOn);

                return chunks;
            }
            else
            {
                var filteredCounts =  new NativeArray<int>(archetypeCount + 1, Allocator.TempJob);
                var sparseChunks = new NativeArray<ArchetypeChunk>(chunkCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var gatherChunksJob = new GatherChunksToArrayWithFilteringJob
                {
                    MatchingArchetypes = matchingArchetypes.Ptr,
                    Filter = filter,
                    Offsets = offsets,
                    FilteredCounts = filteredCounts,
                    SparseChunks = sparseChunks,
                    entityComponentStore = matchingArchetypes.entityComponentStore
                };
                gatherChunksJob.Schedule(archetypeCount, 1, dependsOn).Complete();

                // accumulated filtered counts: filteredCounts[i] becomes the destination offset
                int totalChunks = 0;
                for (int i = 0; i < archetypeCount; ++i)
                {
                    int currentCount = filteredCounts[i];
                    filteredCounts[i] = totalChunks;
                    totalChunks += currentCount;
                }
                filteredCounts[archetypeCount] = totalChunks;

                var joinedChunks = CollectionHelper.CreateNativeArray<ArchetypeChunk>(totalChunks, allocator, NativeArrayOptions.UninitializedMemory);

                jobHandle = new JoinChunksJob
                {
                    DestinationOffsets = filteredCounts,
                    SparseChunks = sparseChunks,
                    Offsets = offsets,
                    JoinedChunks = joinedChunks
                }.Schedule(archetypeCount, 1);

                return joinedChunks;
            }
        }

        [BurstCompile]
        public static void ToArchetypeChunkList(in UnsafeCachedChunkList cachedChunkList,
            in UnsafeMatchingArchetypePtrList matchingArchetypes, ref EntityQueryFilter filter, int doesRequireBatching,
            ref NativeList<ArchetypeChunk> outChunks)
        {
            var cachedChunksPtr = cachedChunkList.Ptr;
            var matchingArchetypesPtr = matchingArchetypes.Ptr;
            var requiresFilter = filter.RequiresMatchesFilter;
            var requiresBatching = doesRequireBatching == 1;
            var chunkMatchingArchetypeIndexPtr = cachedChunkList.PerChunkMatchingArchetypeIndex->Ptr;
            var chunkIndexInArchetypePtr = cachedChunkList.ChunkIndexInArchetype->Ptr;
            int cachedChunkCount = cachedChunkList.Length;
            int currentMatchingArchetypeIndex = -1;
            MatchingArchetype* currentMatchingArchetype = null;
            var currentMatchingArchetypeState = default(EnabledMaskMatchingArchetypeState);
            int* currentArchetypeChunkEntityCountsPtr = null;
            var ecs = cachedChunkList.EntityComponentStore;
            // Fast path if no filtering at all is required
            if (!requiresFilter && !requiresBatching)
            {
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    outChunks.AddNoResize(new ArchetypeChunk(cachedChunksPtr[chunkIndexInCache], ecs));
                }
            }
            else if (requiresBatching)
            {
                // per-entity + per-chunk filtering
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                        var currentArchetype = currentMatchingArchetype->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                        currentMatchingArchetypeState = new EnabledMaskMatchingArchetypeState(currentMatchingArchetype);
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (requiresFilter && !currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;
                    int chunkEntityCount = currentArchetypeChunkEntityCountsPtr[chunkIndexInArchetype];
                    GetEnabledMask(chunkIndexInArchetype, chunkEntityCount, currentMatchingArchetypeState,
                        out var chunkEnabledMask);
                    if (chunkEnabledMask.ULong0 == 0 && chunkEnabledMask.ULong1 == 0)
                        continue;
                    outChunks.AddNoResize(new ArchetypeChunk(cachedChunksPtr[chunkIndexInCache], ecs));
                }
            }
            else
            {
                // chunk filtering only
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (!currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;
                    outChunks.AddNoResize(new ArchetypeChunk(cachedChunksPtr[chunkIndexInCache], ecs));
                }
            }
        }

        [BurstCompile]
        public static void GatherEntitiesWithBatching(Entity* entities,ref EntityQueryFilter filter,
            in UnsafeCachedChunkList cache, ref UnsafeMatchingArchetypePtrList matchingArchetypePtrList)
        {
            Entity* copyDest = entities;
            var chunkCache = new UnsafeChunkCache(filter, true, cache, matchingArchetypePtrList.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledMask = default;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var chunk, out var chunkEntityCount,
                       out byte useEnableBits, ref chunkEnabledMask))
            {
                Entity* chunkEntities = (Entity*)chunk.m_Chunk->Buffer; // Entity is always the first table in the chunk buffer
                if (useEnableBits == 0)
                {
                    UnsafeUtility.MemCpy(copyDest, chunkEntities, chunkEntityCount * sizeof(Entity));
                    copyDest += chunkEntityCount;
                }
                else
                {
                    // If this branch ever becomes a bottleneck for the many-small-batches case, we could add a third path
                    // that iterates over the chunkEnabledMask directly, similar to what IJobEntity generates.
                    int batchStartIndex = 0;
                    int batchEndIndex = 0;
                    while (EnabledBitUtility.GetNextRange(ref chunkEnabledMask, ref batchStartIndex, ref batchEndIndex))
                    {
                        int batchEntityCount = batchEndIndex - batchStartIndex;
                        UnsafeUtility.MemCpy(copyDest, chunkEntities+batchStartIndex, batchEntityCount * sizeof(Entity));
                        batchStartIndex = batchEndIndex;
                        copyDest += batchEntityCount;
                    }
                }
            }
        }

        [BurstCompile]
        public static void GatherEntities(Entity* entities, in UnsafeCachedChunkList cachedChunkList,
            in UnsafeMatchingArchetypePtrList matchingArchetypes)
        {
            var chunkCache = new UnsafeChunkCache(default, false, cachedChunkList, matchingArchetypes.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledBits = default;
            Entity* copyDest = entities;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var chunk, out int chunkEntityCount,
                       out byte useEnabledBits, ref chunkEnabledBits))
            {
                Entity* copySrc = (Entity*)chunk.m_Chunk->Buffer; // Entity is always the first table in the chunk buffer
                UnsafeUtility.MemCpy(copyDest, copySrc, chunkEntityCount*sizeof(Entity));
                copyDest += chunkEntityCount;
            }
        }

        /// <summary>
        ///     Creates a NativeArray containing the entities in a given EntityQuery.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">An atomic safety handle required by GatherEntitiesJob so it can call GetNativeArray() on chunks.</param>
        /// <param name="entityQuery">EntityQuery to gather entities from.</param>
        /// <param name="entityCount">number of entities to reserve for the returned NativeArray.</param>
        /// <param name="filter">EntityQueryFilter for calculating the length of the output array.</param>
        /// <returns>NativeArray of the entities in a given EntityQuery.</returns>
        public static NativeArray<Entity> CreateEntityArray(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator,
            EntityTypeHandle typeHandle,
            EntityQuery entityQuery,
            int entityCount)
        {
            var cache = entityQuery.__impl->_QueryData->GetMatchingChunkCache();
            var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var requiresFilter = entityQuery.HasFilter();
            var requiresBatching = entityQuery.__impl->_QueryData->DoesQueryRequireBatching != 0;
            if (!requiresFilter && !requiresBatching)
            {
                GatherEntities((Entity*)entities.GetUnsafePtr(), in cache, in matchingArchetypes);
            }
            else
            {
                var filter = entityQuery.__impl->_Filter;
                GatherEntitiesWithBatching((Entity*)entities.GetUnsafePtr(), ref filter, in cache, ref matchingArchetypes);
            }


            return entities;
        }


        [BurstCompile]
        public static Entity* CreateEntityArrayFromEntityArray(
            Entity* entities,
            int entityCount,
            Allocator allocator,
            EntityQueryData* queryData,
            EntityComponentStore* ecs,
            ref EntityQueryMask mask,
            ref EntityTypeHandle typeHandle,
            ref EntityQueryFilter filter,
            out int outEntityArrayLength)
        {
            Entity* res = null;
            if (filter.RequiresMatchesFilter)
            {
                var batches = new UnsafeList<ArchetypeChunk>(0, Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeList<int>(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, true, entities, entityCount, &batches, &matchingArchetypeIndices);

                outEntityArrayLength = 0;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (batch.m_Chunk->MatchesFilter(match, ref filter))
                        outEntityArrayLength += batch.Count;
                }

                res = (Entity*)Memory.Unmanaged.Allocate(UnsafeUtility.SizeOf<Entity>() * outEntityArrayLength, UnsafeUtility.AlignOf<Entity>(), allocator);
                var entityCounter = 0;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (!batch.m_Chunk->MatchesFilter(match, ref filter))
                        continue;

                    var destinationPtr = res + entityCounter;
                    var sourcePtr = batch.GetNativeArray(typeHandle).GetUnsafeReadOnlyPtr();
                    var copySizeInBytes = sizeof(Entity) * batch.Count;

                    UnsafeUtility.MemCpy(destinationPtr, sourcePtr, copySizeInBytes);

                    entityCounter += batch.Count;
                }

                batches.Dispose();
                matchingArchetypeIndices.Dispose();
            }
            else
            {
                outEntityArrayLength = CalculateEntityCountInEntityArray(entities, entityCount, queryData, ecs, ref mask, ref filter);
                res = (Entity*)Memory.Unmanaged.Allocate(UnsafeUtility.SizeOf<Entity>() * outEntityArrayLength, UnsafeUtility.AlignOf<Entity>(), allocator);

                var entityCounter = 0;
                for (int i = 0; i < entityCount; ++i)
                {
                    var entity = entities[i];
                    if (mask.MatchesIgnoreFilter(entity))
                        res[entityCounter++] = entity;
                }
            }

            return res;
        }

        [BurstCompile]
        public static byte* CreateComponentDataArrayFromEntityArray(
            Entity* entities,
            int entityCount,
            Allocator allocator,
            EntityQueryData* queryData,
            EntityComponentStore* ecs,
            TypeIndex typeIndex,
            int typeSizeInChunk,
            int typeAlign,
            ref EntityQueryMask mask,
            ref EntityQueryFilter filter,
            out int outEntityArrayLength)
        {
            byte* res = null;
            if (filter.RequiresMatchesFilter)
            {
                var batches = new UnsafeList<ArchetypeChunk>(0, Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeList<int>(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, true, entities, entityCount, &batches, &matchingArchetypeIndices);

                outEntityArrayLength = 0;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (batch.m_Chunk->MatchesFilter(match, ref filter))
                        outEntityArrayLength += batch.Count;
                }

                res = (byte*)Memory.Unmanaged.Allocate(typeSizeInChunk * outEntityArrayLength, typeAlign, allocator);
                var outDataOffsetInBytes = 0;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (!batch.m_Chunk->MatchesFilter(match, ref filter))
                        continue;

                    var archetype = batch.Archetype.Archetype;
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
                    var typeOffset = archetype->Offsets[indexInTypeArray];

                    var src = batch.m_Chunk->Buffer + typeOffset;
                    var dst = res + (outDataOffsetInBytes * typeSizeInChunk);
                    var copySize = typeSizeInChunk * batch.Count;

                    UnsafeUtility.MemCpy(dst, src, copySize);
                    outDataOffsetInBytes += copySize;
                }

                batches.Dispose();
                matchingArchetypeIndices.Dispose();
            }
            else
            {
                outEntityArrayLength = CalculateEntityCountInEntityArray(entities, entityCount, queryData, ecs, ref mask, ref filter);
                res = (byte*)Memory.Unmanaged.Allocate(typeSizeInChunk * outEntityArrayLength, typeAlign, allocator);

                var outDataOffsetInBytes = 0;
                for (int i = 0; i < entityCount; ++i)
                {
                    var entity = entities[i];
                    if (mask.MatchesIgnoreFilter(entity))
                    {
                        var src = ecs->GetComponentDataWithTypeRO(entity, typeIndex);
                        var dst = res + outDataOffsetInBytes;
                        var copySize = typeSizeInChunk;

                        UnsafeUtility.MemCpy(dst, src, copySize);
                        outDataOffsetInBytes += copySize;
                    }
                }
            }

            return res;
        }

        /// <summary>
        ///     Creates a NativeArray containing the entities in a given EntityQuery.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">An atomic safety handle required by GatherEntitiesJob so it can call GetNativeArray() on chunks.</param>
        /// <param name="entityQuery">EntityQuery to gather entities from.</param>
        /// <param name="entityCount">number of entities to reserve for the returned NativeArray.</param>
        /// <param name="outJobHandle">Handle to the GatherEntitiesJob job used to fill the output array.</param>
        /// <param name="dependsOn">Handle to a job this GatherEntitiesJob must wait on.</param>
        /// <returns>NativeArray of the entities in a given EntityQuery.</returns>
        [Obsolete("Use CreateEntityListAsync instead. (RemovedAfter Entities 1.0)")]
        public static NativeArray<Entity> CreateEntityArrayAsync(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator,
            EntityTypeHandle typeHandle,
            EntityQuery entityQuery,
            int entityCount,
            out JobHandle outJobHandle,
            JobHandle dependsOn)
        {
            var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var chunkBaseEntityIndices =
                entityQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, dependsOn, out var baseIndexJob);

            var job = new GatherEntitiesToArrayJob
            {
                ChunkBaseEntityIndices = chunkBaseEntityIndices,
                EntityTypeHandle = typeHandle,
                Entities = (byte*)entities.GetUnsafePtr()
            };
            outJobHandle = job.ScheduleParallel(entityQuery, baseIndexJob);

            return entities;
        }

        /// <summary>
        ///     Creates a NativeList containing the entities in a given EntityQuery.
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">An atomic safety handle required by GatherEntitiesJob so it can call GetNativeArray() on chunks.</param>
        /// <param name="entityQuery">EntityQuery to gather entities from.</param>
        /// <param name="maxEntityCount">number of entities to reserve for the returned NativeArray.</param>
        /// <param name="outJobHandle">Handle to the GatherEntitiesJob job used to fill the output array.</param>
        /// <param name="dependsOn">Handle to a job this GatherEntitiesJob must wait on.</param>
        /// <returns>NativeList of the entities in a given EntityQuery.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "!NET_DOTS")]
        public static NativeList<Entity> CreateEntityListAsync(Allocator allocator,
            EntityTypeHandle typeHandle,
            EntityQuery entityQuery,
            int maxEntityCount,
            JobHandle dependsOn,
            out JobHandle outJobHandle)
        {
            var entities = new NativeList<Entity>(maxEntityCount, allocator);

            var chunkBaseEntityIndices =
                entityQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, dependsOn, out var baseIndexJob);

            var job = new GatherEntitiesJob
            {
                ChunkBaseEntityIndices = chunkBaseEntityIndices,
                OutputList = new TypelessUnsafeList{
                    Ptr = (byte*)entities.m_ListData->Ptr,
                    Length = &(entities.m_ListData->m_length),
                    Capacity = entities.m_ListData->Capacity,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = entities.m_Safety,
#endif
                    },
                EntityTypeHandle = typeHandle,
            };
            outJobHandle = job.ScheduleParallelByRef(entityQuery, baseIndexJob);

            return entities;
        }

        [BurstCompile]
        public static void GatherComponentDataWithBatching(byte* componentData, TypeIndex typeIndex,
            in UnsafeCachedChunkList cache, in UnsafeMatchingArchetypePtrList matchingArchetypePtrList,
            ref EntityQueryFilter filter)
        {
            byte* copyDest = componentData;
            var chunkCache = new UnsafeChunkCache(filter, true, cache, matchingArchetypePtrList.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledMask = default;
            LookupCache typeLookupCache = default;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var chunk, out var chunkEntityCount,
                       out byte useEnableBits, ref chunkEnabledMask))
            {
                var chunkArchetype = chunkCache._CurrentMatchingArchetype->Archetype;
                if (chunkArchetype != typeLookupCache.Archetype)
                    typeLookupCache.Update(chunkArchetype, typeIndex);
                var chunkComponentData = chunk.m_Chunk->Buffer + typeLookupCache.ComponentOffset;

                if (useEnableBits == 0)
                {
                    UnsafeUtility.MemCpy(copyDest, chunkComponentData, chunkEntityCount * typeLookupCache.ComponentSizeOf);
                    copyDest += chunkEntityCount*typeLookupCache.ComponentSizeOf;
                }
                else
                {
                    // If this branch ever becomes a bottleneck for the many-small-batches case, we could add a third path
                    // that iterates over the chukEnabledMask directly, similar to what IJobEntity generates.
                    int batchStartIndex = 0;
                    int batchEndIndex = 0;
                    while (EnabledBitUtility.GetNextRange(ref chunkEnabledMask, ref batchStartIndex, ref batchEndIndex))
                    {
                        int batchEntityCount = batchEndIndex - batchStartIndex;
                        UnsafeUtility.MemCpy(copyDest, chunkComponentData+batchStartIndex * typeLookupCache.ComponentSizeOf, batchEntityCount * typeLookupCache.ComponentSizeOf);
                        batchStartIndex = batchEndIndex;
                        copyDest += batchEntityCount*typeLookupCache.ComponentSizeOf;
                    }
                }
            }
        }

        [BurstCompile]
        public static void GatherComponentData(byte* componentData, TypeIndex typeIndex, in UnsafeCachedChunkList cachedChunkList,
            in UnsafeMatchingArchetypePtrList matchingArchetypePtrList)
        {
            var chunkCache = new UnsafeChunkCache(default, false, cachedChunkList, matchingArchetypePtrList.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledBits = default;
            LookupCache typeLookupCache = default;
            byte* copyDest = componentData;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var chunk, out int chunkEntityCount,
                       out byte useEnabledBits, ref chunkEnabledBits))
            {
                var chunkArchetype = chunkCache._CurrentMatchingArchetype->Archetype;
                if (chunkArchetype != typeLookupCache.Archetype)
                    typeLookupCache.Update(chunkArchetype, typeIndex);
                var copySrc = chunk.m_Chunk->Buffer + typeLookupCache.ComponentOffset;
                var copySize = typeLookupCache.ComponentSizeOf * chunkEntityCount;
                UnsafeUtility.MemCpy(copyDest, copySrc, copySize);
                copyDest += copySize;
            }
        }

        /// <summary>
        /// Creates a NativeArray with the value of a single component for all entities matching the provided query.
        /// The array will be populated by a job scheduled by this function.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">Type handle for the component whose values should be extracted.</param>
        /// <param name="entityCount">Number of entities that match the query. Used as the output array size.</param>
        /// <param name="entityQuery">Entities that match this query will be included in the output.</param>
        /// <param name="outJobHandle">Handle to the job that will populate the output array. The caller must complete this job before the output array contents are valid.</param>
        /// <param name="dependsOn">Input job dependencies for the array-populating job.</param>
        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        [Obsolete("Use CreateComponentDataListAsync instead. (RemovedAfter Entities 1.0)")]
        public static NativeArray<T> CreateComponentDataArrayAsync<T>(
            Allocator allocator,
            ComponentTypeHandle<T> typeHandle,
            int entityCount,
            EntityQuery entityQuery,
            out JobHandle outJobHandle,
            JobHandle dependsOn)
            where T : unmanaged, IComponentData
        {
            var componentData = CollectionHelper.CreateNativeArray<T>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var chunkBaseEntityIndices =
                entityQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, dependsOn, out var baseIndexJob);
            var job = new GatherComponentDataToArrayJob
            {
                ComponentData = (byte*)componentData.GetUnsafePtr(),
                TypeIndex = typeHandle.m_TypeIndex,
                ChunkBaseEntityIndices = chunkBaseEntityIndices,
            };
            outJobHandle = job.ScheduleParallel(entityQuery, baseIndexJob);

            return componentData;
        }

        /// <summary>
        /// Creates a NativeList with the value of a single component for all entities matching the provided query.
        /// The array will be populated by a job scheduled by this function.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">Type handle for the component whose values should be extracted.</param>
        /// <param name="maxEntityCount">Number of entities that match the query. Used as the output array size.</param>
        /// <param name="entityQuery">Entities that match this query will be included in the output.</param>
        /// <param name="dependsOn">Input job dependencies for the array-populating job.</param>
        /// <param name="outJobHandle">Handle to the job that will populate the output array. The caller must complete this job before the output array contents are valid.</param>
        /// <returns>NativeList of all the chunks in the matchingArchetypes list.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "!NET_DOTS")]
        public static NativeList<T> CreateComponentDataListAsync<T>(
            Allocator allocator,
            DynamicComponentTypeHandle typeHandle,
            int maxEntityCount,
            EntityQuery entityQuery,
            JobHandle dependsOn,
            out JobHandle outJobHandle)
            where T : unmanaged, IComponentData
        {
            var componentData = new NativeList<T>(maxEntityCount, allocator);

            var chunkBaseEntityIndices =
                entityQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, dependsOn, out var baseIndexJob);

            // Can't pass a generic NativeList<T> to a non-generic job, so we pass individual ptr/length/capacity fields
            // instead (plus a copy of the list's safety handle)
            var job = new GatherComponentDataJob
            {
                OutputList = new TypelessUnsafeList{
                    Ptr = (byte*)componentData.m_ListData->Ptr,
                    Length = &(componentData.m_ListData->m_length),
                    Capacity = componentData.m_ListData->Capacity,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = componentData.m_Safety,
#endif
                    },
                TypeHandle = typeHandle,
                ChunkBaseEntityIndices = chunkBaseEntityIndices,
            };
            outJobHandle = job.ScheduleParallelByRef(entityQuery, baseIndexJob);

            return componentData;
        }

        /// <summary>
        /// Creates a NativeArray with the value of a single component for all entities matching the provided query.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">Type handle for the component whose values should be extracted.</param>
        /// <param name="entityCount">Number of entities that match the query. Used as the output array size.</param>
        /// <param name="entityQuery">Entities that match this query will be included in the output.</param>
        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "!NET_DOTS")]
        public static NativeArray<T> CreateComponentDataArray<T>(
            Allocator allocator,
            ComponentTypeHandle<T> typeHandle,
            int entityCount,
            EntityQuery entityQuery)
            where T : unmanaged, IComponentData
        {
            var cache = entityQuery.__impl->_QueryData->GetMatchingChunkCache();
            var matchingArchetypes = entityQuery.__impl->_QueryData->MatchingArchetypes;

            var componentData = CollectionHelper.CreateNativeArray<T>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
            var requiresFilter = entityQuery.HasFilter();
            var requiresBatching = entityQuery.__impl->_QueryData->DoesQueryRequireBatching != 0;
            if (!requiresFilter && !requiresBatching)
            {
                GatherComponentData((byte*)componentData.GetUnsafePtr(), typeHandle.m_TypeIndex, in cache, in matchingArchetypes);
            }
            else
            {
                var filter = entityQuery.__impl->_Filter;
                GatherComponentDataWithBatching((byte*)componentData.GetUnsafePtr(), typeHandle.m_TypeIndex, in cache, in matchingArchetypes, ref filter);
            }

            return componentData;
        }

        // In order to maximize EntityQuery.ForEach performance we want to avoid data allocation, as ForEach is main thread only we can afford to allocate a big array and use it to store result.
        // Let's not forget that calls to ForEach can be re-entrant, so we need to cover this use case too.
        // The current solution is to allocate an array of a fixed size (16kb) where will will store the result, we will fall back to the jobified implementation if we run out of space in the buffer
        static readonly int k_EntityQueryResultBufferSize = 16384 / sizeof(Entity);
        struct ResultBufferTag { }
        static readonly SharedStatic<IntPtr> s_EntityQueryResultBuffer = SharedStatic<IntPtr>.GetOrCreate<IntPtr, ResultBufferTag>();
        static readonly SharedStatic<int> s_CurrentOffsetInResultBuffer = SharedStatic<int>.GetOrCreate<int, ResultBufferTag>();

        internal static int currentOffsetInResultBuffer
        {
            get { return s_CurrentOffsetInResultBuffer.Data; }
            set { s_CurrentOffsetInResultBuffer.Data = value; }
        }

#if !UNITY_DOTSRUNTIME
        private static bool s_ShutdownRegistered;
#endif
        private static void Shutdown()
        {
            if (s_EntityQueryResultBuffer.Data != IntPtr.Zero)
            {
                Memory.Unmanaged.Free((void*)s_EntityQueryResultBuffer.Data, Allocator.Persistent);
                s_EntityQueryResultBuffer.Data = IntPtr.Zero;
            }
        }

        [ExcludeFromBurstCompatTesting("Uses managed delegates")]
        internal static void Initialize()
        {
            if (s_EntityQueryResultBuffer.Data == IntPtr.Zero)
                s_EntityQueryResultBuffer.Data = (IntPtr)Memory.Unmanaged.Allocate(k_EntityQueryResultBufferSize * sizeof(Entity), 64, Allocator.Persistent);
#if !UNITY_DOTSRUNTIME
            if (!s_ShutdownRegistered)
            {
                s_ShutdownRegistered = true;
                AppDomain.CurrentDomain.DomainUnload += (_, __) =>
                {
                    Shutdown();
                };
            }
#endif
        }

        public static void GatherEntitiesToArray(EntityQueryData* queryData, ref EntityQueryFilter filter, out EntityQuery.GatherEntitiesResult result)
        {
            var buffer = (Entity*) s_EntityQueryResultBuffer.Data;
            var curOffset = currentOffsetInResultBuffer;

            // Main method that copies the entities of each chunk of a matching archetype to the buffer
            bool AddArchetype(MatchingArchetype* matchingArchetype, ref EntityQueryFilter queryFilter)
            {
                var archetype = matchingArchetype->Archetype;
                var entityCountInArchetype = archetype->EntityCount;
                if (entityCountInArchetype == 0)
                {
                    return true;
                }

                var chunkCount = archetype->Chunks.Count;
                var chunks = archetype->Chunks;
                var counts = archetype->Chunks.GetChunkEntityCountArray();

                for (int i = 0; i < chunkCount; ++i)
                {
                    // Ignore the chunk if the query uses filter and the chunk doesn't comply
                    if (queryFilter.RequiresMatchesFilter && (chunks[i]->MatchesFilter(matchingArchetype, ref queryFilter) == false))
                    {
                        continue;
                    }
                    var entityCountInChunk = counts[i];

                    if ((curOffset + entityCountInChunk) > k_EntityQueryResultBufferSize)
                    {
                        return false;
                    }

                    UnsafeUtility.MemCpy(buffer + curOffset, chunks[i]->Buffer, entityCountInChunk * sizeof(Entity));
                    curOffset += entityCountInChunk;
                }

                return true;
            }

            // Parse all the matching archetypes and add the entities that fits the query and its filter
            bool success = true;
            ref var matchingArchetypes = ref queryData->MatchingArchetypes;
            int archetypeCount = matchingArchetypes.Length;
            var ptrs = matchingArchetypes.Ptr;
            for (var m = 0; m < archetypeCount; m++)
            {
                var match = ptrs[m];
                if (!AddArchetype(match, ref filter))
                {
                    success = false;
                    break;
                }
            }

            result = new EntityQuery.GatherEntitiesResult { StartingOffset = currentOffsetInResultBuffer };
            if (success)
            {
                result.EntityCount = curOffset - currentOffsetInResultBuffer;
                result.EntityBuffer = (Entity*)s_EntityQueryResultBuffer.Data + currentOffsetInResultBuffer;
            }

            currentOffsetInResultBuffer = curOffset;
        }

        [BurstCompile]
        private static void CopyComponentArrayToChunksWithBatching(byte* componentData, TypeIndex typeIndex,
            ref UnsafeMatchingArchetypePtrList matchingArchetypePtrList, ref EntityQueryFilter filter, in UnsafeCachedChunkList cache,
            uint globalSystemVersion)
        {
            byte* copySrc = componentData;
            var chunkCache = new UnsafeChunkCache(filter, true, cache, matchingArchetypePtrList.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledMask = default;
            LookupCache typeLookupCache = default;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var chunk, out var chunkEntityCount,
                       out byte useEnableBits, ref chunkEnabledMask))
            {
                var chunkArchetype = chunkCache._CurrentMatchingArchetype->Archetype;
                var chunkComponentData = ChunkDataUtility.GetComponentDataWithTypeRW(chunk.m_Chunk, chunkArchetype,
                    0, typeIndex, globalSystemVersion, ref typeLookupCache);

                if (useEnableBits == 0)
                {
                    UnsafeUtility.MemCpy(chunkComponentData, copySrc, chunkEntityCount * typeLookupCache.ComponentSizeOf);
                    copySrc += chunkEntityCount*typeLookupCache.ComponentSizeOf;
                }
                else
                {
                    // If this branch ever becomes a bottleneck for the many-small-batches case, we could add a third path
                    // that iterates over the chukEnabledMask directly, similar to what IJobEntity generates.
                    int batchStartIndex = 0;
                    int batchEndIndex = 0;
                    while (EnabledBitUtility.GetNextRange(ref chunkEnabledMask, ref batchStartIndex, ref batchEndIndex))
                    {
                        int batchEntityCount = batchEndIndex - batchStartIndex;
                        UnsafeUtility.MemCpy(chunkComponentData+batchStartIndex * typeLookupCache.ComponentSizeOf, copySrc, batchEntityCount * typeLookupCache.ComponentSizeOf);
                        batchStartIndex = batchEndIndex;
                        copySrc += batchEntityCount*typeLookupCache.ComponentSizeOf;
                    }
                }
            }
        }

        [BurstCompile]
        public static void CopyComponentArrayToChunks(byte* componentData, TypeIndex typeIndex, in UnsafeCachedChunkList cachedChunkList,
            in UnsafeMatchingArchetypePtrList matchingArchetypePtrList, uint globalSystemVersion)
        {
            var chunkCache = new UnsafeChunkCache(default, false, cachedChunkList, matchingArchetypePtrList.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledBits = default;
            LookupCache typeLookupCache = default;
            byte* copySrc = componentData;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var chunk, out int chunkEntityCount,
                       out byte useEnabledBits, ref chunkEnabledBits))
            {
                var chunkArchetype = chunkCache._CurrentMatchingArchetype->Archetype;
                var copyDest = ChunkDataUtility.GetComponentDataWithTypeRW(chunk.m_Chunk, chunkArchetype, 0, typeIndex,
                    globalSystemVersion, ref typeLookupCache);
                var copySize = typeLookupCache.ComponentSizeOf * chunkEntityCount;
                UnsafeUtility.MemCpy(copyDest, copySrc, copySize);
                copySrc += copySize;
            }
        }

        [Obsolete("Use CopyFromComponentDataListAsync instead. (RemovedAfter Entities 1.0)")]
        public static void CopyFromComponentDataArrayAsync<T>(UnsafeMatchingArchetypePtrList matchingArchetypes,
            NativeArray<T> componentDataArray,
            ComponentTypeHandle<T> typeHandle,
            EntityQuery entityQuery,
            ref EntityQueryFilter filter,
            out JobHandle jobHandle,
            JobHandle dependsOn)
            where T : unmanaged, IComponentData
        {
            var chunkBaseEntityIndices =
                entityQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, dependsOn, out var baseIndexJob);
            var job = new CopyComponentArrayToChunksJob
            {
                ComponentData = (byte*)componentDataArray.GetUnsafePtr(),
                TypeIndex = typeHandle.m_TypeIndex,
                GlobalSystemVersion = typeHandle.GlobalSystemVersion,
                ChunkBaseEntityIndices = chunkBaseEntityIndices,
            };
            jobHandle = job.ScheduleParallel(entityQuery, baseIndexJob);
        }

        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "!NET_DOTS")]
        public static void CopyFromComponentDataListAsync<T>(
            NativeList<T> componentDataList,
            DynamicComponentTypeHandle typeHandle,
            EntityQuery entityQuery,
            JobHandle dependsOn,
            out JobHandle outJobHandle
            )
            where T : unmanaged, IComponentData
        {
            var chunkBaseEntityIndices =
                entityQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, dependsOn, out var baseIndexJob);
            // Can't pass a generic NativeList<T> to a non-generic job, so we pass individual ptr/length/capacity fields
            // instead (plus a copy of the list's safety handle)
            var job = new CopyComponentListToChunksJob
            {
                InputList = new TypelessUnsafeList{
                    Ptr = (byte*)componentDataList.m_ListData->Ptr,
                    Length = &(componentDataList.m_ListData->m_length),
                    Capacity = componentDataList.m_ListData->Capacity,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = componentDataList.m_Safety,
#endif
                },
                TypeHandle = typeHandle,
                ChunkBaseEntityIndices = chunkBaseEntityIndices,
            };
            outJobHandle = job.ScheduleParallelByRef(entityQuery, baseIndexJob);
        }

        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "!NET_DOTS")]
        public static void CopyFromComponentDataArray<T>(
            NativeArray<T> componentDataArray,
            ComponentTypeHandle<T> typeHandle,
            EntityQuery entityQuery)
            where T : unmanaged, IComponentData
        {
            var matchingArchetypePtrList = entityQuery.__impl->_QueryData->MatchingArchetypes;
            var cache = entityQuery.__impl->_QueryData->GetMatchingChunkCache();

            var requiresFilter = entityQuery.HasFilter();
            var requiresBatching = entityQuery.__impl->_QueryData->DoesQueryRequireBatching != 0;
            if (!requiresFilter && !requiresBatching)
            {
                CopyComponentArrayToChunks((byte*)componentDataArray.GetUnsafePtr(), typeHandle.m_TypeIndex,
                    in cache, in matchingArchetypePtrList, typeHandle.GlobalSystemVersion);
            }
            else
            {
                var filter = entityQuery.__impl->_Filter;
                CopyComponentArrayToChunksWithBatching((byte*)componentDataArray.GetUnsafePtr(), typeHandle.m_TypeIndex,
                    ref matchingArchetypePtrList, ref filter, in cache, typeHandle.GlobalSystemVersion);
            }
        }

        /// <summary>
        ///     Total number of chunks in a given MatchingArchetype list.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <returns>Number of chunks in a list of archetypes.</returns>
        [BurstCompile]
        public static int CalculateChunkCount(in UnsafeCachedChunkList cachedChunkList, ref UnsafeMatchingArchetypePtrList matchingArchetypes,
            ref EntityQueryFilter filter, int queryHasEnableableComponents)
        {
            var totalChunkCount = 0;
            var matchingArchetypesPtr = matchingArchetypes.Ptr;
            var requiresFilter = filter.RequiresMatchesFilter;
            var requiresBatching = queryHasEnableableComponents == 1;
            var chunkMatchingArchetypeIndexPtr = cachedChunkList.PerChunkMatchingArchetypeIndex->Ptr;
            var chunkIndexInArchetypePtr = cachedChunkList.ChunkIndexInArchetype->Ptr;
            int cachedChunkCount = cachedChunkList.Length;
            int currentMatchingArchetypeIndex = -1;
            MatchingArchetype* currentMatchingArchetype = null;
            var currentMatchingArchetypeState = default(EnabledMaskMatchingArchetypeState);
            int* currentArchetypeChunkEntityCountsPtr = null;
            // Fast path if no filtering at all is required
            if (!requiresFilter && !requiresBatching)
            {
                totalChunkCount = cachedChunkList.Length;
            }
            else if (requiresBatching)
            {
                // per-entity + per-chunk filtering
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                        var currentArchetype = currentMatchingArchetype->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                        currentMatchingArchetypeState = new EnabledMaskMatchingArchetypeState(currentMatchingArchetype);
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (requiresFilter && !currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;

                    int chunkEntityCount = currentArchetypeChunkEntityCountsPtr[chunkIndexInArchetype];
                    GetEnabledMask(chunkIndexInArchetype, chunkEntityCount, currentMatchingArchetypeState,
                        out var chunkEnabledMask);
                    if (chunkEnabledMask.ULong0 != 0 || chunkEnabledMask.ULong1 != 0)
                        totalChunkCount += 1;
                }
            }
            else
            {
                // chunk filtering only
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                        var currentArchetype = currentMatchingArchetype->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (requiresFilter && !currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;
                    totalChunkCount += 1;
                }
            }

            return totalChunkCount;
        }

        [BurstCompile]
        public static bool IsEmpty(EntityQueryData *queryData, in EntityQueryFilter filter)
        {
            var chunkCache = new UnsafeChunkCache(filter, queryData->DoesQueryRequireBatching != 0,
                queryData->GetMatchingChunkCache(), queryData->MatchingArchetypes.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledMask = default;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var archetypeChunk, out var chunkEntityCount,
                       out byte chunkUsesEnabledBits, ref chunkEnabledMask))
            {
                // if we make it here at all, we found a chunk with >1 enabled entity, so the query isn't empty
                return false;
            }

            return true;
        }

        [BurstCompile]
        public static void CalculateFilteredChunkIndexArray(in UnsafeCachedChunkList cachedChunkList,
            in UnsafeMatchingArchetypePtrList matchingArchetypes, ref EntityQueryFilter filter, int doesRequireBatching,
            ref NativeArray<int> outFilteredChunkIndices)
        {
            var filteredChunkCount = 0;
            var matchingArchetypesPtr = matchingArchetypes.Ptr;
            var requiresFilter = filter.RequiresMatchesFilter;
            var requiresBatching = doesRequireBatching == 1;
            var chunkMatchingArchetypeIndexPtr = cachedChunkList.PerChunkMatchingArchetypeIndex->Ptr;
            var chunkIndexInArchetypePtr = cachedChunkList.ChunkIndexInArchetype->Ptr;
            int cachedChunkCount = cachedChunkList.Length;
            int currentMatchingArchetypeIndex = -1;
            MatchingArchetype* currentMatchingArchetype = null;
            var currentMatchingArchetypeState = default(EnabledMaskMatchingArchetypeState);
            int* currentArchetypeChunkEntityCountsPtr = null;
            // Fast path if no filtering at all is required
            if (!requiresFilter && !requiresBatching)
            {
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    outFilteredChunkIndices[chunkIndexInCache] = chunkIndexInCache;
                }
            }
            else if (requiresBatching)
            {
                // per-entity + per-chunk filtering
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    outFilteredChunkIndices[chunkIndexInCache] = -1; // default value for filtered-out chunks
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                        var currentArchetype = currentMatchingArchetype->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                        currentMatchingArchetypeState = new EnabledMaskMatchingArchetypeState(currentMatchingArchetype);
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (requiresFilter && !currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;
                    int chunkEntityCount = currentArchetypeChunkEntityCountsPtr[chunkIndexInArchetype];
                    GetEnabledMask(chunkIndexInArchetype, chunkEntityCount, currentMatchingArchetypeState,
                        out var chunkEnabledMask);
                    if (chunkEnabledMask.ULong0 == 0 && chunkEnabledMask.ULong1 == 0)
                        continue;
                    outFilteredChunkIndices[chunkIndexInCache] = filteredChunkCount++;
                }
            }
            else
            {
                // chunk filtering only
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    outFilteredChunkIndices[chunkIndexInCache] = -1; // default value for filtered-out chunks
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (!currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;
                    outFilteredChunkIndices[chunkIndexInCache] = filteredChunkCount++;
                }
            }
        }

        [BurstCompile]
        public static void CalculateBaseEntityIndexArray(in UnsafeCachedChunkList cachedChunkList,
            in UnsafeMatchingArchetypePtrList matchingArchetypes, ref EntityQueryFilter filter, int doesRequireBatching,
            ref NativeArray<int> outChunkBaseEntityIndices)
        {
            var totalEntityCount = 0;
            var matchingArchetypesPtr = matchingArchetypes.Ptr;
            var requiresFilter = filter.RequiresMatchesFilter;
            var requiresBatching = doesRequireBatching == 1;
            var chunkMatchingArchetypeIndexPtr = cachedChunkList.PerChunkMatchingArchetypeIndex->Ptr;
            var chunkIndexInArchetypePtr = cachedChunkList.ChunkIndexInArchetype->Ptr;
            int cachedChunkCount = cachedChunkList.Length;
            int currentMatchingArchetypeIndex = -1;
            MatchingArchetype* currentMatchingArchetype = null;
            var currentMatchingArchetypeState = default(EnabledMaskMatchingArchetypeState);
            int* currentArchetypeChunkEntityCountsPtr = null;
            // Fast path if no filtering at all is required
            if (!requiresFilter && !requiresBatching)
            {
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    outChunkBaseEntityIndices[chunkIndexInCache] = totalEntityCount;
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        var currentArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex]->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    totalEntityCount += currentArchetypeChunkEntityCountsPtr[chunkIndexInArchetype];
                }
            }
            else if (requiresBatching)
            {
                // per-entity + per-chunk filtering
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    outChunkBaseEntityIndices[chunkIndexInCache] = totalEntityCount;
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                        var currentArchetype = currentMatchingArchetype->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                        currentMatchingArchetypeState = new EnabledMaskMatchingArchetypeState(currentMatchingArchetype);
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (requiresFilter && !currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;

                    int chunkEntityCount = currentArchetypeChunkEntityCountsPtr[chunkIndexInArchetype];
                    GetEnabledMask(chunkIndexInArchetype, chunkEntityCount, currentMatchingArchetypeState,
                        out var chunkEnabledMask);
                    totalEntityCount += EnabledBitUtility.countbits(chunkEnabledMask);
                }
            }
            else
            {
                // chunk filtering only
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    outChunkBaseEntityIndices[chunkIndexInCache] = totalEntityCount;
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                        var currentArchetype = currentMatchingArchetype->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (!currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;

                    int chunkEntityCount = currentArchetypeChunkEntityCountsPtr[chunkIndexInArchetype];

                    totalEntityCount += chunkEntityCount;
                }
            }
        }

        /// <summary>
        ///     Total number of entities contained in a given MatchingArchetype list.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="filter">EntityQueryFilter to use when calculating total number of entities.</param>
        /// <param name="doesRequireBatching">True if this query includes any enableable component types</param>
        /// <returns>Number of entities</returns>
        [BurstCompile]
        public static int CalculateEntityCount(in UnsafeCachedChunkList cachedChunkList,
            ref UnsafeMatchingArchetypePtrList matchingArchetypes, ref EntityQueryFilter filter, int doesRequireBatching)
        {
            var totalEntityCount = 0;
            var matchingArchetypesPtr = matchingArchetypes.Ptr;
            var requiresFilter = filter.RequiresMatchesFilter;
            var requiresBatching = doesRequireBatching == 1;
            var chunkMatchingArchetypeIndexPtr = cachedChunkList.PerChunkMatchingArchetypeIndex->Ptr;
            var chunkIndexInArchetypePtr = cachedChunkList.ChunkIndexInArchetype->Ptr;
            int cachedChunkCount = cachedChunkList.Length;
            int currentMatchingArchetypeIndex = -1;
            MatchingArchetype* currentMatchingArchetype = null;
            var currentMatchingArchetypeState = default(EnabledMaskMatchingArchetypeState);
            int* currentArchetypeChunkEntityCountsPtr = null;
            // Fast path if no filtering at all is required
            if (!requiresFilter && !requiresBatching)
            {
                int matchingArchetypeCount = matchingArchetypes.Length;
                for (int i = 0; i < matchingArchetypeCount; ++i)
                    totalEntityCount += matchingArchetypesPtr[i]->Archetype->EntityCount;
            }
            else if (requiresBatching)
            {
                // per-entity + per-chunk filtering
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                        var currentArchetype = currentMatchingArchetype->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                        currentMatchingArchetypeState = new EnabledMaskMatchingArchetypeState(currentMatchingArchetype);
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (requiresFilter && !currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;

                    int chunkEntityCount = currentArchetypeChunkEntityCountsPtr[chunkIndexInArchetype];
                    GetEnabledMask(chunkIndexInArchetype, chunkEntityCount, currentMatchingArchetypeState,
                        out var chunkEnabledMask);
                    totalEntityCount += EnabledBitUtility.countbits(chunkEnabledMask);
                }
            }
            else
            {
                // chunk filtering only
                for (int chunkIndexInCache = 0; chunkIndexInCache < cachedChunkCount; ++chunkIndexInCache)
                {
                    if (Hint.Unlikely(chunkMatchingArchetypeIndexPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                    {
                        currentMatchingArchetypeIndex = chunkMatchingArchetypeIndexPtr[chunkIndexInCache];
                        currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                        var currentArchetype = currentMatchingArchetype->Archetype;
                        currentArchetypeChunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                    }
                    int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                    if (requiresFilter && !currentMatchingArchetype->ChunkMatchesFilter(chunkIndexInArchetype, ref filter))
                        continue;

                    int chunkEntityCount = currentArchetypeChunkEntityCountsPtr[chunkIndexInArchetype];

                    totalEntityCount += chunkEntityCount;
                }
            }
            return totalEntityCount;
        }

        [BurstCompile]
        public static int CalculateEntityCountInEntityArray(
            Entity* entities,
            int entityCount,
            EntityQueryData* queryData,
            EntityComponentStore* ecs,
            ref EntityQueryMask mask,
            ref EntityQueryFilter filter)
        {
            var length = 0;
            if (filter.RequiresMatchesFilter)
            {
                using var batches = new UnsafeList<ArchetypeChunk>(0,Allocator.TempJob);
                using var matchingArchetypeIndices = new UnsafeList<int>(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, true, entities, entityCount, &batches, &matchingArchetypeIndices);

                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (batch.m_Chunk->MatchesFilter(match, ref filter))
                        length += batch.Count;
                }
            }
            else
            {
                for (int i = 0; i < entityCount; ++i)
                {
                    if (mask.MatchesIgnoreFilter(entities[i]))
                        length++;
                }
            }

            return length;
        }

        [BurstCompile]
        public static bool MatchesAnyInEntityArray(
            Entity* entities,
            int entityCount,
            EntityQueryData* queryData,
            EntityComponentStore* ecs,
            ref EntityQueryMask mask,
            ref EntityQueryFilter filter)
        {
            if (filter.RequiresMatchesFilter)
            {
                var batches = new UnsafeList<ArchetypeChunk>(0, Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeList<int>(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, true, entities, entityCount, &batches, &matchingArchetypeIndices);

                var ret = false;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (batch.m_Chunk->MatchesFilter(match, ref filter))
                    {
                        ret = true;
                        break;
                    }
                }

                batches.Dispose();
                matchingArchetypeIndices.Dispose();
                return ret;
            }
            else
            {
                for (int i = 0; i < entityCount; ++i)
                {
                    if (mask.MatchesIgnoreFilter(entities[i]))
                        return true;
                }
            }

            return false;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) }, RequiredUnityDefine = "ENABLE_UNITY_COLLECTIONS_CHECKS", CompileTarget = GenerateTestsForBurstCompatibilityAttribute.BurstCompatibleCompileTarget.Editor)]
        internal static BufferAccessor<T> GetChunkBufferAccessor<T>(Chunk* chunk, bool isWriting, int typeIndexInArchetype, uint systemVersion, AtomicSafetyHandle safety0, AtomicSafetyHandle safety1)
#else
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        internal static BufferAccessor<T> GetChunkBufferAccessor<T>(Chunk * chunk, bool isWriting, int typeIndexInArchetype, uint systemVersion)
#endif
            where T : unmanaged, IBufferElementData
        {
            var archetype = chunk->Archetype;
            int internalCapacity = archetype->BufferCapacities[typeIndexInArchetype];

            byte* ptr = (!isWriting)
                ? ChunkDataUtility.GetComponentDataRO(chunk, 0, typeIndexInArchetype)
                : ChunkDataUtility.GetComponentDataRW(chunk, 0, typeIndexInArchetype, systemVersion);

            var length = chunk->Count;
            int stride = archetype->SizeOfs[typeIndexInArchetype];

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (Hint.Unlikely(archetype->EntityComponentStore->m_RecordToJournal != 0) && isWriting)
            {
                EntitiesJournaling.AddRecord(
                    recordType: EntitiesJournaling.RecordType.GetBufferRW,
                    entityComponentStore: archetype->EntityComponentStore,
                    globalSystemVersion: systemVersion,
                    chunks: chunk,
                    chunkCount: 1,
                    types: &archetype->Types[typeIndexInArchetype].TypeIndex,
                    typeCount: 1);
            }
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return new BufferAccessor<T>(ptr, length, stride, !isWriting, safety0, safety1, internalCapacity);
#else
            return new BufferAccessor<T>(ptr, length, stride, internalCapacity);
#endif
        }

        internal static void* GetChunkComponentDataPtr(Chunk* chunk, bool isWriting, int indexInArchetype, uint systemVersion)
        {
            byte* ptr = (!isWriting)
                ? ChunkDataUtility.GetComponentDataRO(chunk, 0, indexInArchetype)
                : ChunkDataUtility.GetComponentDataRW(chunk, 0, indexInArchetype, systemVersion);
            return ptr;
        }

        internal static void* GetChunkComponentDataROPtr(Chunk* chunk, int indexInArchetype)
        {
            var archetype = chunk->Archetype;
            return chunk->Buffer + archetype->Offsets[indexInArchetype];
        }

        private static bool FindNextMatchingBatchStart(EntityQueryMask mask, Entity* entities, int totalEntityCount,  ref int currentIndexInEntityArray)
        {
            for (; currentIndexInEntityArray < totalEntityCount; ++currentIndexInEntityArray)
            {
                var currentEntity = entities[currentIndexInEntityArray];

                if (mask.MatchesIgnoreFilter(currentEntity))
                    return true;
            }

            return false;
        }

        [BurstCompile]
        public static void FindFilteredBatchesForEntityArrayWithQuery(
            EntityQueryImpl* query,
            Entity* entities, int entityCount,
            UnsafeList<ArchetypeChunk>* batches)
        {
            var queryImpl = query->_Access;

            var data = query->_QueryData;
            var ecs = query->_Access->EntityComponentStore;
            var queryMask = queryImpl->EntityQueryManager->GetEntityQueryMask(query->_QueryData, ecs);

            ref var filter = ref query->_Filter;
            var isFiltering = filter.RequiresMatchesFilter;

            // Start first batch
            var currentIndexInEntityArray = 0;
            while (FindNextMatchingBatchStart(queryMask, entities, entityCount, ref currentIndexInEntityArray))
            {
                var batchStartEntityInChunk = ecs->GetEntityInChunk(entities[currentIndexInEntityArray++]);

                var currentBatchChunk = batchStartEntityInChunk.Chunk;
                var currentBatchStartIndex = batchStartEntityInChunk.IndexInChunk;
                var currentBatchCounter = 1;

                for (; currentIndexInEntityArray < entityCount; ++currentIndexInEntityArray, currentBatchCounter++)
                {
                    var currentEntityInChunk = ecs->GetEntityInChunk(entities[currentIndexInEntityArray]);

                    // Check if we're looking at the next entity in the same chunk
                    if (currentEntityInChunk.Chunk != currentBatchChunk || currentEntityInChunk.IndexInChunk != currentBatchStartIndex + currentBatchCounter)
                        break;
                }

                if (isFiltering)
                {
                    var matchingArchetypeIndex = EntityQueryManager.FindMatchingArchetypeIndexForArchetype(ref data->MatchingArchetypes, currentBatchChunk->Archetype);
                    if (!currentBatchChunk->MatchesFilter(data->MatchingArchetypes.Ptr[matchingArchetypeIndex], ref filter))
                        continue;
                }

                // Finish the batch
                batches->Add(new ArchetypeChunk
                {
                    m_Chunk = currentBatchChunk,
                    m_EntityComponentStore = ecs,
                    m_BatchStartEntityIndex = currentBatchStartIndex,
                    m_BatchEntityCount = currentBatchCounter
                });
            }
        }

        [BurstCompile]
        public static void FindBatchesForEntityArrayWithQuery(
            EntityComponentStore* ecs,
            EntityQueryData* data,
            bool requiresFilteringOrBatching,
            Entity* entities,
            int entityCount,
            UnsafeList<ArchetypeChunk>* batches,
            UnsafeList<int>* perBatchMatchingArchetypeIndex)
        {
            // Start first batch
            var currentIndexInEntityArray = 0;
            while (FindNextMatchingBatchStart(data->EntityQueryMask, entities, entityCount, ref currentIndexInEntityArray))
            {
                var batchStartEntityInChunk = ecs->GetEntityInChunk(entities[currentIndexInEntityArray++]);

                var currentBatchChunk = batchStartEntityInChunk.Chunk;
                var currentBatchStartIndex = batchStartEntityInChunk.IndexInChunk;
                var currentBatchCounter = 1;

                for (; currentIndexInEntityArray < entityCount; ++currentIndexInEntityArray, currentBatchCounter++)
                {
                    var currentEntityInChunk = ecs->GetEntityInChunk(entities[currentIndexInEntityArray]);

                    // Check if we're looking at the next entity in the same chunk
                    if (currentEntityInChunk.Chunk != currentBatchChunk || currentEntityInChunk.IndexInChunk != currentBatchStartIndex + currentBatchCounter)
                        break;
                }

                // Finish the batch
                batches->Add(new ArchetypeChunk
                {
                    m_Chunk = currentBatchChunk,
                    m_EntityComponentStore = ecs,
                    m_BatchStartEntityIndex = currentBatchStartIndex,
                    m_BatchEntityCount = currentBatchCounter
                });

                if(requiresFilteringOrBatching)
                    perBatchMatchingArchetypeIndex->Add(EntityQueryManager.FindMatchingArchetypeIndexForArchetype(ref data->MatchingArchetypes, currentBatchChunk->Archetype));
            }
        }

        // Helper struct to bundle some invariant data when computing the enabled-bit mask for several chunks within the same archetype
        [NoAlias]
        internal struct EnabledMaskMatchingArchetypeState
        {
            [NoAlias] public MatchingArchetype* MatchingArchetype;
            [NoAlias] public int*               MatchingArchetypeAllComponentTypeIndices;
            [NoAlias] public int*               MatchingArchetypeAnyComponentTypeIndices;
            [NoAlias] public int*               MatchingArchetypeNoneComponentTypeIndices;
            [NoAlias] public v128*              ArchetypeEnabledBits;
            public int                          ArchetypeEnabledBitsPerChunkOffset;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public EnabledMaskMatchingArchetypeState(MatchingArchetype* matchingArchetype)
            {
                var chunkData = matchingArchetype->Archetype->Chunks;
                MatchingArchetype = matchingArchetype;
                MatchingArchetypeAllComponentTypeIndices = matchingArchetype->EnableableIndexInArchetype_All;
                MatchingArchetypeNoneComponentTypeIndices = matchingArchetype->EnableableIndexInArchetype_None;
                MatchingArchetypeAnyComponentTypeIndices = matchingArchetype->EnableableIndexInArchetype_Any;
                ArchetypeEnabledBits = chunkData.GetComponentEnabledMaskArrayForChunk(0);
                ArchetypeEnabledBitsPerChunkOffset = chunkData.GetComponentEnabledBitsSizePerChunk() / sizeof(v128);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void GetEnabledMask(int chunkIndexInArchetype, int chunkEntityCount, in EnabledMaskMatchingArchetypeState archetypeState, out v128 enabledMask)
        {
            var matchingArchetype = archetypeState.MatchingArchetype;
            var allComponentCount = matchingArchetype->EnableableComponentsCount_All;
            var noneComponentCount = matchingArchetype->EnableableComponentsCount_None;
            var anyComponentCount = matchingArchetype->EnableableComponentsCount_Any;
            int* allTypeIndices = archetypeState.MatchingArchetypeAllComponentTypeIndices;
            int* noneTypeIndices = archetypeState.MatchingArchetypeNoneComponentTypeIndices;
            int* anyTypeIndices = archetypeState.MatchingArchetypeAnyComponentTypeIndices;
            var bitsForTypes = archetypeState.ArchetypeEnabledBits +
                               (archetypeState.ArchetypeEnabledBitsPerChunkOffset * chunkIndexInArchetype);

            enabledMask = new v128(ulong.MaxValue);
            // At the very least we want to narrow the mask to include only valid entities.
            // The mask for "All" types will implicitly mask out invalid entities, so this is only needed if
            // no All types are present.
            if (Hint.Unlikely(allComponentCount == 0))
            {
                enabledMask = EnabledBitUtility.ShiftRight(new v128(ulong.MaxValue), 128-chunkEntityCount);
            }
            if (X86.Sse2.IsSse2Supported)
            {
                for (int i = 0; i < allComponentCount; ++i) // masks for "All" types are AND'd together
                {
                    var typeIndexInArchetype = allTypeIndices[i];
                    enabledMask = X86.Sse2.and_si128(bitsForTypes[typeIndexInArchetype], enabledMask);
                }
                if (anyComponentCount > 0)
                {
                    v128 anyCombinedMask = bitsForTypes[anyTypeIndices[0]];
                    for (int i = 1; i < anyComponentCount; ++i) // masks for "Any" types are OR'd with each other, then AND'd with the final result
                    {
                        var typeIndexInArchetype = anyTypeIndices[i];
                        anyCombinedMask = X86.Sse2.or_si128(bitsForTypes[typeIndexInArchetype], anyCombinedMask);
                    }
                    enabledMask = X86.Sse2.and_si128(anyCombinedMask, enabledMask);
                }
                for (int i = 0; i < noneComponentCount; ++i) // masks for "None" types are negated and AND'd together
                {
                    var typeIndexInArchetype = noneTypeIndices[i];
                    enabledMask = X86.Sse2.andnot_si128(bitsForTypes[typeIndexInArchetype], enabledMask);
                }
            }
            else
            {
                for (int i = 0; i < allComponentCount; ++i) // masks for "All" types are AND'd together
                {
                    var typeIndexInArchetype = allTypeIndices[i];
                    enabledMask.ULong0 = bitsForTypes[typeIndexInArchetype].ULong0 & enabledMask.ULong0;
                    enabledMask.ULong1 = bitsForTypes[typeIndexInArchetype].ULong1 & enabledMask.ULong1;
                }
                if (anyComponentCount > 0)
                {
                    v128 anyCombinedMask = new v128(0);
                    for (int i = 0; i < anyComponentCount; ++i) // masks for "Any" types are OR'd with each other, then AND'd with the final result
                    {
                        var typeIndexInArchetype = anyTypeIndices[i];
                        anyCombinedMask.ULong0 = bitsForTypes[typeIndexInArchetype].ULong0 | anyCombinedMask.ULong0;
                        anyCombinedMask.ULong1 = bitsForTypes[typeIndexInArchetype].ULong1 | anyCombinedMask.ULong1;
                    }
                    enabledMask.ULong0 = anyCombinedMask.ULong0 & enabledMask.ULong0;
                    enabledMask.ULong1 = anyCombinedMask.ULong1 & enabledMask.ULong1;
                }
                for (int i = 0; i < noneComponentCount; ++i) // masks for "None" types are negated and AND'd together
                {
                    var typeIndexInArchetype = noneTypeIndices[i];
                    enabledMask.ULong0 = (~bitsForTypes[typeIndexInArchetype].ULong0 & enabledMask.ULong0);
                    enabledMask.ULong1 = (~bitsForTypes[typeIndexInArchetype].ULong1 & enabledMask.ULong1);
                }
            }
        }

        [BurstCompile]
        public static void GetEnabledMask(Chunk* chunk, MatchingArchetype* matchingArchetype, out v128 enabledMask)
        {
            Assert.IsTrue(matchingArchetype->Archetype->ChunkCapacity <= 128);
            GetEnabledMask(chunk->ListIndex, chunk->Count, new EnabledMaskMatchingArchetypeState(matchingArchetype), out enabledMask);
        }

        [BurstCompile]
        public static void SetEnabledBitsOnAllChunks(ref EntityQueryImpl queryImpl, TypeIndex typeIndex, bool value)
        {
            var chunkList = queryImpl._QueryData->GetMatchingChunkCache();
            var chunkCount = chunkList.Length;
            Chunk** chunkListPtr = chunkList.Ptr;
            int* chunkIndexInArchetypePtr = chunkList.ChunkIndexInArchetype->Ptr;
            MatchingArchetype** matchingArchetypesPtr = queryImpl._QueryData->MatchingArchetypes.Ptr;
            int* matchingArchetypeIndicesPtr = chunkList.PerChunkMatchingArchetypeIndex->Ptr;
            bool requiresFilter = queryImpl._Filter.RequiresMatchesFilter;

            int currentMatchingArchetypeIndex = -1;
            MatchingArchetype* currentMatchingArchetype = null;
            v128* chunkEnabledBitsForType = null;
            int* chunkDisabledCountForType = null;
            int chunkEnabledBitsIncrement = 0;
            int* chunkEntityCountsPtr = null;
            for (int chunkIndexInCache = 0; chunkIndexInCache < chunkCount; ++chunkIndexInCache)
            {
                var chunk = chunkListPtr[chunkIndexInCache];
                if (Hint.Unlikely(matchingArchetypeIndicesPtr[chunkIndexInCache] != currentMatchingArchetypeIndex))
                {
                    // Update per-archetype state
                    currentMatchingArchetypeIndex = matchingArchetypeIndicesPtr[chunkIndexInCache];
                    currentMatchingArchetype = matchingArchetypesPtr[currentMatchingArchetypeIndex];
                    var currentArchetype = currentMatchingArchetype->Archetype;
                    chunkEntityCountsPtr = currentArchetype->Chunks.GetChunkEntityCountArray();
                    var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(currentArchetype, typeIndex);
                    chunkEnabledBitsForType = currentArchetype->Chunks.GetComponentEnabledMaskArrayForTypeInChunk(typeIndexInArchetype, 0);
                    chunkDisabledCountForType =
                        currentArchetype->Chunks.GetPointerToChunkDisabledCountForType(typeIndexInArchetype, 0);
                    chunkEnabledBitsIncrement = currentArchetype->Chunks.GetComponentEnabledBitsSizePerChunk() / sizeof(v128);
                }

                if (requiresFilter && !chunk->MatchesFilter(currentMatchingArchetype, ref queryImpl._Filter))
                    continue;

                int chunkIndexInArchetype = chunkIndexInArchetypePtr[chunkIndexInCache];
                int chunkEntityCount = chunkEntityCountsPtr[chunkIndexInArchetype];
                if (value)
                {
                    if (chunkEntityCount < 64)
                        *(chunkEnabledBitsForType + chunkEnabledBitsIncrement * chunkIndexInArchetype) = new v128((1UL << chunkEntityCount)-1, 0UL);
                    else if (chunkEntityCount < 128)
                        *(chunkEnabledBitsForType + chunkEnabledBitsIncrement * chunkIndexInArchetype) = new v128(ulong.MaxValue, (1UL << (chunkEntityCount-64))-1);
                    else
                        *(chunkEnabledBitsForType + chunkEnabledBitsIncrement * chunkIndexInArchetype) = new v128(ulong.MaxValue, ulong.MaxValue);
                    *(chunkDisabledCountForType + chunkEnabledBitsIncrement * chunkIndexInArchetype) = 0;
                }
                else
                {
                    *(chunkEnabledBitsForType + chunkEnabledBitsIncrement * chunkIndexInArchetype) = default(v128);
                    *(chunkDisabledCountForType + chunkEnabledBitsIncrement * chunkIndexInArchetype) = chunkEntityCount;
                }
            }
        }
    }

    /// <summary>
    /// Utilities to help manipulate the per-component enabled bits within a given chunk.
    /// </summary>
    [GenerateTestsForBurstCompatibility]
    public static class EnabledBitUtility
    {
        /// <summary>
        /// Retrieves the next contiguous range of set bits in the provided mask.
        /// </summary>
        /// <param name="mask">The enabled-bit mask. This mask is modified during iteration.</param>
        /// <param name="beginIndex">On success, the index of the start of the next range is written here.</param>
        /// <param name="endIndex">On success, the index of the first bit not in the next range is written here.</param>
        /// <returns>True if another range of contiguous bits was found (in which case, the range info is stored in
        /// <paramref name="beginIndex"/> and <paramref name="endIndex"/>. Otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetNextRange(ref v128 mask, ref int beginIndex, ref int endIndex)
        {
            // count zero bits before first enabled bit (Thats our start)
            var start = tzcnt_u128(mask);
            mask = ShiftRight(mask, start);

            // The mask has been shifted so now we need to find the first disabled bit (Thats the count)
            v128 negatedMask = default;
            if (X86.Sse2.IsSse2Supported)
            {
                negatedMask = X86.Sse2.xor_si128(mask, new v128(0xFFFFFFFF));
            }
            else
            {
                negatedMask = new v128(~mask.ULong0, ~mask.ULong1);
            }
            var count = tzcnt_u128(negatedMask);

            // make sure the mask is valid for the next loop
            mask = ShiftRight(mask, count);

            beginIndex = endIndex + start;
            endIndex = beginIndex + count;

            return count != 0;
        }

        internal static v128 ShiftRight(v128 v, int n)
        {
            if (X86.Sse4_1.IsSse41Supported)
            {
                v128 v2 = X86.Sse2.srli_si128(v, 8);
                v128 v3 = X86.Sse2.srl_epi64(v2, new v128(n - 64, n - 64));
                v128 v1 = X86.Sse2.srl_epi64(v, new v128(n, n));
                v128 v4 = X86.Sse2.sll_epi64(v2, new v128(64 - n, 64 - n));
                v1 = X86.Sse2.or_si128(v1, v4);
                int t = n >= 64 ? 0 : -1;
                return X86.Sse4_1.blendv_ps(v3, v1, new v128(t));
            }
            //else if (Arm.Neon.IsNeonSupported)
            //{
            //    // TODO: working/testing Neon impl
            //    v128 v2 = Arm.Neon.vdupq_laneq_u64(v, 0);
            //    v128 v3 = Arm.Neon.vshrq_n_u64(v2, n - 64);
            //    v128 v1 = Arm.Neon.vshrq_n_u64(v, n);
            //    v128 v4 = Arm.Neon.vshrq_n_u64(v2, 64 - n);
            //    v1 = Arm.Neon.vorrq_u64(v1, v4);
            //    int t = n >= 64 ? 0 : -1;
            //    return Arm.Neon.vbslq_u64(v3, v1, new v128(t));
            //}
            else
            {
                // Unlike SSE (which clamps shift amounts to the field size), C# computes shift amounts as count & 0x3F,
                // so shifts of >=64 and >=128 are special-cased
                v128 nGte128 = new v128(0, 0);
                v128 nGte64 = new v128(v.ULong1 >> (n - 64), 0);

                v128 v2 = new v128(v.ULong1, 0);
                v128 v1 = new v128(v.ULong0 >> n, v.ULong1 >> n);
                v128 v4 = new v128(v2.ULong0 << (64 - n), v2.ULong1 << (64 - n));
                v1 = new v128(v1.ULong0 | v4.ULong0, v1.ULong1 | v4.ULong1);
                v4 = n >= 64 ? nGte64 : v1;
                v4 = n >= 128 ? nGte128 : v4;
                v4 = n == 0 ? v : v4;
                return v4;
            }
        }

        internal static int tzcnt_u128 (v128 u)
        {
            int hi = math.tzcnt(u.ULong1) + 64;
            int lo = math.tzcnt(u.ULong0);
            return lo == 64 ? hi : lo;
        }

        internal static int countbits (v128 u)
        {
            return math.countbits(u.ULong0) + math.countbits(u.ULong1);
        }
    }
}
