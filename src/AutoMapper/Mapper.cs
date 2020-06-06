using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace AutoMapper
{
    using QueryableExtensions;
    using ObjectMappingOperationOptions = MappingOperationOptions<object, object>;
    using IObjectMappingOperationOptions = IMappingOperationOptions<object, object>;

    public class Mapper : IMapper, IInternalRuntimeMapper
    {
        public Mapper(IConfigurationProvider configurationProvider)
            : this(configurationProvider, configurationProvider.ServiceCtor)
        {
        }

        public Mapper(IConfigurationProvider configurationProvider, Func<Type, object> serviceCtor)
        {
            ConfigurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
            ServiceCtor = serviceCtor ?? throw new ArgumentNullException(nameof(serviceCtor));
            DefaultContext = new ResolutionContext(new ObjectMappingOperationOptions(serviceCtor), this);
        }

        internal ResolutionContext DefaultContext { get; }

        ResolutionContext IInternalRuntimeMapper.DefaultContext => DefaultContext; 

        public Func<Type, object> ServiceCtor { get; }

        public IConfigurationProvider ConfigurationProvider { get; }

        public TDestination Map<TDestination>(object source) => Map<object, TDestination>(source);

        public TDestination Map<TDestination>(object source, Action<IMappingOperationOptions<object, TDestination>> opts) => Map(source, default, opts);

        public TDestination Map<TSource, TDestination>(TSource source) => Map(source, default(TDestination));

        public TDestination Map<TSource, TDestination>(TSource source, Action<IMappingOperationOptions<TSource, TDestination>> opts) =>
            Map(source, default, opts);

        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination) =>
            ((IInternalRuntimeMapper)this).Map(source, destination, DefaultContext);

        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination, Action<IMappingOperationOptions<TSource, TDestination>> opts) =>
            MapWithOptions(source, destination, opts);

        public object Map(object source, Type sourceType, Type destinationType) => Map(source, null, sourceType, destinationType);

        public object Map(object source, Type sourceType, Type destinationType, Action<IObjectMappingOperationOptions> opts) =>
            Map(source, null, sourceType, destinationType, opts);

        public object Map(object source, object destination, Type sourceType, Type destinationType) =>
            ((IInternalRuntimeMapper)this).Map(source, destination, DefaultContext, null, sourceType, destinationType);

        public object Map(object source, object destination, Type sourceType, Type destinationType, Action<IObjectMappingOperationOptions> opts) =>
            MapWithOptions(source, destination, opts, sourceType, destinationType);

        private TDestination MapWithOptions<TSource, TDestination>(TSource source, TDestination destination, Action<IMappingOperationOptions<TSource, TDestination>> opts,
            Type sourceType = null, Type destinationType = null)
        {
            var types = TypePair.Create(source, destination, sourceType ?? typeof(TSource), destinationType ?? typeof(TDestination));
            var key = new TypePair(typeof(TSource), typeof(TDestination));

            var typedOptions = new MappingOperationOptions<TSource, TDestination>(ServiceCtor);

            opts(typedOptions);

            var mapRequest = new MapRequest(key, types);

            var func = ConfigurationProvider.GetExecutionPlan<TSource, TDestination>(mapRequest);

            typedOptions.BeforeMapAction(source, destination);

            var context = new ResolutionContext(typedOptions, this);

            destination = func(source, destination, context);

            typedOptions.AfterMapAction(source, destination);

            return destination;
        }

        TDestination IInternalRuntimeMapper.Map<TSource, TDestination>(TSource source, TDestination destination,
            ResolutionContext context, IMemberMap memberMap, Type sourceType, Type destinationType)
        {
            var types = TypePair.Create(source, destination, sourceType ?? typeof(TSource), destinationType ?? typeof(TDestination));

            var func = ConfigurationProvider.GetExecutionPlan<TSource, TDestination>(types, memberMap);

            return func(source, destination, context);
        }

        public IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source, object parameters, params Expression<Func<TDestination, object>>[] membersToExpand)
            => source.ProjectTo(ConfigurationProvider, parameters, membersToExpand);

        public IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source, IDictionary<string, object> parameters, params string[] membersToExpand)
            => source.ProjectTo<TDestination>(ConfigurationProvider, parameters, membersToExpand);

        public IQueryable ProjectTo(IQueryable source, Type destinationType, IDictionary<string, object> parameters, params string[] membersToExpand)
            => source.ProjectTo(destinationType, ConfigurationProvider, parameters, membersToExpand);
    }
}