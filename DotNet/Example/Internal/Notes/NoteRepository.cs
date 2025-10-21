using ColourYourFunctions.Internal.Db;
using ColourYourFunctions.Internal.Notes.Model;
using ColourYourFunctions.Tx;
using Microsoft.EntityFrameworkCore;

namespace ColourYourFunctions.Internal.Notes;

internal sealed class NoteRepository
{
    public async Task<NoteEntity?> GetNoteAsync(
        ITxRead<NoteDbContext> tx,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return await tx.DbContext.Notes.FindAsync([id], cancellationToken);
    }

    public async Task<IReadOnlyCollection<NoteEntity>> FindNotesAsync(
        ITxRead<NoteDbContext> tx,
        string content,
        CancellationToken cancellationToken = default
    )
    {
        return await tx.DbContext.Notes.Where(it => it.Content.Contains(content)).ToListAsync(cancellationToken);
    }

    public NoteEntity Create(
        ITxWrite<NoteDbContext> tx,
        string content,
        int sentiment
    )
    {
        var entity = new NoteEntity { Content = content, Sentiment = sentiment };
        tx.DbContext.Notes.Add(entity);
        return entity;
    }

    public void CreateLog(
        ITxWrite<NoteDbContext> tx,
        NoteEntity note,
        LogType logType
    )
    {
        var entity = new NoteLogEntity
            { Id = note.Id, Content = note.Content, Sentiment = note.Sentiment, LogType = logType };
        tx.DbContext.NoteLogs.Add(entity);
    }
}