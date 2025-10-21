using ColourYourFunctions.Internal.Notes.Model;

namespace ColourYourFunctions.Api.Notes.Model;

public record NoteDto(Guid Id, string Content)
{
    internal static NoteDto Create(NoteEntity entity)
    {
        return new NoteDto(entity.Id, entity.Content);
    }
}