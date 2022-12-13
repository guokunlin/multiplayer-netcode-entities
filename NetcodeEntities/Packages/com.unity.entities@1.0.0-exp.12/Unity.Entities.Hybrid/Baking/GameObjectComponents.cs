﻿using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Conversion;
using UnityEngine;

namespace Unity.Entities.Baking
{
    /// <summary>
    /// Stores meta data about all game objects in the scene.
    /// This means we have a copy of the last converted state, where we can see which game objects existed and what components were attached to it.
    /// </summary>
    struct GameObjectComponents
    {
        public struct ComponentData
        {
            public ComponentData(Component component)
            {
                TypeIndex = TypeManager.GetTypeIndex(component.GetType());
                InstanceID = component.GetInstanceID();
            }

            public TypeIndex TypeIndex;
            public int       InstanceID;
        }

        //UnsafeMultiHashMap<int, TransformData>      _TransformData;
        // GameObject InstanceID -> Component Type + component InstanceID
        // Used to keep the last converted state of each game object.
        UnsafeMultiHashMap<int, ComponentData> _GameObjectComponentMetaData;


        public GameObjectComponents(Allocator allocator)
        {
            _GameObjectComponentMetaData = new UnsafeMultiHashMap<int, ComponentData>(1024, allocator);
        }

        public UnsafeMultiHashMap<int, ComponentData>.Enumerator GetComponents(int instanceID)
        {
            return _GameObjectComponentMetaData.GetValuesForKey(instanceID);
        }

        public bool HasComponent (int gameObjectInstanceID, int componentInstanceID)
        {
            foreach(var com in _GameObjectComponentMetaData.GetValuesForKey(gameObjectInstanceID))
            {
                if (com.InstanceID == componentInstanceID)
                    return true;
            }
            return false;
        }

        public int GetComponent (int gameObjectInstanceID, TypeIndex componentType)
        {
            foreach(var com in _GameObjectComponentMetaData.GetValuesForKey(gameObjectInstanceID))
            {
                if (com.TypeIndex == componentType || TypeManager.IsDescendantOf(com.TypeIndex, componentType))
                    return com.InstanceID;
            }
            return 0;
        }

        public void GetComponents (int gameObjectInstanceID, TypeIndex componentType, ref UnsafeList<int> results)
        {
            foreach(var com in _GameObjectComponentMetaData.GetValuesForKey(gameObjectInstanceID))
            {
                if (com.TypeIndex == componentType || TypeManager.IsDescendantOf(com.TypeIndex, componentType))
                    results.Add(com.InstanceID);
            }
        }

        private void GetComponentsHash(int gameObjectInstanceID, TypeIndex componentType, ref xxHash3.StreamingState hash)
        {
            foreach(var com in _GameObjectComponentMetaData.GetValuesForKey(gameObjectInstanceID))
            {
                if (com.TypeIndex == componentType || TypeManager.IsDescendantOf(com.TypeIndex, componentType))
                    hash.Update(com.InstanceID);
            }
        }

        public Hash128 GetComponentsHash(int gameObjectInstanceID, TypeIndex componentType)
        {
            var hashGenerator = new xxHash3.StreamingState(false);
            GetComponentsHash(gameObjectInstanceID, componentType, ref hashGenerator);
            return new Hash128(hashGenerator.DigestHash128());
        }

        public static int GetComponentInParent(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int gameObject, TypeIndex type)
        {
            int res = components.GetComponent(gameObject, type);
            if (res != 0)
                return res;


            if (!hierarchy.TryGetIndexForInstanceId(gameObject, out var index))
            {
                //Debug.LogError("Invalid internal state");
                return 0;
            }

            // We already checked the first one, so skip that one to avoid a duplicate
            index = hierarchy.GetParentForIndex(index);
            while (index != -1)
            {
                gameObject = hierarchy.GetInstanceIdForIndex(index);
                res = components.GetComponent(gameObject, type);
                if (res != 0)
                    return res;

                index = hierarchy.GetParentForIndex(index);
            }
            return 0;
        }

        public void AddGameObject(GameObject gameObject, List<Component> components)
        {
            var instanceID = gameObject.GetInstanceID();

            foreach (var com in components)
                _GameObjectComponentMetaData.Add(instanceID, new ComponentData(com));
        }

        private static int GetComponentInChildrenInternal(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int index, TypeIndex type)
        {
            int res = 0;
            var childIterator = hierarchy.GetChildIndicesForIndex(index);
            while (childIterator.MoveNext())
            {
                int childIndex = childIterator.Current;
                var gameObject = hierarchy.GetInstanceIdForIndex(childIndex);

                // Look up in the current object
                res = components.GetComponent(gameObject, type);
                if (res != 0)
                    break;

                // Look up in the children
                res = GetComponentInChildrenInternal(ref components, ref hierarchy, childIndex, type);
                if (res != 0)
                    break;
            }
            return res;
        }

        public static int GetComponentInChildren(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int gameObject, TypeIndex type)
        {
            int res = components.GetComponent(gameObject, type);
            if (res != 0)
                return res;

            if (!hierarchy.TryGetIndexForInstanceId(gameObject, out var index))
            {
                return 0;
            }
            return GetComponentInChildrenInternal(ref components, ref hierarchy, index, type);
        }

        private static void GetComponentsInChildrenInternal(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int index, TypeIndex type, ref UnsafeList<int> results)
        {
            var childIterator = hierarchy.GetChildIndicesForIndex(index);
            while (childIterator.MoveNext())
            {
                int childIndex = childIterator.Current;
                var gameObject = hierarchy.GetInstanceIdForIndex(childIndex);

                // Look up in the current object
                components.GetComponents(gameObject, type, ref results);

                // Look up in the children
                GetComponentsInChildrenInternal(ref components, ref hierarchy, childIndex, type, ref results);
            }
        }

