using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameObjectConversion;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities;
using Unity.Entities.Conversion;
using Unity.Entities.Serialization;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.Tasks;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEditor.Build.Pipeline.Injector;
using UnityEditor.Build.Utilities;
using BuildCompression = UnityEngine.BuildCompression;
using BuildPipeline = UnityEditor.BuildPipeline;
using Hash128 = Unity.Entities.Hash128;
using UnityEditor.Experimental;
using UnityEngine;

namespace Unity.Scenes.Editor
{
    internal static class EntitySceneBuildUtility
    {
        private static string WorkingBuildDir = $"Library/EntitySceneBundles";

        internal static void PrepareEntityBinaryArtifacts(Hash128 buildConfigurationGuid, HashSet<Hash128> sceneGuids, Dictionary<Hash128, ArtifactKey> artifactKeys)
        {
            var sceneBuildConfigGuids = new NativeList<GUID>(sceneGuids.Count, Allocator.TempJob);

            do
            {
                var requiresRefresh = false;
                foreach(var sceneGuid in sceneGuids)
                {
                    var guid = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(sceneGuid, buildConfigurationGuid, false, LiveConversionSettings.IsBakingEnabled, LiveConversionSettings.IsBuiltinBuildsEnabled, out var thisRequiresRefresh);
                    sceneBuildConfigGuids.Add(guid);
                    requiresRefresh |= thisRequiresRefresh;
                    artifactKeys.Add(sceneGuid, new ArtifactKey(guid, typeof(SubSceneImporter)));
                }

                if (requiresRefresh)
                    AssetDatabase.Refresh();

                foreach (var sceneGuid in sceneGuids)
                {
                    SceneWithBuildConfigurationGUIDs.EnsureExistsFor(sceneGuid, buildConfigurationGuid, false, LiveConversionSettings.IsBakingEnabled, LiveConversionSettings.IsBuiltinBuildsEnabled, out var thisRequiresRefresh);
                    if(thisRequiresRefresh)
                        Debug.LogWarning("Refresh failed");
                }

                AssetDatabaseExperimental.ProduceArtifactsAsync(sceneBuildConfigGuids.ToArrayNBC(), typeof(SubSceneImporter));
                sceneGuids.Clear();

                foreach (var sceneBuildConfigGuid in sceneBuildConfigGuids)
                {
                    var artifactKey = AssetDatabaseExperimental.ProduceArtifact(new ArtifactKey(sceneBuildConfigGuid, typeof(SubSceneImporter)));
                    AssetDatabaseExperimental.GetArtifactPaths(artifactKey, out var paths);
                    var weakAssetRefsPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesWeakAssetRefs);
                    if (!BlobAssetReference<BlobArray<UntypedWeakReferenceId>>.TryRead(weakAssetRefsPath, 1, out var weakAssets))
                        continue;
                    for(int i=0;i<weakAssets.Value.Length;++i)
                    {
                        var weakAssetRef = weakAssets.Value[i];
                        if (weakAssetRef.GenerationType == WeakReferenceGenerationType.Scene || weakAssetRef.GenerationType == WeakReferenceGenerationType.Prefab)
                        {
                            if(!artifactKeys.ContainsKey(weakAssetRef.AssetId))
                                sceneGuids.Add(weakAssetRef.AssetId);
                        }
                    }
                    weakAssets.Dispose();
                }
                sceneBuildConfigGuids.Clear();
            } while (sceneGuids.Count > 0);

            sceneBuildConfigGuids.Dispose();
        }

        static unsafe BuildUsageTagGlobal ReadGlobalUsageArtifact(string globalUsgExt, string[] artifactPaths)
        {
            var path = artifactPaths.Where(x => x.EndsWith(globalUsgExt, StringComparison.Ordinal)).FirstOrDefault();
            if (string.IsNullOrEmpty(path))
                return default;

            BuildUsageTagGlobal globalUsage = default;
            using (var reader = new StreamBinaryReader(path))
            {
                reader.ReadBytes(&globalUsage, sizeof(BuildUsageTagGlobal));
            }
            return globalUsage;
        }

