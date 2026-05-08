namespace Locate.Core;

public enum SizeFilterMode
{
    LessThan,
    GreaterThan,
}

public sealed record SizeFilter(SizeFilterMode Mode, long Bytes);

public enum DateFilterMode
{
    NewerThan,
    OlderThan,
    ExactlyOn,
    Between,
}

public sealed record DateFilter(DateFilterMode Mode, DateTime From, DateTime? To = null);
