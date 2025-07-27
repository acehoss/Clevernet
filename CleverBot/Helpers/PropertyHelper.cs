using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;

namespace CleverBot.Helpers;

public static class PropertyHelper
{
    public static string GetJsonPropertyName<T, TProp>(this T obj, Expression<Func<T, TProp>> propertyLambda)
    {
        if (propertyLambda.Body is not MemberExpression memberExpression || memberExpression.Member is not PropertyInfo propertyInfo)
            throw new ArgumentException("Expression must represent a property.", nameof(propertyLambda));

        var jsonPropertyNameAttribute = propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
        return jsonPropertyNameAttribute?.Name ?? propertyInfo.Name;
    }
}