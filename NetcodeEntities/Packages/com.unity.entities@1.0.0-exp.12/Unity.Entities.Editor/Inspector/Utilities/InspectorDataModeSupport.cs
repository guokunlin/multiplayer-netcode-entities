using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Editor.Bridge;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities.Editor
{
    static class InspectorDataModeSupport
    {
        static readonly DataMode[] k_EditorDataModes =  { DataMode.Authoring, DataMode.Runtime };
        static readonly DataMode[] k_RuntimeDataModes = { DataMode.Authoring, DataMode.Mixed, DataMode.Runtime };

        static readonly ProfilerMarker k_GetEditorMarker = new("GetEditor");
        static readonly ProfilerMarker k_SelectionCompareMarker = new("Compare Selection");
        static readonly ProfilerMarker k_SelectEditorMarker = new("Select Editor");

        static int s_LastSelectionCount = 0;
        static int s_LastSelectionHash = 0;
        static int s_LastActiveContext = 0;
        static DataMode s_LastInspectorDataMode = DataMode.Disabled;
        static Type s_LastSelectedEditorType;

        [InitializeOnLoadMethod]
        static void Init()
        {
            SelectionBridge.PostProcessSelectionMetaData += OnPostProcessSelectionMetaData;
            SelectionBridge.DeclareDataModeSupport += OnDeclareDataModeSupport;
        }

        static void OnPostProcessSelectionMetaData()
        {
            if (Selection.activeObject is EntitySelectionProxy ||
                Selection.activeContext is HierarchySelectionContext or EntitySelectionProxy { Exists : true } ||
                !IsSelectionTypeSupported())
                return; // Nothing to patch

            // We are selecting a naked GameObject or a GameObject with an invalid EntitySelectionProxy
            // It may be because we switched play modes, the selection comes from somewhere else, or any other reason
            // So we will attempt to patch-in an EntitySelectionProxy, if possible

            // Regardless if patching was successful, we must provide the correct DataMode hint which will always
            // match the play mode since this selection was not properly formed.

            var defaultWorld = World.DefaultGameObjectInjectionWorld;
            if (defaultWorld is not { IsCreated: true })
            {
                // If the default world doesn't exist, we are not in a DOTS context.
                // Use a DataMode that fits the PlayMode.
                SelectionBridge.UpdateSelectionMetaData(null, Application.isPlaying ? DataMode.Runtime : DataMode.Authoring);
                return;
            }

            // We know we have a GameObject selected at this point
            var activeGameObject = Selection.activeGameObject;
            var primaryEntity = defaultWorld.EntityManager.Debug.GetPrimaryEntityForAuthoringObject(activeGameObject);

            // Try to always use `Authoring` when no context was provided.
            // The only exception is in PlayMode when the GameObject is not part of a SubScene: we can't save changes to those.
            var dataModeHint = Application.isPlaying && !activeGameObject.scene.isSubScene
                ? DataMode.Runtime
                : DataMode.Authoring;

            if (!defaultWorld.EntityManager.SafeExists(primaryEntity))
            {
                // We couldn't find the corresponding Entity in the default World.
                // It might exist in a World we don't know about, but there's not much we can do in that case.
                SelectionBridge.UpdateSelectionMetaData(null, dataModeHint);
                return;
            }

            // Successfully patched GameObject with missing primary Entity
            var context = EntitySelectionProxy.CreateInstance(defaultWorld, primaryEntity);
            SelectionBridge.UpdateSelectionMetaData(context, dataModeHint);
        }

        static void OnDeclareDataModeSupport(UnityObject activeSelection, UnityObject activeContext, HashSet<DataMode> supportedModes)
        {
            if (activeSelection is EntitySelectionProxy || activeContext is EntitySelectionProxy || IsSelectionTypeSupported())
                AddSupportedDataModes(supportedModes);
        }

        static void AddSupportedDataModes(HashSet<DataMode> supportedDataModes)
        {
            var modes = EditorApplication.isPlaying ? k_RuntimeDataModes : k_EditorDataModes;
            foreach (var mode in modes)
            {
                supportedDataModes.Add(mode);
            }
        }

        static bool IsSelectionTypeSupported()
            => Selection.activeObject is GameObject go && !PrefabUtility.IsPartOfPrefabAsset(go);

        [RootEditor(supportsAddComponent : false), UsedImplicitly]
        public static Type GetEditor(UnityObject[] targets, UnityObject context, DataMode inspectorDataMode)
        {
            using var getEditorScope = k_GetEditorMarker.Auto();

            // Ensures this runs every time the selection changes, before the inspector is created
            LivePropertyBridge.EnableLivePropertyFeatureGlobally(inspectorDataMode is DataMode.Runtime or DataMode.Mixed);

            if (targets == null || targets.Length == 0)
                return null;

            using var filteredTargetsPool = PooledList<UnityObject>.Make();

            // Check if we can use cached editor type based on selection
            using (k_SelectionCompareMarker.Auto())
            {
                FilterOutRemovedEntitiesFromTargets(targets, filteredTargetsPool.List);

                var selectionHash = GetSelectionHash(filteredTargetsPool.List);
                var contextHash = context is null or EntitySelectionProxy { Exists: false } ? 0 : context.GetHashCode();

                if (filteredTargetsPool.List.Count == s_LastSelectionCount && selectionHash == s_LastSelectionHash &&
                    contextHash == s_LastActiveContext && inspectorDataMode == s_LastInspectorDataMode)
                {
                    return s_LastSelectedEditorType;
                }

                s_LastSelectionCount = filteredTargetsPool.List.Count;
                s_LastSelectionHash = selectionHash;
                s_LastActiveContext = contextHash;
                s_LastInspectorDataMode = inspectorDataMode;
            }

            // If not, do the whole editor selection process and cache it
            s_LastSelectedEditorType = SelectEditor(filteredTargetsPool.List, context, inspectorDataMode);
            return s_LastSelectedEditorType;

            static void FilterOutRemovedEntitiesFromTargets(UnityObject[] targets, List<UnityObject> filteredTargets)
            {
                foreach (var target in targets)
                {
                    if (target is null or EntitySelectionProxy { Exists: false })
                        continue;

                    filteredTargets.Add(target);
                }
            }
        }

        static Type SelectEditor([NotNull] List<UnityObject> targets, UnityObject context, DataMode inspectorDataMode)
        {
            using var selectEditorScope = k_SelectEditorMarker.Auto();

            var hasAtLeastOneNonSavableGameObjectTarget = false;
            var hasAtLeastOnePrefabTarget = false;

            foreach (var target in targets)
            {
                switch (target)
                {
                    case EntitySelectionProxy proxy:
                    {
                        // If a valid EntitySelectionProxy was directly selected
                        // it means we have no backing GameObject, nothing more
                        // is required to provide an inspector. If the proxy was
                        // invalid, however, we can't handle that so we bail.
                        return proxy.Exists
                            ? inspectorDataMode == DataMode.Authoring
                                ? typeof(UnsupportedEntityEditor) // This entity only exists at runtime
                                : typeof(EntityEditor)
                            : null;
                    }
                    case GameObject go:
                    {
                        // GameObjects outside of SubScenes cannot save PlayMode changes
                        hasAtLeastOneNonSavableGameObjectTarget = hasAtLeastOneNonSavableGameObjectTarget || !go.scene.isSubScene;

                        // The authoring of GameObjects that are part of prefabs at runtime is not currently supported
                        hasAtLeastOnePrefabTarget = hasAtLeastOnePrefabTarget || PrefabUtility.IsPartOfPrefabAsset(go);
                        break;
                    }
                }
            }

            return inspectorDataMode switch
            {
                // Trying to author a GameObject which can't be saved during PlayMode.
                DataMode.Authoring
                    when EditorApplication.isPlaying &&
                         hasAtLeastOneNonSavableGameObjectTarget
                    => context is EntitySelectionProxy { Exists: true }
                        ? typeof(UnsupportedEntityEditor)
                        : typeof(UnsupportedGameObjectEditor),

                // Trying to author a GameObject that is part of a prefab asset in PlayMode.
                // This is poorly supported at the moment and the UX around it is very confusing,
                // so we're disabling the possibility for the moment.
                DataMode.Authoring
                    when EditorApplication.isPlaying &&
                         hasAtLeastOnePrefabTarget
                    => typeof(UnsupportedPrefabEntityEditor),

                // Inspecting the Runtime representation of a GameObject that is converted/baked into an Entity.
                DataMode.Runtime
                    when context is EntitySelectionProxy { Exists: true }
                    => typeof(EntityEditor),

                // Anything else: show the default inspector.
                _ => null
            };
        }

        static int GetSelectionHash(List<UnityObject> targets)
        {
            var hash = 0;
            unchecked
            {
                for (var i = 0; i < targets.Count; ++i)
                {
                    if (targets[i] is EntitySelectionProxy { Exists: false })
                        continue;

                    hash = hash * 31 + targets[i].GetHashCode();
                }
            }

            return hash;
        }
    }
}
