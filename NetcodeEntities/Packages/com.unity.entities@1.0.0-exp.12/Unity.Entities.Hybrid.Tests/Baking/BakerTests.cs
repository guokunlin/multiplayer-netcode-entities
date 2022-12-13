using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Conversion;
using Unity.Entities.Tests;
using Unity.Scenes.Editor.Tests;
using UnityEditor.Search;
using UnityEditorInternal;
using UnityEngine;

namespace Unity.Entities.Hybrid.Tests.Baking
{
    public class DefaultAuthoringComponent : MonoBehaviour { public int Field; }
    public class Authoring_WithGameObjectField : MonoBehaviour { public GameObject GameObjectField; }
    public class Authoring_AddComponentByComponentType_PrimaryEntity : MonoBehaviour { public int Field; }
    public class Authoring_AddComponentByComponentType_SecondaryValidEntity : MonoBehaviour { public int Field; }
    public class Authoring_AddComponentByMultipleComponentTypes_SecondaryValidEntity : MonoBehaviour { public int Field; }
    public class Authoring_AddComponentGeneric_PrimaryValidEntity : MonoBehaviour { public int Field; }
    public class Authoring_AddChunkComponentGeneric_SecondaryValidEntity : MonoBehaviour { public int Field; }
    public class Authoring_AddSharedComponentGeneric_SecondaryValidEntity : MonoBehaviour { public int Field; }
    public class Authoring_AddUnmanagedSharedComponentGeneric_SecondaryValidEntity : MonoBehaviour { public int Field; }
    public class Authoring_AddBufferGeneric_SecondaryValidEntity : MonoBehaviour { public int Field; }
    public class Authoring_BaseClass : MonoBehaviour { public int Field; }
    public class Authoring_DerivedFromBaseClass : Authoring_BaseClass { public int Field2; }
    public class Authoring_DerivedFromDerivedClass : Authoring_DerivedFromBaseClass { public int Field3; }
    public abstract class Authoring_Abstract : MonoBehaviour
    {
        public int Field;
        public abstract void DoSomething();
    }
    public class Authoring_DerivedFromAbstract : Authoring_Abstract
    {
        public int Field2;
        public override void DoSomething() { UnityEngine.Debug.Log("Derived"); }
    }

    struct ComponentTest1 : IComponentData
    {
        public int Field;
    }

    struct SharedComponentTest1 : ISharedComponentData
    {
        public int Field;
    }

    struct GetComponentTest1 : IComponentData
    {
        public int Field;
        public int GUID;
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    class ManagedComponent : IComponentData
    {
        public int Field;
    }
#endif

    struct ComponentTest2 : IComponentData
    {
        public int Field;
    }

    struct ComponentTest3 : IComponentData
    {
        public int Field;
    }

    struct UnmanagedSharedComponent : ISharedComponentData
    {
        public int Field;
    }

    struct IntElement : IBufferElementData
    {
        public static implicit operator int(IntElement e)
        {
            return e.Value;
        }

        public static implicit operator IntElement(int e)
        {
            return new IntElement {Value = e};
        }

        public int Value;
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    class AddManagedComponentBakerTest : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent authoring)
        {
            AddComponentObject(GetEntity(authoring), new ManagedComponent() { Field = 2});
            AddComponentObject(CreateAdditionalEntity(), new ManagedComponent(){Field = 4});
        }
    }
#endif

    class AddComponent_WithUnregisteredEntity_Baker : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var manager = World.DefaultGameObjectInjectionWorld.EntityManager;
            AddComponent(manager.CreateEntity(), new ComponentTest1());
        }
    }
#if !UNITY_DISABLE_MANAGED_COMPONENTS
    class AddComponentObject_WithUnregisteredEntity_Baker : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var manager = World.DefaultGameObjectInjectionWorld.EntityManager;
            AddComponentObject(manager.CreateEntity(), new ManagedComponent());
        }
    }
