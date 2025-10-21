using System.Collections.Immutable;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IsolationLevel = System.Data.IsolationLevel;

namespace ColourYourFunctions.Tx;

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

    private sealed record Tx(TDbContext DbContext, TxOptions Options, bool ReadOnly) : ITxWrite<TDbContext>
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => DbContext.SaveChangesAsync(cancellationToken);
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

            if (top.ReadOnly && !isReadOnly)
            {
                Debug.Assert(false, "TxNeverNest read -> write");
            }

            if (!top.ReadOnly && !isReadOnly)
            {
                Debug.Assert(false, "TxNeverNest write -> write");
            }
            
            if (!top.ReadOnly && isolationLevel == IsolationLevel.ReadCommitted)
            {
                Debug.Assert(false, "TxNeverNest write -> ReadCommitted");
            }

            return Task.CompletedTask;
        }
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
        var isolationLevel = options?.IsolationLevel ?? configuration?.DefaultIsolationLevel ?? IsolationLevel.ReadCommitted;
        var timeout = options?.Timeout ?? configuration?.DefaultTimeout ?? TimeSpan.FromSeconds(30);
        var numRetries = options?.RetryCount ?? configuration?.RetryCount ?? 3;
        
        var txOptions = options ?? new TxOptions(
            Timeout: timeout,
            RetryCount: numRetries,
            IsolationLevel: isolationLevel
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
                    if (!readOnly)
                    {
                        await dbContext.SaveChangesAsync(token);
                    }

                    if (configuration is { TestRetries: > 0 } && testRetries <= configuration.TestRetries)
                    {
                        throw new TestRetryException();
                    }

                    if (!readOnly)
                    {
                        await tx.CommitAsync(token);
                    }
                    else
                    {
                        await tx.RollbackAsync(CancellationToken.None);
                    }

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
                if (retryCount > numRetries)
                {
                    throw;
                }
            }
            finally
            {
                if (dbTx != null)
                {
                    PopTx(dbTx);
                }
            }
        }
    }

    public async Task<T> ExecuteReadAsync<T>(Func<ITxRead<TDbContext>, Task<T>> func, TxOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(func, options, readOnly: true, cancellationToken);

    public async Task<T> ExecuteRead<T>(Func<ITxRead<TDbContext>, T> func, TxOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(tx => Task.FromResult(func(tx)), options, readOnly: true, cancellationToken);

    public Task<T> ExecuteWriteAsync<T>(Func<ITxWrite<TDbContext>, Task<T>> func, TxOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(func, options, false, cancellationToken);

    public async Task ExecuteWriteAsync(Func<ITxWrite<TDbContext>, Task> func, TxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async tx =>
        {
            await func(tx);
            return Task.FromResult(true);
        }, options, false, cancellationToken);
    }


    public async Task ExecuteWrite(Action<ITxWrite<TDbContext>> func, TxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(tx =>
        {
            func(tx);
            return Task.FromResult(true);
        }, options, false, cancellationToken);
    }
}

public sealed class TestRetryException(string message, Exception? innerException)
    : DbException(message, innerException)
{
    public TestRetryException() : this("Test retry", null)
    {
    }

    public TestRetryException(string message) : this(message, null)
    {
    }
}