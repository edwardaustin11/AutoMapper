using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using AutoMapper.Internal;
namespace AutoMapper.Internal.Mappers
{
    using static ExpressionFactory;

    public class ReadOnlyCollectionMapper : IObjectMapper
    {
        public bool IsMatch(in TypePair context) => context.SourceType.IsCollection() && context.DestinationType.IsGenericType(typeof(ReadOnlyCollection<>));
        public Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap,
            MemberMap memberMap, Expression sourceExpression, Expression destExpression) =>
            MapReadOnlyCollection(typeof(List<>), typeof(ReadOnlyCollection<>), configurationProvider, profileMap, memberMap, sourceExpression, destExpression);
    }
}