﻿using System;
using System.Collections.Generic;
using Unity.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using HierarchyModel = Unity.Entities.Editor.Hierarchy;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// The <see cref="HierarchyElement"/> handles all rendering for the hierarchy. It is responsible for managing the list view and loading states.
    /// </summary>
    class HierarchyElement : VisualElement
    {
        /// <summary>
        /// Nested element to handle the initial loading screen.
        /// </summary>
        class HierarchyLoadingElement : VisualElement
        {
            readonly ProgressBar m_ProgressBar;
            readonly SpinnerElement m_SpinnerElement;

            public HierarchyLoadingElement()
            {
                Resources.Templates.Hierarchy.Loading.Clone(this);
                AddToClassList(UssClasses.Hierarchy.Loading);
                m_ProgressBar = this.Q<ProgressBar>();
                m_SpinnerElement = this.Q<SpinnerElement>();
            }

            public void SetProgress(float progress)
            {
                m_ProgressBar.value = progress * 100;
            }

            public void SetVisibility(bool isVisible)
            {
                VisualElementExtensions.SetVisibility(this, isVisible);

                if (isVisible)
                    m_SpinnerElement.Resume();
                else
                    m_SpinnerElement.Pause();
            }
        }

        class PrefabStageElement : VisualElement
        {
            readonly Button m_BackButton;
            readonly Label m_NameLabel;

            public PrefabStageElement()
            {
                Resources.Templates.Hierarchy.PrefabStage.Clone(this);
                AddToClassList(UssClasses.Hierarchy.PrefabStage);

                m_NameLabel = this.Q<Label>();

                this.Q(className: UssClasses.Hierarchy.PrefabStage).AddManipulator(new Clickable(OnContainerClicked));
                this.Q(className: UssClasses.Hierarchy.PrefabStage + "__back").AddManipulator(new Clickable(OnBackClicked));
            }

            public void SetText(string text)
            {
                m_NameLabel.text = text;
            }

            void OnBackClicked()
            {
                StageNavigationBridge.NavigateBack();
            }

            void OnContainerClicked()
            {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

                if (!prefabStage)
                    return;

                EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(prefabStage.assetPath));
            }
        }

        enum ViewType
        {
            None,
            Hidden,
            Loading,
            Hierarchy,
            Message
        }

        static readonly int[] k_SingleSelectionBuffer = new int[1];

        const string k_ListViewName = "unity-tree-view__list-view";

        static readonly string k_ComponentTypeNotFoundTitle = L10n.Tr("Type not found");
        static readonly string k_ComponentTypeNotFoundContent = L10n.Tr("\"{0}\" is not a component type");
        static readonly string k_NoEntitiesFoundTitle = L10n.Tr("No entity matches your search");

        ViewType m_ViewType;

        readonly HierarchyModel m_Model;
        readonly HierarchyNodes m_Nodes;

        internal readonly HierarchyMultiColumnListView HierarchyMultiColumnListView;

        readonly HierarchyContextMenu m_HierarchyContextMenu;
        readonly PrefabStageElement m_PrefabStageElement;
        readonly HierarchyLoadingElement m_LoadingElement;
        readonly CenteredMessageElement m_MessageElement;

        /// <summary>
        /// The last version the list view refreshed at.
        /// </summary>
        int m_ViewChangeVersion;

        public event Action<HierarchyNodeHandle> OnSelectionChanged;

        internal void SetDecorators(HierarchyDecoratorCollection decorators)
            => HierarchyMultiColumnListView.SetDecorators(decorators);

        public HierarchyElement(HierarchyModel model)
        {
            Resources.Templates.DotsEditorCommon.AddStyles(this);
            Resources.Templates.Hierarchy.Root.AddStyles(this);

            AddToClassList(UssClasses.Hierarchy.Root);

            m_Model = model;
            m_Nodes = m_Model.GetNodes();

            m_HierarchyContextMenu = new HierarchyContextMenu(m_Model, this);
            HierarchyMultiColumnListView = new HierarchyMultiColumnListView(m_Model, m_HierarchyContextMenu)
            {
                name = k_ListViewName,
                selectionType = SelectionType.Single,
                itemsSource = m_Model.GetNodes()
            };
            m_HierarchyContextMenu.RegisterCallbacksOnTarget(this);

            HierarchyMultiColumnListView.OnMakeItem += OnMakeItem;
            HierarchyMultiColumnListView.AddToClassList(k_ListViewName);
            HierarchyMultiColumnListView.selectedIndicesChanged += HandleListViewSelectionIndicesChanged;
            HierarchyMultiColumnListView.RegisterCallback<KeyDownEvent>(OnKeyDown);

            var listViewInnerScrollView = HierarchyMultiColumnListView.Q<ScrollView>();
            listViewInnerScrollView.contentContainer.RegisterCallback<PointerDownEvent>(OnMouseDown);
            listViewInnerScrollView.mode = ScrollViewMode.Vertical;
            listViewInnerScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            m_PrefabStageElement = new PrefabStageElement();
            m_LoadingElement = new HierarchyLoadingElement();
            m_MessageElement = new CenteredMessageElement();

            hierarchy.Add(m_PrefabStageElement);
            hierarchy.Add(HierarchyMultiColumnListView);
            hierarchy.Add(m_LoadingElement);
            hierarchy.Add(m_MessageElement);

            m_PrefabStageElement.SetVisibility(false);
            HierarchyMultiColumnListView.SetVisibility(false);
            m_LoadingElement.SetVisibility(false);
            m_MessageElement.SetVisibility(false);

            HierarchyMultiColumnListView.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == (int) MouseButton.LeftMouse)
                {
                    // Clear the global selection.
                    Selection.activeObject = null;
                }
            });

            listViewInnerScrollView.StretchToParentSize();

            // TEMP: disable header visibility until we have more columns.
            HierarchyMultiColumnListView.SetHeaderVisible(false);

            Refresh();
        }

        public void HandleCommand(string commandName) => m_HierarchyContextMenu?.HandleCommand(HierarchyMultiColumnListView.selectedIndex > 0 ? m_Nodes[HierarchyMultiColumnListView.selectedIndex].GetHandle() : default, commandName);

        void OnMouseDown(PointerDownEvent evt)
        {
            if (evt.button == (int)MouseButton.RightMouse)
            {
                var itemIndex = ListViewBridge.VirtualizationControllerGetItemIndexFromMousePosition(HierarchyMultiColumnListView, evt.localPosition);
                HierarchyMultiColumnListView.SetSelection(itemIndex);
            }
        }

        void SetView(ViewType viewType)
        {
            if (m_ViewType == viewType)
                return;

            m_ViewType = viewType;

            switch (m_ViewType)
            {
                case ViewType.Hidden:
                    HierarchyMultiColumnListView.SetVisibility(false);
                    m_LoadingElement.SetVisibility(false);
                    m_MessageElement.SetVisibility(false);
                    break;
                case ViewType.Loading:
                    HierarchyMultiColumnListView.SetVisibility(false);
                    m_LoadingElement.SetVisibility(true);
                    m_MessageElement.SetVisibility(false);
                    break;
                case ViewType.Hierarchy:
                    HierarchyMultiColumnListView.SetVisibility(true);
                    m_LoadingElement.SetVisibility(false);
                    m_MessageElement.SetVisibility(false);
                    break;
                case ViewType.Message:
                    HierarchyMultiColumnListView.SetVisibility(false);
                    m_LoadingElement.SetVisibility(false);
                    m_MessageElement.SetVisibility(true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(viewType), viewType, null);
            }
        }

        public void SetSelection(HierarchyNodeHandle handle)
        {
            // Force this node to be visible. This may trigger a rebuild of the expanded nodes model.
            m_Nodes.SetAncestorsExpanded(handle);
            k_SingleSelectionBuffer[0] = m_Nodes.IndexOf(handle);
            HierarchyMultiColumnListView.SetSelectionWithoutNotify(k_SingleSelectionBuffer);
        }

        public void ClearSelection()
        {
            HierarchyMultiColumnListView.ClearSelection();
        }

        public void Refresh()
        {
            if (m_Nodes.ChangeVersion == 0)
            {
                m_LoadingElement.SetProgress(m_Model.GetEstimatedProgress());
                SetView(ViewType.Loading);
                return;
            }

            if (m_Nodes.HasFilter())
            {
                var filter = m_Nodes.GetFilter();

                if (!filter.IsValid && !string.IsNullOrEmpty(filter.ErrorComponentType))
                {
                    m_MessageElement.Title = k_ComponentTypeNotFoundTitle;
                    m_MessageElement.Message = string.Format(k_ComponentTypeNotFoundContent, filter.ErrorComponentType);
                    SetView(ViewType.Message);
                    return;
                }

                if (m_Nodes.Count == 0)
                {
                    m_MessageElement.Title = k_NoEntitiesFoundTitle;
                    m_MessageElement.Message = string.Empty;
                    SetView(ViewType.Message);
                    return;
                }
            }

            SetView(ViewType.Hierarchy);

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

            m_PrefabStageElement.SetVisibility(prefabStage != null);
            m_PrefabStageElement.SetText(prefabStage != null ? prefabStage.prefabContentsRoot.name : string.Empty);

            if (m_Nodes.IsChanged(m_ViewChangeVersion))
            {
                m_ViewChangeVersion = m_Nodes.ChangeVersion;
                HierarchyMultiColumnListView.RefreshItems();
            }
        }

        void HandleListViewSelectionIndicesChanged(IEnumerable<int> indices)
        {
            using var enumerator = indices.GetEnumerator();

            var index = enumerator.MoveNext() ? enumerator.Current : default;
            var handle = (uint)index < m_Nodes.Count ? m_Nodes[index].GetHandle() : default;

            OnSelectionChanged?.Invoke(handle);
        }

        /// <summary>
        /// Invoked by the <see cref="UnityEngine.UIElements.ListView"/> whenever a new virtualized item is created.
        /// </summary>
        void OnMakeItem(HierarchyListViewItem element)
        {
            element.Q<Toggle>().RegisterCallback<MouseUpEvent>(OnToggleExpandedState);
        }

        void OnToggleExpandedState(MouseUpEvent evt)
        {
            var toggle = evt.target as VisualElement;
            var element = toggle.GetFirstAncestorOfType<HierarchyListViewItem>();
            var handle = element.Handle;

            SetExpandedState(handle, (evt.modifiers & EventModifiers.Alt) != 0, null);

            evt.StopPropagation();

            // Make sure our TreeView gets focus.
            HierarchyMultiColumnListView.Focus();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (HierarchyMultiColumnListView.selectedIndex == -1) // nothing is currently selected
                return;

            var shouldStopPropagation = true;
            var currentNode = m_Nodes[HierarchyMultiColumnListView.selectedIndex].GetHandle();

            switch (evt.keyCode)
            {
                case KeyCode.RightArrow:
                    SetExpandedState(currentNode, (evt.modifiers & EventModifiers.Alt) != 0, true);
                    break;
                case KeyCode.LeftArrow:
                    SetExpandedState(currentNode, (evt.modifiers & EventModifiers.Alt) != 0, false);
                    break;
                default:
                    shouldStopPropagation = false;
                    break;
            }

            if (shouldStopPropagation)
                evt.StopPropagation();
        }

        void SetExpandedState(HierarchyNodeHandle handle, bool recursive, bool? isExpanded)
        {
            // Flip the expanded state for the underlying nodes.
            if (m_Nodes[handle].GetChildCount() > 0)
            {
                isExpanded ??= !m_Nodes.IsExpanded(handle);

                if (recursive)
                    SetExpandedRecursive(handle, isExpanded.Value, m_Nodes);
                else
                    m_Nodes.SetExpanded(handle, isExpanded.Value);

                Refresh();
            }

            static void SetExpandedRecursive(HierarchyNodeHandle handle, bool isExpanded, HierarchyNodes nodes)
            {
                nodes.SetExpanded(handle, isExpanded);

                if (nodes[handle].GetChildCount() == 0)
                    return;

                foreach (var children in nodes[handle])
                    SetExpandedRecursive(children.GetHandle(), isExpanded, nodes);
            }
        }

        public void Reset()
        {
            m_ViewChangeVersion = 0;
            Refresh();
        }
    }
}
