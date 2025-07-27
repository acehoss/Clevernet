using System;
using System.ComponentModel;
using System.Reflection;

namespace CleverBot.Helpers;

public static class EnumHelper
{
    public static string GetDescription(this Enum enumValue)
    {
        var fieldInfo = enumValue.GetType().GetField(enumValue.ToString());
        if (fieldInfo != null)
        {
            var descriptionAttribute = fieldInfo.GetCustomAttribute<DescriptionAttribute>();
            if (descriptionAttribute != null)
            {
                return descriptionAttribute.Description;
            }
        }
        return enumValue.ToString();
    }
}