        // This function is responsible for providing all the entity scenes to the build.
        //
        // The way these files get generated is that we have a SceneWithBuildConfiguration file, (which is a bit of a hack to work around the inability for scriptable importers to take arguments, so
        // instead we create a different file that points to the scene we want to import, and points to the buildconfiguration we want to import it for).   The SubsceneImporter will import this file,
        // and it will make 3 (relevant) kind of files:
        // - headerfile
        // - entitybinaryformat file (the actual entities payloads)
        // - a SerializedFile that has an array of UnityEngine.Object PPtrs that are used by this entity file.
        //
        // The first two we deal with very simply: they just need to be copied into the build, and we're done.
        // the third one, we will feed as input to the Scriptable build pipeline (which is actually about creating assetbundles), and create an assetbundle that
        // has all those objects in it that the 3rd file referred to.  We do this with a batch api, first we loop through all subscenes, and register with this batch
        // api which assetbundles we'd like to see produced, and then at the end, we say "okay make them please".  this assetbundle creation api has a caching system
        // that is separate from the assetpipeline caching system, so if all goes well, the call to produce these assetbundles will return very fast and did nothing.
        //
        // The reason for the strange looking api, where a two callbacks get passed in is to make integration of the new incremental buildpipeline easier, as this code
        // needs to be compatible both with the current buildpipeline in the dots-repo, as well as with the incremental buildpipeline.  When that is merged, we can simplify this.
        internal static void PrepareAdditionalFiles(Hash128[] sceneGuids, ArtifactKey[] entitySceneArtifacts, BuildTarget target, Action<string, string> RegisterFileCopy, string outputDirectory)
        {
            if (target == BuildTarget.NoTarget)
                throw new InvalidOperationException($"Invalid build target '{target.ToString()}'.");

            Assert.AreEqual(sceneGuids.Length, entitySceneArtifacts.Length);

            var content = new BundleBuildContent(new AssetBundleBuild[0]);
            var bundleNames = new HashSet<string>();
            var subScenePaths = new Dictionary<Hash128, string>();
            var dependencyInputData = new Dictionary<SceneSection, SectionDependencyInfo>();

            var refExt = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesUnityObjectReferences);
            var headerExt = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesHeader);
            var binaryExt = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesBinary);
            string conversionLogExtension = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesConversionLog);
            string globalUsgExt = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesGlobalUsage);
            string exportedTypes = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesExportedTypes);

            var group = BuildPipeline.GetBuildTargetGroup(target);
            var parameters = new BundleBuildParameters(target, @group, WorkingBuildDir)
            {
                BundleCompression = BuildCompression.LZ4Runtime
            };

            var artifactHashes = new UnityEngine.Hash128[entitySceneArtifacts.Length];
            AssetDatabaseCompatibility.ProduceArtifactsRefreshIfNecessary(entitySceneArtifacts, artifactHashes);

            List<(Hash128, string)> sceneGuidExportedTypePaths = new List<(Hash128, string)>();
            for (int i = 0; i != entitySceneArtifacts.Length; i++)
            {
                var sceneGuid = sceneGuids[i];
                var sceneBuildConfigGuid = entitySceneArtifacts[i].guid;
                var artifactHash = artifactHashes[i];

                bool foundEntityHeader = false;

                if (!artifactHash.isValid)
                    throw new Exception($"Building EntityScene artifact failed: '{AssetDatabaseCompatibility.GuidToPath(sceneGuid)}' ({sceneGuid}). There were exceptions during the entity scene imports.");

                AssetDatabaseCompatibility.GetArtifactPaths(artifactHash, out var artifactPaths);

                foreach (var artifactPath in artifactPaths)
                {
                    //UnityEngine.Debug.Log($"guid: {sceneGuid} artifact: '{artifactPath}'");

                    //@TODO: This looks like a workaround. Whats going on here?
                    var ext = Path.GetExtension(artifactPath).Replace(".", "");

                    if (ext == conversionLogExtension)
                    {
                        var res = ConversionLogUtils.PrintConversionLogToUnityConsole(artifactPath);

                        if (res.HasException)
                        {
                            throw new Exception("Building entity scenes failed. There were exceptions during the entity scene imports.");
                        }
                    }
                    else if (ext == headerExt)
                    {
                        foundEntityHeader = true;

                        if (!string.IsNullOrEmpty(artifactPaths.FirstOrDefault(a => a.EndsWith(refExt, StringComparison.Ordinal))))
                        {
                            subScenePaths[sceneGuid] = artifactPath;
                        }
                        else
                        {
                            //if there are no reference bundles, then deduplication can be skipped
                            var destinationFile = EntityScenesPaths.RelativePathFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesHeader, -1);
                            DoCopy(RegisterFileCopy, outputDirectory, artifactPath, destinationFile);
                        }
                    }
                    else if (ext == binaryExt)
                    {
                        var destinationFile = EntityScenesPaths.RelativePathFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesBinary, EntityScenesPaths.GetSectionIndexFromPath(artifactPath));
                        DoCopy(RegisterFileCopy, outputDirectory, artifactPath, destinationFile);
                    }
                    else if (ext == refExt)
                    {
                        var globalUsage = ReadGlobalUsageArtifact(globalUsgExt, artifactPaths);
                        content.CustomAssets.Add(new CustomContent
                        {
                            Asset = sceneBuildConfigGuid,
                            Processor = (guid, processor) =>
                            {
                                var sectionIndex = EntityScenesPaths.GetSectionIndexFromPath(artifactPath);
                                processor.GetObjectIdentifiersAndTypesForSerializedFile(artifactPath, out ObjectIdentifier[] objectIds, out Type[] types, globalUsage);
                                dependencyInputData[new SceneSection() { SceneGUID = sceneGuid, Section = sectionIndex }] = CreateDependencyInfo(objectIds, target, parameters.ScriptInfo);
                                var bundleName = EntityScenesPaths.GetFileName(sceneGuid, EntityScenesPaths.PathType.EntitiesUnityObjectReferencesBundle, sectionIndex);
                                processor.CreateAssetEntryForObjectIdentifiers(objectIds, artifactPath, bundleName, bundleName, typeof(ReferencedUnityObjects));
                                bundleNames.Add(bundleName);
                            }
                        });
                    }
                    else if (ext == exportedTypes)
                    {
                        sceneGuidExportedTypePaths.Add((sceneGuid, artifactPath));
                    }
                }

                if (!foundEntityHeader)
                {
                    Debug.LogError($"Failed to build EntityScene for '{AssetDatabaseCompatibility.GuidToPath(sceneGuid)}'");
                }
            }

            WriteExportedTypesDebugLog(sceneGuidExportedTypePaths);

            if (content.CustomAssets.Count <= 0)
                return;

            var dependencyMapping = new Dictionary<Hash128, Dictionary<SceneSection, List<Hash128>>>();
            var explicitLayout = new BundleExplictObjectLayout();
            ContentPipeline.BuildCallbacks.PostDependencyCallback = (buildParams, dependencyData) => ExtractDuplicateObjects(buildParams, dependencyInputData, explicitLayout, bundleNames, dependencyMapping);
            var status = ContentPipeline.BuildAssetBundles(parameters, content, out IBundleBuildResults result, CreateTaskList(), explicitLayout);
            PostBuildCallback?.Invoke(dependencyMapping);
            foreach (var bundleName in bundleNames)
            {
                // Console.WriteLine("Copy bundle: " + bundleName);
                DoCopy(RegisterFileCopy, outputDirectory, $"{WorkingBuildDir}/{bundleName}", $"{EntityScenesPaths.k_EntitySceneSubDir}/{bundleName}");
            }


            foreach (var ssIter in subScenePaths)
            {
                string headerArtifactPath = ssIter.Value;
                Hash128 sceneGUID = ssIter.Key;

                dependencyMapping.TryGetValue(sceneGUID, out var sceneDependencyData);
                var tempPath = $"{WorkingBuildDir}/{sceneGUID}.{EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesHeader)}";

                if (!ResolveSceneSectionUtility.ReadHeader(headerArtifactPath, out var sceneMetaDataRef, sceneGUID, out var headerBlobOwner))
                {
                    continue;
                }
                UpdateSceneMetaDataDependencies(ref sceneMetaDataRef, sceneDependencyData, tempPath);
                sceneMetaDataRef.Dispose();
                headerBlobOwner.Release();

                var headerDestPath = EntityScenesPaths.RelativePathFolderFor(sceneGUID, EntityScenesPaths.PathType.EntitiesHeader, -1);
                DoCopy(RegisterFileCopy, outputDirectory, tempPath, headerDestPath);
            }

            var succeeded = status >= ReturnCode.Success;
            if (!succeeded)
                throw new InvalidOperationException($"BuildAssetBundles failed with status '{status}'.");
        }

        internal static Action<Dictionary<Hash128, Dictionary<SceneSection, List<Hash128>>>> PostBuildCallback;

        internal static string GetExportedTypesLogsFilePath()
        {
            return Application.dataPath + "/../Logs/" + SerializeUtility.k_ExportedTypesDebugLogFileName;
        }

        internal static void WriteExportedTypesDebugLog(IEnumerable<(Hash128 sceneGuid, string entitiesExportedTypesPath)> scenes)
        {
            StreamWriter writer = File.CreateText(GetExportedTypesLogsFilePath());

            // Write all types
            writer.WriteLine($"::All Types in TypeManager (by stable hash)::");
            IEnumerable<TypeManager.TypeInfo> typesToWrite = TypeManager.AllTypes;
            var debugTypeHashes = typesToWrite.OrderBy(ti => ti.StableTypeHash)
                .Where(ti => ti.Type != null).Select(ti =>
                    $"0x{ti.StableTypeHash:x16} - {ti.StableTypeHash,22} - {ti.Type.FullName}");
            foreach(var type in debugTypeHashes)
                writer.WriteLine(type);
            writer.WriteLine("\n");

            // Write all exported types per scene
            foreach(var scene in scenes)
            {
                var srcLogFile = File.ReadLines(scene.entitiesExportedTypesPath);
                writer.WriteLine($"Exported Types (by stable hash) for scene: {scene.sceneGuid.ToString()}");
                foreach (var line in srcLogFile)
                {
                    if(line.StartsWith("0x"))
                        writer.WriteLine(line);
                }
                writer.WriteLine("\n");
            }
            writer.Close();
        }

        static void UpdateSceneMetaDataDependencies(ref BlobAssetReference<SceneMetaData> sceneMetaData, Dictionary<SceneSection, List<Hash128>> sceneDependencyData, string outPath)
        {
            var blob = new BlobBuilder(Allocator.Temp);
            ref var root = ref blob.ConstructRoot<SceneMetaData>();
            var sectionDataArray = blob.Construct(ref root.Sections, sceneMetaData.Value.Sections.ToArray());

            // recursively copy scene section metadata
            {
                ref var sceneSectionCustomMetadata = ref sceneMetaData.Value.SceneSectionCustomMetadata;
                var sceneMetaDataLength = sceneSectionCustomMetadata.Length;
                var dstMetadataArray = blob.Allocate(ref root.SceneSectionCustomMetadata, sceneMetaDataLength);

                for (int i = 0; i < sceneMetaDataLength; i++)
                {
                    var metaData = blob.Allocate(ref dstMetadataArray[i], sceneSectionCustomMetadata[i].Length);
                    for (int j = 0; j < metaData.Length; j++)
                    {
                        metaData[j].StableTypeHash = sceneSectionCustomMetadata[i][j].StableTypeHash;
                        blob.Construct(ref metaData[j].Data, sceneSectionCustomMetadata[i][j].Data.ToArray());
                    }
                }
            }

            blob.AllocateString(ref root.SceneName, sceneMetaData.Value.SceneName.ToString());
            BlobBuilderArray<BlobArray<Hash128>> deps = blob.Allocate(ref root.Dependencies, sceneMetaData.Value.Sections.Length);

            if (sceneDependencyData != null)
            {
                for (int i = 0; i < deps.Length; i++)
                {
                    var section = new SceneSection()
                    {
                        SceneGUID = sceneMetaData.Value.Sections[i].SceneGUID,
                        Section = sceneMetaData.Value.Sections[i].SubSectionIndex
                    };

                    if (sceneDependencyData.TryGetValue(section, out var bundleIds))
                        blob.Construct(ref deps[i], bundleIds.ToArray());
                }
            }

            EditorEntityScenes.WriteHeader(outPath, ref root, sectionDataArray, blob);
        }

        static void DoCopy(Action<string, string> RegisterFileCopy, string outputStreamingAssetsFolder, string src, string dst)
        {
            RegisterFileCopy(src, outputStreamingAssetsFolder + "/" + dst);
#if USE_ASSETBUNDLES_IN_EDITOR_PLAY_MODE
            if (!Directory.Exists(UnityEngine.Application.streamingAssetsPath))
                Directory.CreateDirectory(UnityEngine.Application.streamingAssetsPath);
            RegisterFileCopy(src, UnityEngine.Application.streamingAssetsPath + "/" + dst);
#endif
        }

        internal static SectionDependencyInfo CreateDependencyInfo(ObjectIdentifier[] objectIds, BuildTarget target, UnityEditor.Build.Player.TypeDB scriptInfo)
        {
            //TODO: cache this dependency data
            var dependencies = ContentBuildInterface.GetPlayerDependenciesForObjects(objectIds, target, scriptInfo);
            var depTypes = ContentBuildInterface.GetTypeForObjects(dependencies);
            var paths = dependencies.Select(i => AssetDatabaseCompatibility.GuidToPath(i.guid)).ToArray();
            return new SectionDependencyInfo() { Dependencies = dependencies, Paths = paths, Types = depTypes };
        }

        internal struct SectionDependencyInfo
        {
            public ObjectIdentifier[] Dependencies;
            public Type[] Types;
            public string[] Paths;
        }

        static ReturnCode ExtractDuplicateObjects(IBuildParameters parameters, Dictionary<SceneSection, SectionDependencyInfo> dependencyInpuData, BundleExplictObjectLayout layout, HashSet<string> bundleNames, Dictionary<Hash128, Dictionary<SceneSection, List<Hash128>>> result)
        {
            var bundleLayout = new Dictionary<Hash128, List<ObjectIdentifier>>();
            CreateAssetLayoutData(dependencyInpuData, result, bundleLayout);
            ExtractExplicitBundleLayout(bundleLayout, layout, bundleNames);
            return ReturnCode.Success;
        }

        static void ExtractExplicitBundleLayout(Dictionary<Hash128, List<ObjectIdentifier>> bundleLayout, BundleExplictObjectLayout layout, HashSet<string> bundleNames)
        {
            foreach (var sectionIter in bundleLayout)
            {
                var bundleName = $"{sectionIter.Key}.bundle";
                foreach (var i in sectionIter.Value)
                {
                    try
                    { layout.ExplicitObjectLocation.Add(i, bundleName); }
                    catch { Debug.LogError($"Trying to add bundle: '{bundleName}' current value '{layout.ExplicitObjectLocation[i]}' object type '{ContentBuildInterface.GetTypeForObject(i).Name}'"); };
                }
                bundleNames.Add(bundleName);
            }
        }

        /// <summary>
        /// Create bundle layout and depedendency data for subscene bundles
        /// </summary>
        /// <param name="dependencyInputData">Mapping of SceneSection to dependency info for that section.</param>
        /// <param name="dependencyResult">Mapping of subscene id to mapping of section to bundle ids</param>
        /// <param name="bundleLayoutResult">Mapping of bundle ids to included objects</param>
        internal static void CreateAssetLayoutData(Dictionary<SceneSection, SectionDependencyInfo> dependencyInputData, Dictionary<Hash128, Dictionary<SceneSection, List<Hash128>>> dependencyResult, Dictionary<Hash128, List<ObjectIdentifier>> bundleLayoutResult)
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            if (!ValidateInput(dependencyInputData, out var error))
            {
                Debug.Log(error);
                return;
            }

            var depToSections = new Dictionary<ObjectIdentifier, List<SceneSection>>();
            //for each subscene, collect all dependencies and map them to the scenes they are referenced by.
            //also create a mapping from the subscene to all of its depedencies
            foreach (var sectionIter in dependencyInputData)
            {
                foreach (var dependency in sectionIter.Value.Dependencies)
                {
                    // Built In Resources we reference directly
                    if (dependency.guid == GUIDHelper.UnityBuiltinResources)
                        continue;

                    if (!depToSections.TryGetValue(dependency, out List<SceneSection> sectionList))
                    {
                        sectionList = new List<SceneSection>();
                        depToSections.Add(dependency, sectionList);
                    }
                    sectionList.Add(sectionIter.Key);
                }
            }

            //convert each list of scenes into a hash
            var objToSectionUsageHash = new Dictionary<ObjectIdentifier, Hash128>();
            foreach (var objIter in depToSections)
            {
                if (objIter.Value.Count <= 1)
                    continue;

                objToSectionUsageHash.Add(objIter.Key, HashingMethods.Calculate(objIter.Value).ToHash128());
            }

            if (objToSectionUsageHash.Count > 0)
            {
                //create mapping from scene hash to included dependencies
                foreach (var objIter in objToSectionUsageHash)
                {
                    if (!bundleLayoutResult.TryGetValue(objIter.Value, out var ids))
                        bundleLayoutResult.Add(objIter.Value, ids = new List<ObjectIdentifier>());
                    ids.Add(objIter.Key);
                }

                foreach (var sectionIter in dependencyInputData)
                {
                    var bundleHashes = new HashSet<Hash128>();
                    foreach (var dep in dependencyInputData[sectionIter.Key].Dependencies)
                        if (objToSectionUsageHash.TryGetValue(dep, out var sceneHash))
                            bundleHashes.Add(sceneHash);
                    if (!dependencyResult.TryGetValue(sectionIter.Key.SceneGUID, out var sectionMap))
                        dependencyResult.Add(sectionIter.Key.SceneGUID, sectionMap = new Dictionary<SceneSection, List<Hash128>>());
                    sectionMap[sectionIter.Key] = bundleHashes.ToList();
                }
            }

            sw.Stop();
            Console.WriteLine($"CreateAssetLayoutData time: {sw.Elapsed}");
        }

        internal static bool ValidateInput(Dictionary<SceneSection, SectionDependencyInfo> dependencyInputData, out string firstError)
        {
            firstError = null;
            if (dependencyInputData == null)
            {
                firstError = "NULL dependencyInputData.";
                return false;
            }
            foreach (var sec in dependencyInputData)
            {
                if (!sec.Key.SceneGUID.IsValid)
                {
                    firstError = "Invalid scene guid for section.";
                    return false;
                }
                if (sec.Key.Section < 0)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - Invalid section index {sec.Key.Section}.";
                    return false;
                }
                if (sec.Value.Dependencies == null)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - null Dependencies.";
                    return false;
                }
                if (sec.Value.Paths == null)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - null Paths.";
                    return false;
                }
                if (sec.Value.Types == null)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - null Types.";
                    return false;
                }
                if (sec.Value.Dependencies.Length != sec.Value.Paths.Length || sec.Value.Dependencies.Length != sec.Value.Types.Length)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - Data length mismatch: Dependencies: {sec.Value.Dependencies.Length}, Types: {sec.Value.Types.Length}, Paths: {sec.Value.Paths.Length}.";
                    return false;
                }
                for (int i = 0; i < sec.Value.Dependencies.Length; i++)
                {
                    if (sec.Value.Dependencies[i].guid.Empty())
                    {
                        firstError = $"Scene {sec.Key.SceneGUID} - Dependencies[{i}] has invalid GUID, path='{sec.Value.Paths[i]}'.";
                        return false;
                    }
                    if (sec.Value.Types[i] == null)
                    {
                        firstError = $"Scene {sec.Key.SceneGUID} - Types[{i}] is NULL, path='{sec.Value.Paths[i]}'.";
                        return false;
                    }
                    if (string.IsNullOrEmpty(sec.Value.Paths[i]))
                    {
                        firstError = $"Scene {sec.Key.SceneGUID} - Paths[{i}] is NULL or empty.";
                        return false;
                    }
                }
            }
            return true;
        }

        internal class UpdateBundlePacking : IBuildTask
        {
            public int Version { get { return 1; } }

#pragma warning disable 649
            [InjectContext]
            IBundleWriteData m_WriteData;

            [InjectContext(ContextUsage.In, true)]
            IBundleExplictObjectLayout m_Layout;

            [InjectContext(ContextUsage.In)]
            IDeterministicIdentifiers m_PackingMethod;
#pragma warning restore 649

            public ReturnCode Run()
            {
                if (m_Layout != null)
                {
                    var extractedBundlesToFileDependencies = new Dictionary<string, HashSet<string>>();
                    foreach (var pair in m_Layout.ExplicitObjectLocation)
                    {
                        ObjectIdentifier objectID = pair.Key;
                        string bundleName = pair.Value;
                        string internalName = string.Format(CommonStrings.AssetBundleNameFormat, m_PackingMethod.GenerateInternalFileName(bundleName));
                        foreach (var assetFilesPair in m_WriteData.AssetToFiles)
                        {
                            if (assetFilesPair.Value.Contains(internalName))
                            {
                                if (!extractedBundlesToFileDependencies.TryGetValue(internalName, out var dependencies))
                                {
                                    extractedBundlesToFileDependencies.Add(internalName, dependencies = new HashSet<string>());
                                    foreach (var afp in assetFilesPair.Value)
                                        dependencies.Add(afp);
                                }
                            }
                        }
                    }
                    Dictionary<string, WriteCommand> fileToCommand = m_WriteData.WriteOperations.ToDictionary(x => x.Command.internalName, x => x.Command);
                    foreach (var pair in extractedBundlesToFileDependencies)
                    {
                        var refMap = m_WriteData.FileToReferenceMap[pair.Key];
                        foreach (var fileDependency in pair.Value)
                        {
                            var cmd = fileToCommand[fileDependency];
                            refMap.AddMappings(fileDependency, cmd.serializeObjects.ToArray());
                        }
                        var cmd2 = fileToCommand[pair.Key];
                        refMap.AddMappings(pair.Key, cmd2.serializeObjects.ToArray(), true);
                    }
                }
                return ReturnCode.Success;
            }
        }

        static IList<IBuildTask> CreateTaskList()
        {
            var taskList = DefaultBuildTasks.Create(DefaultBuildTasks.Preset.AssetBundleBuiltInShaderExtraction);
            // Remove the shader task to use the DOTS dedupe pass only
            taskList.Remove(taskList.First(x => x is CreateBuiltInShadersBundle));
            // Insert the dedupe dependency resolver task
            taskList.Insert(taskList.IndexOf(taskList.First(x => x is GenerateSubAssetPathMaps)), new UpdateBundlePacking());
            return taskList;
        }
    }
}
