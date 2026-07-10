namespace AndromedaFleetCommand.Core.Missions;

public enum TutorialAction
{
    SwitchShip,
    IssueOrder,
    ActivateAbility
}

public sealed class TutorialTracker
{
    private static readonly (TutorialAction Action, string Prompt)[] Steps =
    [
        (TutorialAction.SwitchShip, "STEP 1/3  Press Tab or 1–4 to switch ships"),
        (TutorialAction.IssueOrder, "STEP 2/3  Press Enter and order: Frigate Two, intercept the bomber"),
        (TutorialAction.ActivateAbility, "STEP 3/3  Press Q to activate the selected ship's tactical ability")
    ];

    public int CompletedSteps { get; private set; }
    public bool IsComplete => CompletedSteps >= Steps.Length;
    public string CurrentPrompt => IsComplete
        ? "TRAINING COMPLETE  Command the fleet and finish the objective"
        : Steps[CompletedSteps].Prompt;

    public bool Notify(TutorialAction action)
    {
        if (IsComplete || Steps[CompletedSteps].Action != action) return false;
        CompletedSteps++;
        return true;
    }
}
