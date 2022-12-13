using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    [BurstCompile]
    unsafe partial struct StructuralChange
    {
        [BurstCompile]
        public static void AddComponentEntitiesBatch(EntityComponentStore* entityComponentStore, UnsafeList<EntityBatchInChunk>* entityBatchList, TypeIndex typeIndex)
        {
            entityComponentStore->AddComponent(entityBatchList, ComponentType.FromTypeIndex(typeIndex), 0);
        }

        [BurstCompile]
        public static void AddComponentsEntitiesBatch(EntityComponentStore* entityComponentStore, UnsafeList<EntityBatchInChunk>* entityBatchList, ref ComponentTypeSet typeSet)
        {
            entityComponentStore->AddComponents(entityBatchList, ref typeSet);
        }

        [BurstCompile]
        public static bool AddComponentEntity(EntityComponentStore* entityComponentStore, Entity* entity, TypeIndex typeIndex)
        {
            return entityComponentStore->AddComponent(*entity, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstCompile]
        public static void AddComponentsEntity(EntityComponentStore* entityComponentStore, Entity* entity, ref ComponentTypeSet typeSet)
        {
            entityComponentStore->AddComponent(*entity, typeSet);
        }

        [BurstCompile]
        public static void AddComponentChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, TypeIndex typeIndex)
        {
            entityComponentStore->AddComponent(chunks, chunkCount, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstCompile]
        public static void AddComponentsChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, ref ComponentTypeSet typeSet)
        {
            entityComponentStore->AddComponents(chunks, chunkCount, typeSet);
        }

        [BurstCompile]
        public static bool RemoveComponentEntity(EntityComponentStore* entityComponentStore, Entity* entity, TypeIndex typeIndex)
        {
            return entityComponentStore->RemoveComponent(*entity, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstCompile]
        public static void RemoveComponentsEntity(EntityComponentStore* entityComponentStore, Entity* entity, ref ComponentTypeSet typeSet)
        {
            entityComponentStore->RemoveComponent(*entity, typeSet);
        }

        [BurstCompile]
        public static void RemoveComponentEntitiesBatch(EntityComponentStore* entityComponentStore, UnsafeList<EntityBatchInChunk>* entityBatchList, TypeIndex typeIndex)
        {
            entityComponentStore->RemoveComponent(entityBatchList, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstCompile]
        public static void RemoveComponentsEntitiesBatch(EntityComponentStore* entityComponentStore, UnsafeList<EntityBatchInChunk>* entityBatchList, ref ComponentTypeSet typeSet)
        {
            entityComponentStore->RemoveComponents(entityBatchList, ref typeSet);
        }

        [BurstCompile]
        public static void RemoveComponentChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, TypeIndex typeIndex)
        {
            entityComponentStore->RemoveComponent(chunks, chunkCount, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstCompile]
        public static void RemoveComponentsChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, ref ComponentTypeSet typeSet)
        {
            entityComponentStore->RemoveComponents(chunks, chunkCount, typeSet);
        }

        [BurstCompile]
        public static void AddSharedComponentDataIndexWithBurst(EntityComponentStore* entityComponentStore, Entity* entities, int entityCount,
            in ComponentType componentType, int newSharedComponentDataIndex)
        {
            for (int i = 0; i < entityCount; ++i)
            {
                entityComponentStore->AddComponent(entities[i], componentType);
                entityComponentStore->SetSharedComponentDataIndex(entities[i], componentType, newSharedComponentDataIndex);
            }
        }

        [BurstCompile]
        public static void SetSharedComponentDataIndexWithBurst(EntityComponentStore* entityComponentStore, Entity* entities, int entityCount,
            in ComponentType componentType, int newSharedComponentDataIndex)
        {
            for (int i = 0; i < entityCount; ++i)
            {
                entityComponentStore->SetSharedComponentDataIndex(entities[i], componentType, newSharedComponentDataIndex);
            }
        }

        [BurstCompile]
        public static void AddSharedComponentChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, TypeIndex componentTypeIndex, int sharedComponentIndex)
        {
            entityComponentStore->AddComponent(chunks, chunkCount, ComponentType.FromTypeIndex(componentTypeIndex), sharedComponentIndex);
        }

        [BurstCompile]
        public static void MoveEntityArchetype(EntityComponentStore* entityComponentStore, Entity* entity, void* dstArchetype)
        {
            entityComponentStore->Move(*entity, (Archetype*)dstArchetype);
        }

        [BurstCompile]
        public static void SetChunkComponent(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, void* componentData, TypeIndex componentTypeIndex)
        {
            entityComponentStore->SetChunkComponent(chunks, chunkCount, componentData, componentTypeIndex);
        }

        [BurstCompile]
        public static void CreateEntity(EntityComponentStore* entityComponentStore, void* archetype, Entity* outEntities, int count)
        {
            entityComponentStore->CreateEntities((Archetype*)archetype, outEntities, count);
        }

        [BurstCompile]
        public static void DestroyEntity(EntityComponentStore* entityComponentStore, Entity* entities, int count)
        {
            entityComponentStore->DestroyEntities(entities, count);
        }

        [BurstCompile]
        public static void InstantiateEntities(EntityComponentStore* entityComponentStore, Entity* srcEntity, Entity* outputEntities, int instanceCount)
        {
            entityComponentStore->InstantiateEntities(*srcEntity, outputEntities, instanceCount);
        }
    }
}
