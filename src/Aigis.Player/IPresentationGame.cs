namespace Aigis.Player;

/// <summary>Receives device samples once per display update; owns its fixed simulation worker.
/// GameHost must not advance these games through its synchronous catch-up loop.</summary>
public interface IPresentationGame<in TInput>
{
    public void SubmitPresentation(TInput input);
}
