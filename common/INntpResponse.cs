namespace mcnntp.common;

public interface INntpResponse
{
    public int Code { get; }

    public string? Message { get; }

    // NOTE: Implementers must remember to implement the following ToString() implementation.
    // public override string ToString() => $"{Code} {Message}";
}
