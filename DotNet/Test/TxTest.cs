using System.Data;
using ColourYourFunctions.Internal;
using ColourYourFunctions.Internal.Db;
using ColourYourFunctions.Internal.Notes;
using ColourYourFunctions.Tx;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ColourYourFunctions.Test;

[TestFixture]
public class TxTest
{
    [OneTimeSetUp]
    public void Setup()
    {
        File.Delete("test.db");

        _provider = new ServiceCollection()
            .AddLogging()
            .Wire("Data Source=test.db;Cache=Shared;Mode=ReadWriteCreate")
            .AddSingleton(new TxConfiguration(
                TimeSpan.FromSeconds(10),
                TestRetries: 1,
                DefaultIsolationLevel: IsolationLevel.ReadUncommitted))
            .BuildServiceProvider();

        using var db = _provider.GetRequiredService<NoteDbContext>();
        db.Database.EnsureCreated();
    }

    private ServiceProvider? _provider;
    private ServiceProvider Provider => _provider ?? throw new InvalidOperationException("Setup not called");

    [Test(Description = "Test concurrency, tx stack")]
    public void TestConcurrency()
    {
        var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        var cancellationToken = cancellationSource.Token;
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();

        var tasks = Enumerable.Range(1, 10)
            .Select(_ => factory.ExecuteWriteAsync(_ => factory.ExecuteReadAsync(async _ =>
            {
                await Task.Delay(100, cancellationToken);
                return true;
            }, cancellationToken: cancellationToken), cancellationToken: cancellationToken));

        Task.WaitAll(tasks.ToArray<Task>(), cancellationToken);
    }

    [Test(Description = "Assert tx-read tx-repeat")]
    public async Task CanRead()
    {
        var service = Provider.GetRequiredService<NoteService>();
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();

        var list = await factory.ExecuteReadAsync(async tx =>
            await service.FindNotesAsync(tx, "Assert tx-read"));

        Assert.That(list.Count, Is.EqualTo(0));
    }

    [Test(Description = "Assert tx-write tx-repeat tx-atomic")]
    public async Task CanWrite()
    {
        var service = Provider.GetRequiredService<NoteService>();
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();

        _ = await factory.ExecuteWriteAsync(async tx =>
            await service.CreateAsync(tx, "Assert tx-write"));

        var list = await factory.ExecuteReadAsync(async tx =>
            await service.FindNotesAsync(tx, "Assert tx-write"));

        Assert.That(list.Count, Is.EqualTo(1));
    }

    [Test(Description = "Can nest reads")]
    public async Task CanNestReads()
    {
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();
        await factory.ExecuteRead(_ => { return factory.ExecuteRead(_ => true); });

        await factory.ExecuteWriteAsync(_ =>
        {
            factory.ExecuteRead(_ => true);
            return Task.CompletedTask;
        });
    }


    //Always fails, can't catch assertion
    [Test(Description = "Assert tx-read tx-never")]
    public async Task AssertTxNeverRead()
    {
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();
        await factory.ExecuteRead(_ =>
        {
            TxAssert.AssertTxNeverAsync();
            return true;
        });

        Assert.Fail("Should not get here, must die with assertion");
    }

    //Always fails, can't catch assertion
    [Test(Description = "Assert tx-write tx-never")]
    public async Task AssertTxNeverWrite()
    {
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();
        await factory.ExecuteWriteAsync(_ =>
        {
            TxAssert.AssertTxNeverAsync();
            return Task.CompletedTask;
        });

        Assert.Fail("Should not get here, must die with assertion");
    }

    //Always fails, can't catch assertion
    [Test(Description = "Assert tx-read tx-never-nest")]
    public async Task AssertTxNeverNestReadWrite()
    {
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();

        await factory.ExecuteReadAsync(async _ =>
        {
            await factory.ExecuteWriteAsync(_ =>
                Task.CompletedTask
            );
            return true;
        });

        Assert.Fail("Should not get here, must die with assertion");
    }

    //Always fails, can't catch assertion
    [Test(Description = "Assert tx-read tx-never-nest")]
    public async Task AssertTxNeverNestWriteReadCommitted()
    {
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();

        await factory.ExecuteWriteAsync(async _ =>
        {
            await factory.ExecuteReadAsync(_ =>
                    Task.FromResult(true)
                , new TxOptions(IsolationLevel: IsolationLevel.ReadCommitted));
            return true;
        });

        Assert.Fail("Should not get here, must die with assertion");
    }

    //Always fails, can't catch assertion
    [Test(Description = "Assert tx-write tx-never-nest")]
    public async Task AssertTxNeverNestWriteWrite()
    {
        var factory = Provider.GetRequiredService<ITxFactory<NoteDbContext>>();

        await factory.ExecuteWriteAsync(async _ => { await factory.ExecuteWriteAsync(_ => Task.CompletedTask); });

        Assert.Fail("Should not get here, must die with assertion");
    }
}