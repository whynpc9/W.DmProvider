using System;
using W.Dm.Internal.Types;

namespace W.Dm;

/// <summary>Checked input normalization before the selected wire codec runs.</summary>
internal static class DmSysTypeConvertion
{
    internal static string StringConvertion(string value, DmParameter parameter)
    {
        if (value != null && parameter.m_SetSizeFlag && parameter.do_Size > 0 && value.Length > parameter.do_Size)
            throw new OverflowException("Parameter text exceeds its declared size.");
        return value;
    }

    internal static decimal DecimalConvertion(decimal value, DmParameter parameter) => value;

    internal static object TypeConvertion(DmParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        object value = parameter.do_Value;
        parameter.ValidateInputSourceRange(value);
        if (value is null or DBNull) return value;
        if (value is string text) return StringConvertion(text, parameter);
        if (value is char[] chars)
        {
            if (parameter.m_SetSizeFlag && parameter.do_Size > 0 && chars.Length > parameter.do_Size)
                throw new OverflowException("Parameter text exceeds its declared size.");
            return chars;
        }
        if (value is byte[] bytes)
        {
            if (parameter.m_SetSizeFlag && parameter.do_Size > 0 && bytes.Length > parameter.do_Size)
                throw new OverflowException("Parameter binary value exceeds its declared size.");
            return bytes;
        }
        if (value is Enum) return DmNumericInput.ToEnumUnderlying(value);
        if (value is float or double) return DmNumericInput.ValidateFiniteFloating(value);
        return value;
    }
}
