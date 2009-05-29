using System;
using System.Collections.Generic;

namespace AutoMapper.Mappers
{
    public static class MapperRegistry
    {
        public static Func<IEnumerable<IObjectMapper>> AllMappers = () => new IObjectMapper[]
        {
            new TypeMapMapper(),
            new StringMapper(),
            new FlagsEnumMapper(),
            new EnumMapper(),
            new ArrayMapper(),
            new DictionaryMapper(),
            new EnumerableMapper(),
            new AssignableMapper(),
            new TypeConverterMapper(),
            new NullableMapper(),
        };
    }
}