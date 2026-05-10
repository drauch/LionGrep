namespace LionGrep.Core;

public enum SizeFilterMode
{
    LessThan,
    GreaterThan,
    Between,
}

public sealed record SizeFilter(SizeFilterMode Mode, long Bytes, long? UpperBytes = null);

public enum DateFilterMode
{
    NewerThan,
    OlderThan,
    ExactlyOn,
    Between,
}

public sealed record DateFilter(DateFilterMode Mode, DateTime From, DateTime? To = null);
