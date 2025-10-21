namespace ColourYourFunctions.Internal.Notes.Model;

internal enum LogType
{
    Opened,
    Updated,
    Deleted
}

internal sealed class NoteLogEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Content { get; set; }
    public LogType LogType { get; set; }

    public int Sentiment { get; set; }
}