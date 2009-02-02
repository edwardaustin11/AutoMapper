using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AutoMapper
{
	public class PropertyMap
	{
		private readonly LinkedList<IValueResolver> _sourceValueResolvers = new LinkedList<IValueResolver>();
		private readonly IList<Type> _valueFormattersToSkip = new List<Type>();
		private readonly IList<IValueFormatter> _valueFormatters = new List<IValueFormatter>();
		private bool _ignored;
		private bool _hasCustomValueResolver;
		private IValueResolver _customResolver;
		private IValueResolver _customMemberResolver;
		private object _nullSubstitute;

		public PropertyMap(PropertyInfo destinationProperty)
		{
			DestinationProperty = destinationProperty;
		}

		public PropertyInfo DestinationProperty { get; private set; }

		public IEnumerable<IValueResolver> GetSourceValueResolvers()
		{
			yield return new DefaultResolver();

			yield return _customMemberResolver;

			yield return _customResolver;

			foreach (var resolver in _sourceValueResolvers)
			{
				yield return resolver;
			}

			yield return new NullReplacementMethod(_nullSubstitute);
		}

		public void RemoveLastResolver()
		{
			_sourceValueResolvers.RemoveLast();
		}

		public ResolutionResult ResolveValue(object input)
		{
			var result = new ResolutionResult(input);

			foreach (var resolver in GetSourceValueResolvers())
			{
				if (resolver != null)
				{
					result = resolver.Resolve(result);
				}
			}

			return result;
		}

		public void ChainResolver(IValueResolver IValueResolver)
		{
			_sourceValueResolvers.AddLast(IValueResolver);
		}

		public void AddFormatterToSkip<TValueFormatter>() where TValueFormatter : IValueFormatter
		{
			_valueFormattersToSkip.Add(typeof(TValueFormatter));
		}

		public bool FormattersToSkipContains(Type valueFormatterType)
		{
			return _valueFormattersToSkip.Contains(valueFormatterType);
		}

		public void AddFormatter(IValueFormatter valueFormatter)
		{
			_valueFormatters.Add(valueFormatter);
		}

		public IValueFormatter[] GetFormatters()
		{
			return _valueFormatters.ToArray();
		}

		public void AssignCustomValueResolver(IValueResolver valueResolver)
		{
			_customResolver = valueResolver;
			ResetSourceMemberChain();
			_hasCustomValueResolver = true;
		}

		public void ChainTypeMemberForResolver(IValueResolver valueResolver)
		{
			ResetSourceMemberChain();
			_customMemberResolver = valueResolver;
		}

		public void ChainConstructorForResolver(IValueResolver valueResolver)
		{
			_customResolver = valueResolver;
		}

		public void Ignore()
		{
			_ignored = true;
		}

		public bool IsMapped()
		{
			return _sourceValueResolvers.Count > 0 || _hasCustomValueResolver || _ignored;
		}

		public bool CanResolveValue()
		{
			return (_sourceValueResolvers.Count > 0 || _hasCustomValueResolver) && !_ignored;
		}

		public void RemoveLastFormatter()
		{
			_valueFormatters.RemoveAt(_valueFormatters.Count - 1);
		}

		public void SetNullSubstitute(object nullSubstitute)
		{
			_nullSubstitute = nullSubstitute;
		}

		private void ResetSourceMemberChain()
		{
			_sourceValueResolvers.Clear();
		}
	}
}
