﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper.Execution;
using AutoMapper.Internal;

namespace AutoMapper.QueryableExtensions.Impl
{
    using static Expression;
    using static ExpressionFactory;
    using ParameterBag = IDictionary<string, object>;
    using TypePairCount = IDictionary<ExpressionRequest, int>;
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IExpressionBuilder
    {
        QueryExpressions GetMapExpression(Type sourceType, Type destinationType, object parameters, MemberPath[] membersToExpand);
        QueryExpressions CreateMapExpression(ExpressionRequest request, TypePairCount typePairCount, LetPropertyMaps letPropertyMaps);
        Expression CreateMapExpression(ExpressionRequest request, Expression instanceParameter, TypePairCount typePairCount, LetPropertyMaps letPropertyMaps);
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class ExpressionBuilder : IExpressionBuilder
    {
        internal static List<IExpressionResultConverter> DefaultResultConverters() =>
            new List<IExpressionResultConverter>
            {
                new MemberResolverExpressionResultConverter(),
                new MemberGetterExpressionResultConverter()
            };

        internal static List<IExpressionBinder> DefaultBinders() =>
            new List<IExpressionBinder>
            {
                new CustomProjectionExpressionBinder(),
                new NullableSourceExpressionBinder(),
                new AssignableExpressionBinder(),
                new EnumerableExpressionBinder(),
                new MappedTypeExpressionBinder(),
                new StringExpressionBinder(),
                new EnumToUnderlyingTypeBinder(),
                new UnderlyingTypeToEnumBinder(),
                new EnumToEnumBinder(),
            };

        private readonly LockingConcurrentDictionary<ExpressionRequest, QueryExpressions> _expressionCache;
        private readonly IGlobalConfiguration _configurationProvider;

        public ExpressionBuilder(IGlobalConfiguration configurationProvider)
        {
            _configurationProvider = configurationProvider;
            _expressionCache = new LockingConcurrentDictionary<ExpressionRequest, QueryExpressions>(CreateMapExpression);
        }

        public QueryExpressions GetMapExpression(Type sourceType, Type destinationType, object parameters, MemberPath[] membersToExpand)
        {
            var expressionRequest = new ExpressionRequest(
                sourceType ?? throw new ArgumentNullException(nameof(sourceType)),
                destinationType ?? throw new ArgumentNullException(nameof(destinationType)),
                membersToExpand ?? throw new ArgumentNullException(nameof(membersToExpand)),
                null);
            var cachedExpressions = _expressionCache.GetOrAdd(expressionRequest);
            return cachedExpressions.Transform(Prepare);
            LambdaExpression Prepare(Expression cachedExpression)
            {
                var result = parameters == null ? cachedExpression : ParameterExpressionVisitor.SetParameters(parameters, cachedExpression);
                return (LambdaExpression) (_configurationProvider.EnableNullPropagationForQueryMapping ? NullsafeQueryRewriter.NullCheck(result) : result);
            }
        }

        private QueryExpressions CreateMapExpression(ExpressionRequest request) => 
            CreateMapExpression(request, new Dictionary<ExpressionRequest, int>(), new FirstPassLetPropertyMaps(_configurationProvider, MemberPath.Empty));

        public QueryExpressions CreateMapExpression(ExpressionRequest request, TypePairCount typePairCount, LetPropertyMaps letPropertyMaps)
        {
            var instanceParameter = Parameter(request.SourceType, "dto"+ request.SourceType.Name);
            var projection = CreateMapExpressionCore(request, instanceParameter, typePairCount, letPropertyMaps, out var typeMap);
            return letPropertyMaps.Count > 0 ?
                letPropertyMaps.GetSubQueryExpression(this, projection, typeMap, request, instanceParameter, typePairCount) :
                new QueryExpressions(projection, instanceParameter);
        }

        public Expression CreateMapExpression(ExpressionRequest request, Expression instanceParameter, TypePairCount typePairCount, LetPropertyMaps letPropertyMaps) =>
            CreateMapExpressionCore(request, instanceParameter, typePairCount, letPropertyMaps, out var _);

        private Expression CreateMapExpressionCore(ExpressionRequest request, Expression instanceParameter, TypePairCount typePairCount, LetPropertyMaps letPropertyMaps, out TypeMap typeMap)
        {
            typeMap = _configurationProvider.ResolveTypeMap(request.SourceType, request.DestinationType) ?? throw QueryMapperHelper.MissingMapException(request.SourceType, request.DestinationType);
            return CreateMapExpressionCore(request, instanceParameter, typePairCount, typeMap, letPropertyMaps);
        }

        private Expression CreateMapExpressionCore(ExpressionRequest request, Expression instanceParameter, TypePairCount typePairCount, TypeMap typeMap, LetPropertyMaps letPropertyMaps)
        {
            if (typeMap.CustomMapExpression != null)
            {
                return typeMap.CustomMapExpression.ReplaceParameters(instanceParameter);
            }
            var propertiesProjections = new List<MemberBinding>();
            int depth;
            if (OverMaxDepth())
            {
                if (typeMap.Profile.AllowNullDestinationValues)
                {
                    return null;
                }
            }
            else
            {
                ProjectProperties();
            }
            var constructorExpression = CreateDestination();
            var expression = MemberInit(constructorExpression, propertiesProjections);
            return expression;
            bool OverMaxDepth()
            {
                if (typePairCount.TryGetValue(request, out depth))
                {
                    depth++;
                }
                typePairCount[request] = depth;
                return typeMap.MaxDepth > 0 && depth >= typeMap.MaxDepth;
            }
            void ProjectProperties()
            {
                foreach (var propertyMap in typeMap.PropertyMaps.Where(pm => pm.CanResolveValue && pm.DestinationMember.CanBeSet()).OrderBy(pm => pm.DestinationName))
                {
                    var propertyProjection = TryProjectMember(propertyMap, propertyMap.ExplicitExpansion);
                    if (propertyProjection != null)
                    {
                        propertiesProjections.Add(Bind(propertyMap.DestinationMember, propertyProjection));
                    }
                }
            }
            Expression TryProjectMember(IMemberMap memberMap, bool? explicitExpansion = null)
            {
                var propertyExpression = new PropertyExpression(memberMap);
                letPropertyMaps.Push(propertyExpression);
                var memberExpression = ShouldExpand() ? ProjectMemberCore() : null;
                letPropertyMaps.Pop();
                return memberExpression;
                bool ShouldExpand() => explicitExpansion != true || request.ShouldExpand(letPropertyMaps.GetCurrentPath());
                Expression ProjectMemberCore()
                {
                    var resolvedSource = ResolveSource();
                    propertyExpression.Expression = resolvedSource.NonMarkerExpression;
                    var propertyRequest = new ExpressionRequest(resolvedSource.Type, memberMap.DestinationType, request.MembersToExpand, request);
                    if (propertyRequest.AlreadyExists && depth >= _configurationProvider.RecursiveQueriesMaxDepth)
                    {
                        return null;
                    }
                    var propertyTypeMap = _configurationProvider.ResolveTypeMap(resolvedSource.Type, memberMap.DestinationType);
                    var binder = _configurationProvider.Binders.FirstOrDefault(b => b.IsMatch(memberMap, propertyTypeMap, resolvedSource));
                    if (binder == null)
                    {
                        ThrowCannotMap(memberMap, resolvedSource);
                    }
                    var mappedExpression = binder.Build(_configurationProvider, memberMap, propertyTypeMap, propertyRequest, resolvedSource, typePairCount, letPropertyMaps);
                    return mappedExpression == null ?
                        null :
                        memberMap.ValueTransformers
                            .Concat(typeMap.ValueTransformers)
                            .Concat(typeMap.Profile.ValueTransformers)
                            .Where(vt => vt.IsMatch(memberMap))
                            .Aggregate(mappedExpression, (current, vtConfig) => ToType(vtConfig.TransformerExpression.ReplaceParameters(ToType(current, vtConfig.ValueType)), memberMap.DestinationType));
                    ExpressionResolutionResult ResolveSource()
                    {
                        var result = new ExpressionResolutionResult(instanceParameter, request.SourceType);
                        var matchingExpressionConverter = _configurationProvider.ResultConverters.FirstOrDefault(c => c.CanGetExpressionResolutionResult(result, memberMap));
                        if (matchingExpressionConverter == null)
                        {
                            ThrowCannotMap(memberMap, result);
                        }
                        result = matchingExpressionConverter.GetExpressionResolutionResult(result, memberMap, letPropertyMaps);
                        if (memberMap.NullSubstitute != null && result.ResolutionExpression is MemberExpression && (result.Type.IsNullableType() || result.Type == typeof(string)))
                        {
                            return new ExpressionResolutionResult(memberMap.NullSubstitute(result.ResolutionExpression));
                        }
                        return result;
                    }
                }
            }
            NewExpression CreateDestination()
            {
                var ctorExpr = typeMap.CustomCtorExpression;
                if (ctorExpr != null)
                {
                    return (NewExpression)ctorExpr.ReplaceParameters(instanceParameter);
                }
                return typeMap.ConstructorMap?.CanResolve == true ? ConstructorMapping(typeMap.ConstructorMap) : New(typeMap.DestinationTypeToUse);
                NewExpression ConstructorMapping(ConstructorMap constructorMap) => 
                    New(constructorMap.Ctor, constructorMap.CtorParams.Select(map => TryProjectMember(map) ?? Default(map.DestinationType)));
            }
        }
        private static void ThrowCannotMap(IMemberMap propertyMap, ExpressionResolutionResult result)
        {
            var message =
                $"Unable to create a map expression from {propertyMap.SourceMember?.DeclaringType?.Name}.{propertyMap.SourceMember?.Name} ({result.Type}) to {propertyMap.DestinationType.Name}.{propertyMap.DestinationName} ({propertyMap.DestinationType})";
            throw new AutoMapperMappingException(message, null, propertyMap.TypeMap.Types, propertyMap.TypeMap, propertyMap);
        }
        private abstract class ParameterExpressionVisitor : ExpressionVisitor
        {
            public static Expression SetParameters(object parameters, Expression expression)
            {
                var visitor = parameters is ParameterBag dictionary ? (ParameterExpressionVisitor)new ConstantExpressionReplacementVisitor(dictionary) : new ObjectParameterExpressionReplacementVisitor(parameters);
                return visitor.Visit(expression);
            }

            protected abstract Expression GetValue(string name);

            protected override Expression VisitMember(MemberExpression node)
            {
                if(!node.Member.DeclaringType.Has<CompilerGeneratedAttribute>())
                {
                    return base.VisitMember(node);
                }
                var parameterName = node.Member.Name;
                var parameterValue = GetValue(parameterName);
                if(parameterValue == null)
                {
                    const string vbPrefix = "$VB$Local_";
                    if(!parameterName.StartsWith(vbPrefix, StringComparison.Ordinal) || (parameterValue = GetValue(parameterName.Substring(vbPrefix.Length))) == null)
                    {
                        return base.VisitMember(node);
                    }
                }
                return ToType(parameterValue, node.Member.GetMemberType());
            }

            private class ObjectParameterExpressionReplacementVisitor : ParameterExpressionVisitor
            {
                private readonly object _parameters;

                public ObjectParameterExpressionReplacementVisitor(object parameters) => _parameters = parameters;

                protected override Expression GetValue(string name)
                {
                    var matchingMember = _parameters.GetType().GetProperty(name);
                    return matchingMember != null ? Property(Constant(_parameters), matchingMember) : null;
                }
            }
            private class ConstantExpressionReplacementVisitor : ParameterExpressionVisitor
            {
                private readonly ParameterBag _paramValues;

                public ConstantExpressionReplacementVisitor(ParameterBag paramValues) => _paramValues = paramValues;

                protected override Expression GetValue(string name) =>
                    _paramValues.TryGetValue(name, out object parameterValue) ? Constant(parameterValue) : null;
            }
        }
        [EditorBrowsable(EditorBrowsableState.Never)]
        public class FirstPassLetPropertyMaps : LetPropertyMaps
        {
            readonly Stack<PropertyExpression> _currentPath = new Stack<PropertyExpression>();
            readonly List<PropertyPath> _savedPaths = new List<PropertyPath>();
            readonly MemberPath _parentPath;
            public FirstPassLetPropertyMaps(IGlobalConfiguration configurationProvider, MemberPath parentPath) : base(configurationProvider) =>
                _parentPath = parentPath;

            public override Expression GetSubQueryMarker(LambdaExpression letExpression)
            {
                var propertyPath = new PropertyPath(_currentPath.Reverse().ToArray(), letExpression);
                var existingPath = _savedPaths.SingleOrDefault(s => s.IsEquivalentTo(propertyPath));
                if (existingPath.Marker != null)
                {
                    return existingPath.Marker;
                }
                _savedPaths.Add(propertyPath);
                return propertyPath.Marker;
            }

            public override void Push(PropertyExpression propertyExpression) => _currentPath.Push(propertyExpression);

            public override MemberPath GetCurrentPath() => _parentPath.Concat(
                _currentPath.Reverse().Select(p => (p.PropertyMap as PropertyMap)?.DestinationMember).Where(p => p != null));

            public override void Pop() => _currentPath.Pop();

            public override int Count => _savedPaths.Count;

            public override LetPropertyMaps New() => new FirstPassLetPropertyMaps(ConfigurationProvider, GetCurrentPath());

            public override QueryExpressions GetSubQueryExpression(ExpressionBuilder builder, Expression projection, TypeMap typeMap, ExpressionRequest request, ParameterExpression instanceParameter, TypePairCount typePairCount)
            {
                var letMapInfos = _savedPaths.Select(path =>
                new {
                    path.LetExpression,
                    MapFromSource = path.GetSourceExpression(instanceParameter),
                    Property = path.GetPropertyDescription(),
                    path.Marker
                }).ToArray();

                var properties = letMapInfos.Select(m => m.Property).Concat(GetMemberAccessesVisitor.Retrieve(projection, instanceParameter));

                var letType = ProxyGenerator.GetSimilarType(typeof(object), properties);
                TypeMap letTypeMap;
                lock(ConfigurationProvider)
                {
                    letTypeMap = TypeMapFactory.CreateTypeMap(request.SourceType, letType, typeMap.Profile);
                }
                var secondParameter = Parameter(letType, "dtoLet");

                ReplaceSubQueries();

                var letClause = builder.CreateMapExpressionCore(request, instanceParameter, typePairCount, letTypeMap, base.New());
                return new QueryExpressions(Lambda(projection, secondParameter), Lambda(letClause, instanceParameter));

                void ReplaceSubQueries()
                {
                    foreach(var letMapInfo in letMapInfos)
                    {
                        var letProperty = letType.GetProperty(letMapInfo.Property.Name);
                        var letPropertyMap = letTypeMap.FindOrCreatePropertyMapFor(letProperty);
                        letPropertyMap.CustomMapExpression = Lambda(letMapInfo.LetExpression.ReplaceParameters(letMapInfo.MapFromSource), secondParameter);
                        projection = projection.Replace(letMapInfo.Marker, MakeMemberAccess(secondParameter, letProperty));
                    }
                    projection = new ReplaceMemberAccessesVisitor(instanceParameter, secondParameter).Visit(projection);
                }
            }

            class GetMemberAccessesVisitor : ExpressionVisitor
            {
                private readonly Expression _target;

                public List<MemberInfo> Members { get; } = new List<MemberInfo>();

                public GetMemberAccessesVisitor(Expression target)
                {
                    _target = target;
                }

                protected override Expression VisitMember(MemberExpression node)
                {
                    if(node.Expression == _target)
                    {
                        Members.Add(node.Member);
                    }
                    return base.VisitMember(node);
                }

                public static IEnumerable<PropertyDescription> Retrieve(Expression expression, Expression target)
                {
                    var visitor = new GetMemberAccessesVisitor(target);
                    visitor.Visit(expression);
                    return visitor.Members.Select(member => new PropertyDescription(member.Name, member.GetMemberType()));
                }
            }

            class ReplaceMemberAccessesVisitor : ExpressionVisitor
            {
                private readonly Expression _oldObject, _newObject;

                public ReplaceMemberAccessesVisitor(Expression oldObject, Expression newObject)
                {
                    _oldObject = oldObject;
                    _newObject = newObject;
                }

                protected override Expression VisitMember(MemberExpression node)
                {
                    if(node.Expression != _oldObject)
                    {
                        return base.VisitMember(node);
                    }
                    return PropertyOrField(_newObject, node.Member.Name);
                }
            }
        }
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class LetPropertyMaps
    {
        protected LetPropertyMaps(IGlobalConfiguration configurationProvider) => ConfigurationProvider = configurationProvider;

        public virtual Expression GetSubQueryMarker(LambdaExpression letExpression) => null;

        public virtual void Push(PropertyExpression propertyExpression) { }

        public virtual MemberPath GetCurrentPath() => MemberPath.Empty;

        public virtual void Pop() {}

        public virtual int Count => 0;

        public IGlobalConfiguration ConfigurationProvider { get; }

        public virtual LetPropertyMaps New() => new LetPropertyMaps(ConfigurationProvider);

        public virtual QueryExpressions GetSubQueryExpression(ExpressionBuilder builder, Expression projection, TypeMap typeMap, ExpressionRequest request, ParameterExpression instanceParameter, TypePairCount typePairCount)
            => new QueryExpressions(projection, instanceParameter);

        public readonly struct PropertyPath
        {
            public PropertyPath(PropertyExpression[] properties, LambdaExpression letExpression)
            {
                Properties = properties;
                Marker = Default(letExpression.Body.Type);
                LetExpression = letExpression;
            }
            private PropertyExpression[] Properties { get; }
            public Expression Marker { get; }
            public LambdaExpression LetExpression { get; }
            public Expression GetSourceExpression(Expression parameter) => Properties.Take(Properties.Length - 1).Select(p=>p.Expression).Chain(parameter);
            public PropertyDescription GetPropertyDescription() => new PropertyDescription("__" + string.Join("#", Properties.Select(p => p.PropertyMap.DestinationName)), LetExpression.Body.Type);
            internal bool IsEquivalentTo(PropertyPath other) => LetExpression == other.LetExpression && Properties.Length == other.Properties.Length &&
                Properties.Take(Properties.Length - 1).Zip(other.Properties, (left, right) => left.PropertyMap == right.PropertyMap).All(item => item);
        }
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct QueryExpressions
    {
        public QueryExpressions(Expression projection, ParameterExpression parameter) : this(projection == null ? null : Lambda(projection, parameter)) { }
        public QueryExpressions(LambdaExpression projection, LambdaExpression letClause  = null)
        {
            LetClause = letClause;
            Projection = projection;
        }
        public LambdaExpression LetClause { get; }
        public LambdaExpression Projection { get; }
        public bool Empty => Projection == null;
        public QueryExpressions Transform(Func<LambdaExpression, LambdaExpression> func) => new QueryExpressions(func(Projection), func(LetClause));
        public T Chain<T>(T source, Func<T, LambdaExpression, T> select) => 
            LetClause == null ? select(source, Projection) : select(select(source, LetClause), Projection);
    }
    public static class ExpressionBuilderExtensions
    {
        public static Expression<Func<TSource, TDestination>> GetMapExpression<TSource, TDestination>(this IExpressionBuilder expressionBuilder) => 
            (Expression<Func<TSource, TDestination>>) expressionBuilder.GetMapExpression(typeof(TSource), typeof(TDestination), null, Array.Empty<MemberPath>()).Projection;
    }
    public class PropertyExpression
    {
        public PropertyExpression(IMemberMap propertyMap) => PropertyMap = propertyMap;
        public Expression Expression { get; set; }
        public IMemberMap PropertyMap { get; }
    }
}