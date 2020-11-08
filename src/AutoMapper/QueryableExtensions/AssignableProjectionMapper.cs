using AutoMapper.Internal;
using System.ComponentModel;
using System.Linq.Expressions;

namespace AutoMapper.QueryableExtensions.Impl
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class AssignableProjectionMapper : IProjectionMapper
    {
        public bool IsMatch(IMemberMap memberMap, TypeMap memberTypeMap, Expression resolvedSource)
            => memberMap.DestinationType.IsAssignableFrom(resolvedSource.Type);
        public Expression Project(IGlobalConfiguration configuration, IMemberMap memberMap, TypeMap memberTypeMap, ProjectionRequest request, Expression resolvedSource, LetPropertyMaps letPropertyMaps)
            => ExpressionFactory.ToType(resolvedSource, memberMap.DestinationType);
    }
}