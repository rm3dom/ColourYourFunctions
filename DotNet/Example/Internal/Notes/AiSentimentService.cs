using ColourYourFunctions.Tx;

namespace ColourYourFunctions.Internal.Notes;

internal sealed class AiSentimentService
{
    /// <summary>
    /// Calls an AI service, it is potentially a long-running operation.
    /// </summary>
    [TxNever]
#pragma warning disable CA1822
    public async Task<int> GetSentimentAsync(string text)
#pragma warning restore CA1822
    {
        //Dummy
        await Task.Delay(1000);
        return 0;
    }
}