        public static void GetComponentsInChildren(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int gameObject, TypeIndex type, ref UnsafeList<int> results)
        {
            components.GetComponents(gameObject, type, ref results);

            if (hierarchy.TryGetIndexForInstanceId(gameObject, out var index))
            {
                GetComponentsInChildrenInternal(ref components, ref hierarchy, index, type, ref results);
            }
        }

        private static void GetComponentsInChildrenInternalHash(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int index, TypeIndex type, ref xxHash3.StreamingState hashGenerator)
        {
            var childIterator = hierarchy.GetChildIndicesForIndex(index);
            while (childIterator.MoveNext())
            {
                int childIndex = childIterator.Current;
                var gameObject = hierarchy.GetInstanceIdForIndex(childIndex);

                // Look up in the current object
                components.GetComponentsHash(gameObject, type, ref hashGenerator);

                // Look up in the children
                GetComponentsInChildrenInternalHash(ref components, ref hierarchy, childIndex, type, ref hashGenerator);
            }
        }

        public static Hash128 GetComponentsInChildrenHash(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int gameObject, TypeIndex type)
        {
            var hashGenerator = new xxHash3.StreamingState(false);
            components.GetComponentsHash(gameObject, type, ref hashGenerator);

            if (hierarchy.TryGetIndexForInstanceId(gameObject, out var index))
            {
                GetComponentsInChildrenInternalHash(ref components, ref hierarchy, index, type, ref hashGenerator);
            }
            return new Hash128(hashGenerator.DigestHash128());
        }

        public static void GetComponentsInParent(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int gameObject, TypeIndex type, ref UnsafeList<int> results)
        {
            components.GetComponents(gameObject, type, ref results);

            if (hierarchy.TryGetIndexForInstanceId(gameObject, out var index))
            {
                // We already checked the first one, so skip that one to avoid a duplicate
                index = hierarchy.GetParentForIndex(index);
                while (index != -1)
                {
                    gameObject = hierarchy.GetInstanceIdForIndex(index);
                    components.GetComponents(gameObject, type, ref results);

                    index = hierarchy.GetParentForIndex(index);
                }
            }
        }

        private static void GetComponentsInParentHash(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int gameObject, TypeIndex type, ref xxHash3.StreamingState hashGenerator)
        {
            components.GetComponentsHash(gameObject, type, ref hashGenerator);

            if (hierarchy.TryGetIndexForInstanceId(gameObject, out var index))
            {
                // We already checked the first one, so skip that one to avoid a duplicate
                index = hierarchy.GetParentForIndex(index);
                while (index != -1)
                {
                    gameObject = hierarchy.GetInstanceIdForIndex(index);
                    components.GetComponentsHash(gameObject, type, ref hashGenerator);

                    index = hierarchy.GetParentForIndex(index);
                }
            }
        }

        public static Hash128 GetComponentsInParentHash(ref GameObjectComponents components, ref SceneHierarchy hierarchy, int gameObject, TypeIndex type)
        {
            var hashGenerator = new xxHash3.StreamingState(false);
            GetComponentsInParentHash(ref components, ref hierarchy, gameObject, type, ref hashGenerator);
            return new Hash128(hashGenerator.DigestHash128());
        }

        /// <summary>
        /// Replaces state of the component meta data with the current state
        /// </summary>
        /// <param name="gameObject"></param>
        /// <param name="components"></param>
        /// <returns>Returns true if the game object was created</returns>
        public unsafe bool UpdateGameObject(GameObject gameObject, List<Component> currentComponentsOnGameObject, List<Component> outAddedComponents, List<Component> outExistingComponents, ref UnsafeParallelHashSet<int> removed)
        {
            //TODO: DOTS-5453

            var instanceID = gameObject.GetInstanceID();

            // Find the components that were added, and thus need to be baked
            foreach (var newComponent in currentComponentsOnGameObject)
            {
                bool found = false;
                foreach (var oldComponent in _GameObjectComponentMetaData.GetValuesForKey (instanceID))
                {
                    if (oldComponent.InstanceID == newComponent.GetInstanceID())
                    {
                        outExistingComponents.Add(newComponent);
                        found = true;
                        break;
                    }
                }

                if (!found)
                    outAddedComponents.Add(newComponent);
            }

            foreach (var oldComponent in _GameObjectComponentMetaData.GetValuesForKey (instanceID))
            {
                bool found = false;
                foreach (var newComponent in currentComponentsOnGameObject)
                {
                    if (oldComponent.InstanceID == newComponent.GetInstanceID())
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    removed.Add(oldComponent.InstanceID);
            }

            int gameObjectInstanceID = gameObject.GetInstanceID();
            var removedCount  = _GameObjectComponentMetaData.Remove(gameObjectInstanceID);
            foreach (var com in currentComponentsOnGameObject)
                _GameObjectComponentMetaData.Add(instanceID, new ComponentData(com));

            return removedCount == 0;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="gameObjectInstanceID"></param>
        /// <returns>Returns true if the game object was still alive.</returns>
        public bool DestroyGameObject(int gameObjectInstanceID)
        {
            return _GameObjectComponentMetaData.Remove(gameObjectInstanceID) != 0;
        }

        public void Dispose()
        {
            if (_GameObjectComponentMetaData.IsCreated)
                _GameObjectComponentMetaData.Dispose();
        }
    }
}
