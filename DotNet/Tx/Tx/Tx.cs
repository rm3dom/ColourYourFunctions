using Microsoft.EntityFrameworkCore;
using IsolationLevel = System.Data.IsolationLevel;

namespace ColourYourFunctions.Tx;

/// <summary>
/// A transaction read participant.
/// Asserts tx-read, tx-atomic, tx-repeat, tx-never-nest.
/// </summary>
public interface ITxRead<out TDbContext> where TDbContext : DbContext
{
    TDbContext DbContext { get; }
}

/// <summary>
/// A transaction write participant.
/// Asserts tx-write, tx-atomic, tx-repeat, tx-never-nest.
/// </summary>
public interface ITxWrite<out TDbContext> : ITxRead<TDbContext> where TDbContext : DbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Global configuration.
/// </summary>
/// <param name="DefaultTimeout"></param>
/// <param name="TestRetries"></param>
/// <param name="NeverNestReads">Some databases do not allow any nesting</param>
/// <param name="DefaultIsolationLevel"></param>
public record TxConfiguration(
    TimeSpan DefaultTimeout,
    int RetryCount = 3,
    int TestRetries = 0,
    bool NeverNestReads = false,
    System.Data.IsolationLevel DefaultIsolationLevel = System.Data.IsolationLevel.ReadCommitted
);

/// <summary>
/// Transaction options.
/// </summary>
/// <param name="Timeout">Cancellation time out</param>
/// <param name="RetryCount">How many times to retry on a SqlException</param>
public record TxOptions(TimeSpan? Timeout = null, int? RetryCount = null, IsolationLevel? IsolationLevel = null);

/// <summary>
/// The function may never be called in a transaction.
/// Asserts tx-never.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TxNeverAttribute : Attribute;

/// <summary>
/// A nested write transaction is not allowed.
/// Asserts tx-never-nest.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TxNeverNestAttribute : Attribute;

public interface ITxReadonlyFactory<out TDbContext> where TDbContext : DbContext
{
    /// <summary>
    /// Start a Read transaction. Nested writes are not allowed.
    /// Asserts tx-read, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    Task<T> ExecuteReadAsync<T>(
        Func<ITxRead<TDbContext>, Task<T>> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Start a Read transaction. Nested writes are not allowed.
    /// Asserts tx-read, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    Task<T> ExecuteRead<T>(
        Func<ITxRead<TDbContext>, T> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );
}

public interface ITxFactory<out TDbContext> : ITxReadonlyFactory<TDbContext> where TDbContext : DbContext
{
    /// <summary>
    /// Start a Read/Writes transaction. Nested writes are not allowed.
    /// Asserts tx-write, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    [TxNeverNest]
    Task<T> ExecuteWriteAsync<T>(
        Func<ITxWrite<TDbContext>, Task<T>> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Start a Read/Writes transaction. Nested writes are not allowed.
    /// Asserts tx-write, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    [TxNeverNest]
    Task ExecuteWriteAsync(
        Func<ITxWrite<TDbContext>, Task> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Start a Read/Writes transaction. Nested writes are not allowed.
    /// Asserts tx-write, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    [TxNeverNest]
    Task ExecuteWrite(
        Action<ITxWrite<TDbContext>> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );
}

public interface ITxAsserter
{
    Task AssertTxNeverAsync();
    Task AssertTxNeverNestAsync(TxOptions options, bool isReadOnly, IsolationLevel isolationLevel);
}

public static class TxAssert
{
    private static volatile ITxAsserter? _asserter;

    public static void InstallAsserter(ITxAsserter asserter)
    {
        _asserter = asserter;
    }

    public static Task AssertTxNeverAsync() =>
        _asserter?.AssertTxNeverAsync() ?? Task.CompletedTask;

    public static Task AssertTxNeverNestAsync(TxOptions options, bool isReadOnly, IsolationLevel isolationLevel) =>
        _asserter?.AssertTxNeverNestAsync(options, isReadOnly, isolationLevel) ?? Task.CompletedTask;
}