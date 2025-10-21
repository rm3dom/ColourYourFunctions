using ColourYourFunctions.Internal.Db;
using ColourYourFunctions.Internal.Notes;
using ColourYourFunctions.Tx;
using Microsoft.EntityFrameworkCore;

namespace ColourYourFunctions.Internal;

public static class Wiring
{
    public static IServiceCollection
        Wire(this IServiceCollection sc, string connectionString = "Data Source=notes.db") =>
        sc.AddDbContext<NoteDbContext>(builder =>
                builder.UseSqlite(connectionString)
            )
            .AddSingleton<ITxFactory<NoteDbContext>, DbContextTx<NoteDbContext>>()
            .AddSingleton<NoteService>()
            .AddSingleton<NoteRepository>()
            .AddSingleton<AiSentimentService>();
}