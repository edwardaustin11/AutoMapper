using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AutoMapper.Internal;

namespace AutoMapper.Mappers
{
    using Execution;
    using static Expression;
    using static ExpressionFactory;
    public class ArrayMapper : EnumerableMapperBase
    {
        public override bool IsMatch(in TypePair context) => context.DestinationType.IsArray && context.SourceType.IsEnumerableType();

        public override Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap,
            IMemberMap memberMap, Expression sourceExpression, Expression destExpression)
        {
            var sourceElementType = ReflectionHelper.GetElementType(sourceExpression.Type);
            var destinationElementType = destExpression.Type.GetElementType();

            var itemParam = Parameter(sourceElementType, "sourceItem");
            var itemExpr = ExpressionBuilder.MapExpression(configurationProvider, profileMap, new TypePair(sourceElementType, destinationElementType), itemParam);

            //var count = source.Count();
            //var array = new TDestination[count];

            //int i = 0;
            //foreach (var item in source)
            //    array[i++] = newItemFunc(item, context);
            //return array;

            var countParam = Parameter(typeof(int), "count");
            var arrayParam = Parameter(destExpression.Type, "destinationArray");
            var indexParam = Parameter(typeof(int), "destinationArrayIndex");

            var actions = new List<Expression>();
            var parameters = new List<ParameterExpression> { countParam, arrayParam, indexParam };

            var countMethod = typeof(Enumerable)
                .GetTypeInfo()
                .DeclaredMethods
                .Single(mi => mi.Name == "Count" && mi.GetParameters().Length == 1)
                .MakeGenericMethod(sourceElementType);
            actions.Add(Assign(countParam, Call(countMethod, sourceExpression)));
            actions.Add(Assign(arrayParam, NewArrayBounds(destinationElementType, countParam)));
            actions.Add(Assign(indexParam, Constant(0)));
            actions.Add(ForEach(sourceExpression, itemParam,
                Assign(ArrayAccess(arrayParam, PostIncrementAssign(indexParam)), itemExpr)
                ));
            actions.Add(arrayParam);

            return Block(parameters, actions);
        }
    }
}
