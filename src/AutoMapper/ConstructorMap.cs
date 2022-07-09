﻿using AutoMapper.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
namespace AutoMapper
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class ConstructorMap
    {
        private bool? _canResolve;
        private readonly Dictionary<string, ConstructorParameterMap> _ctorParams = new(StringComparer.OrdinalIgnoreCase);
        public ConstructorInfo Ctor { get; }
        public TypeMap TypeMap { get; }
        public IReadOnlyCollection<ConstructorParameterMap> CtorParams => _ctorParams.Values;
        public ConstructorMap(ConstructorInfo ctor, TypeMap typeMap)
        {
            Ctor = ctor;
            TypeMap = typeMap;
        }
        public bool CanResolve
        {
            get => _canResolve ??= ParametersCanResolve();
            set => _canResolve = value;
        }
        private bool ParametersCanResolve()
        {
            foreach (var param in _ctorParams.Values)
            {
                if (!param.CanResolveValue)
                {
                    return false;
                }
            }
            return true;
        }
        public ConstructorParameterMap this[string name] => _ctorParams.GetValueOrDefault(name);
        public void AddParameter(ParameterInfo parameter, IEnumerable<MemberInfo> sourceMembers, bool canResolve)
        {
            if (parameter.Name == null)
            {
                return;
            }
            _ctorParams.Add(parameter.Name, new ConstructorParameterMap(TypeMap, parameter, sourceMembers.ToArray(), canResolve));
        }
        public bool ApplyIncludedMember(IncludedMember includedMember)
        {
            var typeMap = includedMember.TypeMap;
            if (CanResolve || typeMap.ConstructorMap == null)
            {
                return false;
            }
            bool canResolve = false;
            foreach (var includedParam in typeMap.ConstructorMap._ctorParams.Values)
            {
                if (!includedParam.CanResolveValue)
                {
                    continue;
                }
                var name = includedParam.DestinationName;
                if (_ctorParams.TryGetValue(name, out var existingParam) && existingParam.CanResolveValue)
                {
                    continue;
                }
                canResolve = true;
                _canResolve = null;
                _ctorParams[name] = new ConstructorParameterMap(includedParam, includedMember);
            }
            return canResolve;
        }
    }
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class ConstructorParameterMap : MemberMap
    {
        private Type _sourceType;
        public ConstructorParameterMap(TypeMap typeMap, ParameterInfo parameter, MemberInfo[] sourceMembers, bool canResolveValue) : base(typeMap)
        {
            Parameter = parameter;
            CanResolveValue = canResolveValue;
            if (sourceMembers.Length > 0)
            {
                MapByConvention(sourceMembers);
            }
            else
            {
                SourceMembers = Array.Empty<MemberInfo>();
            }
        }
        public ConstructorParameterMap(ConstructorParameterMap parameterMap, IncludedMember includedMember) : 
            this(includedMember.TypeMap, parameterMap.Parameter, parameterMap.SourceMembers, parameterMap.CanResolveValue) =>
            IncludedMember = includedMember.Chain(parameterMap.IncludedMember);
        public ParameterInfo Parameter { get; }
        public override Type SourceType => _sourceType ??= GetSourceType();
        public override Type DestinationType => Parameter.ParameterType;
        public override MemberInfo[] SourceMembers { get; set; }
        public override string DestinationName => Parameter.Name;
        public override bool CanResolveValue { get; set; }
        public Expression DefaultValue() => Parameter.GetDefaultValue();
        public override string ToString() => Parameter.Member.DeclaringType + "." + Parameter.Member + ".parameter " + Parameter.Name;
    }
}