using ColourYourFunctions.Internal.Db;
using ColourYourFunctions.Internal.Notes.Model;
using ColourYourFunctions.Tx;

namespace ColourYourFunctions.Internal.Notes;

internal sealed class NoteService(
    NoteRepository noteRepository,
    AiSentimentService sentimentService
)
{
    public async Task<NoteEntity?> GetNoteAsync(
        ITxRead<NoteDbContext> tx,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var note = await noteRepository.GetNoteAsync(tx, id, cancellationToken);

        // Uncomment to see the compile time error:
        // if (note != null)
        // {
        //     noteRepository.CreateLog(tx, note, LogType.Opened);
        // }

        return note;
    }

    //A better alternative to the above, when read/ write is needed
    public async Task<NoteEntity?> GetNoteAlternativeAsync(
        ITxFactory<NoteDbContext> txFactory,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        //Reads first
        var note = await txFactory.ExecuteReadAsync(
            tx => GetNoteAsync(tx, id, cancellationToken),
            cancellationToken: cancellationToken
        );

        //Do some other long-running non tx stuff after the reads....

        //Then writes
        if (note != null)
        {
            await txFactory.ExecuteWrite(
                tx => noteRepository.CreateLog(tx, note, LogType.Opened),
                cancellationToken: cancellationToken
            );
        }

        return note;
    }

    public Task<IReadOnlyCollection<NoteEntity>> FindNotesAsync(
        ITxRead<NoteDbContext> tx,
        string content,
        CancellationToken cancellationToken = default
    ) => noteRepository.FindNotesAsync(tx, content, cancellationToken);

    public async Task<NoteEntity> CreateAsync(
        ITxWrite<NoteDbContext> tx,
        string content,
        CancellationToken cancellationToken = default
    )
    {
        //Long-running must not be run in a Tx, it attributed with [TxNever]
        var sentiment =  await sentimentService.GetSentimentAsync(content);
        
        var note = noteRepository.Create(tx, content, sentiment);
        await tx.SaveChangesAsync(cancellationToken);
        return note;
    }
    
    
}