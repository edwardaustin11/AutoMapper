using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace AutoMapper
{
    public interface IMemberMap
    {
        TypeMap TypeMap { get; }
        Type SourceType { get; }
        IEnumerable<MemberInfo> SourceMembers { get; }
        MemberInfo DestinationMember { get; }
        Type DestinationMemberType { get; }
        TypePair Types { get; }
        bool CanResolveValue();
        bool Ignored { get; }
        bool Inline { get; set; }
        bool UseDestinationValue { get; }
        object NullSubstitute { get; }
        LambdaExpression PreCondition { get; }
        LambdaExpression Condition { get; }
        LambdaExpression CustomMapExpression { get; }
        LambdaExpression CustomMapFunction { get; }
        ValueResolverConfiguration ValueResolverConfig { get; }
        ValueConverterConfiguration ValueConverterConfig { get; }
        IEnumerable<ValueTransformerConfiguration> ValueTransformers { get; }
    }

    internal class DefaultMemberMap : IMemberMap
    {
        private DefaultMemberMap() { }

        public static readonly IMemberMap Instance = new DefaultMemberMap();

        public TypeMap TypeMap => default;
        public Type SourceType => default;
        public IEnumerable<MemberInfo> SourceMembers { get; } 
            = Enumerable.Empty<MemberInfo>();
        public MemberInfo DestinationMember => default;
        public Type DestinationMemberType => default;
        public TypePair Types => default;
        public bool CanResolveValue() => default;

        public bool Ignored => default;
        public bool Inline { get; set; } = true;
        public bool UseDestinationValue => default;
        public object NullSubstitute => default;
        public LambdaExpression PreCondition => default;
        public LambdaExpression Condition => default;
        public LambdaExpression CustomMapExpression => default;
        public LambdaExpression CustomMapFunction => default;
        public ValueResolverConfiguration ValueResolverConfig => default;
        public ValueConverterConfiguration ValueConverterConfig => default;

        public IEnumerable<ValueTransformerConfiguration> ValueTransformers { get; } 
            = Enumerable.Empty<ValueTransformerConfiguration>();
    }
}