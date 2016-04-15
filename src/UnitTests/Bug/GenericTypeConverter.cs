﻿using System.Collections;
using System.Collections.Generic;
using Should;
using Xunit;

namespace AutoMapper.UnitTests.Bug
{
    public class GenericTypeConverter : AutoMapperSpecBase
    {
        Destination<int> _destination;
        OtherDestination<int> _otherDestination;
        int _openGenericToNonGenericDestination;
        Destination<int> _nonGenericToOpenGenericDestination;
        OtherDestination<int> _closedGenericToOpenGenericDestination;
        Destination<object> _openGenericToClosedGenericDestination;

        public class Source<T>
        {
            public T Value { get; set; }
        }

        public class Destination<T>
        {
            public T Value { get; set; }
        }

        public class OtherSource<T>
        {
            public T Value { get; set; }
        }

        public class OtherDestination<T>
        {
            public T Value { get; set; }
        }

        public class Converter<T> :
            ITypeConverter<Source<T>, Destination<T>>,
            ITypeConverter<OtherSource<T>, OtherDestination<T>>,
            ITypeConverter<Source<T>, int>,
            ITypeConverter<int, Destination<T>>,
            ITypeConverter<OtherSource<T>, Destination<object>>,
            ITypeConverter<Source<object>, OtherDestination<T>>
        {
            public static Destination<T> SomeDestination = new Destination<T>();
            public static OtherDestination<T> SomeOtherDestination = new OtherDestination<T>();
            public static int NongenericDestination = default(int);
            public static OtherDestination<T> OpenDestinationViaClosedSource = new OtherDestination<T>();
            public static Destination<object> ClosedDestinationViaOpenSource = new Destination<object>();

            public Destination<T> Convert(Source<T> source, ResolutionContext context)
            {
                return SomeDestination;
            }

            OtherDestination<T> ITypeConverter<OtherSource<T>, OtherDestination<T>>.Convert(OtherSource<T> source, ResolutionContext context)
            {
                return SomeOtherDestination;
            }

            int ITypeConverter<Source<T>, int>.Convert(Source<T> source, ResolutionContext context)
            {
                return NongenericDestination;
            }

            Destination<T> ITypeConverter<int, Destination<T>>.Convert(int source, ResolutionContext context)
            {
                return SomeDestination;
            }

            Destination<object> ITypeConverter<OtherSource<T>, Destination<object>>.Convert(OtherSource<T> source, ResolutionContext context)
            {
                return ClosedDestinationViaOpenSource;
            }

            OtherDestination<T> ITypeConverter<Source<object>, OtherDestination<T>>.Convert(Source<object> source, ResolutionContext context)
            {
                return OpenDestinationViaClosedSource;
            }
        }

        public class Converter<T1, T2> : ITypeConverter<Hashtable, IReadOnlyDictionary<T1, T2>>

        {
            public static IReadOnlyDictionary<T1, T2> ReadOnlyDictionaryDestination = new Dictionary<T1, T2>();

            public IReadOnlyDictionary<T1, T2> Convert(Hashtable source, ResolutionContext context)
            {
                return ReadOnlyDictionaryDestination;
            }
        }

        protected override MapperConfiguration Configuration { get; } = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap(typeof (Source<>), typeof (Destination<>)).ConvertUsing(typeof (Converter<>));
            cfg.CreateMap(typeof (OtherSource<>), typeof (OtherDestination<>)).ConvertUsing(typeof (Converter<>));
            cfg.CreateMap(typeof (Source<>), typeof (int)).ConvertUsing(typeof (Converter<>));
            cfg.CreateMap(typeof (int), typeof (Destination<>)).ConvertUsing(typeof (Converter<>));
            cfg.CreateMap(typeof (OtherSource<>), typeof (Destination<object>)).ConvertUsing(typeof (Converter<>));
            cfg.CreateMap(typeof (Source<int>), typeof (OtherDestination<>)).ConvertUsing(typeof (Converter<>));

            cfg.CreateMap(typeof (Hashtable), typeof (IReadOnlyDictionary<,>)).ConvertUsing(typeof (Converter<,>));
        });

        protected override void Because_of()
        {
            _destination = Mapper.Map<Destination<int>>(new Source<int>());
            _otherDestination = Mapper.Map<OtherDestination<int>>(new OtherSource<int>());
            _openGenericToNonGenericDestination = Mapper.Map<int>(new Source<int>());
            _nonGenericToOpenGenericDestination = Mapper.Map<Destination<int>>(default(int));
            _openGenericToClosedGenericDestination = Mapper.Map<Destination<object>>(new OtherSource<int>());
            _closedGenericToOpenGenericDestination = Mapper.Map<OtherDestination<int>>(new Source<object>());
        }

        [Fact]
        public void Should_use_generic_converter_with_correct_interface()
        {
            _destination.ShouldBeSameAs(Converter<int>.SomeDestination);
            _otherDestination.ShouldBeSameAs(Converter<int>.SomeOtherDestination);
            _openGenericToNonGenericDestination.ShouldEqual(Converter<int>.NongenericDestination);
            _nonGenericToOpenGenericDestination.ShouldBeSameAs(Converter<int>.SomeDestination);
            _openGenericToClosedGenericDestination.ShouldEqual(Converter<int>.ClosedDestinationViaOpenSource);
            _closedGenericToOpenGenericDestination.ShouldEqual(Converter<int>.OpenDestinationViaClosedSource);
        }

        [Fact]
        public void Should_use_generic_converter_when_covered_by_object_map()
        {
            Mapper.Map<IReadOnlyDictionary<int, int>>(new Hashtable()).ShouldBeSameAs(Converter<int, int>.ReadOnlyDictionaryDestination);
        }
    }
}