using Microsoft.EntityFrameworkCore;
using IsolationLevel = System.Data.IsolationLevel;

namespace ColourYourFunctions.Tx;

/// <summary>
///     A transaction read participant.
///     Asserts tx-read, tx-repeat, tx-never-nest.
/// </summary>
public interface ITxRead<out TDbContext> where TDbContext : DbContext
{
    TDbContext DbContext { get; }
}

/// <summary>
///     A transaction write participant.
///     Asserts tx-write, tx-atomic, tx-repeat, tx-never-nest.
/// </summary>
public interface ITxWrite<out TDbContext> : ITxRead<TDbContext> where TDbContext : DbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Global configuration for transactions.
/// </summary>
/// <param name="DefaultTimeout">Default timeout applied to transactions when no option overrides it.</param>
/// <param name="RetryCount">Default retry count used for transient database errors.</param>
/// <param name="TestRetries">When > 0, forces additional test retries by throwing TestRetryException.</param>
/// <param name="DefaultIsolationLevel">Default isolation level to use for new transactions.</param>
public record TxConfiguration(
    TimeSpan DefaultTimeout,
    int RetryCount = 3,
    int TestRetries = 0,
    IsolationLevel DefaultIsolationLevel = IsolationLevel.ReadCommitted
);

/// <summary>
///     Per-call transaction options that override global configuration.
/// </summary>
/// <param name="Timeout">Cancellation timeout for this transaction call.</param>
/// <param name="RetryCount">How many times to retry on a transient DbException.</param>
/// <param name="IsolationLevel">Optional isolation level override for this transaction.</param>
public record TxOptions(TimeSpan? Timeout = null, int? RetryCount = null, IsolationLevel? IsolationLevel = null);

/// <summary>
///     The function may never be called in a transaction.
///     Asserts tx-never.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TxNeverAttribute : Attribute;

/// <summary>
///     A nested write transaction is not allowed.
///     Asserts tx-never-nest.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TxNeverNestAttribute : Attribute;

public interface ITxReadonlyFactory<out TDbContext> where TDbContext : DbContext
{
    /// <summary>
    ///     Start a Read transaction. Nested writes are not allowed.
    ///     Asserts tx-read, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    Task<T> ExecuteReadAsync<T>(
        Func<ITxRead<TDbContext>, Task<T>> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Start a Read transaction. Nested writes are not allowed.
    ///     Asserts tx-read, tx-atomic, tx-repeat, tx-never-nest.
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
    ///     Start a Read/Writes transaction. Nested writes are not allowed.
    ///     Asserts tx-write, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    [TxNeverNest]
    Task<T> ExecuteWriteAsync<T>(
        Func<ITxWrite<TDbContext>, Task<T>> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Start a Read/Writes transaction. Nested writes are not allowed.
    ///     Asserts tx-write, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    [TxNeverNest]
    Task ExecuteWriteAsync(
        Func<ITxWrite<TDbContext>, Task> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Start a Read/Writes transaction. Nested writes are not allowed.
    ///     Asserts tx-write, tx-atomic, tx-repeat, tx-never-nest.
    /// </summary>
    [TxNeverNest]
    Task ExecuteWrite(
        Action<ITxWrite<TDbContext>> func,
        TxOptions? options = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
///     Abstraction for enforcing transaction assertions in the current execution context.
/// </summary>
public interface ITxAsserter
{
    /// <summary>
    ///     Asserts that the current execution is not inside a transaction (tx-never).
    /// </summary>
    Task AssertTxNeverAsync();

    /// <summary>
    ///     Asserts that a transaction does not violate nested write/read rules (tx-never-nest).
    /// </summary>
    /// <param name="options">Transaction options in effect.</param>
    /// <param name="isReadOnly">Whether the requested transaction is read-only.</param>
    /// <param name="isolationLevel">The isolation level for the requested transaction.</param>
    Task AssertTxNeverNestAsync(TxOptions options, bool isReadOnly, IsolationLevel isolationLevel);
}

/// <summary>
///     Entry point for transaction assertion hooks. A host can install a custom asserter.
/// </summary>
public static class TxAssert
{
    private static volatile ITxAsserter? _asserter;

    /// <summary>
    ///     Installs the concrete assertion implementation.
    /// </summary>
    public static void InstallAsserter(ITxAsserter asserter)
    {
        _asserter = asserter;
    }

    /// <summary>
    ///     Asserts that the current execution is not inside a transaction (tx-never).
    /// </summary>
    public static Task AssertTxNeverAsync()
    {
        return _asserter?.AssertTxNeverAsync() ?? Task.CompletedTask;
    }

    /// <summary>
    ///     Asserts that a transaction does not violate nested write/read rules (tx-never-nest).
    /// </summary>
    public static Task AssertTxNeverNestAsync(TxOptions options, bool isReadOnly, IsolationLevel isolationLevel)
    {
        return _asserter?.AssertTxNeverNestAsync(options, isReadOnly, isolationLevel) ?? Task.CompletedTask;
    }
}