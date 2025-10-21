using ColourYourFunctions.Api.Notes.Model;
using ColourYourFunctions.Internal.Db;
using ColourYourFunctions.Internal.Notes;
using ColourYourFunctions.Tx;
using Microsoft.AspNetCore.Mvc;

namespace ColourYourFunctions.Api.Notes;

[ApiController]
internal sealed class NoteController(
    NoteService noteService,
    ITxFactory<NoteDbContext> txFactory) : ControllerBase
{
    [HttpGet("api/notes/{id}")]
    public async Task<NoteDto?> GetNoteAsync([FromQuery] Guid id)
    {
        var note = await txFactory.ExecuteReadAsync(tx =>
                noteService.GetNoteAsync(tx, id, HttpContext.RequestAborted),
            cancellationToken: HttpContext.RequestAborted
        );

        return note switch
        {
            null => null,
            _ => NoteDto.Create(note)
        };
    }

    [HttpGet("api/notes/{id}/alt")]
    public async Task<NoteDto?> GetNoteAlternativeAsync([FromQuery] Guid id)
    {
        //A better alternative to the above, when read/ write is needed
        var note = await noteService.GetNoteAlternativeAsync(txFactory, id, HttpContext.RequestAborted);

        return note switch
        {
            null => null,
            _ => NoteDto.Create(note)
        };
    }

    [HttpGet("api/notes")]
    public async Task<IEnumerable<NoteDto>> FindNotesAsync([FromQuery] string content)
    {
        var notes = await txFactory.ExecuteReadAsync(tx =>
                noteService.FindNotesAsync(tx, content, HttpContext.RequestAborted),
            cancellationToken: HttpContext.RequestAborted
        );

        return notes.Select(NoteDto.Create);
    }

    [HttpPost("api/notes")]
    public async Task<NoteDto> CreateAsync(
        string content
    )
    {
        var notes = await txFactory.ExecuteWriteAsync(tx =>
                noteService.CreateAsync(tx, content, HttpContext.RequestAborted),
            cancellationToken: HttpContext.RequestAborted
        );

        return NoteDto.Create(notes);
    }
}