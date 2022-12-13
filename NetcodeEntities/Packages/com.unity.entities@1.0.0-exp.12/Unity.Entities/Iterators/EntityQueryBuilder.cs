using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    /// <summary>
    /// Describes a query to find archetypes in terms of required, optional, and excluded components.
    /// </summary>
    /// <remarks>
    /// EntityQueryBuilder is unmanaged and compatible with the Burst compiler. It is the recommended way
    /// to create an EntityQuery, and can be used for both SystemBase and ISystem.
    ///
    /// Use an EntityQueryBuilder object to describe complex queries.
    ///
    /// A query description combines the component types you specify in `WithAll`, `WithAny`, and `WithNone`
    /// sets according to the following rules:
    ///
    /// * All - Includes archetypes that have every component in this set
    /// * Any - Includes archetypes that have at least one component in this set
    /// * None - Excludes archetypes that have any component in this set
    ///
    /// For example, given entities with the following components:
    ///
    /// * Player has components: ObjectPosition, ObjectRotation, Player
    /// * Enemy1 has components: ObjectPosition, ObjectRotation, Melee
    /// * Enemy2 has components: ObjectPosition, ObjectRotation, Ranger
    ///
    /// The query description below matches all of the archetypes that:
    /// have any of [Melee or Ranger], AND have none of [Player], AND have all of [ObjectPosition and ObjectRotation]
    ///
    /// <example>
    /// <code lang="csharp" source="../../DocCodeSamples.Tests/EntityQueryExamples.cs" region="query-builder" title="Query Builder"/>
    /// </example>
    ///
    /// In other words, the query created from this description selects the Enemy1 and Enemy2 entities, but not the Player entity.
    /// </remarks>
    [GenerateTestsForBurstCompatibility]
    public unsafe ref struct EntityQueryBuilder
    {
        internal struct ComponentIndexArray
        {
            public ushort Index;
            public ushort Count;
        }
        internal struct QueryTypes
        {
            public ComponentIndexArray All;
            public ComponentIndexArray Any;
            public ComponentIndexArray None;
            public EntityQueryOptions Options;
        }

        // Internal data for the EntityQueryBuilder so it can use reference semantics.
        internal struct BuilderData
        {
            // These first two lists represent the fully-specified component type constraints for the query.

            // A homogenous list of ComponentTypes use for query specification.
            internal UnsafeList<ComponentType> _typeData;
            // List of structs that provide indices and ranges into the _typeData list for each of the All,
            // Any, and None constraints. There will be one element in this list for each ArchetypeQuery created
            // in the EntityQuery (see AddAdditionalQuery).
            internal UnsafeList<QueryTypes> _indexData;

            // These lists accumulate the constraints for the query. When the EntityQueryBuilder is used
            // to create a query, FinalizeQueryInternal is called, which will write this pending data
            // to _typeData and _indexData.
            internal UnsafeList<ComponentType> _all;
            internal UnsafeList<ComponentType> _any;
            internal UnsafeList<ComponentType> _none;
            internal EntityQueryOptions _pendingOptions;
            internal byte _isFinalized;
        }

        internal BuilderData* _builderDataPtr;
        private AllocatorManager.AllocatorHandle _allocator;

        /// <summary>
        /// Create an entity query description builder.
        /// </summary>
        /// <param name="allocator">The allocator used to allocate the builder's arrays. Typically Allocator.Temp.</param>
        /// <remarks>
        /// It is safe to use Allocator.Temp for all EntityQueryBuilders. Since they are a ref struct,
        /// their lifetime is limited to the current frame.
        /// </remarks>
        public EntityQueryBuilder(Allocator allocator)
        {
            _allocator = allocator;
            _builderDataPtr = _allocator.Allocate(default(BuilderData), 1);

            _builderDataPtr->_typeData = new UnsafeList<ComponentType>(2, _allocator);
            _builderDataPtr->_indexData = new UnsafeList<QueryTypes>(2, _allocator);
            _builderDataPtr->_all = new UnsafeList<ComponentType>(6, _allocator);
            _builderDataPtr->_any = new UnsafeList<ComponentType>(6, _allocator);
            _builderDataPtr->_none = new UnsafeList<ComponentType>(6, _allocator);

            _builderDataPtr->_pendingOptions = default;
            _builderDataPtr->_isFinalized = 0;
        }

        /// <summary>
        /// Create an EntityQueryBuilder from a list of component types.
        /// </summary>
        internal EntityQueryBuilder(Allocator allocator, ComponentType* componentTypes, int count)
        {
            _allocator = allocator;
            _builderDataPtr = _allocator.Allocate(default(BuilderData), 1);

            _builderDataPtr->_typeData = new UnsafeList<ComponentType>(2, _allocator);
            _builderDataPtr->_indexData = new UnsafeList<QueryTypes>(2, _allocator);
            _builderDataPtr->_all = new UnsafeList<ComponentType>(count, _allocator);
            // There's no way to specify optional constraints with an array of ComponentType
            _builderDataPtr->_any = new UnsafeList<ComponentType>(1, _allocator);
            _builderDataPtr->_none = new UnsafeList<ComponentType>(6, _allocator);
            _builderDataPtr->_pendingOptions = default;
            _builderDataPtr->_isFinalized = 0;

            for (var i = 0; i < count; i++)
            {
                var componentType = componentTypes[i];
                if (componentType.AccessModeType == ComponentType.AccessMode.Exclude)
                {
                    var noneType = ComponentType.ReadOnly(componentType.TypeIndex);
                    WithNone(&noneType, 1);
                }
                else
                {
                    WithAll(&componentType, 1);
                }
            }
        }

        /// <summary>
        /// Set options for the current query.
        /// </summary>
        /// <remarks>
        /// You should not need to set these options for most queries.
        ///
        /// You should only call WithOptions once for each query description. Subsequent calls
        /// override previous options, rather than adding to them. Use the bitwise OR
        /// operator '|' to combine multiple options.
        /// </remarks>
        /// <param name="options"><see cref="EntityQueryOptions"/> flags to set for the current query</param>
        /// <returns>The builder object that invoked this method.</returns>
        public EntityQueryBuilder WithOptions(EntityQueryOptions options)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if(_builderDataPtr->_pendingOptions != default(EntityQueryOptions))
            {
                throw new InvalidOperationException("EntityQueryBuilder.WithOptions should only be called once " +
                                                    "for each query description. Subsequent calls will override previous " +
                                                    "options, rather than adding to them. Use the bitwise OR operator '|'" +
                                                    "to combine multiple options.");
            }
#endif
            _builderDataPtr->_pendingOptions = options;
            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <summary>
        /// Add an "all" matching type to the current query.
        /// </summary>
        /// <param name="t">The component type</param>
        /// <returns>The builder object that invoked this method.</returns>
        [Obsolete("Use WithAll<T,...> instead, or WithAll(INativeList) if component types are not known at compile time. (RemovedAfter Entities 1.0)", false)]
        public EntityQueryBuilder AddAll(ComponentType t)
        {
            _builderDataPtr->_isFinalized = 0;
            return WithAll(&t, 1);
        }

        /// <summary>
        /// Add required component types to the query.
        /// </summary>
        /// <remarks>
        /// To match the resulting query, an Entity must have all of the query's required component types.
        ///
        /// WithAll accepts up to seven type arguments. You can add more component types by chaining calls together.
        ///
        /// <example>
        /// <code lang="csharp" source="../../DocCodeSamples.Tests/EntityQueryExamples.cs" region="query-builder-chained-withall" title="Query Builder With Chained WithAll Calls"/>
        /// </example>
        ///
        /// To add component types that are not known at compile time, use <see cref="M:Unity.Entities.EntityQueryBuilder.WithAll``1(``0@)"/>
        /// </remarks>
        /// <typeparam name="T1">A required component type</typeparam>
        /// <returns>The builder object that invoked this method.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithAll<T1>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAll``1"/>
        /// <typeparam name="T2">A required component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithAll<T1,T2>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAll``2"/>
        /// <typeparam name="T3">A required component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAll<T1,T2,T3>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAll``3"/>
        /// <typeparam name="T4">A required component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAll<T1,T2,T3,T4>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAll``4"/>
        /// <typeparam name="T5">A required component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAll<T1,T2,T3,T4,T5>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAll``5"/>
        /// <typeparam name="T6">A required component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAll<T1,T2,T3,T4,T5,T6>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T6>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAll``6"/>
        /// <typeparam name="T7">A required component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAll<T1,T2,T3,T4,T5,T6,T7>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T6>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T7>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <summary>
        /// Add required component types to the query with ReadWrite mode.
        /// </summary>
        /// <remarks>
        /// If a query uses the <see cref="F:Unity.Entities.EntityQueryOptions.FilterWriteGroup"/> option,
        /// you must use WithAllRW to specify the query's writeable required components. Refer to the
        /// [write groups guide](xref:systems-write-groups) for more information.
        ///
        /// <see cref="M:Unity.Entities.EntityQueryBuilder.WithAll``1"/> assumes the component type is read-only.
        ///
        /// To match the resulting query, an Entity must have all of the query's required component types.
        ///
        /// WithAllRW accepts up to two type arguments. You can add more component types by chaining calls together.
        ///
        /// <example>
        /// <code lang="csharp" source="../../DocCodeSamples.Tests/EntityQueryExamples.cs" region="query-builder-chained-withallrw" title="Query Builder With Chained WithAllRW Calls"/>
        /// </example>
        ///
        /// </remarks>
        /// <typeparam name="T1">A required ReadWrite component type</typeparam>
        /// <returns>The builder object that invoked this method.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithAllRW<T1>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadWrite });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAllRW``1"/>
        /// <typeparam name="T2">A required ReadWrite component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithAllRW<T1,T2>()
        {
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadWrite });
            _builderDataPtr->_all.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadWrite });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <summary>
        /// Add a list of required component types to the query.
        /// </summary>
        /// <remarks>
        /// To match the resulting query, an Entity must have all of the query's required component types.
        /// </remarks>
        /// <param name="componentTypes">
        /// A list of component types that implements <see cref="T:Unity.Collections.INativeList`1"/>.
        /// For example, <see cref="T:Unity.Collections.NativeList`1"/> or
        /// <see cref="T:Unity.Collections.FixedList64Bytes`1"/>
        /// </param>
        /// <typeparam name="T">A container of component types</typeparam>
        /// <returns>The builder object that invoked this method.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(FixedList32Bytes<ComponentType>)})]
        public EntityQueryBuilder WithAll<T>(ref T componentTypes)
            where T : INativeList<ComponentType>
        {
            for (var i = 0; i < componentTypes.Length; i++)
            {
                _builderDataPtr->_all.Add(componentTypes[i]);
            }

            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        internal EntityQueryBuilder WithAll(ComponentType* componentTypes, int count)
        {
            for (var i = 0; i < count; i++)
            {
                _builderDataPtr->_all.Add(componentTypes[i]);
            }

            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <summary>
        /// Add an "any" matching type to the current query.
        /// </summary>
        /// <param name="t">The component type</param>
        /// <returns>The builder object that invoked this method.</returns>
        [Obsolete("Use WithAny<T,...> instead, or WithAny(INativeList) if component types are not known at compile time. (RemovedAfter Entities 1.0)", false)]
        public EntityQueryBuilder AddAny(ComponentType t)
        {
            _builderDataPtr->_isFinalized = 0;
            return WithAny(&t, 1);
        }


        /// <summary>
        /// Add optional component types to the query.
        /// </summary>
        /// <remarks>
        /// To match the resulting query, an Entity must have at least one of the query's optional component types.
        ///
        /// WithAny accepts up to seven type arguments. You can add more component types by chaining calls together.
        ///
        /// In the following example, an Entity must have either an ObjectUniformScale, ObjectNonUniformScale, and/or ObjectCompositeScale
        /// component in order to match the query:
        ///
        /// <example>
        /// <code lang="csharp" source="../../DocCodeSamples.Tests/EntityQueryExamples.cs" region="query-builder-chained-withany" title="Query Builder With Chained WithAny Calls"/>
        /// </example>
        ///
        /// To add component types that are not known at compile time, use <see cref="M:Unity.Entities.EntityQueryBuilder.WithAny``1(``0@)"/>
        /// </remarks>
        /// <typeparam name="T1">An optional component type</typeparam>
        /// <returns>The builder object that invoked this method.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithAny<T1>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAny``1"/>
        /// <typeparam name="T2">An optional component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithAny<T1,T2>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAny``2"/>
        /// <typeparam name="T3">An optional component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAny<T1,T2,T3>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAny``3"/>
        /// <typeparam name="T4">An optional component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAny<T1,T2,T3,T4>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAny``4"/>
        /// <typeparam name="T5">An optional component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAny<T1,T2,T3,T4,T5>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAny``5"/>
        /// <typeparam name="T6">An optional component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAny<T1,T2,T3,T4,T5,T6>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T6>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAny``6"/>
        /// <typeparam name="T7">An optional component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithAny<T1,T2,T3,T4,T5,T6,T7>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T6>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T7>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <summary>
        /// Add optional component types to the query with ReadWrite mode.
        /// </summary>
        /// <remarks>
        /// If a query uses the <see cref="F:Unity.Entities.EntityQueryOptions.FilterWriteGroup"/> option,
        /// you must use WithAnyRW to specify the query's writeable optional components. Refer to the
        /// [write groups guide](xref:systems-write-groups) for more information.
        ///
        /// <see cref="M:Unity.Entities.EntityQueryBuilder.WithAny``1"/> assumes the component type is read-only.
        ///
        /// To match the resulting query, an Entity must have all of the query's optional component types.
        ///
        /// WithAnyRW accepts up to two type arguments. You can add more component types by chaining calls together.
        ///
        /// In the following example, an Entity must have either a Scale, NonUniformScale, and/or CompositeScale
        /// component in order to match the query:
        ///
        /// <example>
        /// <code lang="csharp" source="../../DocCodeSamples.Tests/EntityQueryExamples.cs" region="query-builder-chained-withaanyrw" title="Query Builder With Chained WithAnyRW Calls"/>
        /// </example>
        ///
        /// </remarks>
        /// <typeparam name="T1">An optional ReadWrite component type</typeparam>
        /// <returns>The builder object that invoked this method.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithAnyRW<T1>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadWrite });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithAnyRW``1"/>
        /// <typeparam name="T2">An optional ReadWrite component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithAnyRW<T1,T2>()
        {
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadWrite });
            _builderDataPtr->_any.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadWrite });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <summary>
        /// Add optional component types to the query.
        /// </summary>
        /// <remarks>
        /// To match the resulting query, an Entity must have at least one of the query's optional component types.
        ///
        /// To add component types that are known at compile time, use <see cref="M:Unity.Entities.EntityQueryBuilder.WithAny``1"/>
        /// </remarks>
        /// <param name="componentTypes">
        /// A list of component types that implements <see cref="T:Unity.Collections.INativeList`1"/>.
        /// For example, <see cref="T:Unity.Collections.NativeList`1"/> or
        /// <see cref="T:Unity.Collections.FixedList64Bytes`1"/>
        /// </param>
        /// <typeparam name="T">A container of component types</typeparam>
        /// <returns>The builder object that invoked this method.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(FixedList32Bytes<ComponentType>)})]
        public EntityQueryBuilder WithAny<T>(ref T componentTypes)
            where T : INativeList<ComponentType>
        {
            for (var i = 0; i < componentTypes.Length; i++)
            {
                _builderDataPtr->_any.Add(componentTypes[i]);
            }

            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        internal EntityQueryBuilder WithAny(ComponentType* componentTypes, int count)
        {
            for (var i = 0; i < count; i++)
            {
                _builderDataPtr->_any.Add(componentTypes[i]);
            }

            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <summary>
        /// Add a "none" matching type to the current query.
        /// </summary>
        /// <param name="t">The component type</param>
        /// <remarks>Types in the None list are never written to. If the AccessModeType field of the
        /// provided component type is <see cref="ComponentType.AccessMode.ReadWrite"/>, will be forced to
        /// <see cref="NativeArray{T}.ReadOnly"/> in the query.</remarks>
        /// <returns>The builder object that invoked this method.</returns>
        [Obsolete("Use WithNone<T,...> instead, or WithNone(INativeList) if component types are not known at compile time. (RemovedAfter Entities 1.0)", false)]
        public EntityQueryBuilder AddNone(ComponentType t)
        {
            _builderDataPtr->_isFinalized = 0;
            // The access mode of types in the None list is forced to ReadOnly; the query will not be accessing these
            // types at all, and should not be requesting read/write access to them.
            t.AccessModeType = ComponentType.AccessMode.ReadOnly;
            return WithNone(&t, 1);
        }

        /// <summary>
        /// Add excluded component types to the query.
        /// </summary>
        /// <remarks>
        /// To match the resulting query, an Entity must not have any of the query's excluded component types.
        ///
        /// WithNone accepts up to seven type arguments. You can add more component types by chaining calls together.
        ///
        /// To add component types that are not known at compile time, use <see cref="M:Unity.Entities.EntityQueryBuilder.WithNone``1(``0@)"/>
        ///
        /// Component types added using WithNone are never written to. If the <see cref="ComponentType.AccessMode"/> field of the
        /// provided component type is <see cref="ComponentType.AccessMode.ReadWrite"/>, it will be forced to
        /// <see cref="ComponentType.AccessMode.ReadOnly"/> in the final query.
        /// </remarks>
        /// <typeparam name="T1">An excluded component type</typeparam>
        /// <returns>The builder object that invoked this method.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public EntityQueryBuilder WithNone<T1>()
        {
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithNone``1"/>
        /// <typeparam name="T2">An excluded component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)})]
        public EntityQueryBuilder WithNone<T1,T2>()
        {
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithNone``2"/>
        /// <typeparam name="T3">An excluded component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithNone<T1,T2,T3>()
        {
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithNone``3"/>
        /// <typeparam name="T4">An excluded component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithNone<T1,T2,T3,T4>()
        {
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithNone``4"/>
        /// <typeparam name="T5">An excluded component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithNone<T1,T2,T3,T4,T5>()
        {
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithNone``5"/>
        /// <typeparam name="T6">An excluded component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithNone<T1,T2,T3,T4,T5,T6>()
        {
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T6>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        /// <inheritdoc cref="M:Unity.Entities.EntityQueryBuilder.WithNone``6"/>
        /// <typeparam name="T7">An excluded component type</typeparam>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData),
            typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData), typeof(BurstCompatibleComponentData)
        })]
        public EntityQueryBuilder WithNone<T1,T2,T3,T4,T5,T6,T7>()
        {
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T1>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T2>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T3>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T4>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T5>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T6>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_none.Add(new ComponentType{ TypeIndex = TypeManager.GetTypeIndex<T7>(), AccessModeType = ComponentType.AccessMode.ReadOnly });
            _builderDataPtr->_isFinalized = 0;
            return this;
        }

        /// <summary>
        /// Add excluded component types to the query.
        /// </summary>
        /// <remarks>
        /// To match the resulting query, an Entity must not have any of the query's excluded component types.
        ///
        /// To add component types that are known at compile time, use <see cref="M:Unity.Entities.EntityQueryBuilder.WithNone``1"/>
        ///
        /// Component types added using WithNone are never written to. If the <see cref="ComponentType.AccessMode"/> field of the
        /// provided component type is <see cref="ComponentType.AccessMode.ReadWrite"/>, it will be forced to
        /// <see cref="ComponentType.AccessMode.ReadOnly"/> in the final query.
        /// </remarks>
        /// <param name="componentTypes">
        /// A list of component types that implements <see cref="T:Unity.Collections.INativeList`1"/>.
        /// For example, <see cref="T:Unity.Collections.NativeList`1"/> or
        /// <see cref="T:Unity.Collections.FixedList64Bytes`1"/>
        /// </param>
        /// <typeparam name="T">A container of component types</typeparam>
        /// <returns>The builder object that invoked this method.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(FixedList32Bytes<ComponentType>)})]
        public EntityQueryBuilder WithNone<T>(ref T componentTypes)
            where T : INativeList<ComponentType>
        {
            for (var i = 0; i < componentTypes.Length; i++)
            {
                _builderDataPtr->_none.Add(ComponentType.ReadOnly(componentTypes[i].TypeIndex));
            }

            _builderDataPtr->_isFinalized = 0;
            return this;
        }
        internal EntityQueryBuilder WithNone(ComponentType* componentTypes, int count)
        {
            for (var i = 0; i < count; i++)
            {
                _builderDataPtr->_none.Add(ComponentType.ReadOnly(componentTypes[i].TypeIndex));
            }

            _builderDataPtr->_isFinalized = 0;
            return this;
        }


        /// <summary>
        /// Add an additional query description to a single EntityQuery.
        /// </summary>
        /// <remarks>
        /// The resulting EntityQuery will match all entities matched by any individual query description. In terms of
        /// set theory, the query matches the union of its query descriptions, not the intersection.
        /// <example>
        /// <code lang="csharp" source="../../DocCodeSamples.Tests/EntityQueryExamples.cs" region="combine-query-builder" title="Query Builder With Multiple Descriptions"/>
        /// </example>
        /// The EntityQuery created from this builder matches entities that have a Parent component
        /// but no Child component OR have a Child component but no Parent component.
        /// </remarks>
        /// <returns>Returns the amended EntityQuery.</returns>
        public EntityQueryBuilder AddAdditionalQuery()
        {
            return FinalizeQueryInternal();
        }

        /// <summary>
        /// Calling this method has no effect; it is temporarily provided for backwards compatibility.
        /// </summary>
        /// <returns></returns>
        [Obsolete("It is no longer necessary to call FinalizeQuery on every EntityQueryBuilder. " +
                  "If you want to build an EntityQuery with multiple query descriptions, call AddAdditionalQuery " +
                  "before each subsequent query description. (RemovedAfter Entities 1.0)", true)]
        public EntityQueryBuilder FinalizeQuery()
        {
            return this;
        }

        /// <summary>
        /// Store the pending query constraints into the builder, and clear the pending state.
        /// </summary>
        /// <remarks>
        /// Components added to any, all, and none are stored in the builder once this is called.
        /// If you don't call this, nothing is recorded and the query will be empty.
        /// </remarks>
        /// <returns>The builder object that invoked this method.</returns>
        internal EntityQueryBuilder FinalizeQueryInternal()
        {
            if (_builderDataPtr->_isFinalized != 0)
                return this;

            QueryTypes qd = default;

            // For each type of constraint, add the TypeManager type indices to the _typeData list,
            // and record the starting index and number of types written into the QueryTypes struct.
            TransferArray(ref _builderDataPtr->_all, ref qd.All);
            TransferArray(ref _builderDataPtr->_any, ref qd.Any);
            TransferArray(ref _builderDataPtr->_none, ref qd.None);
            qd.Options = _builderDataPtr->_pendingOptions;

            // Add the QueryTypes struct to the list of _indexData. There should be one QueryTypes
            // entry in the list for each subquery
            _builderDataPtr->_indexData.Add(qd);
            _builderDataPtr->_pendingOptions = default;
            _builderDataPtr->_isFinalized = 1;

            return this;
        }

        private void TransferArray(ref UnsafeList<ComponentType> source, ref ComponentIndexArray result)
        {
            result.Index = (ushort)_builderDataPtr->_typeData.Length;
            result.Count = (ushort)source.Length;
            _builderDataPtr->_typeData.AddRange(source);
            source.Clear();
        }

        /// <summary>
        /// Dispose the builder and release the memory.
        /// </summary>
        public void Dispose()
        {
            _builderDataPtr->_typeData.Dispose();
            _builderDataPtr->_indexData.Dispose();
            _builderDataPtr->_all.Dispose();
            _builderDataPtr->_any.Dispose();
            _builderDataPtr->_none.Dispose();

            if (CollectionHelper.ShouldDeallocate(_allocator))
            {
                AllocatorManager.Free(_allocator, _builderDataPtr);
                _allocator = AllocatorManager.Invalid;
            }

            _builderDataPtr = null;
        }

        /// <summary>
        /// Reset the builder for reuse.
        /// </summary>
        /// <remarks>
        /// To create another EntityQuery without allocating additional memory, call this method after you create an
        /// query with <see cref="M:EntityQueryBuilder.Build"/>.
        /// </remarks>
        public void Reset()
        {
            _builderDataPtr->_typeData.Clear();
            _builderDataPtr->_indexData.Clear();
            _builderDataPtr->_all.Clear();
            _builderDataPtr->_any.Clear();
            _builderDataPtr->_none.Clear();
            _builderDataPtr->_pendingOptions = default;
            _builderDataPtr->_isFinalized = 0;
        }

        /// <summary>
        /// Create an EntityQuery owned by an <see cref="T:Unity.Entities.ISystem"/>'s <see cref="T:Unity.Entities.SystemState"/>.
        /// </summary>
        /// <remarks>
        /// The System owning the systemState object retains this EntityQuery and disposes it at the end of the System's lifespan.
        /// </remarks>
        /// <param name="systemState">SystemState of the system that will own the EntityQuery.</param>
        /// <returns>An EntityQuery based on the constraints set in the EntityQueryBuilder</returns>
        public EntityQuery Build(ref SystemState systemState)
        {
            return systemState.GetEntityQuery(this);
        }

        /// <summary>
        /// Create an EntityQuery owned by an <see cref="T:Unity.Entities.SystemBase"/>.
        /// </summary>
        /// <remarks>The SystemBase argument retains the EntityQuery, and
        /// disposes it at the end of that system's lifetime.
        /// </remarks>
        /// <param name="systemBase">System that will own the EntityQuery.</param>
        /// <returns>An EntityQuery based on the constraints set in the EntityQueryBuilder</returns>
        [ExcludeFromBurstCompatTesting("SystemBase is a managed class")]
        public EntityQuery Build(SystemBase systemBase)
        {
            return systemBase.GetEntityQuery(this);
        }

        /// <summary>
        /// Create an EntityQuery owned by an <see cref="T:Unity.Entities.EntityManager"/>.
        /// </summary>
        /// <remarks>
        /// The EntityManager retains the EntityQuery, and
        /// disposes it at the end of that EntityManager's corresponding World's lifetime.
        /// </remarks>
        /// <param name="entityManager">EntityManager that will own the EntityQuery.</param>
        /// <returns>An EntityQuery based on the constraints set in the EntityQueryBuilder.</returns>
        public EntityQuery Build(EntityManager entityManager)
        {
            // TODO: https://jira.unity3d.com/browse/DOTS-6788 EntityManager should own and dispose this EntityQuery
            return entityManager.CreateEntityQuery(this);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal static void ValidateComponentTypes(in UnsafeList<ComponentType> componentTypes, ref UnsafeList<TypeIndex> allTypeIds)
        {
            // Needs to make sure that AccessModeType is not Exclude
            var entityTypeIndex = TypeManager.GetTypeIndex<Entity>();
            for (int i = 0; i < componentTypes.Length; i++)
            {
                var componentType = componentTypes[i];
                allTypeIds.Add(componentType.TypeIndex);
                if (componentType.AccessModeType == ComponentType.AccessMode.Exclude)
                    throw new ArgumentException("EntityQueryDesc cannot contain Exclude Component types");

                if (componentType.TypeIndex == entityTypeIndex)
                {
                    throw new ArgumentException("Entity is not allowed in list of component types for EntityQuery");
                }
            }
        }

#if !NET_DOTS
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [BurstDiscard]
        internal static void ThrowDuplicateComponentTypeError(TypeIndex curId)
        {
            var typeName = TypeManager.GetType(curId).Name;
            throw new EntityQueryDescValidationException(
                $"EntityQuery contains a filter with duplicate component type name {typeName}.  Queries can only contain a single component of a given type in a filter.");
        }
#endif

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal static void Validate(in UnsafeList<ComponentType> allTypes, in UnsafeList<ComponentType> anyTypes, in UnsafeList<ComponentType> noneTypes)
        {
            // Determine the number of ComponentTypes contained in the filters
            var itemCount = allTypes.Length + anyTypes.Length + noneTypes.Length;

            // Project all the ComponentType Ids of None, All, Any queryDesc filters into the same array to identify duplicated later on

            var allComponentTypeIds = new UnsafeList<TypeIndex>(itemCount, Allocator.Temp);
            ValidateComponentTypes(allTypes, ref allComponentTypeIds);
            ValidateComponentTypes(anyTypes, ref allComponentTypeIds);
            ValidateComponentTypes(noneTypes, ref allComponentTypeIds);

            // Check for duplicate, only if necessary
            if (itemCount > 1)
            {
                // Build a new unsafeList for None, All, Any


                // Sort the Ids to have identical value adjacent
                allComponentTypeIds.Sort();

                // Check for identical values
                var refId = allComponentTypeIds[0];
                for (int i = 1; i < allComponentTypeIds.Length; i++)
                {
                    var curId = allComponentTypeIds[i];
                    if (curId == refId)
                    {
#if !NET_DOTS
                        ThrowDuplicateComponentTypeError(curId);
#endif
                        throw new EntityQueryDescValidationException(
                            $"EntityQuery contains an EntityQueryDesc with duplicate component type index {curId}.  Queries can only contain a single component of a given type in a EntityQueryDesc.");
                    }

                    refId = curId;
                }
            }

            allComponentTypeIds.Dispose();
        }
    }

    /// <inheritdoc cref="EntityQueryBuilder"/>
    [Obsolete("Use EntityQueryBuilder (UnityUpgradable) -> EntityQueryBuilder")]
    public struct EntityQueryDescBuilder
    {
        /// <inheritdoc cref="EntityQueryBuilder.WithOptions"/>
        [Obsolete("Use WithOptions(EntityQueryOptions) (UnityUpgradable) -> EntityQueryBuilder.WithOptions(*)", false)]
        public EntityQueryDescBuilder Options(EntityQueryOptions options)
        {
            return this;
        }
    }
}
