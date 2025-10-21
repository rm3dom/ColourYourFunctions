namespace ColourYourFunctions.Internal.Notes.Model;

internal sealed class NoteEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = string.Empty;

    public int Sentiment { get; set; }
}