namespace AndromedaFleetCommand.Core.Missions;

public enum TutorialAction
{
    SwitchShip,
    PlotManeuver,
    IssueOrder,
    ActivateAbility,
    EndTurn
}

public sealed record TutorialStep(
    TutorialAction Action,
    string Title,
    string KeyboardPrompt,
    string ControllerPrompt,
    string Purpose);

public sealed class TutorialTracker
{
    public static IReadOnlyList<TutorialStep> Steps { get; } =
    [
        new(TutorialAction.SwitchShip, "Choose your ship",
            "Press Tab or 1–2 to switch ships",
            "Tap LB or RB to switch ships",
            "Every allied hull is yours."),
        new(TutorialAction.PlotManeuver, "Plot a maneuver",
            "Press W/A/S/D or F to queue one tactical action",
            "Use the left stick or R3 to queue one tactical action",
            "Preview the move before anyone commits."),
        new(TutorialAction.IssueOrder, "Command the fleet",
            "Press Enter • order Frigate Two to intercept the bomber",
            "Press Enter to type, or Y when local voice is ready",
            "Pilots understand your intent."),
        new(TutorialAction.ActivateAbility, "Change the battle",
            "Press Q to trigger this ship's tactical ability",
            "Press B to trigger this ship's tactical ability",
            "Each class fights differently."),
        new(TutorialAction.EndTurn, "Commit the plan",
            "Press Space to resolve the turn",
            "Press A to resolve the turn",
            "Both fleets execute together.")
    ];

    public int CompletedSteps { get; private set; }
    public bool IsComplete => CompletedSteps >= Steps.Count;
    public int StepNumber => Math.Min(CompletedSteps + 1, Steps.Count);
    public double Progress => (double)CompletedSteps / Steps.Count;
    public TutorialStep? CurrentStep => IsComplete ? null : Steps[CompletedSteps];
    public string CurrentPrompt => GetPrompt(false);

    public string GetPrompt(bool controller) => IsComplete
        ? "CAPTAIN CERTIFIED • Destroy the raider leader"
        : $"STEP {StepNumber}/{Steps.Count}  {(controller ? CurrentStep!.ControllerPrompt : CurrentStep!.KeyboardPrompt)}";

    public bool Notify(TutorialAction action)
    {
        if (IsComplete || CurrentStep!.Action != action) return false;
        CompletedSteps++;
        return true;
    }
}