#endif

    class BakerDependsOnInheritedType : Baker<AuthoringComponentTestInheritance>
    {
        public override void Bake(AuthoringComponentTestInheritance component)
        {
            GetComponentsInParent<AuthoringComponentBaseTest>();
        }
    }

    // test for
    //     void AddComponent(ComponentType componentType)
    class AddComponentByComponentType_PrimaryEntity : Baker<Authoring_AddComponentByComponentType_PrimaryEntity>
    {
        public override void Bake(Authoring_AddComponentByComponentType_PrimaryEntity component)
        {
            AddComponent(ComponentType.ReadWrite<ComponentTest1>());
        }
    }

    // test for
    //     void AddComponent(Entity entity, ComponentType componentType)
    //
    // with valid secondary entity
    class AddComponentByComponentType_SecondaryValidEntity : Baker<Authoring_AddComponentByComponentType_SecondaryValidEntity>
    {
        public override void Bake(Authoring_AddComponentByComponentType_SecondaryValidEntity component)
        {
            AddComponent(CreateAdditionalEntity(), ComponentType.ReadWrite<ComponentTest1>());
        }
    }

    // test for
    //     void AddComponent(Entity entity, ComponentType componentType)
    //
    // with invalid secondary entity
    class AddComponentByComponentType_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddComponent(Entity.Null, ComponentType.ReadWrite<ComponentTest1>());
        }
    }

    // test for
    //     void AddComponent<T>()
    public sealed class AddComponentGeneric_PrimaryValidEntity : Baker<Authoring_AddComponentGeneric_PrimaryValidEntity>
    {
        public override void Bake(Authoring_AddComponentGeneric_PrimaryValidEntity component)
        {
            AddComponent<ComponentTest1>(new ComponentTest1() {Field = 3});
        }
    }

    // test for
    //     void AddComponent<T>(Entity entity)
    //
    // with valid secondary entity
    class AddComponentGeneric_SecondaryValidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddComponent<ComponentTest1>(CreateAdditionalEntity(), new ComponentTest1() {Field = 3});
        }
    }

    // test for
    //     void AddComponent<T>(Entity entity)
    //
    // with invalid secondary entity
    class AddComponentGeneric_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddComponent<ComponentTest1>(Entity.Null, new ComponentTest1() {Field = 3});
        }
    }

    // test for
    //     void AddComponent(ComponentTypes types)
    class AddComponentByMultipleComponentTypes_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            ComponentTypeSet componentTypeSet = new ComponentTypeSet(ComponentType.ReadWrite<ComponentTest1>(), ComponentType.ReadWrite<ComponentTest2>());
            AddComponent(componentTypeSet);
        }
    }

    // test for
    //     void AddComponent(Entity entity, ComponentTypes types)
    //
    // with valid secondary entity
    class AddComponentByMultipleComponentTypes_SecondaryValidEntity : Baker<Authoring_AddComponentByMultipleComponentTypes_SecondaryValidEntity>
    {
        public override void Bake(Authoring_AddComponentByMultipleComponentTypes_SecondaryValidEntity component)
        {
            ComponentTypeSet componentTypeSet = new ComponentTypeSet(ComponentType.ReadWrite<ComponentTest1>(), ComponentType.ReadWrite<ComponentTest2>());
            AddComponent(CreateAdditionalEntity(), componentTypeSet);
        }
    }

    // test for
    //     void AddComponent(Entity entity, ComponentTypes types)
    //
    // with invalid secondary entity
    class AddComponentByMultipleComponentTypes_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            ComponentTypeSet componentTypeSet = new ComponentTypeSet(ComponentType.ReadWrite<ComponentTest1>(), ComponentType.ReadWrite<ComponentTest2>());
            AddComponent(Entity.Null, componentTypeSet);
        }
    }

    // test for
    //     void AddChunkComponent<T>() where T : struct, IComponentData
    class AddChunkComponentGeneric_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddComponent(ComponentType.ChunkComponent<ComponentTest1>());
        }
    }

    // test for
    //     void AddChunkComponent<T>(Entity entity)
    //
    // with valid secondary entity
    class AddChunkComponentGeneric_SecondaryValidEntity : Baker<Authoring_AddChunkComponentGeneric_SecondaryValidEntity>
    {
        public override void Bake(Authoring_AddChunkComponentGeneric_SecondaryValidEntity component)
        {
            AddComponent(CreateAdditionalEntity(), ComponentType.ChunkComponent<ComponentTest1>());
        }
    }

    // test for
    //     void AddChunkComponent<T>(Entity entity)
    //
    // with invalid secondary entity
    class AddChunkComponentGeneric_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddComponent(Entity.Null, ComponentType.ChunkComponent<ComponentTest1>());
        }
    }

    // test for
    //     void AddSharedComponent<T>(T componentData)
    class AddSharedComponentGeneric_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddSharedComponentManaged<UnmanagedSharedComponent>(new UnmanagedSharedComponent { Field = 1 });
        }
    }

    // test for
    //     void AddSharedComponentManaged<T>(Entity entity)
    //
    // with valid secondary entity
    class AddSharedComponentGeneric_SecondaryValidEntity : Baker<Authoring_AddSharedComponentGeneric_SecondaryValidEntity>
    {
        public override void Bake(Authoring_AddSharedComponentGeneric_SecondaryValidEntity component)
        {
            AddSharedComponentManaged<UnmanagedSharedComponent>(CreateAdditionalEntity(), new UnmanagedSharedComponent { Field = 3 });
        }
    }

    // test for
    //     void AddSharedComponentManaged<T>(Entity entity) where T : struct, IComponentData
    //
    // with invalid secondary entity
    class AddSharedComponentGeneric_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddSharedComponentManaged<UnmanagedSharedComponent>(Entity.Null, new UnmanagedSharedComponent { Field = 3 });
        }
    }

    // test for
    //     void AddSharedComponent<T>(Entity entity, T componentData)
    class AddUnmanagedSharedComponentGeneric_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddSharedComponent<UnmanagedSharedComponent>(new UnmanagedSharedComponent { Field = 1 });
        }
    }

    // test for
    //     void AddSharedComponent<T>(Entity entity, T componentData)
    //
    // with valid secondary entity
    class AddUnmanagedSharedComponentGeneric_SecondaryValidEntity : Baker<Authoring_AddUnmanagedSharedComponentGeneric_SecondaryValidEntity>
    {
        public override void Bake(Authoring_AddUnmanagedSharedComponentGeneric_SecondaryValidEntity component)
        {
            AddSharedComponent<UnmanagedSharedComponent>(CreateAdditionalEntity(), new UnmanagedSharedComponent { Field = 3 });
        }
    }

    // test for
    //     void AddSharedComponent<T>(Entity entity, T componentData)
    //
    // with invalid secondary entity
    class AddUnmanagedSharedComponentGeneric_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddSharedComponent<UnmanagedSharedComponent>(Entity.Null, new UnmanagedSharedComponent { Field = 3 });
        }
    }

    // test for
    //     void AppendToBuffer<T>()
    class AppendToBufferGeneric_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddBuffer<IntElement>();
            AppendToBuffer(new IntElement{Value = 1});
            AppendToBuffer(new IntElement{Value = 2});
            AppendToBuffer(new IntElement{Value = 3});
        }
    }

    // test for
    //     void AppendToBuffer<T>(Entity entity)
    //
    // with valid secondary entity
    class AppendToBufferGeneric_SecondaryValidEntity : Baker<Authoring_AddBufferGeneric_SecondaryValidEntity>
    {
        public override void Bake(Authoring_AddBufferGeneric_SecondaryValidEntity component)
        {
            var entity = CreateAdditionalEntity();
            AddBuffer<IntElement>(entity);
            AppendToBuffer(entity, new IntElement{Value = 1});
            AppendToBuffer(entity, new IntElement{Value = 2});
            AppendToBuffer(entity, new IntElement{Value = 3});
        }
    }

    // test for
    //     void AppendToBuffer<T>(Entity entity)
    //
    // with invalid secondary entity
    class AppendToBufferGeneric_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AppendToBuffer(Entity.Null, new IntElement{Value = 1});
            AppendToBuffer(Entity.Null, new IntElement{Value = 2});
            AppendToBuffer(Entity.Null, new IntElement{Value = 3});
        }
    }

    // test for
    //     void SetBuffer<T>()
    class SetBufferGeneric_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddBuffer<IntElement>();
            DynamicBuffer<IntElement> buffer = SetBuffer<IntElement>();
            buffer.CopyFrom(new IntElement[] { 1, 2, 3 });
        }
    }

    // test for
    //     void SetBuffer<T>(Entity entity)
    //
    // with valid secondary entity
    class SetBufferGeneric_SecondaryValidEntity : Baker<Authoring_AddBufferGeneric_SecondaryValidEntity>
    {
        public override void Bake(Authoring_AddBufferGeneric_SecondaryValidEntity component)
        {
            var entity = CreateAdditionalEntity();
            AddBuffer<IntElement>(entity);
            DynamicBuffer<IntElement> buffer = SetBuffer<IntElement>(entity);
            buffer.CopyFrom(new IntElement[] { 1, 2, 3 });
        }
    }

    // test for
    //     void SetBuffer<T>(Entity entity)
    //
    // with invalid secondary entity
    class SetBufferGeneric_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            DynamicBuffer<IntElement> buffer = SetBuffer<IntElement>(Entity.Null);
            buffer.CopyFrom(new IntElement[] { 1, 2, 3 });
        }
    }

    // test for
    //     void AddBuffer<T>()
    class AddBufferGeneric_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            buffer.CopyFrom(new IntElement[] { 1, 2, 3 });
        }
    }

    // test for
    //     void AddBuffer<T>(Entity entity)
    //
    // with valid secondary entity
    class AddBufferGeneric_SecondaryValidEntity : Baker<Authoring_AddBufferGeneric_SecondaryValidEntity>
    {
        public override void Bake(Authoring_AddBufferGeneric_SecondaryValidEntity component)
        {
            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>(CreateAdditionalEntity());
            buffer.CopyFrom(new IntElement[] { 1, 2, 3 });
        }
    }

    // test for
    //     void AddBuffer<T>(Entity entity)
    //
    // with invalid secondary entity
    class AddBufferGeneric_SecondaryInvalidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>(Entity.Null);
            buffer.CopyFrom(new IntElement[] { 1, 2, 3 });
        }
    }

    // test for
    //     void AddComponent<T>()
    public sealed class AddDuplicateComponent_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            AddComponent<ComponentTest1>(new ComponentTest1() {Field = 3});
            AddComponent<ComponentTest1>(new ComponentTest1() {Field = 4});
        }
    }

    // test for
    //     void AddComponent<T>(Entity entity)
    //
    // with valid secondary entity
    public sealed class AddDuplicateComponent_SecondaryValidEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Entity additionalEntity = CreateAdditionalEntity();
            AddComponent<ComponentTest1>(additionalEntity, new ComponentTest1() {Field = 3});
            AddComponent<ComponentTest1>(additionalEntity, new ComponentTest1() {Field = 4});
        }
    }


    // test for
    //     void GetComponent<T>()
    public sealed class GetComponent_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponent<Collider>();
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponent<T>(Component)
    public sealed class GetComponent_Component : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponent<Collider>(component);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponent<T>(GameObject)
    public sealed class GetComponent_GameObject : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponent<Collider>(component.gameObject);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponent<T>(Component)
    public sealed class GetComponent_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            var found = GetComponent<Collider>(nullComponent);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponent<T>(GameObject)
    public sealed class GetComponent_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGo = null;
            var found = GetComponent<Collider>(nullGo);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponents<T>()
    public sealed class GetComponents_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponents<Collider>();
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(Component)
    public sealed class GetComponents_Component : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponents<Collider>(component);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(GameObject)
    public sealed class GetComponents_GameObject : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponents<Collider>(component.gameObject);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(List)
    public sealed class GetComponents_PrimaryEntity_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponents<Collider>(found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(Component, List)
    public sealed class GetComponents_Component_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponents<Collider>(component, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(GameObject, List)
    public sealed class GetComponents_GameObject_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponents<Collider>(component.gameObject, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(GameObject, List)
    public sealed class GetComponents_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GameObject nullGo = null;
            GetComponents<Collider>(nullGo, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(Component, List)
    public sealed class GetComponents_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            Component nullComponent = null;
            GetComponents<Collider>(nullComponent, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(GameObject, List)
    public sealed class GetComponents_GameObjectNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GameObject nullGo = null;
            GetComponents<Collider>(nullGo, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponents<T>(Component, List)
    public sealed class GetComponents_ComponentNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            Component nullComponent = null;
            GetComponents<Collider>(nullComponent, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentInParent<T>()
    public sealed class GetComponentInParent_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentInParent<Collider>();
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentInParent<T>(Component)
    public sealed class GetComponentInParent_Component : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentInParent<Collider>(component);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentInParent<T>(GameObject)
    public sealed class GetComponentInParent_GameObject : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentInParent<Collider>(component.gameObject);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentInParent<T>(Component)
    public sealed class GetComponentInParent_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            var found = GetComponentInParent<Collider>(nullComponent);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentInParent<T>(GameObject)
    public sealed class GetComponentInParent_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGameObject = null;
            var found = GetComponentInParent<Collider>(nullGameObject);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentsInParent<T>()
    public sealed class GetComponentsInParent_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentsInParent<Collider>();
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(Component)
    public sealed class GetComponentsInParent_Component : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentsInParent<Collider>(component);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(GameObject)
    public sealed class GetComponentsInParent_GameObject : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentsInParent<Collider>(component.gameObject);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(Component)
    public sealed class GetComponentsInParent_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            var found = GetComponentsInParent<Collider>(nullComponent);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(GameObject)
    public sealed class GetComponentsInParent_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGameObject = null;
            var found = GetComponentsInParent<Collider>(nullGameObject);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(List)
    public sealed class GetComponentsInParent_PrimaryEntity_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponentsInParent<Collider>(found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(Component, List)
    public sealed class GetComponentsInParent_Component_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponentsInParent<Collider>(component, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(GameObject, List)
    public sealed class GetComponentsInParent_GameObject_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponentsInParent<Collider>(component.gameObject, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(Component, List)
    public sealed class GetComponentsInParent_ComponentNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            List<Collider> found = new List<Collider>();
            GetComponentsInParent<Collider>(nullComponent, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInParent<T>(GameObject, List)
    public sealed class GetComponentsInParent_GameObjectNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGameObject = null;
            List<Collider> found = new List<Collider>();
            GetComponentsInParent<Collider>(nullGameObject, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentInChildren<T>(Component)
    public sealed class GetComponentInChildren_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentInChildren<Collider>();
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentInChildren<T>(Component)
    public sealed class GetComponentInChildren_Component : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentInChildren<Collider>(component);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentInChildren<T>(GameObject)
    public sealed class GetComponentInChildren_GameObject : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentInChildren<Collider>(component.gameObject);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentInChildren<T>(Component)
    public sealed class GetComponentInChildren_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            var found = GetComponentInChildren<Collider>(nullComponent);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentInChildren<T>(GameObject)
    public sealed class GetComponentInChildren_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGo = null;
            var found = GetComponentInChildren<Collider>(nullGo);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetComponentsInChildren<T>()
    public sealed class GetComponentsInChildren_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentsInChildren<Collider>();
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(GameObject)
    public sealed class GetComponentsInChildren_GameObject : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentsInChildren<Collider>(component.gameObject);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(Component)
    public sealed class GetComponentsInChildren_Component : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetComponentsInChildren<Collider>(component);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(GameObject)
    public sealed class GetComponentsInChildren_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGo = null;
            var found = GetComponentsInChildren<Collider>(nullGo);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(Component)
    public sealed class GetComponentsInChildren_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            var found = GetComponentsInChildren<Collider>(nullComponent);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(List)
    public sealed class GetComponentsInChildren_PrimaryEntity_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponentsInChildren<Collider>(found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(GameObject, List)
    public sealed class GetComponentsInChildren_GameObject_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponentsInChildren<Collider>(component.gameObject, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(Component, List)
    public sealed class GetComponentsInChildren_Component_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<Collider> found = new List<Collider>();
            GetComponentsInChildren<Collider>(component, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(GameObject, List)
    public sealed class GetComponentsInChildren_GameObjectNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGo = null;
            List<Collider> found = new List<Collider>();
            GetComponentsInChildren<Collider>(nullGo, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetComponentsInChildren<T>(Component, List)
    public sealed class GetComponentsInChildren_ComponentNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            List<Collider> found = new List<Collider>();
            GetComponentsInChildren<Collider>(nullComponent, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParent()
    public sealed class GetParent_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetParent();
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetParent(Component)
    public sealed class GetParent_Component : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetParent(component);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetParent(GameObject)
    public sealed class GetParent_GameObject : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetParent(component.gameObject);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetParent(Component)
    public sealed class GetParent_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            var found = GetParent(nullComponent);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetParent(GameObject)
    public sealed class GetParent_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGameObject = null;
            var found = GetParent(nullGameObject);
            AddComponent(new GetComponentTest1()
            {
                Field = (found != null ? 1 : 0),
                GUID = found != null ? found.GetInstanceID() : 0
            });
        }
    }

        // test for
    //     void GetParents()
    public sealed class GetParents_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetParents();
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParents(Component)
    public sealed class GetParents_Component : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetParents(component);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParents(GameObject)
    public sealed class GetParents_GameObject : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetParents(component.gameObject);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParents(Component)
    public sealed class GetParents_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            var found = GetParents(nullComponent);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParents(GameObject)
    public sealed class GetParents_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGameObject = null;
            var found = GetParents(nullGameObject);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParent<T>(List)
    public sealed class GetParents_PrimaryEntity_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<GameObject> found = new List<GameObject>();
            GetParents(found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParents(Component, List)
    public sealed class GetParents_Component_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<GameObject> found = new List<GameObject>();
            GetParents(component, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParents(GameObject, List)
    public sealed class GetParents_GameObject_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<GameObject> found = new List<GameObject>();
            GetParents(component.gameObject, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParents(Component, List)
    public sealed class GetParents_ComponentNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            List<GameObject> found = new List<GameObject>();
            GetParents(nullComponent, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetParents(GameObject, List)
    public sealed class GetParents_GameObjectNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGameObject = null;
            List<GameObject> found = new List<GameObject>();
            GetParents(nullGameObject, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChild()
    public sealed class GetChild_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public static int QueryIndex;
        public override void Bake(DefaultAuthoringComponent component)
        {
            int count = GetChildCount();
            AddComponent(new GetComponentTest1()
            {
                Field = count,
                GUID = count > 0 ? GetChild(QueryIndex).GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetChild(GameObject)
    public sealed class GetChild_GameObject : Baker<DefaultAuthoringComponent>
    {
        public static int QueryIndex;
        public override void Bake(DefaultAuthoringComponent component)
        {
            int count = GetChildCount(component.gameObject);
            AddComponent(new GetComponentTest1()
            {
                Field = count,
                GUID = count > 0 ? GetChild(component.gameObject, QueryIndex).GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetChild(Component)
    public sealed class GetChild_Component : Baker<DefaultAuthoringComponent>
    {
        public static int QueryIndex;
        public override void Bake(DefaultAuthoringComponent component)
        {
            int count = GetChildCount(component);
            AddComponent(new GetComponentTest1()
            {
                Field = count,
                GUID = count > 0 ? GetChild(component, QueryIndex).GetInstanceID() : 0
            });
        }
    }

    // test for
    //     void GetChild(GameObject)
    public sealed class GetChild_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGo = null;
            AddComponent(new GetComponentTest1()
            {
                Field = GetChildCount(),
                GUID = GetChild(nullGo, 0).GetInstanceID()
            });
        }
    }

    // test for
    //     void GetChild(Component)
    public sealed class GetChild_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            AddComponent(new GetComponentTest1()
            {
                Field = GetChildCount(),
                GUID = GetChild(nullComponent, 0).GetInstanceID()
            });
        }
    }

    // test for
    //     void GetChildren()
    public sealed class GetChildren_PrimaryEntity : Baker<DefaultAuthoringComponent>
    {
        public static bool Recursive;
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetChildren(Recursive);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren(GameObject)
    public sealed class GetChildren_GameObject : Baker<DefaultAuthoringComponent>
    {
        public static bool Recursive;
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetChildren(component.gameObject, Recursive);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren(Component)
    public sealed class GetChildren_Component : Baker<DefaultAuthoringComponent>
    {
        public static bool Recursive;
        public override void Bake(DefaultAuthoringComponent component)
        {
            var found = GetChildren(component, Recursive);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren(GameObject)
    public sealed class GetChildren_GameObjectNull : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGo = null;
            var found = GetChildren(nullGo);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren(Component)
    public sealed class GetChildren_ComponentNull : Baker<DefaultAuthoringComponent>
    {
        public static bool Recursive;
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            var found = GetChildren(nullComponent);
            AddComponent(new ComponentTest1() {Field = found.Length});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren(List)
    public sealed class GetChildren_PrimaryEntity_PassingList : Baker<DefaultAuthoringComponent>
    {
        public static bool Recursive;
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<GameObject> found = new List<GameObject>();
            GetChildren(found, Recursive);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren(GameObject, List)
    public sealed class GetChildren_GameObject_PassingList : Baker<DefaultAuthoringComponent>
    {
        public static bool Recursive;
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<GameObject> found = new List<GameObject>();
            GetChildren(component.gameObject, found, Recursive);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren(Component, List)
    public sealed class GetChildren_Component_PassingList : Baker<DefaultAuthoringComponent>
    {
        public static bool Recursive;
        public override void Bake(DefaultAuthoringComponent component)
        {
            List<GameObject> found = new List<GameObject>();
            GetChildren(component, found, Recursive);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren<T>(GameObject, List)
    public sealed class GetChildren_GameObjectNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            GameObject nullGo = null;
            List<GameObject> found = new List<GameObject>();
            GetChildren(nullGo, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }

    // test for
    //     void GetChildren<T>(Component, List)
    public sealed class GetChildren_ComponentNull_PassingList : Baker<DefaultAuthoringComponent>
    {
        public override void Bake(DefaultAuthoringComponent component)
        {
            Component nullComponent = null;
            List<GameObject> found = new List<GameObject>();
            GetChildren(nullComponent, found);
            AddComponent(new ComponentTest1() {Field = found.Count});

            DynamicBuffer<IntElement> buffer = AddBuffer<IntElement>();
            foreach (var obj in found)
            {
                buffer.Add(obj.GetInstanceID());
            }
        }
    }


    // test for
    //  Base Class (Base Class)
    class BaseClassBaker : Baker<Authoring_BaseClass>
    {
        public override void Bake(Authoring_BaseClass component)
        {
            AddComponent<ComponentTest1>();
        }
    }

    // test for
    //  Base Class (Derived Class)
    class DerivedClassBaker : Baker<Authoring_DerivedFromBaseClass>
    {
        public override void Bake(Authoring_DerivedFromBaseClass component)
        {
            AddComponent<ComponentTest2>();
        }
    }

    // test for
    //  Base Class (Derived Derived Class)
    class DerivedDerivedClassBaker : Baker<Authoring_DerivedFromDerivedClass>
    {
        public override void Bake(Authoring_DerivedFromDerivedClass component)
        {
            AddComponent<ComponentTest3>();
        }
    }

    // test for
    //  Base Class (Base Class)
    [BakeDerivedTypes]
    class BaseClassBaker_WithAttribute : Baker<Authoring_BaseClass>
    {
        public override void Bake(Authoring_BaseClass component)
        {
            AddComponent<ComponentTest1>();
        }
    }

    // test for
    //  Base Class (Derived Class)
    [BakeDerivedTypes]
    class DerivedClassBaker_WithAttribute : Baker<Authoring_DerivedFromBaseClass>
    {
        public override void Bake(Authoring_DerivedFromBaseClass component)
        {
            AddComponent<ComponentTest2>();
        }
    }

    // test for
    //  Abstract Class (Abstract Class)
    class AbstractClassBaker : Baker<Authoring_Abstract>
    {
        public override void Bake(Authoring_Abstract component)
        {
            AddComponent<ComponentTest1>();
        }
    }

    // test for
    //  Abstract Class (Derived Class)
    class DerivedFromAbstractClassBaker : Baker<Authoring_DerivedFromAbstract>
    {
        public override void Bake(Authoring_DerivedFromAbstract component)
        {
            AddComponent<ComponentTest2>();
        }
    }

    // test for
    //  Abstract Class (Abstract Class)
    [BakeDerivedTypes]
    class AbstractClassBaker_WithAttribute : Baker<Authoring_Abstract>
    {
        public override void Bake(Authoring_Abstract component)
        {
            AddComponent<ComponentTest1>();
        }
    }

    public class BakerTests : BakingSystemFixtureBase
    {
        private BakingSystem m_BakingSystem;
        private GameObject m_Prefab;
        private GameObject m_Prefab1;
        private bool m_PreviousBakingState;
        private TestLiveConversionSettings m_Settings;

        readonly string kNullEntityException = "InvalidOperationException: Entity Entity.Null doesn't belong to the current authoring component.";
        readonly System.Text.RegularExpressions.Regex kNumberedEntityException = new System.Text.RegularExpressions.Regex(@"InvalidOperationException: Entity Entity\(\d+:\d+\) doesn't belong to the current authoring component.");

        [SetUp]
        public override void Setup()
        {
            m_Settings.Setup(true);
            base.Setup();

            m_BakingSystem = World.GetOrCreateSystemManaged<BakingSystem>();

            var blobAssetStore = new BlobAssetStore(128);
            var bakingSettings = MakeDefaultSettings();
            bakingSettings.BlobAssetStore = blobAssetStore;

            m_BakingSystem.BakingSettings = bakingSettings;

            m_Manager = World.EntityManager;
            m_Prefab = InstantiatePrefab("Prefab");
            m_Prefab1 = InstantiatePrefab("Prefab");
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            if (m_BakingSystem != null)
            {
                var assetStore = m_BakingSystem.BlobAssetStore;
                if (assetStore.IsCreated)
                    assetStore.Dispose();
            }
            m_BakingSystem = null;
            m_Settings.TearDown();
        }

        T GetBakedSingleton<T>() where T : unmanaged, IComponentData
        {
            Assert.AreEqual(1, m_BakingSystem.EntityManager.UniversalQuery.CalculateEntityCount());

            return m_BakingSystem.GetSingleton<T>();
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void Baker_AddComponentObjectGeneric()
        {
            var component = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddManagedComponentBakerTest)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component);
                Assert.IsTrue(m_Manager.HasComponent<ManagedComponent>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<ManagedComponent>(entities[1]));

                var comp1 = m_Manager.GetComponentData<ManagedComponent>(entities[0]);
                var comp2 = m_Manager.GetComponentData<ManagedComponent>(entities[1]);

                Assert.IsTrue(comp1.Field == 2);
                Assert.IsTrue(comp2.Field == 4);
            }
        }
#endif

        [DisableAutoCreation]
        public class BakerWithPrefabReference : Baker<Authoring_WithGameObjectField>
        {
            public override void Bake(Authoring_WithGameObjectField authoring)
            {
                GetEntity(authoring.GameObjectField);
            }
        }

        [Test]
        public void Reference_WithPrefab_PrefabGetsBaked()
        {
            using var overrideBake = new BakerDataUtility.OverrideBakers(true, typeof(BakerTests.BakerWithPrefabReference));
            var com = m_Prefab.AddComponent<Authoring_WithGameObjectField>();
            com.GameObjectField = LoadPrefab("Prefab");

            BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

            var entity = m_BakingSystem.GetEntity(com.GameObjectField);
            Assert.IsTrue(m_Manager.HasComponent<Prefab>(entity));

        }


        [DisableAutoCreation]
        public class BakerWithRegisterPrefabsForBaking : Baker<Authoring_WithGameObjectField>
        {
            public override void Bake(Authoring_WithGameObjectField authoring)
            {
                RegisterPrefabForBaking(authoring.GameObjectField);
            }
        }

        [Test]
        public void RegisterPrefabsForBaking_PrefabGetsBaked()
        {
            using var overrideBake = new BakerDataUtility.OverrideBakers(true, typeof(BakerTests.BakerWithRegisterPrefabsForBaking));
            var com = m_Prefab.AddComponent<Authoring_WithGameObjectField>();
            com.GameObjectField = LoadPrefab("Prefab");

            BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

            var entity = m_BakingSystem.GetEntity(com.GameObjectField);
            Assert.IsTrue(m_Manager.HasComponent<Prefab>(entity));

        }

        [Test]
        public void AddComponent_WithUnregistredEntity_Throws_Test()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddComponent_WithUnregisteredEntity_Baker)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNumberedEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void AddComponentObject_WithUnregistredEntity_Throws_Test()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddComponentObject_WithUnregisteredEntity_Baker)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNumberedEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }
#endif

        [Test]
        public void AddComponent_WithValidEntity_Doesnt_Throw()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddComponent_WithUnregisteredEntity_Baker)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNumberedEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void Baker_Destroy_Entities_On_ConvertGameObjects_CodePath_Test()
        {
            var componentEntity = m_Prefab.AddComponent<Authoring_AddComponentByComponentType_PrimaryEntity>();
            var componentOnAdditionalEntities = m_Prefab.AddComponent<Authoring_AddComponentByComponentType_SecondaryValidEntity>();

            BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

            var entities1 = m_BakingSystem.GetEntitiesForBakers(componentEntity);
            Assert.IsTrue(entities1.Length == 1);

            var entities2 = m_BakingSystem.GetEntitiesForBakers(componentOnAdditionalEntities);
            Assert.IsTrue(entities2.Length == 2);

            EntitiesAssert.Contains(m_Manager, EntityMatch.Partial<ComponentTest1>(entities1[0]));
            EntitiesAssert.Contains(m_Manager, EntityMatch.Partial<ComponentTest1>(entities2[1]));

            //@TODO: Discuss with team if this is really the behaviour we want. Seems like common case is to directly generate entitites into the live game world with the ConvertGameObjects codepath
            //Test if fully rebaking the same gameobject, internally on the ConvertGameObjects path, will destroy the primary and additional entities before recreating new ones.
            BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

            entities1 = m_BakingSystem.GetEntitiesForBakers(componentEntity);
            Assert.IsTrue(entities1.Length == 1);

            entities2 = m_BakingSystem.GetEntitiesForBakers(componentOnAdditionalEntities);
            Assert.IsTrue(entities2.Length == 2);

            EntitiesAssert.Contains(m_Manager, EntityMatch.Partial<ComponentTest1>(entities1[0]));
            EntitiesAssert.Contains(m_Manager, EntityMatch.Partial<ComponentTest1>(entities2[1]));
        }

        [Test]
        public void AddComponentByComponentType()
        {
            var component0 = m_Prefab.AddComponent<Authoring_AddComponentByComponentType_PrimaryEntity>();
            var component1 = m_Prefab.AddComponent<Authoring_AddComponentByComponentType_SecondaryValidEntity>();

            BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

            // jiv - m_BakingSystem.GetEntities(component0) will fail.
            var entities = m_BakingSystem.GetEntitiesForBakers(component1);
            Assert.IsTrue(entities.Length == 2);

            Assert.IsTrue(m_Manager.HasComponent<ComponentTest1>(entities[0]));
            Assert.IsTrue(m_Manager.HasComponent<ComponentTest1>(entities[1]));
        }

        [Test]
        public void AddComponentByComponentType_Throws()
        {
            var component0 = m_Prefab.AddComponent<Authoring_AddComponentByComponentType_PrimaryEntity>();
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddComponentByComponentType_SecondaryInvalidEntity), typeof(AddComponentByComponentType_PrimaryEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddComponentGeneric()
        {
            var component0 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddComponentGeneric_SecondaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component0);
                Assert.IsTrue(entities.Length == 2);

                Assert.IsTrue(m_Manager.HasComponent<ComponentTest1>(entities[1]));

                ComponentTest1 comp1 = m_Manager.GetComponentData<ComponentTest1>(entities[1]);
                Assert.IsTrue(comp1.Field == 3);
            }
        }

        [Test]
        public void AddComponentGeneric_Throws()
        {
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddComponentGeneric_SecondaryInvalidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddComponentByMultipleComponentTypes()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();
            var component1 = m_Prefab.AddComponent<Authoring_AddComponentByMultipleComponentTypes_SecondaryValidEntity>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddComponentByMultipleComponentTypes_PrimaryEntity),
                typeof(AddComponentByMultipleComponentTypes_SecondaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component1);
                Assert.IsTrue(entities.Length == 2);

                Assert.IsTrue(m_Manager.HasComponent<ComponentTest1>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<ComponentTest2>(entities[0]));

                Assert.IsTrue(m_Manager.HasComponent<ComponentTest1>(entities[1]));
                Assert.IsTrue(m_Manager.HasComponent<ComponentTest2>(entities[1]));
            }
        }

        [Test]
        public void AddComponentByMultipleComponentTypes_Throws()
        {
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true,
                typeof(AddComponentByMultipleComponentTypes_SecondaryInvalidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddChunkComponentGeneric()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();
            var component1 = m_Prefab.AddComponent<Authoring_AddChunkComponentGeneric_SecondaryValidEntity>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddChunkComponentGeneric_PrimaryEntity),
                typeof(AddChunkComponentGeneric_SecondaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component1);
                Assert.IsTrue(entities.Length == 2);

                Assert.IsTrue(m_Manager.HasChunkComponent<ComponentTest1>(entities[0]));
                Assert.IsTrue(m_Manager.HasChunkComponent<ComponentTest1>(entities[1]));
            }
        }

        [Test]
        public void AddChunkComponentGeneric_Throws()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddChunkComponentGeneric_SecondaryInvalidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddSharedComponentGeneric()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();
            var component1 = m_Prefab.AddComponent<Authoring_AddSharedComponentGeneric_SecondaryValidEntity>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddSharedComponentGeneric_PrimaryEntity),
                typeof(AddSharedComponentGeneric_SecondaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component1);
                Assert.IsTrue(entities.Length == 2);

                Assert.IsTrue(m_Manager.HasComponent<UnmanagedSharedComponent>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<UnmanagedSharedComponent>(entities[1]));

                UnmanagedSharedComponent unmanagedSharedComponentPrimary0 = m_Manager.GetSharedComponentManaged<UnmanagedSharedComponent>(entities[0]);
                Assert.IsTrue(unmanagedSharedComponentPrimary0.Field == 1);

                UnmanagedSharedComponent unmanagedSharedComponentPrimary1 = m_Manager.GetSharedComponentManaged<UnmanagedSharedComponent>(entities[1]);
                Assert.IsTrue(unmanagedSharedComponentPrimary1.Field == 3);
            }
        }

        [Test]
        public void AddSharedComponentGeneric_Throws()
        {
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddSharedComponentGeneric_SecondaryInvalidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddUnmanagedSharedComponentGeneric()
        {
            var component0 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            var component1 = m_Prefab.AddComponent<Authoring_AddUnmanagedSharedComponentGeneric_SecondaryValidEntity>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(AddUnmanagedSharedComponentGeneric_PrimaryEntity),
                typeof(AddUnmanagedSharedComponentGeneric_SecondaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component1);
                Assert.IsTrue(entities.Length == 2);

                Assert.IsTrue(m_Manager.HasComponent<UnmanagedSharedComponent>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<UnmanagedSharedComponent>(entities[1]));

                UnmanagedSharedComponent unmanagedSharedComponentPrimary0 = m_Manager.GetSharedComponentManaged<UnmanagedSharedComponent>(entities[0]);
                Assert.IsTrue(unmanagedSharedComponentPrimary0.Field == 1);

                UnmanagedSharedComponent unmanagedSharedComponentPrimary1 = m_Manager.GetSharedComponentManaged<UnmanagedSharedComponent>(entities[1]);
                Assert.IsTrue(unmanagedSharedComponentPrimary1.Field == 3);
            }
        }

        [Test]
        public void AddUnmanagedSharedComponentGeneric_Throws()
        {
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true,
                typeof(AddUnmanagedSharedComponentGeneric_SecondaryInvalidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddBufferGeneric()
        {
            var component0 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            var component1 = m_Prefab.AddComponent<Authoring_AddBufferGeneric_SecondaryValidEntity>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddBufferGeneric_PrimaryEntity),
                typeof(AddBufferGeneric_SecondaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component1);
                Assert.IsTrue(entities.Length == 2);

                var resultBuffer0 = m_Manager.GetBuffer<IntElement>(entities[0]);
                var resultBuffer1 = m_Manager.GetBuffer<IntElement>(entities[1]);
                Assert.IsTrue(resultBuffer0.Length == resultBuffer1.Length);
                for (int i=0; i<resultBuffer0.Length; ++i)
                {
                    Assert.AreEqual(i + 1, resultBuffer0[i].Value);
                    Assert.AreEqual(i + 1, resultBuffer1[i].Value);
                }
            }
        }

        [Test]
        public void AddBufferGeneric_Throws()
        {
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddBufferGeneric_SecondaryInvalidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void SetBufferGeneric()
        {
            var component0 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            var component1 = m_Prefab.AddComponent<Authoring_AddBufferGeneric_SecondaryValidEntity>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(SetBufferGeneric_PrimaryEntity),
                typeof(SetBufferGeneric_SecondaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component1);
                Assert.IsTrue(entities.Length == 2);

                var resultBuffer0 = m_Manager.GetBuffer<IntElement>(entities[0]);
                var resultBuffer1 = m_Manager.GetBuffer<IntElement>(entities[1]);
                Assert.IsTrue(resultBuffer0.Length == resultBuffer1.Length);
                for (int i=0; i<resultBuffer0.Length; ++i)
                {
                    Assert.AreEqual(i + 1, resultBuffer0[i].Value);
                    Assert.AreEqual(i + 1, resultBuffer1[i].Value);
                }
            }
        }

        [Test]
        public void SetBufferGeneric_Throws()
        {
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(SetBufferGeneric_SecondaryInvalidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AppendToBufferGeneric()
        {
            var component0 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            var component1 = m_Prefab.AddComponent<Authoring_AddBufferGeneric_SecondaryValidEntity>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AppendToBufferGeneric_PrimaryEntity),
                typeof(AppendToBufferGeneric_SecondaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(component1);
                Assert.IsTrue(entities.Length == 2);

                var resultBuffer0 = m_Manager.GetBuffer<IntElement>(entities[0]);
                var resultBuffer1 = m_Manager.GetBuffer<IntElement>(entities[1]);
                Assert.IsTrue(resultBuffer0.Length == resultBuffer1.Length);
                for (int i=0; i<resultBuffer0.Length; ++i)
                {
                    Assert.AreEqual(i + 1, resultBuffer0[i].Value);
                    Assert.AreEqual(i + 1, resultBuffer1[i].Value);
                }
            }
        }

        [Test]
        public void AppendToBufferGeneric_Throws()
        {
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AppendToBufferGeneric_SecondaryInvalidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, kNullEntityException);

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddDuplicateComponent_PrimaryEntity_Throws()
        {
            var component0 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddDuplicateComponent_PrimaryEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to add duplicate component Unity.Entities.Hybrid.Tests.Baking.ComponentTest1 for Baker AddDuplicateComponent_PrimaryEntity with authoring component DefaultAuthoringComponent.  Previous component added by Baker AddDuplicateComponent_PrimaryEntity");

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddDuplicateComponent_SecondaryEntity_Throws()
        {
            var component0 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, typeof(AddDuplicateComponent_SecondaryValidEntity)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to add duplicate component Unity.Entities.Hybrid.Tests.Baking.ComponentTest1 for Baker AddDuplicateComponent_SecondaryValidEntity with authoring component DefaultAuthoringComponent.  Previous component added by Baker AddDuplicateComponent_SecondaryValidEntity");

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddDuplicateComponentAcrossBakers_Throws()
        {
            var component0 = m_Prefab.AddComponent<Authoring_AddComponentByComponentType_PrimaryEntity>();
            var component1 = m_Prefab.AddComponent<Authoring_AddComponentGeneric_PrimaryValidEntity>();

            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to add duplicate component Unity.Entities.Hybrid.Tests.Baking.ComponentTest1 for Baker AddComponentGeneric_PrimaryValidEntity with authoring component Authoring_AddComponentGeneric_PrimaryValidEntity.  Previous component added by Baker AddComponentByComponentType_PrimaryEntity");

            BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
        }

        [Test]
        public void AddDuplicateComponentAcrossBakers_Multiple_Throws()
        {
            var component0 = m_Prefab.AddComponent<Authoring_AddComponentGeneric_PrimaryValidEntity>();
            var component1 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to add duplicate component Unity.Entities.Hybrid.Tests.Baking.ComponentTest1 for Baker AddComponentByMultipleComponentTypes_PrimaryEntity with authoring component DefaultAuthoringComponent.  Previous component added by Baker AddComponentGeneric_PrimaryValidEntity");

            using (new BakerDataUtility.OverrideBakers(true, typeof(AddComponentGeneric_PrimaryValidEntity),
                typeof(AddComponentByMultipleComponentTypes_PrimaryEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        class AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker1: Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent authoring)
            {
                AddComponent(ComponentType.ReadWrite<ComponentTest1>());
            }
        }

        class AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker2 : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent authoring)
            {
                AddComponent(ComponentType.ReadWrite<ComponentTest2>());
            }
        }

        class AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker1_Throws : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent authoring)
            {
                AddComponent(ComponentType.ReadWrite<ComponentTest2>());
            }
        }

        class AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker2_Throws : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent authoring)
            {
                AddComponent(ComponentType.ReadWrite<ComponentTest2>());
            }
        }

        [Test]
        public void AddDifferentComponents_FromMultipleBakers_PerAuthoringComponent_Is_Valid()
        {
            var component = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true,
                typeof(AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker1),
                typeof(AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker2)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entity = m_BakingSystem.GetEntitiesForBakers(component);
                Assert.IsTrue(entity.m_length == 1);
                EntitiesAssert.Contains(m_Manager, EntityMatch.Partial<ComponentTest1, ComponentTest2>(entity[0]));
            }
        }

        [Test]
        public void AddSameComponent_FromMultipleBakers_PerAuthoringComponent_Throws()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true,
                typeof(AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker1_Throws),
                typeof(AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker2_Throws)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to add duplicate component Unity.Entities.Hybrid.Tests.Baking.ComponentTest2 for Baker AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker2_Throws with authoring component DefaultAuthoringComponent.  Previous component added by Baker AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker1_Throws");

                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void AddSameComponent_FromMultipleBakers_PerAuthoringComponent_EndToEnd()
        {
            var componentEntity0 = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            var componentEntity1 = m_Prefab.AddComponent<Authoring_AddComponentGeneric_PrimaryValidEntity>();
            using (new BakerDataUtility.OverrideBakers(true,
                typeof(AddComponent_WithMultipleBakers_PerAuthoringComponent_Baker1_Throws),
                typeof(AddComponentGeneric_PrimaryValidEntity)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab, m_Prefab1}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity1);
                Assert.IsTrue(entities.Length == 1);

                ComponentTest1 component1 = m_Manager.GetComponentData<ComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == 3);
            }
        }

        [DisableAutoCreation]
        internal class DefaultAuthoringAddComponentBaker : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                AddComponent(GetEntity(), new ComponentTest1{Field = component.Field});
            }
        }

        [DisableAutoCreation]
        internal class DefaultAuthoringAddSharedComponentBaker : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                AddSharedComponent(GetEntity(), new SharedComponentTest1{Field = component.Field});
            }
        }

        [DisableAutoCreation]
        class DefaultAuthoringSetComponentBaker_Value5 : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                SetComponent(GetEntity(), new ComponentTest1 { Field = 5});
            }
        }

        [DisableAutoCreation]
        class DefaultAuthoringUnsafeSetComponentBaker_Value5 : Baker<DefaultAuthoringComponent>
        {
            public override unsafe void Bake(DefaultAuthoringComponent authoring)
            {
                AddComponent<ComponentTest1>(GetEntity());

                var component = new ComponentTest1 {Field = 5};
                var typeIndex = TypeManager.GetTypeIndex<ComponentTest1>();
                var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;
                UnsafeSetComponent(GetEntity(), typeIndex, typeSize, &component);
            }
        }

        [DisableAutoCreation]
        class DefaultAuthoringUnsafeAddComponentBaker_Value5 : Baker<DefaultAuthoringComponent>
        {
            public override unsafe void Bake(DefaultAuthoringComponent authoring)
            {
                var component = new ComponentTest1 {Field = 5};
                var typeIndex = TypeManager.GetTypeIndex<ComponentTest1>();
                var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;
                UnsafeAddComponent(GetEntity(), typeIndex, typeSize, &component);
            }
        }

        [Test]
        public void SetComponentFromDifferentBakerThrows()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(DefaultAuthoringAddComponentBaker),
                typeof(DefaultAuthoringSetComponentBaker_Value5)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to set component Unity.Entities.Hybrid.Tests.Baking.ComponentTest1 for Baker DefaultAuthoringSetComponentBaker_Value5 with authoring component DefaultAuthoringComponent but the component was added by a different Baker DefaultAuthoringAddComponentBaker");
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                // SetComponent was rejected thus the value continues to be 0
                Assert.AreEqual(0, GetBakedSingleton<ComponentTest1>().Field);
            }
        }

        [DisableAutoCreation]
        class DefaultAuthoringSetSharedComponentBaker_Value5 : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                SetSharedComponent(GetEntity(), new SharedComponentTest1() { Field = 5});
            }
        }

        [Test]
        public void SetSharedComponentFromDifferentBakerThrows()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(DefaultAuthoringAddSharedComponentBaker),
                typeof(DefaultAuthoringSetSharedComponentBaker_Value5)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to set component Unity.Entities.Hybrid.Tests.Baking.SharedComponentTest1 for Baker DefaultAuthoringSetSharedComponentBaker_Value5 with authoring component DefaultAuthoringComponent but the component was added by a different Baker DefaultAuthoringAddSharedComponentBaker");
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                // SetSharedComponent was rejected thus the value continues to be 0
                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc {All = new ComponentType[]{typeof(SharedComponentTest1)}});
                var entities = query.ToEntityArray(Allocator.Temp);
                var data = World.EntityManager.GetSharedComponentManaged<SharedComponentTest1>(entities[0]);
                Assert.AreEqual(0, data.Field);
            }
        }

        [DisableAutoCreation]
        class SetSharedComponentFromSameBakerWorksBaker : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                AddSharedComponent(GetEntity(), new SharedComponentTest1());
                SetSharedComponent(GetEntity(), new SharedComponentTest1() { Field = 5});
            }
        }
        [Test]
        public void SetSharedComponentFromSameBakerWorks()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(SetSharedComponentFromSameBakerWorksBaker)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc {All = new ComponentType[]{typeof(SharedComponentTest1)}});
                var entities = query.ToEntityArray(Allocator.Temp);
                var data = World.EntityManager.GetSharedComponentManaged<SharedComponentTest1>(entities[0]);
                Assert.AreEqual(5, data.Field);
            }
        }

        [DisableAutoCreation]
        class SetComponentFromSameBakerWorksBaker : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                AddComponent(GetEntity(), new ComponentTest1());
                SetComponent(GetEntity(), new ComponentTest1 { Field = 5});
            }
        }
        [Test]
        public void SetComponentFromSameBakerWorks()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(SetComponentFromSameBakerWorksBaker)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                Assert.AreEqual(5, GetBakedSingleton<ComponentTest1>().Field);
            }
        }

        [DisableAutoCreation]
        class SetBufferFromOtherBakerThrowsBaker : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                var buffer = SetBuffer<IntElement>();
                buffer.Add(new IntElement {Value = 4});
                buffer.Add(new IntElement {Value = 5});
                buffer.Add(new IntElement {Value = 6});
            }
        }

        [Test]
        public void SetBufferFromDifferentBakerThrows()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(SetBufferFromSameBakerWorksBaker),
                typeof(SetBufferFromOtherBakerThrowsBaker)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to set component Unity.Entities.Hybrid.Tests.Baking.IntElement for Baker SetBufferFromOtherBakerThrowsBaker with authoring component DefaultAuthoringComponent but the component was added by a different Baker SetBufferFromSameBakerWorksBaker");
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc {All = new ComponentType[]{typeof(IntElement)}});
                var entities = query.ToEntityArray(Allocator.Temp);
                var data = World.EntityManager.GetBuffer<IntElement>(entities[0]);
                Assert.AreEqual(3, data.Length);
                Assert.AreEqual(1, data[0].Value);
                Assert.AreEqual(2, data[1].Value);
                Assert.AreEqual(3, data[2].Value);
            }
        }

        [DisableAutoCreation]
        class SetBufferFromSameBakerWorksBaker : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                AddBuffer<IntElement>();
                var buffer = SetBuffer<IntElement>();
                buffer.Add(new IntElement {Value = 1});
                buffer.Add(new IntElement {Value = 2});
                buffer.Add(new IntElement {Value = 3});
            }
        }

        [Test]
        public void SetBufferFromSameBakerWorks()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(SetBufferFromSameBakerWorksBaker)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc {All = new ComponentType[]{typeof(IntElement)}});
                var entities = query.ToEntityArray(Allocator.Temp);
                var data = World.EntityManager.GetBuffer<IntElement>(entities[0]);
                Assert.AreEqual(3, data.Length);
                Assert.AreEqual(1, data[0].Value);
                Assert.AreEqual(2, data[1].Value);
                Assert.AreEqual(3, data[2].Value);
            }
        }

        [DisableAutoCreation]
        class AppendToBufferFromOtherBakerThrowsBaker : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                AppendToBuffer(new IntElement {Value = 4});
                AppendToBuffer(new IntElement {Value = 5});
                AppendToBuffer(new IntElement {Value = 6});
            }
        }

        [Test]
        public void AppendToBufferFromDifferentBakerThrows()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(AppendToBufferFromSameBakerWorksBaker),
                typeof(AppendToBufferFromOtherBakerThrowsBaker)))
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, "InvalidOperationException: Baking error: Attempt to set component Unity.Entities.Hybrid.Tests.Baking.IntElement for Baker AppendToBufferFromOtherBakerThrowsBaker with authoring component DefaultAuthoringComponent but the component was added by a different Baker AppendToBufferFromSameBakerWorksBaker");
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc {All = new ComponentType[]{typeof(IntElement)}});
                var entities = query.ToEntityArray(Allocator.Temp);
                var data = World.EntityManager.GetBuffer<IntElement>(entities[0]);
                Assert.AreEqual(3, data.Length);
                Assert.AreEqual(1, data[0].Value);
                Assert.AreEqual(2, data[1].Value);
                Assert.AreEqual(3, data[2].Value);
            }
        }

        [DisableAutoCreation]
        class AppendToBufferFromSameBakerWorksBaker : Baker<DefaultAuthoringComponent>
        {
            public override void Bake(DefaultAuthoringComponent component)
            {
                AddBuffer<IntElement>();
                AppendToBuffer(new IntElement {Value = 1});
                AppendToBuffer(new IntElement {Value = 2});
                AppendToBuffer(new IntElement {Value = 3});
            }
        }

        [Test]
        public void AppendToBufferFromSameBakerWorks()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(AppendToBufferFromSameBakerWorksBaker)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc {All = new ComponentType[]{typeof(IntElement)}});
                var entities = query.ToEntityArray(Allocator.Temp);
                var data = World.EntityManager.GetBuffer<IntElement>(entities[0]);
                Assert.AreEqual(3, data.Length);
                Assert.AreEqual(1, data[0].Value);
                Assert.AreEqual(2, data[1].Value);
                Assert.AreEqual(3, data[2].Value);
            }
        }

        [Test]
        public void UnsafeSetComponentFromSameBakerWorks()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(DefaultAuthoringUnsafeSetComponentBaker_Value5)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                Assert.AreEqual(5, GetBakedSingleton<ComponentTest1>().Field);
            }
        }

        [Test]
        public void UnsafeAddComponentFromSameBakerWorks()
        {
            m_Prefab.AddComponent<DefaultAuthoringComponent>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(DefaultAuthoringUnsafeAddComponentBaker_Value5)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                Assert.AreEqual(5, GetBakedSingleton<ComponentTest1>().Field);
            }
        }

        [Test]
        public void Authoring_GetComponent([Values(0,1)] int count, [Values(typeof(GetComponent_PrimaryEntity), typeof(GetComponent_Component),typeof(GetComponent_GameObject))] Type bakerType)
        {
            var componentEntity = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                for (int index = 0; index < count; ++index)
                    m_Prefab.AddComponent<BoxCollider>();
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                GetComponentTest1 component1 = m_Manager.GetComponentData<GetComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == count);

                // Make sure we found the same component
                var foundComponent = m_Prefab.GetComponentInParent<Collider>();
                if (foundComponent != null)
                {
                    Assert.AreEqual(component1.GUID, foundComponent.GetInstanceID());
                }
            }
        }

        [Test]
        public void Authoring_GetComponent_NullError([Values(1)] int count, [Values(typeof(GetComponent_ComponentNull),typeof(GetComponent_GameObjectNull))]Type bakerType)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));

            var componentEntity = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                for (int index = 0; index < count; ++index)
                    m_Prefab.AddComponent<BoxCollider>();
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void Authoring_GetComponents([Values(0,1,2)] int count, [Values(typeof(GetComponents_PrimaryEntity),typeof(GetComponents_Component),typeof(GetComponents_GameObject),typeof(GetComponents_PrimaryEntity_PassingList),typeof(GetComponents_Component_PassingList),typeof(GetComponents_GameObject_PassingList))] Type bakerType)
        {
            var componentEntity = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                for (int index = 0; index < count; ++index)
                    m_Prefab.AddComponent<BoxCollider>();
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                ComponentTest1 component1 = m_Manager.GetComponentData<ComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == count);

                var foundComponents = m_Prefab.GetComponents<Collider>();
                if (foundComponents != null && foundComponents.Length > 0)
                {
                    var elements = m_Manager.GetBuffer<IntElement>(entities[0]);
                    Assert.AreEqual(foundComponents.Length, elements.Length);
                    for (int index = 0; index < foundComponents.Length; ++index)
                    {
                        Assert.AreEqual(foundComponents[index].GetInstanceID(), elements[index].Value);
                    }
                }
            }
        }

        [Test]
        public void Authoring_GetComponents_NullError([Values(1)] int count, [Values(typeof(GetComponents_GameObjectNull),typeof(GetComponents_ComponentNull),typeof(GetComponents_GameObjectNull_PassingList),typeof(GetComponents_ComponentNull_PassingList))] Type bakerType)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));
            var componentEntity = m_Prefab.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                for (int index = 0; index < count; ++index)
                    m_Prefab.AddComponent<BoxCollider>();
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void Authoring_GetComponentInParent([Values] BakerTestsHierarchyHelper.ParentHierarchyMaskTests mask, [Values(typeof(GetComponentInParent_PrimaryEntity),typeof(GetComponentInParent_Component),typeof(GetComponentInParent_GameObject))]  Type bakerType)
        {
            GameObject current = BakerTestsHierarchyHelper.CreateParentHierarchyWithType<BoxCollider>(mask, m_Prefab, out int added);
            var componentEntity = current.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {current}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                GetComponentTest1 component1 = m_Manager.GetComponentData<GetComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == (mask > 0 ? 1 : 0));

                // Make sure we found the same component
                var foundComponent = current.GetComponentInParent<Collider>();
                if (foundComponent != null)
                {
                    Assert.AreEqual(component1.GUID, foundComponent.GetInstanceID());
                }
            }
        }

        [Test]
        public void Authoring_GetComponentInParent_NullError([Values(BakerTestsHierarchyHelper.ParentHierarchyMaskTests.All)] BakerTestsHierarchyHelper.ParentHierarchyMaskTests mask, [Values(typeof(GetComponentInParent_ComponentNull),typeof(GetComponentInParent_GameObjectNull))]  Type bakerType)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));
            GameObject current = BakerTestsHierarchyHelper.CreateParentHierarchyWithType<BoxCollider>(mask, m_Prefab, out int added);
            var componentEntity = current.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {current}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void Authoring_GetComponentsInParent([Values] BakerTestsHierarchyHelper.ParentHierarchyMaskTests mask, [Values(typeof(GetComponentsInParent_PrimaryEntity),typeof(GetComponentsInParent_Component),typeof(GetComponentsInParent_GameObject),typeof(GetComponentsInParent_PrimaryEntity_PassingList),typeof(GetComponentsInParent_Component_PassingList),typeof(GetComponentsInParent_GameObject_PassingList))]  Type bakerType)
        {
            GameObject current = BakerTestsHierarchyHelper.CreateParentHierarchyWithType<BoxCollider>(mask, m_Prefab, out int added);
            var componentEntity = current.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {current}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                ComponentTest1 component1 = m_Manager.GetComponentData<ComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == added);

                var foundComponents = current.GetComponentsInParent<Collider>();
                if (foundComponents != null && foundComponents.Length > 0)
                {
                    var elements = m_Manager.GetBuffer<IntElement>(entities[0]);
                    Assert.AreEqual(foundComponents.Length, elements.Length);
                    for (int index = 0; index < foundComponents.Length; ++index)
                    {
                        Assert.AreEqual(foundComponents[index].GetInstanceID(), elements[index].Value);
                    }
                }
            }
        }

        [Test]
        public void Authoring_GetComponentsInParent_NullError([Values(BakerTestsHierarchyHelper.ParentHierarchyMaskTests.All)] BakerTestsHierarchyHelper.ParentHierarchyMaskTests mask, [Values(typeof(GetComponentsInParent_ComponentNull),typeof(GetComponentsInParent_GameObjectNull),typeof(GetComponentsInParent_ComponentNull_PassingList),typeof(GetComponentsInParent_GameObjectNull_PassingList))]  Type bakerType)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));
            GameObject current = BakerTestsHierarchyHelper.CreateParentHierarchyWithType<BoxCollider>(mask, m_Prefab, out int added);
            var componentEntity = current.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {current}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
            }
        }

        [Test]
        public void Authoring_GetComponentInChildren([Values] BakerTestsHierarchyHelper.HierarchyChildrenTests mask, [Values(typeof(GetComponentInChildren_PrimaryEntity),typeof(GetComponentInChildren_Component),typeof(GetComponentInChildren_GameObject))] Type bakerType)
        {
            GameObject root = BakerTestsHierarchyHelper.CreateChildrenHierarchyWithType<BoxCollider>(3, 2, (uint)mask, m_Prefab, out int added);
            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                GetComponentTest1 component1 = m_Manager.GetComponentData<GetComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == (mask > 0 ? 1 : 0));

                // Make sure we found the same component
                var foundComponent = root.GetComponentInChildren<Collider>();
                if (foundComponent != null)
                {
                    Assert.AreEqual(component1.GUID, foundComponent.GetInstanceID());
                }
            }
        }

        [Test]
        public void Authoring_GetComponentInChildren_NullError([Values(BakerTestsHierarchyHelper.HierarchyChildrenTests.All)] BakerTestsHierarchyHelper.HierarchyChildrenTests mask, [Values(typeof(GetComponentInChildren_ComponentNull),typeof(GetComponentInChildren_GameObjectNull))] Type bakerType)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));
            GameObject root = BakerTestsHierarchyHelper.CreateChildrenHierarchyWithType<BoxCollider>(3, 2, (uint)mask, m_Prefab, out int added);
            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void Authoring_GetComponentsInChildren([Values] BakerTestsHierarchyHelper.HierarchyChildrenTests mask, [Values(typeof(GetComponentsInChildren_PrimaryEntity),typeof(GetComponentsInChildren_Component),typeof(GetComponentsInChildren_GameObject),typeof(GetComponentsInChildren_PrimaryEntity_PassingList),typeof(GetComponentsInChildren_Component_PassingList),typeof(GetComponentsInChildren_GameObject_PassingList))] Type bakerType )
        {
            GameObject root = BakerTestsHierarchyHelper.CreateChildrenHierarchyWithType<BoxCollider>(3, 2, (uint)mask, m_Prefab, out int added);
            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                ComponentTest1 component1 = m_Manager.GetComponentData<ComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == added);

                var foundComponents = root.GetComponentsInChildren<Collider>();
                if (foundComponents != null && foundComponents.Length > 0)
                {
                    var elements = m_Manager.GetBuffer<IntElement>(entities[0]);
                    Assert.AreEqual(foundComponents.Length, elements.Length);
                    for (int index = 0; index < foundComponents.Length; ++index)
                    {
                        Assert.AreEqual(foundComponents[index].GetInstanceID(), elements[index].Value);
                    }
                }
            }
        }

        [Test]
        public void Authoring_GetComponentsInChildren_NullError([Values(BakerTestsHierarchyHelper.HierarchyChildrenTests.All)] BakerTestsHierarchyHelper.HierarchyChildrenTests mask, [Values(typeof(GetComponentsInChildren_ComponentNull),typeof(GetComponentsInChildren_GameObjectNull),typeof(GetComponentsInChildren_ComponentNull_PassingList),typeof(GetComponentsInChildren_GameObjectNull_PassingList))] Type bakerType )
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));
            GameObject root = BakerTestsHierarchyHelper.CreateChildrenHierarchyWithType<BoxCollider>(3, 2, (uint)mask, m_Prefab, out int added);
            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void Authoring_GetParent([Values(1,2,3)] int count, [Values(typeof(GetParent_PrimaryEntity),typeof(GetParent_Component),typeof(GetParent_GameObject))]  Type bakerType)
        {
            GameObject current = BakerTestsHierarchyHelper.CreateParentHierarchy(count, m_Prefab);
            var componentEntity = current.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {current}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                GetComponentTest1 component1 = m_Manager.GetComponentData<GetComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == (count > 1 ? 1 : 0));

                // Make sure we found the same component
                var found = current.transform.parent;
                if (found != null)
                {
                    Assert.AreEqual(component1.GUID, found.gameObject.GetInstanceID());
                }
            }
        }

        [Test]
        public void Authoring_GetParent_NullError([Values(BakerTestsHierarchyHelper.ParentHierarchyMaskTests.All)] BakerTestsHierarchyHelper.ParentHierarchyMaskTests mask, [Values(typeof(GetParent_ComponentNull),typeof(GetParent_GameObjectNull))]  Type bakerType)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));
            GameObject current = BakerTestsHierarchyHelper.CreateParentHierarchyWithType<BoxCollider>(mask, m_Prefab, out int added);
            var componentEntity = current.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {current}, m_BakingSystem.BakingSettings);
            }
        }

         [Test]
        public void Authoring_GetParents([Values(1,2,3)] int count, [Values(typeof(GetParents_PrimaryEntity),typeof(GetParents_Component),typeof(GetParents_GameObject),typeof(GetParents_PrimaryEntity_PassingList),typeof(GetParents_Component_PassingList),typeof(GetParents_GameObject_PassingList))]  Type bakerType)
        {
            GameObject current = BakerTestsHierarchyHelper.CreateParentHierarchy(count, m_Prefab);
            var componentEntity = current.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {current}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                ComponentTest1 component1 = m_Manager.GetComponentData<ComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == count - 1);

                var foundComponents = current.GetComponentsInParent<Transform>();
                if (foundComponents != null && foundComponents.Length > 0)
                {
                    var elements = m_Manager.GetBuffer<IntElement>(entities[0]);
                    Assert.AreEqual(foundComponents.Length - 1, elements.Length);
                    for (int index = 1; index < foundComponents.Length; ++index)
                    {
                        Assert.AreEqual(foundComponents[index].gameObject.GetInstanceID(), elements[index - 1].Value);
                    }
                }
            }
        }

        [Test]
        public void Authoring_GetParents_NullError([Values(BakerTestsHierarchyHelper.ParentHierarchyMaskTests.All)] BakerTestsHierarchyHelper.ParentHierarchyMaskTests mask, [Values(typeof(GetParents_ComponentNull),typeof(GetParents_GameObjectNull),typeof(GetParents_ComponentNull_PassingList),typeof(GetParents_GameObjectNull_PassingList))]  Type bakerType)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));
            GameObject current = BakerTestsHierarchyHelper.CreateParentHierarchyWithType<BoxCollider>(mask, m_Prefab, out int added);
            var componentEntity = current.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {current}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
            }
        }

        [Test]
        public void Authoring_GetChild([Values(0,1,2,3,4)] int query, [Values(typeof(GetChild_PrimaryEntity),typeof(GetChild_Component),typeof(GetChild_GameObject))] Type bakerType)
        {
            GetChild_PrimaryEntity.QueryIndex = query;
            GetChild_Component.QueryIndex = query;
            GetChild_GameObject.QueryIndex = query;

            if (query == 4)
            {
                // Expect error
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*Transform child out of bounds.*"));
            }

            GameObject root = new GameObject();
            List<GameObject> goList = new List<GameObject>();
            BakerTestsHierarchyHelper.CreateChildrenHierarchy(root, 1, 4, goList);

            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);

                if (query < 4)
                {
                    var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                    Assert.IsTrue(entities.Length == 1);

                    GetComponentTest1 component1 = m_Manager.GetComponentData<GetComponentTest1>(entities[0]);
                    Assert.IsTrue(component1.Field == 4);

                    // Make sure we found the same gameobject
                    var foundComponent = root.transform.GetChild(query);
                    if (foundComponent != null)
                    {
                        Assert.AreEqual(component1.GUID, foundComponent.gameObject.GetInstanceID());
                    }
                }
            }
        }

        [Test]
        public void Authoring_GetChild_NoChildren([Values(typeof(GetChild_PrimaryEntity),typeof(GetChild_Component),typeof(GetChild_GameObject))] Type bakerType)
        {
            GetChild_PrimaryEntity.QueryIndex = 0;
            GetChild_Component.QueryIndex = 0;
            GetChild_GameObject.QueryIndex = 0;

            GameObject root = new GameObject();
            List<GameObject> goList = new List<GameObject>();

            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                GetComponentTest1 component1 = m_Manager.GetComponentData<GetComponentTest1>(entities[0]);
                Assert.IsTrue(component1.Field == 0);
            }
        }

        [Test]
        public void Authoring_GetChild_NullError([Values(typeof(GetChild_ComponentNull),typeof(GetChild_GameObjectNull))] Type bakerType)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));
            GameObject root = new GameObject();
            List<GameObject> goList = new List<GameObject>();
            BakerTestsHierarchyHelper.CreateChildrenHierarchy(root, 1, 4, goList);

            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void Authoring_GetChildren([Values(0,1,2)] int depth, [Values(typeof(GetChildren_PrimaryEntity),typeof(GetChildren_Component),typeof(GetChildren_GameObject),typeof(GetChildren_PrimaryEntity_PassingList),typeof(GetChildren_Component_PassingList),typeof(GetChildren_GameObject_PassingList))] Type bakerType, [Values] bool recursive)
        {
            // Set the recursive in the bakers, to avoid duplicating all the bakers
            GetChildren_PrimaryEntity.Recursive = recursive;
            GetChildren_Component.Recursive = recursive;
            GetChildren_GameObject.Recursive = recursive;
            GetChildren_PrimaryEntity_PassingList.Recursive = recursive;
            GetChildren_Component_PassingList.Recursive = recursive;
            GetChildren_GameObject_PassingList.Recursive = recursive;

            GameObject root = new GameObject();
            List<GameObject> goList = new List<GameObject>();
            BakerTestsHierarchyHelper.CreateChildrenHierarchy(root, (uint)depth, 4, goList);

            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);

                var entities = m_BakingSystem.GetEntitiesForBakers(componentEntity);
                Assert.IsTrue(entities.Length == 1);

                var elements = m_Manager.GetBuffer<IntElement>(entities[0]);
                if (recursive)
                {
                    var list = root.GetComponentsInChildren<Transform>();
                    Assert.AreEqual(list.Length - 1, elements.Length);
                    for (int listIndex = 1; listIndex < list.Length; ++listIndex)
                    {
                        Assert.AreEqual(list[listIndex].gameObject.GetInstanceID(), elements[listIndex - 1].Value);
                    }
                }
                else
                {
                    int index = 0;
                    foreach (Transform child in root.transform)
                    {
                        Assert.AreEqual(child.gameObject.GetInstanceID(), elements[index].Value);
                        ++index;
                    }
                    Assert.AreEqual(index, elements.Length);
                }
            }
        }

        [Test]
        public void Authoring_GetChildren_NullError([Values(2)] int depth, [Values(typeof(GetChildren_ComponentNull),typeof(GetChildren_GameObjectNull),typeof(GetChildren_ComponentNull_PassingList),typeof(GetChildren_GameObjectNull_PassingList))] Type bakerType )
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex(@".*NullReferenceException.*"));

            GameObject root = new GameObject();
            List<GameObject> goList = new List<GameObject>();
            BakerTestsHierarchyHelper.CreateChildrenHierarchy(root, (uint)depth, 4, goList);
            var componentEntity = root.AddComponent<DefaultAuthoringComponent>();
            using (new BakerDataUtility.OverrideBakers(true, bakerType))
            {
                BakingUtility.BakeGameObjects(World, new[] {root}, m_BakingSystem.BakingSettings);
            }
        }

        [Test]
        public void BaseAuthoringType_HasAttribute_AllBakersRun()
        {
            m_Prefab.AddComponent<Authoring_DerivedFromDerivedClass>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(BaseClassBaker_WithAttribute),typeof(DerivedClassBaker_WithAttribute),typeof(DerivedDerivedClassBaker)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc {All = new ComponentType[]{typeof(ComponentTest1),typeof(ComponentTest2),typeof(ComponentTest3)}});
                var entities = query.ToEntityArray(Allocator.Temp);
                Assert.AreEqual(1, entities.Length);
            }
        }

        [Test]
        public void BaseAuthoringType_HasNoAttribute_OnlyOneBakerRun()
        {
            m_Prefab.AddComponent<Authoring_DerivedFromDerivedClass>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(BaseClassBaker),typeof(DerivedClassBaker),typeof(DerivedDerivedClassBaker)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    None = new ComponentType[]{typeof(ComponentTest1),typeof(ComponentTest2)},
                    All = new ComponentType[]{typeof(ComponentTest3)}
                });
                var entities = query.ToEntityArray(Allocator.Temp);
                Assert.AreEqual(1, entities.Length);
            }
        }

        [Test]
        public void AbstractAuthoringType_HasAttribute_AllBakersRun()
        {
            m_Prefab.AddComponent<Authoring_DerivedFromAbstract>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(AbstractClassBaker_WithAttribute),typeof(DerivedFromAbstractClassBaker)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc {All = new ComponentType[]{typeof(ComponentTest1),typeof(ComponentTest2)}});
                var entities = query.ToEntityArray(Allocator.Temp);
                Assert.AreEqual(1, entities.Length);
            }
        }

        [Test]
        public void AbstractAuthoringType_HasNoAttribute_OnlyOneBakerRun()
        {
            m_Prefab.AddComponent<Authoring_DerivedFromAbstract>();

            using (new BakerDataUtility.OverrideBakers(true, typeof(AbstractClassBaker),typeof(DerivedFromAbstractClassBaker)))
            {
                BakingUtility.BakeGameObjects(World, new[] {m_Prefab}, m_BakingSystem.BakingSettings);

                var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    None = new ComponentType[]{typeof(ComponentTest1)},
                    All = new ComponentType[]{typeof(ComponentTest2)}
                });
                var entities = query.ToEntityArray(Allocator.Temp);
                Assert.AreEqual(1, entities.Length);
            }
        }
    }
}
