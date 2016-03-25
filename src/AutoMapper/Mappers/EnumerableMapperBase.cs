namespace AutoMapper.Mappers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    public abstract class EnumerableMapperBase<TEnumerable> : IObjectMapper
        where TEnumerable : IEnumerable
    {
        public object Map(ResolutionContext context)
        {
            if (context.IsSourceValueNull && context.Mapper.ShouldMapSourceCollectionAsNull(context))
            {
                return null;
            }

            ICollection<object> enumerableValue = ((IEnumerable) context.SourceValue ?? new object[0])
                .Cast<object>()
                .ToList();

            Type sourceElementType = TypeHelper.GetElementType(context.SourceType, enumerableValue);
            Type destElementType = TypeHelper.GetElementType(context.DestinationType);

            // If you can just assign the collection from one side to the other and the element types don't need to be mapped
            if (ShouldAssignEnumerable(context))
            {
                var elementTypeMap = context.ConfigurationProvider.ResolveTypeMap(sourceElementType, destElementType);
                if (elementTypeMap == null)
                    return context.SourceValue;
            }

            var sourceLength = enumerableValue.Count;
            var destination = GetOrCreateDestinationObject(context, destElementType, sourceLength);
            var enumerable = GetEnumerableFor(destination);

            ClearEnumerable(enumerable);

            int i = 0;
            var itemContext = new ResolutionContext(context);
            foreach (object item in enumerableValue)
            {
                var sourceItemType = item?.GetType() ?? sourceElementType;
                var typeMap = context.ConfigurationProvider.ResolveTypeMap(sourceItemType, destElementType);

                itemContext.Fill(item, null, sourceItemType, destElementType, typeMap);
                var mappedValue = context.Mapper.Map(itemContext);

                SetElementValue(enumerable, mappedValue, i);

                i++;
            }

            object valueToAssign = destination;
            return valueToAssign;
        }

        protected virtual bool ShouldAssignEnumerable(ResolutionContext context)
        {
            return false;
        }

        protected virtual object GetOrCreateDestinationObject(ResolutionContext context, Type destElementType, int sourceLength)
        {
            if (context.DestinationValue != null)
            {
                // If the source is not an array, assume we can add to it...
                if (!(context.DestinationValue is Array))
                    return context.DestinationValue;

                // If the source is an array, ensure that we have enough room...
                var array = (Array) context.DestinationValue;

                if (array.Length >= sourceLength)
                    return context.DestinationValue;
            }

            return CreateDestinationObject(context, destElementType, sourceLength);
        }

        protected virtual TEnumerable GetEnumerableFor(object destination)
        {
            return (TEnumerable) destination;
        }

        protected virtual void ClearEnumerable(TEnumerable enumerable)
        {
        }

        protected virtual object CreateDestinationObject(ResolutionContext context, Type destinationElementType, int count)
        {
            var destinationType = context.DestinationType;

            if (!destinationType.IsInterface() && !destinationType.IsArray)
            {
                return context.Mapper.CreateObject(context);
            }
            return CreateDestinationObjectBase(destinationElementType, count);
        }

        public abstract bool IsMatch(TypePair context);


        protected abstract void SetElementValue(TEnumerable destination, object mappedValue, int index);
        protected abstract TEnumerable CreateDestinationObjectBase(Type destElementType, int sourceLength);
    }
}