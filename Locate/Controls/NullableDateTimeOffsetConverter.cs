using System;
using Microsoft.UI.Xaml.Data;

namespace Locate.Controls;

/// <summary>
/// Bridges <see cref="Nullable{DateTime}"/> on the source (Preset model) and
/// <see cref="Nullable{DateTimeOffset}"/> expected by <c>CalendarDatePicker.Date</c>. The local
/// timezone is used for the offset on display; on write-back the wall-clock time is preserved.
/// Null round-trips as null in either direction.
/// </summary>
public sealed class NullableDateTimeOffsetConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is DateTime dt)
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local));
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language)
    {
        if (value is DateTimeOffset dto)
            return dto.LocalDateTime;
        return null;
    }
}
