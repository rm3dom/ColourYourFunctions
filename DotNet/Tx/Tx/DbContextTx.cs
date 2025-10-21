using System.Collections.Immutable;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IsolationLevel = System.Data.IsolationLevel;

namespace ColourYourFunctions.Tx;

/// <summary>
///     Default ITxFactory implementation using an EF Core DbContext per transaction attempt.
///     Provides retry, timeout and isolation-level handling and enforces tx-never-nest rules.
/// </summary>
/// <typeparam name="TDbContext">The EF Core DbContext type used for the transaction scope.</typeparam>
public class DbContextTx<TDbContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<DbContextTx<TDbContext>> logger,
    TxConfiguration? configuration = null
) : ITxFactory<TDbContext> where TDbContext : DbContext
{
    private static readonly AsyncLocal<ImmutableStack<Tx>> TxStack = new();

    static DbContextTx()
    {
        TxAssert.InstallAsserter(new TxAsserter());
    }

    /// <summary>
    ///     Executes the provided function inside a read-only transaction.
    ///     Nested writes are not allowed and will assert via TxAssert.
    /// </summary>
    /// <typeparam name="T">Return type.</typeparam>
    /// <param name="func">The function to execute with a transactional read participant.</param>
    /// <param name="options">Optional transaction options (timeout, retries, isolation level).</param>
    /// <param name="cancellationToken">Cancellation token for the overall operation.</param>
    /// <returns>The result of the function.</returns>
    public async Task<T> ExecuteReadAsync<T>(Func<ITxRead<TDbContext>, Task<T>> func, TxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(func, options, true, cancellationToken);
    }

    /// <summary>
    ///     Executes the provided function inside a read-only transaction.
    ///     Synchronous helper that wraps the function into a Task.
    /// </summary>
    /// <typeparam name="T">Return type.</typeparam>
    /// <param name="func">The function to execute with a transactional read participant.</param>
    /// <param name="options">Optional transaction options (timeout, retries, isolation level).</param>
    /// <param name="cancellationToken">Cancellation token for the overall operation.</param>
    /// <returns>The result of the function.</returns>
    public async Task<T> ExecuteRead<T>(Func<ITxRead<TDbContext>, T> func, TxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(tx => Task.FromResult(func(tx)), options, true, cancellationToken);
    }

    /// <summary>
    ///     Executes the provided function inside a read/write transaction.
    ///     Retries on transient DbException up to the configured retry count.
    /// </summary>
    /// <typeparam name="T">Return type.</typeparam>
    /// <param name="func">The function to execute with a transactional write participant.</param>
    /// <param name="options">Optional transaction options (timeout, retries, isolation level).</param>
    /// <param name="cancellationToken">Cancellation token for the overall operation.</param>
    /// <returns>The result of the function.</returns>
    public Task<T> ExecuteWriteAsync<T>(Func<ITxWrite<TDbContext>, Task<T>> func, TxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(func, options, false, cancellationToken);
    }

    /// <summary>
    ///     Executes the provided function inside a read/write transaction.
    ///     Convenience overload for functions returning Task.
    /// </summary>
    /// <param name="func">The function to execute with a transactional write participant.</param>
    /// <param name="options">Optional transaction options (timeout, retries, isolation level).</param>
    /// <param name="cancellationToken">Cancellation token for the overall operation.</param>
    public async Task ExecuteWriteAsync(Func<ITxWrite<TDbContext>, Task> func, TxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async tx =>
        {
            await func(tx);
            return Task.FromResult(true);
        }, options, false, cancellationToken);
    }


    /// <summary>
    ///     Executes the provided action inside a read/write transaction.
    ///     Synchronous helper that wraps the action into a Task.
    /// </summary>
    /// <param name="func">The action to execute with a transactional write participant.</param>
    /// <param name="options">Optional transaction options (timeout, retries, isolation level).</param>
    /// <param name="cancellationToken">Cancellation token for the overall operation.</param>
    public async Task ExecuteWrite(Action<ITxWrite<TDbContext>> func, TxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(tx =>
        {
            func(tx);
            return Task.FromResult(true);
        }, options, false, cancellationToken);
    }

    private void PushTx(Tx tx)
    {
        var stack = TxStack.Value ?? ImmutableStack<Tx>.Empty;
        TxStack.Value = stack.Push(tx);
    }

    private void PopTx(Tx tx)
    {
        var stack = TxStack.Value;

        Debug.Assert(stack != null, "Tx stack null");

        var popped = stack.Pop(out var topTx);

        Debug.Assert(tx == topTx, "Invalid tx at top of stack");

        TxStack.Value = popped;
    }

    private async Task<T> ExecuteAsync<T>(
        Func<ITxWrite<TDbContext>, Task<T>> func,
        TxOptions? options,
        bool readOnly,
        CancellationToken cancellationToken)
    {
        var isolationLevel = options?.IsolationLevel ??
                             configuration?.DefaultIsolationLevel ?? IsolationLevel.ReadCommitted;
        var timeout = options?.Timeout ?? configuration?.DefaultTimeout ?? TimeSpan.FromSeconds(30);
        var numRetries = options?.RetryCount ?? configuration?.RetryCount ?? 3;

        var txOptions = options ?? new TxOptions(
            timeout,
            numRetries,
            isolationLevel
        );

        await TxAssert.AssertTxNeverNestAsync(txOptions, readOnly, isolationLevel);

        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationSource.CancelAfter(timeout);
        var token = cancellationSource.Token;

        var retryCount = 0;
        var testRetries = 0;
        while (true)
        {
            Tx? dbTx = null;
            try
            {
                //Get a new DbContext for each retry
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
                dbTx = new Tx(dbContext, txOptions, readOnly);
                PushTx(dbTx);

                retryCount++;
                testRetries++;
                await using var tx = await dbContext.Database.BeginTransactionAsync(isolationLevel, token);
                try
                {
                    var res = await func(dbTx);
                    if (!readOnly) await dbContext.SaveChangesAsync(token);

                    if (configuration is { TestRetries: > 0 } && testRetries <= configuration.TestRetries)
                        throw new TestRetryException();

                    if (!readOnly)
                        await tx.CommitAsync(token);
                    else
                        await tx.RollbackAsync(CancellationToken.None);

                    return res;
                }
                catch (Exception)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
            catch (DbException ex)
            {
                logger.LogError(ex, "Transaction error: {Message}", ex.Message);
                if (retryCount > numRetries) throw;
            }
            finally
            {
                if (dbTx != null) PopTx(dbTx);
            }
        }
    }

    private sealed record Tx(TDbContext DbContext, TxOptions Options, bool ReadOnly) : ITxWrite<TDbContext>
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class TxAsserter : ITxAsserter
    {
        public Task AssertTxNeverAsync()
        {
            var stack = TxStack.Value;
            Debug.Assert(stack == null || stack.IsEmpty, "TxNever");
            return Task.CompletedTask;
        }

        public Task AssertTxNeverNestAsync(TxOptions options, bool isReadOnly, IsolationLevel isolationLevel)
        {
            var stack = TxStack.Value;
            if (stack == null || stack.IsEmpty) return Task.CompletedTask;

            var top = stack.Peek();

            if (top.ReadOnly && !isReadOnly) Debug.Assert(false, "TxNeverNest read -> write");

            if (!top.ReadOnly && !isReadOnly) Debug.Assert(false, "TxNeverNest write -> write");

            if (!top.ReadOnly && isolationLevel == IsolationLevel.ReadCommitted)
                Debug.Assert(false, "TxNeverNest write -> ReadCommitted");

            return Task.CompletedTask;
        }
    }
}

/// <summary>
///     Exception thrown intentionally to simulate retries during testing.
///     Only used when TxConfiguration.TestRetries is configured to a value > 0.
/// </summary>
public sealed class TestRetryException(string message, Exception? innerException)
    : DbException(message, innerException)
{
    /// <summary>
    ///     Creates a new TestRetryException with a default message.
    /// </summary>
    public TestRetryException() : this("Test retry", null)
    {
    }

    /// <summary>
    ///     Creates a new TestRetryException with a custom message.
    /// </summary>
    public TestRetryException(string message) : this(message, null)
    {
    }
}