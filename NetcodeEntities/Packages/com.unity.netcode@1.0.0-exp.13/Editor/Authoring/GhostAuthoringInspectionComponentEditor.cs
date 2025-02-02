using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Entities.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.NetCode.Editor
{
    // TODO: Undo/redo is broken in the Editor.
    // TODO: Support copy/paste individual meta datas + main components.
    // TODO: Support multi-object-edit.
    // TODO: Support light-mode.

    /// <summary>UIToolkit drawer for <see cref="GhostAuthoringInspectionComponent"/>.</summary>
    [CustomEditor(typeof(GhostAuthoringInspectionComponent))]
    class GhostAuthoringInspectionComponentEditor : UnityEditor.Editor
    {
        const string k_ShowDisabledComponentsMetaDataKey = "NetCode.Inspection.ShowDisabledComponentsMetaDataKey";
        const string k_PackageId = "Packages/com.unity.netcode";

        // TODO - Manually loaded prefabs as uss is not working.
        static Texture2D PrefabEntityIcon => AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.unity.entities/Editor Default Resources/icons/dark/Entity/EntityPrefab.png");
        static Texture2D ComponentIcon => AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.unity.entities/Editor Default Resources/icons/dark/Components/Component.png");

        static bool ShowDisabledComponentsMetaData
        {
            get => EditorPrefs.GetBool(k_ShowDisabledComponentsMetaDataKey, false);
            set => EditorPrefs.SetBool(k_ShowDisabledComponentsMetaDataKey, value);
        }

        VisualElement m_Root;
        VisualElement m_ResultsPane;

        HelpBox m_ShowingDisabledComponentsHelpBox;
        HelpBox m_UnableToFindComponentHelpBox;
        HelpBox m_NoEntityHelpBox;
        GhostAuthoringComponent m_GhostAuthoringRoot;
        List<Component> m_ReusableComponents = new List<Component>(8);

        void OnEnable()
        {
            EditorApplication.update += OnUpdate;
            Undo.undoRedoPerformed += RequestRebuildInspector;
            GhostAuthoringInspectionComponent.forceRebuildInspector = true;

            var inspection = ((GhostAuthoringInspectionComponent)target);
            inspection.GetComponents<Component>(m_ReusableComponents);
        }

        void OnDisable()
        {
            EditorApplication.update -= OnUpdate;
            Undo.undoRedoPerformed -= RequestRebuildInspector;
        }

        void OnUpdate()
        {
            var inspection = ((GhostAuthoringInspectionComponent)target);
            // Detect other changes:
            var componentsCount = m_ReusableComponents.Count;
            m_ReusableComponents.Clear();
            if (componentsCount != m_ReusableComponents.Count)
                GhostAuthoringInspectionComponent.forceBake = true;

            if (GhostAuthoringInspectionComponent.forceBake && m_GhostAuthoringRoot)
            {
                GhostAuthoringComponentEditor.BakeNetCodePrefab(m_GhostAuthoringRoot);
            }

            if (GhostAuthoringInspectionComponent.forceSave)
            {
                GhostAuthoringInspectionComponent.forceSave = false;
                GhostAuthoringInspectionComponent.forceRebuildInspector = true;


                EditorSceneManager.MarkSceneDirty(inspection.gameObject.scene);
                Array.Sort(inspection.ComponentOverrides);
                EditorUtility.SetDirty(inspection);
            }

            if (GhostAuthoringInspectionComponent.toggleShowingUnmodifiableComponents)
            {
                GhostAuthoringInspectionComponent.toggleShowingUnmodifiableComponents = false;
                ShowDisabledComponentsMetaData ^= true;
                GhostAuthoringInspectionComponent.forceRebuildInspector = true;
            }

            if (GhostAuthoringInspectionComponent.forceRebuildInspector)
                RebuildWindow();
        }

        static void RequestRebuildInspector() => GhostAuthoringInspectionComponent.forceRebuildInspector = true;

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();
            m_Root.style.overflow = new StyleEnum<Overflow>(Overflow.Hidden);
            m_Root.style.flexShrink = 1;

            var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>(Path.Combine(k_PackageId, "Editor/Authoring/GhostAuthoringEditor.uss"));
            m_Root.styleSheets.Add(ss);

            m_UnableToFindComponentHelpBox = new HelpBox($"Unable to find associated {nameof(GhostAuthoringComponent)} in root or parent. " +
                $"Either ensure it exists, or remove this component.", HelpBoxMessageType.Error);
            m_Root.Add(m_UnableToFindComponentHelpBox);

            m_NoEntityHelpBox = new HelpBox($"This GameObject does not create any Entities during baking.", HelpBoxMessageType.Info);
            m_Root.Add(m_NoEntityHelpBox);

            // TODO - Support edge-case where user adds an override to a type and then disables it in code.
            // TODO - Explicitly support changing variant but not anything else if the user does not add the `[SupportPrefabOverrides]` attribute.
            m_ShowingDisabledComponentsHelpBox = new HelpBox($"Components may be un-editable for any of the following reasons:\na) The component implements `[DontSupportPrefabOverrides]` and thus cannot be modified at all.\nb) The component is an input component, thus must be serialized.\nc) The component has not opted into modification via `[SupportPrefabOverrides]`.\nd) The component only has one supported variant.\nToggle viewing disabled components via the Context Menu.", HelpBoxMessageType.Info);
            m_Root.Add(m_ShowingDisabledComponentsHelpBox);

            m_ResultsPane = new VisualElement();
            m_ResultsPane.name = "ResultsPane";

            m_Root.Add(m_ResultsPane);

            RebuildWindow();

            return m_Root;
        }

        void RebuildWindow()
        {
            if (m_Root == null)
                return; // Wait for CreateInspectorGUI.
            GhostAuthoringInspectionComponent.forceRebuildInspector = false;

            var inspection = ((GhostAuthoringInspectionComponent)target);
            if (!m_GhostAuthoringRoot) m_GhostAuthoringRoot = inspection.transform.root.GetComponent<GhostAuthoringComponent>() ?? inspection.GetComponentInParent<GhostAuthoringComponent>();
            var bakingSucceeded = GhostAuthoringComponentEditor.bakingSucceeded;
            BakedGameObjectResult bakedGameObjectResult = default;
            var hasEntitiesForThisGameObject = bakingSucceeded && GhostAuthoringComponentEditor.TryGetEntitiesAssociatedWithAuthoringGameObject(inspection, out bakedGameObjectResult);

            SetVisualElementVisibility(m_UnableToFindComponentHelpBox, !m_GhostAuthoringRoot);
            SetVisualElementVisibility(m_NoEntityHelpBox, hasEntitiesForThisGameObject && bakedGameObjectResult.BakedEntities.Count == 0);
            SetVisualElementVisibility(m_ShowingDisabledComponentsHelpBox, ShowDisabledComponentsMetaData);

            var isEditable = bakingSucceeded && GhostAuthoringComponentEditor.IsViewingPrefab(inspection.gameObject, out _);
            m_ResultsPane.SetEnabled(isEditable);

            m_ResultsPane.Clear();

            if (!hasEntitiesForThisGameObject)
                return;

            foreach (var bakedEntityResult in bakedGameObjectResult.BakedEntities)
            {
                var entityHeader = new FoldoutHeaderElement("EntityLabel", bakedEntityResult.EntityName,
                    $"({(bakedEntityResult.EntityIndex + 1)} / {bakedGameObjectResult.BakedEntities.Count})",
                    "Displays the entity or entities created during Baking of this GameObject.");

                entityHeader.AddToClassList("ghost-inspection-entity-header");
                //entityLabel.label.AddToClassList("ghost-inspection-entity-header__label");
                entityHeader.icon.AddToClassList("ghost-inspection-entity-header__icon");
                entityHeader.icon.style.backgroundImage = PrefabEntityIcon;
                entityHeader.foldout.text += (bakedEntityResult.IsPrimaryEntity) ? " (Primary)" : " (Linked)";
                m_ResultsPane.Add(entityHeader);

                var allComponents = bakedEntityResult.BakedComponents;
                var replicated = new List<BakedComponentItem>(allComponents.Count);
                var nonReplicated = new List<BakedComponentItem>(allComponents.Count);
                foreach (var component in allComponents)
                {
                    if (!component.DoesAllowPrefabTypeModification && !component.DoesAllowVariantModification && !ShowDisabledComponentsMetaData)
                        continue;
                    if (component.anyVariantIsSerialized)
                        replicated.Add(component);
                    else nonReplicated.Add(component);
                }

                var replicatedContainer = CreateReplicationHeaderElement(entityHeader.foldout.contentContainer, replicated,
                "ReplicatedLabel", "Meta-data for GhostComponents", "Lists all netcode meta-data for replicated (i.e. synced) component types.", GhostAuthoringComponentEditor.netcodeColor);

                // Prefer default variants:
                if (bakedEntityResult.GoParent.SourceInspection.ComponentOverrides.Length > 0)
                {
                    replicatedContainer.contentContainer.Add(
                        new HelpBox($"If you intend to use one Variant across <b>all Ghosts</b> (e.g. `Translation - 2D` for a 2D game), prefer to set it as the \"Default Variant\" by implementing `RegisterDefaultVariants` " +
                            "in your own system (derived from `DefaultVariantSystemBase`), rather than using these controls. " +
                            "You can also set defaults for `GhostPrefabTypes` via the `GhostComponentAttribute`.", HelpBoxMessageType.Info));
                }
                else
                {
                    m_ResultsPane.Add(
                        new HelpBox($"Note that this Inspection Component is optional. As you haven't made any overrides, you can safely remove this component.", HelpBoxMessageType.Info));
                }
                // Warn about replicating child components:
                if (!bakedEntityResult.IsRoot)
                {
                    if (replicated.Any(x => x.variant.IsSerialized))
                    {
                        replicatedContainer.contentContainer.Add(new HelpBox("Note: Serializing child entities is relatively slow. " +
                            "Prefer to have multiple Ghosts with faked parenting, if possible.", HelpBoxMessageType.Warning));
                    }
                }

                CreateReplicationHeaderElement(entityHeader.foldout.contentContainer, nonReplicated,
                    "NonReplicatedLabel", "Meta-data for non-replicated Components", "Lists all netcode meta-data for non-replicated component types.", Color.white);
            }
        }

        static void SetVisualElementVisibility(VisualElement visualElement, bool visibleCondition)
        {
            visualElement.style.display = new StyleEnum<DisplayStyle>(visibleCondition ? DisplayStyle.Flex : DisplayStyle.None);
        }

        VisualElement CreateReplicationHeaderElement(VisualElement parentContent, List<BakedComponentItem> bakedComponents, string headerName, string title, string tooltip, Color iconTintColor)
        {
            var header = new FoldoutHeaderElement(headerName, title, bakedComponents.Count.ToString(), tooltip);
            header.AddToClassList("ghost-inspection-replication-header");
            //header.label.AddToClassList("ghost-inspection-replication-header");
            header.icon.AddToClassList("ghost-inspection-entity-header__icon");
            header.icon.style.unityBackgroundImageTintColor = iconTintColor;
            header.icon.style.backgroundImage = ComponentIcon;
            parentContent.Add(header);

            var componentListView = new VisualElement();
            componentListView.AddToClassList("ghost-inspection-entity-content");
            header.foldout.contentContainer.Add(componentListView);

            if (bakedComponents.Count > 0)
            {
                for (var i = 0; i < bakedComponents.Count; i++)
                {
                    var metaData = bakedComponents[i];
                    var metaDataRootElement = CreateMetaDataInspector(metaData, i);
                    componentListView.Add(metaDataRootElement);
                }
            }
            else
            {
                header.SetEnabled(false);
                header.foldout.SetValueWithoutNotify(false);
            }

            return componentListView;
        }

        VisualElement CreateMetaDataInspector(BakedComponentItem bakedComponent, int componentIndex)
        {
            var tooltip = $"NetCode meta data for the '{bakedComponent.fullname}' component.";

            static OverrideTracking CreateOverrideTracking(BakedComponentItem bakedComponentItem, VisualElement insertIntoOverrideTracking)
            {
                return new OverrideTracking("MetaDataInspector", insertIntoOverrideTracking, bakedComponentItem.HasPrefabOverride(),
                    "Reset Entire Component", bakedComponentItem.RemoveEntirePrefabOverride, true);
            }

            if (bakedComponent.anyVariantIsSerialized || bakedComponent.HasMultipleVariants)
            {
                var componentMetaDataFoldout = new Foldout();
                componentMetaDataFoldout.name = "ComponentMetaDataFoldout";
                componentMetaDataFoldout.text = bakedComponent.managedType.Name;
                componentMetaDataFoldout.style.alignContent = new StyleEnum<Align>(Align.Center);
                componentMetaDataFoldout.style.marginBottom = 3;
                componentMetaDataFoldout.style.flexShrink = 1;
                componentMetaDataFoldout.SetValueWithoutNotify(true);
                componentMetaDataFoldout.focusable = false;

                var toggle = componentMetaDataFoldout.Q<Toggle>();
                toggle.tooltip = bakedComponent.fullname;
                toggle.style.flexShrink = 1;
                var foldoutLabel = toggle.Q<Label>(className: UssClasses.UIToolkit.Toggle.Text); // TODO - DropdownField should expose!
                LabelStyle(foldoutLabel);

                var toggleChild = toggle.Q<VisualElement>(className: UssClasses.UIToolkit.BaseField.Input); // TODO - DropdownField should expose!;
                InsertGhostModeToggles(bakedComponent, toggleChild);

                var sendToDropdown = CreateSentToDropdown(bakedComponent);
                componentMetaDataFoldout.Add(sendToDropdown);

                var variantDropdown = CreateVariantDropdown(bakedComponent);
                variantDropdown.SetEnabled(bakedComponent.DoesAllowVariantModification);
                componentMetaDataFoldout.Add(variantDropdown);

                var parent = foldoutLabel.parent;
                var parentIndex = foldoutLabel.parent.IndexOf(foldoutLabel);
                var overrideTracking = CreateOverrideTracking(bakedComponent, foldoutLabel);
                parent.Insert(parentIndex, overrideTracking);
                return componentMetaDataFoldout;
            }

            var componentMetaDataLabel = new Label();
            InsertGhostModeToggles(bakedComponent, componentMetaDataLabel);
            componentMetaDataLabel.name = "ComponentMetaDataLabel";
            componentMetaDataLabel.text = bakedComponent.managedType.Name;
            componentMetaDataLabel.tooltip = tooltip;
            componentMetaDataLabel.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleLeft);
            // TODO - The text here doesn't clip properly because the buttons are CHILDREN of the label. I.e. The buttons are INSIDE the labels rect.
            LabelStyle(componentMetaDataLabel);

            return CreateOverrideTracking(bakedComponent, componentMetaDataLabel);
        }

        static void LabelStyle(Label label)
        {
            label.style.flexShrink = 1;
            label.style.minWidth = 1;
            label.style.overflow = new StyleEnum<Overflow>(Overflow.Hidden);
        }

        static VisualElement CreateVariantDropdown(BakedComponentItem bakedComponent)
        {
            var dropdown = new DropdownField
            {
                name = "VariantDropdownField",
                label = "Variant",
                tooltip = @"Variants change how a components fields are serialized (i.e. replicated).
Use this dropdown to select which variant is used on this component (on this specific ghost entity, and thus; ghost type).

Note that:

 - <b>Components added to the root entity</b> will default to the ""Default Serializer"" (the serializer generated by the SourceGenerators), unless you have modified the default (via a `DefaultVariantSystemBase` derived system).

 - <b>Components added to child entities</b> will default to the `DontSerializeVariant` global variant, as serializing children involves entity memory random-access, which is expensive.",
            };
            DropdownStyle(dropdown);

            for (var i = 0; i < bakedComponent.availableVariants.Length; i++)
            {
                dropdown.choices.Add(bakedComponent.availableVariantReadableNames[i]);
            }

            // Set current value:
            {
                var index = Array.FindIndex(bakedComponent.availableVariants, x => x.Hash == bakedComponent.variant.Hash);
                if (index >= 0)
                {
                    var selectedVariantName = bakedComponent.availableVariantReadableNames[index];
                    dropdown.SetValueWithoutNotify(selectedVariantName);
                }
                else
                {
                    dropdown.SetValueWithoutNotify($"!! Unknown Variant Hash {bakedComponent.VariantHash} !! (Fallback: {bakedComponent.variant.CreateReadableName(bakedComponent.metaData)})");
                    dropdown.style.backgroundColor = GhostAuthoringComponentEditor.brokenColor;
                }
            }

            // Handle value changed.
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var indexOf = Array.IndexOf(bakedComponent.availableVariantReadableNames, evt.newValue);
                if (indexOf >= 0)
                {
                    bakedComponent.variant = bakedComponent.availableVariants[indexOf];
                    bakedComponent.SaveVariant(false, false);
                    dropdown.style.color = new StyleColor(StyleKeyword.Null);
                }
                else
                {
                    Debug.LogError($"Unable to find variant `{evt.newValue}` to select it! Keeping existing!");
                }
            });

            var isOverridenFromDefault = bakedComponent.HasPrefabOverride() && bakedComponent.GetPrefabOverride().IsVariantOverriden;
            var overrideTracking = new OverrideTracking("VariantDropdown", dropdown, isOverridenFromDefault, "Reset Variant", x => bakedComponent.ResetVariantToDefault(), true);
            return overrideTracking;
        }

        /// <summary>Visualizes prefab overrides for custom controls attached to this.</summary>
        class OverrideTracking : VisualElement
        {
            public VisualElement MainField;
            public VisualElement Override;

            public OverrideTracking(string prefabType, VisualElement mainField, bool defaultOverride, string rightClickResetTitle, Action<DropdownMenuAction> rightClickResetAction, bool shrink)
            {
                name = $"{prefabType}OverrideTracking";
                style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Column);
                style.alignItems = new StyleEnum<Align>(Align.Center);
                style.flexGrow = 0;
                style.flexShrink = shrink ? 1 : 0;

                mainField.style.flexGrow = 1;
                mainField.style.flexShrink = 1;
                mainField.style.overflow = new StyleEnum<Overflow>(Overflow.Hidden);
                Add(mainField);

                Override = new VisualElement
                {
                    name = nameof(Override),
                };
                Override.style.height = Override.style.maxHeight = 2;
                Override.style.minWidth = 15;
                Override.style.marginLeft = Override.style.marginRight = 2;
                Override.style.paddingTop = Override.style.paddingBottom = 1;

                Override.style.flexGrow = 1;
                Override.style.flexShrink = 1;
                Override.style.alignSelf = new StyleEnum<Align>(Align.Stretch);
                Override.style.backgroundColor = Color.white;
                Add(Override);

                if (defaultOverride)
                {
                    this.AddManipulator(new ContextualMenuManipulator(evt =>
                    {
                        evt.menu.AppendAction(rightClickResetTitle, rightClickResetAction);
                    }));
                }
                SetOverride(defaultOverride);
            }

            void SetOverride(bool isDefaultOverride)
            {
                Override.style.display = new StyleEnum<DisplayStyle>(isDefaultOverride ? DisplayStyle.Flex : DisplayStyle.None);
                Override.MarkDirtyRepaint();
            }
        }

        static VisualElement CreateSentToDropdown(BakedComponentItem bakedComponent)
        {
            var doesAllowSendTypeOptimizationModification = bakedComponent.DoesAllowSendTypeOptimizationModification;

            var dropdown = new DropdownField();
            dropdown.name = "SendToDropdownField";
            dropdown.label = "Send Optimization";

            dropdown.SetEnabled(doesAllowSendTypeOptimizationModification);

            DropdownStyle(dropdown);

            if (doesAllowSendTypeOptimizationModification)
            {
                dropdown.choices.Add(GetNameForGhostSendType(GhostSendType.DontSend));
                dropdown.choices.Add(GetNameForGhostSendType(GhostSendType.OnlyPredictedClients));
                dropdown.choices.Add(GetNameForGhostSendType(GhostSendType.OnlyInterpolatedClients));
                dropdown.choices.Add(GetNameForGhostSendType(GhostSendType.AllClients));
                dropdown.RegisterValueChangedCallback(OnSendToChanged);
            }

            UpdateUi(GetNameForGhostSendType(bakedComponent.SendTypeOptimization));

            // Handle value changed.
            void OnSendToChanged(ChangeEvent<string> evt)
            {
                var flag = GetFlagForGhostSendTypeOptimization(evt.newValue);
                bakedComponent.SetSendTypeOptimization(flag);
                UpdateUi(evt.newValue);
            }

            void UpdateUi(string buttonValue)
            {
                dropdown.tooltip = $"Optimization that allows you to specify whether or not the server should send (i.e. replicate) the `{bakedComponent.fullname}` component to client ghosts, " +
                    "depending on whether or not a given client is Predicting or Interpolating this ghost." +
                    "\n\nE.g. Only sending the `PhysicsVelocity` component for \"known always predicted\" ghosts, as interpolated ghosts don't use (read) the PhysicsVelocity Component." +
                    "\n\nNote: This optimization is only possible when we can infer the GhostMode at compile time: I.e. When the GhostAuthoringComponent has `OwnerPredicted` selected, or when `SupportedGhostModes` is set to either `Interpolated` or `Predicted` (but not both).";

                dropdown.tooltip += doesAllowSendTypeOptimizationModification
                    ? $"\n\n<color=yellow>The current setting means that {GetTooltipForGhostSendType(bakedComponent.SendTypeOptimization)}</color>"
                    : "\n\n<color=grey>This dropdown is currently disabled as we cannot infer GhostMode, or it's not currently applicable to this specific component.</color>";
                dropdown.tooltip += "\n\nOther send rules may still apply. See documentation for further details.";

                dropdown.value = doesAllowSendTypeOptimizationModification ? buttonValue : "n/a";
                dropdown.MarkDirtyRepaint();
            }

            var isOverridenFromDefault = bakedComponent.HasPrefabOverride() && bakedComponent.GetPrefabOverride().IsSendTypeOptimizationOverriden;
            var overrideTracking = new OverrideTracking("SendToDropdown", dropdown, isOverridenFromDefault, "Reset SendType Override", bakedComponent.ResetSendTypeToDefault, true);
            return overrideTracking;
        }

        static void DropdownStyle(DropdownField dropdownRoot)
        {
            dropdownRoot.style.flexGrow = 0;
            dropdownRoot.style.flexShrink = 1;

            // Label:
            var label = dropdownRoot.Q<Label>();
            label.style.flexGrow = 0;
            label.style.flexShrink = 1;

            label.style.minWidth = 15;
            label.style.width = 110;

            // Dropdown widget:
            var dropdownWidget = dropdownRoot.Q<VisualElement>(className: UssClasses.UIToolkit.BaseField.Input); // TODO - DropdownField should expose!
            dropdownWidget.style.flexGrow = 1;
            dropdownWidget.style.flexShrink = 1;
            dropdownWidget.style.minWidth = 45;
            dropdownWidget.style.width = 100;
        }

        static string GetTooltipForGhostSendType(GhostSendType ghostSendType)
        {
            switch (ghostSendType)
            {
                case GhostSendType.DontSend: return "this component will <b>not</b> be replicated ever, regardless of what `GhostPrefabType` each ghost is in.";
                case GhostSendType.OnlyInterpolatedClients:return "this component will <b>only</b> be replicated for <b>Interpolated Ghosts</b>.";
                case GhostSendType.OnlyPredictedClients: return "this component will <b>only</b> be replicated for <b>Predicted Ghosts</b>.";
                case GhostSendType.AllClients: return "this component <b>will</b> be replicated for both `Predicted` and `Interpolated` Ghosts.";
                default:
                    throw new ArgumentOutOfRangeException(nameof(ghostSendType), ghostSendType, null);
            }
        }

        static string GetNameForGhostSendType(GhostSendType ghostSendType)
        {
            switch (ghostSendType)
            {
                case GhostSendType.DontSend: return "Never Send";
                case GhostSendType.AllClients: return "Always Send";
                case GhostSendType.OnlyInterpolatedClients: return "Only Send when \"Known Interpolated\"";
                case GhostSendType.OnlyPredictedClients: return "Only Send when \"Known Predicted\"";
                default:
                    throw new ArgumentOutOfRangeException(nameof(ghostSendType), ghostSendType, null);
            }
        }
        static GhostSendType GetFlagForGhostSendTypeOptimization(string ghostSendType)
        {
            for (var type = GhostSendType.DontSend; type <= GhostSendType.AllClients; type++)
            {
                var testName = GetNameForGhostSendType(type);
                if (string.Equals(testName, ghostSendType, StringComparison.OrdinalIgnoreCase))
                    return type;
            }

            throw new ArgumentOutOfRangeException(nameof(ghostSendType), ghostSendType, nameof(GetFlagForGhostSendTypeOptimization));
        }

        void InsertGhostModeToggles(BakedComponentItem bakedComponent, VisualElement parent)
        {
            parent.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
            parent.style.flexShrink = 1;

            var separator = new VisualElement();
            separator.name = nameof(separator);
            separator.style.flexGrow = 1;
            separator.style.flexShrink = 1;
            parent.Add(separator);

            var buttonContainer = new VisualElement();
            buttonContainer.name = "GhostPrefabTypeButtons";
            buttonContainer.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
            buttonContainer.SetEnabled(bakedComponent.DoesAllowPrefabTypeModification);

            buttonContainer.Add(CreateButton("S",  GhostPrefabType.Server, "Server"));
            buttonContainer.Add(CreateButton("IC", GhostPrefabType.InterpolatedClient, "Interpolated Client"));
            buttonContainer.Add(CreateButton("PC", GhostPrefabType.PredictedClient, "Predicted Client"));

            var isOverridenFromDefault = bakedComponent.HasPrefabOverride() && bakedComponent.GetPrefabOverride().IsPrefabTypeOverriden;
            var overrideTracking = new OverrideTracking("PrefabType", buttonContainer, isOverridenFromDefault, $"Reset PrefabType Override", bakedComponent.ResetPrefabTypeToDefault, false);

            parent.Add(overrideTracking);

            VisualElement CreateButton(string abbreviation, GhostPrefabType type, string prefabType)
            {
                var button = new Button();
                //button.Q<Label>().style.alignContent = new StyleEnum<Align>(Align.Center);

                button.text = abbreviation;
                button.style.width = 36;
                button.style.height = 22;
                button.style.marginLeft = 1;
                button.style.marginRight = 1;
                button.style.paddingLeft = 1;
                button.style.paddingRight = 1;

                button.style.alignContent = new StyleEnum<Align>(Align.Center);
                button.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);

                UpdateUi();

                button.clicked += ButtonToggled;

                void ButtonToggled()
                {
                    bakedComponent.TogglePrefabType(type);
                    UpdateUi();
                }
                void UpdateUi()
                {
                    var defaultValue = (bakedComponent.defaultVariant.PrefabType & type) != 0;
                    var isSet = (bakedComponent.PrefabType & type) != 0;
                    button.style.backgroundColor = isSet ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.48f, 0.15f, 0.15f);

                    //button.style.color = isSet ? Color.cyan : Color.red;
                    button.tooltip = $"NetCode creates multiple versions of the '{bakedComponent.EntityParent.EntityName}' ghost prefab (one for each mode [Server, Interpolated Client, PredictedClient])." +
                        $"\n\nThis toggle determines if the `{bakedComponent.fullname}` component should be added to the `{prefabType}` version of this ghost." +
                        $" Current value indicates {(isSet ? "<color=cyan>YES</color>" : "<color=red>NO</color>")} and thus <color=yellow>PrefabType is `{bakedComponent.PrefabType}`</color>." +
                        $"\n\nDefault value is: {(defaultValue ? "YES" : "NO")}\n\nTo enable write-access to this toggle, add a `SupportsPrefabOverrides` attribute to your component type." +
                        " <b>Note: It's better practice to create a custom Variant that sets the desired PrefabType (and apply it as the default variant).</b>";
                    button.MarkDirtyRepaint();
                }

                return button;
            }
        }

        class FoldoutHeaderElement : VisualElement
        {
            public readonly Foldout foldout;
            //public readonly Label label;
            public readonly Image icon;
            public readonly VisualElement rowHeader;

            public FoldoutHeaderElement(string headerName, string labelText, string lengthText, string subElementsTooltip)
            {
                name = $"{headerName}FoldoutHeader";

                foldout = new Foldout();
                foldout.name = $"{headerName}Foldout";
                foldout.text = labelText;
                foldout.contentContainer.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Column);
                Add(foldout);

                var toggle = foldout.Q<Toggle>();
                toggle.tooltip = subElementsTooltip;
                foldout.focusable = false;

                icon = new Image();
                icon.name = $"{headerName}Icon";
                icon.tooltip = subElementsTooltip;
                icon.AddToClassList("entity-info__icon");

                rowHeader = toggle[0];
                rowHeader.style.alignItems = new StyleEnum<Align>(Align.Center);
                rowHeader.style.marginTop = new StyleLength(3);
                rowHeader.style.height = new StyleLength(20);
                rowHeader.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
                rowHeader.Insert(1, icon);

                var lengthLabel = new Label();
                lengthLabel.name = $"{headerName}LengthLabel";
                lengthLabel.style.flexGrow = new StyleFloat(1);
                lengthLabel.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Normal);
                lengthLabel.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleRight);
                lengthLabel.style.justifyContent = new StyleEnum<Justify>(Justify.FlexEnd);
                lengthLabel.style.alignContent = new StyleEnum<Align>(Align.FlexEnd);
                lengthLabel.text = lengthText;
                rowHeader.Add(lengthLabel);
            }
        }
    }
}
