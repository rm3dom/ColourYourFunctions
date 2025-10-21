using ColourYourFunctions.Tx;

namespace ColourYourFunctions.Internal.Notes;

internal sealed class AiSentimentService
{
    /// <summary>
    ///     Calls an AI service, it is potentially a long-running operation.
    /// </summary>
    [TxNever]
    public async Task<int> GetSentimentAsync(string text)
    {
        await TxAssert.AssertTxNeverAsync();
        //Dummy
        await Task.Delay(1000);
        return 0;
    }
}