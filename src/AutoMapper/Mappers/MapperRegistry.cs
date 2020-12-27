using System.Collections.Generic;
namespace AutoMapper.Internal.Mappers
{
    internal static class MapperRegistry
    {
        /* Mapping order:
         - Nullables
         - Collections
         - Assignable
         - Primitives
         - Converters
         - Conversion operators
         - "Special" cases
         */
        public static List<IObjectMapper> Mappers() => new List<IObjectMapper>
        {
            new NullableSourceMapper(),
            new NullableDestinationMapper(),
            new MultidimensionalArrayMapper(),
            new ArrayCopyMapper(),
            new ArrayMapper(),
            new NameValueCollectionMapper(),
            new ReadOnlyDictionaryMapper(),
            new ReadOnlyCollectionMapper(),
            new CollectionMapper(),
            new AssignableMapper(),
            new StringToEnumMapper(),
            new EnumToStringMapper(),
            new EnumToEnumMapper(),
            new StringMapper(),
            new ConvertMapper(),
            new ParseStringMapper(),
            new KeyValueMapper(),
            new ConstructorMapper(),
            new ConversionOperatorMapper("op_Implicit"),
            new ConversionOperatorMapper("op_Explicit"),
            new FromStringDictionaryMapper(),
            new ToStringDictionaryMapper(),
            new FromDynamicMapper(),
            new ToDynamicMapper(),
            new TypeConverterMapper(),// the most expensive
        };
    }
}