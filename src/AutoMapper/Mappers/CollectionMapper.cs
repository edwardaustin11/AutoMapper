using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Collections.Specialized;
using System.Linq;
namespace AutoMapper.Internal.Mappers
{
    using Execution;
    using static Execution.ExpressionBuilder;
    using static Expression;
    using static ReflectionHelper;
    public class CollectionMapper : IObjectMapper
    {
        public TypePair? GetAssociatedTypes(TypePair context) => new(GetElementType(context.SourceType), GetElementType(context.DestinationType));
        public bool IsMatch(TypePair context) => context.IsCollection();
        public Expression MapExpression(IGlobalConfiguration configuration, ProfileMap profileMap, MemberMap memberMap, Expression sourceExpression, Expression destExpression)
        {
            var destinationType = destExpression.Type;
            if (destinationType.IsArray)
            {
                return ArrayMapper.MapToArray(configuration, profileMap, sourceExpression, destinationType);
            }
            if (destinationType.IsGenericType(typeof(ReadOnlyCollection<>)))
            {
                return MapReadOnlyCollection(typeof(List<>), typeof(ReadOnlyCollection<>));
            }
            if (destinationType.IsGenericType(typeof(ReadOnlyDictionary<,>)) || destinationType.IsGenericType(typeof(IReadOnlyDictionary<,>)))
            {
                return MapReadOnlyCollection(typeof(Dictionary<,>), typeof(ReadOnlyDictionary<,>));
            }
            if (destinationType == sourceExpression.Type && destinationType.Name == nameof(NameValueCollection))
            {
                return CreateNameValueCollection(sourceExpression);
            }
            return MapCollectionCore(destExpression);
            Expression MapReadOnlyCollection(Type genericCollectionType, Type genericReadOnlyCollectionType)
            {
                var destinationTypeArguments = destinationType.GenericTypeArguments;
                var closedCollectionType = genericCollectionType.MakeGenericType(destinationTypeArguments);
                var dict = MapCollectionCore(configuration.Default(closedCollectionType));
                var readOnlyClosedType = destinationType.IsInterface ? genericReadOnlyCollectionType.MakeGenericType(destinationTypeArguments) : destinationType;
                return New(readOnlyClosedType.GetConstructors()[0], dict);
            }
            Expression MapCollectionCore(Expression destExpression)
            {
                var destinationType = destExpression.Type;
                var sourceType = sourceExpression.Type;
                MethodInfo addMethod = null;
                bool isIList = false, mustUseDestination = memberMap is { MustUseDestination: true };
                Type destinationCollectionType = null, destinationElementType = null;
                GetDestinationType();
                var passedDestination = Variable(destExpression.Type, "passedDestination");
                var newExpression = Variable(passedDestination.Type, "collectionDestination");
                var sourceElementType = GetEnumerableElementType(sourceType);
                if (destinationCollectionType == null || (sourceType == sourceElementType && destinationType == destinationElementType))
                {
                    if (destinationType.IsAssignableFrom(sourceType))
                    {
                        return sourceExpression;
                    }
                    throw new NotSupportedException($"Unknown collection. Consider a custom type converter from {sourceType} to {destinationType}.");
                }
                var itemParam = Parameter(sourceElementType, "item");
                var itemExpr = configuration.MapExpression(profileMap, new TypePair(sourceElementType, destinationElementType), itemParam);
                Expression destination, assignNewExpression;
                UseDestinationValue();
                var (variables, statements) = configuration.ScratchPad();
                statements.Add(itemExpr);
                var addCall = Call(destination, addMethod, statements);
                statements.Clear();
                var addItems = ForEach(variables, statements, itemParam, sourceExpression, addCall);
                var overMaxDepth = OverMaxDepth(memberMap?.TypeMap);
                if (overMaxDepth != null)
                {
                    addItems = Condition(overMaxDepth, ExpressionBuilder.Empty, addItems);
                }
                var clearMethod = isIList ? IListClear : destinationCollectionType.GetMethod("Clear");
                statements.Clear();
                variables.Clear();
                variables.Add(newExpression);
                variables.Add(passedDestination);
                var checkContext = CheckContext();
                if (checkContext != null)
                {
                    statements.Add(checkContext);
                }
                statements.Add(Assign(passedDestination, destExpression));
                statements.Add(assignNewExpression);
                statements.Add(Call(destination, clearMethod));
                statements.Add(addItems);
                statements.Add(destination);
                return Block(variables, statements);
                void GetDestinationType()
                {
                    var immutableCollection = !mustUseDestination && destinationType.IsValueType;
                    if (immutableCollection)
                    {
                        return;
                    }
                    destinationCollectionType = destinationType.GetICollectionType();
                    isIList = destExpression.Type.IsListType();
                    if (destinationCollectionType == null)
                    {
                        if (isIList)
                        {
                            destinationCollectionType = typeof(IList);
                            addMethod = IListAdd;
                            destinationElementType = GetEnumerableElementType(destinationType);
                        }
                        else
                        {
                            if (!destinationType.IsInterface)
                            {
                                return;
                            }
                            destinationElementType = GetEnumerableElementType(destinationType);
                            destinationCollectionType = typeof(ICollection<>).MakeGenericType(destinationElementType);
                            destExpression = Convert(mustUseDestination ? destExpression : Null, destinationCollectionType);
                            addMethod = destinationCollectionType.GetMethod("Add");
                        }
                    }
                    else
                    {
                        destinationElementType = destinationCollectionType.GenericTypeArguments[0];
                        addMethod = destinationCollectionType.GetMethod("Add");
                    }
                }
                void UseDestinationValue()
                {
                    if (mustUseDestination)
                    {
                        destination = passedDestination;
                        assignNewExpression = ExpressionBuilder.Empty;
                    }
                    else
                    {
                        destination = newExpression;
                        var ctor = ObjectFactory.GenerateConstructorExpression(passedDestination.Type, configuration);
                        assignNewExpression = Assign(newExpression, Coalesce(passedDestination, ctor));
                    }
                }
                Expression CheckContext()
                {
                    var elementTypeMap = configuration.ResolveTypeMap(sourceElementType, destinationElementType);
                    return elementTypeMap == null ? null : ExpressionBuilder.CheckContext(elementTypeMap);
                }
            }
        }
        private static Expression CreateNameValueCollection(Expression sourceExpression) =>
            New(typeof(NameValueCollection).GetConstructor(new[] { typeof(NameValueCollection) }), sourceExpression);
        static class ArrayMapper
        {
            private static readonly MethodInfo ToArrayMethod = typeof(Enumerable).GetStaticMethod("ToArray");
            private static readonly MethodInfo CopyToMethod = typeof(Array).GetMethod("CopyTo", new[] { typeof(Array), typeof(int) });
            private static readonly MethodInfo CountMethod = typeof(Enumerable).StaticGenericMethod("Count", parametersCount: 1);
            private static readonly MethodInfo MapMultidimensionalMethod = typeof(ArrayMapper).GetStaticMethod(nameof(MapMultidimensional));
            private static readonly ParameterExpression Index = Variable(typeof(int), "destinationArrayIndex");
            private static readonly BinaryExpression ResetIndex = Assign(Index, Zero);
            private static readonly UnaryExpression[] IncrementIndex = new[] { PostIncrementAssign(Index) };
            private static Array MapMultidimensional(Array source, Type destinationElementType, ResolutionContext context)
            {
                var sourceElementType = source.GetType().GetElementType();
                var destinationArray = Array.CreateInstance(destinationElementType, Enumerable.Range(0, source.Rank).Select(source.GetLength).ToArray());
                var filler = new MultidimensionalArrayFiller(destinationArray);
                foreach (var item in source)
                {
                    filler.NewValue(context.Map(item, null, sourceElementType, destinationElementType, null));
                }
                return destinationArray;
            }
            public static Expression MapToArray(IGlobalConfiguration configuration, ProfileMap profileMap, Expression sourceExpression, Type destinationType)
            {
                var destinationElementType = destinationType.GetElementType();
                if (destinationType.GetArrayRank() > 1)
                {
                    return Call(MapMultidimensionalMethod, sourceExpression, Constant(destinationElementType), ContextParameter);
                }
                var sourceType = sourceExpression.Type;
                Type sourceElementType = typeof(object);
                Expression createDestination;
                var destination = Parameter(destinationType, "destinationArray");
                var (variables, statements) = configuration.ScratchPad();
                if (sourceType.IsArray)
                {
                    var mapFromArray = MapFromArray();
                    if (mapFromArray != null)
                    {
                        return mapFromArray;
                    }
                }
                else
                {
                    var mapFromIEnumerable = MapFromIEnumerable();
                    if (mapFromIEnumerable != null)
                    {
                        return mapFromIEnumerable;
                    }
                    var count = Call(CountMethod.MakeGenericMethod(sourceElementType), sourceExpression);
                    statements.Add(count);
                    createDestination = Assign(destination, NewArrayBounds(destinationElementType, statements));
                }
                var itemParam = Parameter(sourceElementType, "sourceItem");
                var itemExpr = configuration.MapExpression(profileMap, new TypePair(sourceElementType, destinationElementType), itemParam);
                var setItem = Assign(ArrayAccess(destination, IncrementIndex), itemExpr);
                variables.Clear();
                statements.Clear();
                var forEach = ForEach(variables, statements, itemParam, sourceExpression, setItem);
                variables.Clear();
                statements.Clear();
                variables.Add(destination);
                variables.Add(Index);
                statements.Add(createDestination);
                statements.Add(ResetIndex);
                statements.Add(forEach);
                statements.Add(destination);
                return Block(variables, statements);
                Expression MapFromArray()
                {
                    sourceElementType = sourceType.GetElementType();
                    statements.Add(ArrayLength(sourceExpression));
                    createDestination = Assign(destination, NewArrayBounds(destinationElementType, statements));
                    if (MustMap(sourceElementType, destinationElementType))
                    {
                        return null;
                    }
                    variables.Clear();
                    statements.Clear();
                    variables.Add(destination);
                    statements.Add(createDestination);
                    statements.Add(Call(sourceExpression, CopyToMethod, destination, Zero));
                    statements.Add(destination);
                    return Block(variables, statements);
                }
                Expression MapFromIEnumerable()
                {
                    var iEnumerableType = sourceType.GetIEnumerableType();
                    if (iEnumerableType == null || MustMap(sourceElementType = iEnumerableType.GenericTypeArguments[0], destinationElementType))
                    {
                        return null;
                    }
                    return Call(ToArrayMethod.MakeGenericMethod(sourceElementType), sourceExpression);
                }
                bool MustMap(Type sourceType, Type destinationType) => !destinationType.IsAssignableFrom(sourceType) || 
                    configuration.FindTypeMapFor(sourceType, destinationType) != null;
            }
        }
    }
    public class MultidimensionalArrayFiller
    {
        private readonly int[] _indices;
        private readonly Array _destination;
        public MultidimensionalArrayFiller(Array destination)
        {
            _indices = new int[destination.Rank];
            _destination = destination;
        }
        public void NewValue(object value)
        {
            var dimension = _destination.Rank - 1;
            var changedDimension = false;
            while (_indices[dimension] == _destination.GetLength(dimension))
            {
                _indices[dimension] = 0;
                dimension--;
                if (dimension < 0)
                {
                    throw new InvalidOperationException("Not enough room in destination array " + _destination);
                }
                _indices[dimension]++;
                changedDimension = true;
            }
            _destination.SetValue(value, _indices);
            if (changedDimension)
            {
                _indices[dimension + 1]++;
            }
            else
            {
                _indices[dimension]++;
            }
        }
    }
}