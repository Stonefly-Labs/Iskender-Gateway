using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis;

/// <summary>
/// Reads a script's reply by the shape the script returns. A reply of any other shape throws an
/// <see cref="UnexpectedRedisReplyException"/> inside <see cref="RedisConnection.RunAsync{T}"/>, so it is classified as
/// <c>core.internal</c> with class <c>Unexpected</c> and event 5010, and no cast failure crosses a port (FR-022).
/// Owned by ISK-0005.
/// </summary>
internal static class RedisReplies
{
    /// <summary>An array of exactly <paramref name="length"/> elements.</summary>
    public static RedisResult[] Array(RedisResult reply, int length) =>
        ArrayOrNil(reply, length) ?? throw new UnexpectedRedisReplyException();

    /// <summary>An array of exactly <paramref name="length"/> elements, or <c>null</c> for a nil reply.</summary>
    public static RedisResult[]? ArrayOrNil(RedisResult reply, int length)
    {
        if (reply is null)
        {
            throw new UnexpectedRedisReplyException();
        }

        if (reply.IsNull)
        {
            return null;
        }

        return reply.Resp2Type == ResultType.Array && (RedisResult[])reply! is { } elements && elements.Length == length
            ? elements
            : throw new UnexpectedRedisReplyException();
    }

    /// <summary>An integer reply.</summary>
    public static long Integer(RedisResult reply) =>
        reply is { IsNull: false, Resp2Type: ResultType.Integer } ? (long)reply : throw new UnexpectedRedisReplyException();

    /// <summary>A flag reply, 0 or 1.</summary>
    public static bool Flag(RedisResult reply) => Integer(reply) switch
    {
        0 => false,
        1 => true,
        _ => throw new UnexpectedRedisReplyException(),
    };

    /// <summary>A bulk string reply, as bytes.</summary>
    public static byte[] Bytes(RedisResult reply) =>
        reply is { IsNull: false, Resp2Type: ResultType.BulkString } ? (byte[])reply! : throw new UnexpectedRedisReplyException();
}

/// <summary>A script reply had a shape its script never returns. It never carries the reply.</summary>
internal sealed class UnexpectedRedisReplyException : Exception
{
    public UnexpectedRedisReplyException()
        : base("The server returned an unexpected script reply.")
    {
    }

    public UnexpectedRedisReplyException(string message)
        : base(message)
    {
    }

    public UnexpectedRedisReplyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
