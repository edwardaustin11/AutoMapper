namespace AutoMapper.QueryableExtensions.Impl
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    public class DelegateBasedResolverResultConverter : IExpressionResultConverter
    {
        public ExpressionResolutionResult GetExpressionResolutionResult(
            ExpressionResolutionResult expressionResolutionResult, PropertyMap propertyMap, IValueResolver valueResolver)
        {
            return ExpressionResolutionResult(expressionResolutionResult, (valueResolver as IDelegateResolver).Expression);
        }

        private static ExpressionResolutionResult ExpressionResolutionResult(
            ExpressionResolutionResult expressionResolutionResult, LambdaExpression lambdaExpression)
        {
            var oldParameter = lambdaExpression.Parameters.Single();
            var newParameter = expressionResolutionResult.ResolutionExpression;
            var converter = new ParameterConversionVisitor(newParameter, oldParameter);

            Expression currentChild = converter.Visit(lambdaExpression.Body);
            Type currentChildType = currentChild.Type;

            return new ExpressionResolutionResult(currentChild, currentChildType);
        }

        public ExpressionResolutionResult GetExpressionResolutionResult(ExpressionResolutionResult expressionResolutionResult,
            ConstructorParameterMap propertyMap, IValueResolver valueResolver)
        {
            return ExpressionResolutionResult(expressionResolutionResult, null);
        }

        public bool CanGetExpressionResolutionResult(ExpressionResolutionResult expressionResolutionResult,
            IValueResolver valueResolver)
        {
            return valueResolver is IDelegateResolver;
        }
    }
}