using ColourYourFunctions.Internal.Notes.Model;
using Microsoft.EntityFrameworkCore;

namespace ColourYourFunctions.Internal.Db;

internal sealed class NoteDbContext(DbContextOptions options) : DbContext(options)
{   
    public DbSet<NoteEntity> Notes { get; set; }
    public DbSet<NoteLogEntity> NoteLogs { get; set; }
}