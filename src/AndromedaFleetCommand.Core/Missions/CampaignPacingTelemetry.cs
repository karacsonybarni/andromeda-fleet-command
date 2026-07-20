using System.Globalization;
using System.Text;
using System.Text.Json;
using AndromedaFleetCommand.Core.Model;

namespace AndromedaFleetCommand.Core.Missions;

public sealed record CampaignAttempt(MissionId MissionId, BattleStatus Outcome, double ActiveSeconds);

public sealed record MissionPacingStats(
    MissionId MissionId,
    int Attempts,
    int Victories,
    int Defeats,
    double TargetSeconds,
    double? LatestVictorySeconds,
    double? FastestVictorySeconds)
{
    public double? LatestVarianceSeconds => LatestVictorySeconds - TargetSeconds;
}

public sealed class CampaignPacingTelemetry
{
    public const int MaximumAttemptsPerMission = 100;
    private const double MaximumAttemptSeconds = 12 * 60 * 60;
    private readonly CampaignAttempt[] _attempts;

    public CampaignPacingTelemetry(IEnumerable<CampaignAttempt> attempts)
    {
        _attempts = attempts.ToArray();
    }

    public static CampaignPacingTelemetry Empty => new([]);
    public IReadOnlyList<CampaignAttempt> Attempts => Array.AsReadOnly(_attempts);

    public int MeasuredMissionCount => MissionCatalog.All.Count(mission =>
        StatsFor(mission.Id).LatestVictorySeconds.HasValue);

    public bool HasCompleteCampaignMeasurement => MeasuredMissionCount == MissionCatalog.All.Count;

    public double MeasuredVictorySeconds => MissionCatalog.All.Sum(mission =>
        StatsFor(mission.Id).LatestVictorySeconds ?? 0);

    public double TargetCampaignSeconds => MissionCatalog.All.Sum(mission => mission.EstimatedMinutes * 60d);

    public CampaignPacingTelemetry Record(MissionId missionId, BattleStatus outcome, double activeSeconds)
    {
        if (missionId == MissionId.FleetDuel || MissionCatalog.All.All(mission => mission.Id != missionId) ||
            outcome is not (BattleStatus.PlayerVictory or BattleStatus.EnemyVictory) ||
            !double.IsFinite(activeSeconds) || activeSeconds < 0)
            return this;

        var normalizedSeconds = Math.Min(activeSeconds, MaximumAttemptSeconds);
        var updated = _attempts.ToList();
        updated.Add(new(missionId, outcome, normalizedSeconds));

        var missionAttemptCount = updated.Count(attempt => attempt.MissionId == missionId);
        if (missionAttemptCount > MaximumAttemptsPerMission)
        {
            var oldest = updated.FindIndex(attempt => attempt.MissionId == missionId);
            updated.RemoveAt(oldest);
        }

        return new(updated);
    }

    public MissionPacingStats StatsFor(MissionId missionId)
    {
        var mission = MissionCatalog.Get(missionId);
        var attempts = _attempts.Where(attempt => attempt.MissionId == missionId).ToArray();
        var victories = attempts.Where(attempt => attempt.Outcome == BattleStatus.PlayerVictory).ToArray();
        return new(
            missionId,
            attempts.Length,
            victories.Length,
            attempts.Count(attempt => attempt.Outcome == BattleStatus.EnemyVictory),
            mission.EstimatedMinutes * 60d,
            victories.LastOrDefault()?.ActiveSeconds,
            victories.Length == 0 ? null : victories.Min(attempt => attempt.ActiveSeconds));
    }
}

public sealed class CampaignPacingTelemetryStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CampaignPacingTelemetry Load()
    {
        if (!File.Exists(path)) return CampaignPacingTelemetry.Empty;
        try
        {
            var save = JsonSerializer.Deserialize<CampaignPacingSave>(File.ReadAllText(path), JsonOptions);
            if (save is null) return CampaignPacingTelemetry.Empty;

            var telemetry = CampaignPacingTelemetry.Empty;
            foreach (var attempt in save.Attempts ?? [])
                telemetry = telemetry.Record(attempt.MissionId, attempt.Outcome, attempt.ActiveSeconds);
            return telemetry;
        }
        catch (JsonException)
        {
            return CampaignPacingTelemetry.Empty;
        }
    }

    public void Save(CampaignPacingTelemetry telemetry)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        var save = new CampaignPacingSave(1, telemetry.Attempts.ToArray());
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(save, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private sealed record CampaignPacingSave(int Version, CampaignAttempt[]? Attempts);
}

public static class CampaignPacingReport
{
    public static void Save(string path, CampaignPacingTelemetry telemetry)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, Build(telemetry));
        File.Move(temporaryPath, path, true);
    }

    public static string Build(CampaignPacingTelemetry telemetry)
    {
        var report = new StringBuilder();
        report.AppendLine("# Crown of Andromeda campaign pacing report");
        report.AppendLine();
        report.AppendLine($"Measured victories: **{telemetry.MeasuredMissionCount}/{MissionCatalog.All.Count} missions**");
        report.AppendLine($"Authored campaign target: **{FormatDuration(telemetry.TargetCampaignSeconds)}**");
        report.AppendLine(telemetry.HasCompleteCampaignMeasurement
            ? $"Latest successful playthrough total: **{FormatDuration(telemetry.MeasuredVictorySeconds)}** " +
              $"({FormatVariance(telemetry.MeasuredVictorySeconds - telemetry.TargetCampaignSeconds)})"
            : "A full-campaign duration is intentionally withheld until every mission has a recorded victory.");
        report.AppendLine();
        report.AppendLine("Active battle time excludes menus and pauses. The latest victory represents the current pacing pass; the fastest victory helps identify shortcut strategies.");
        report.AppendLine();
        report.AppendLine("| # | Mission | Target | Attempts | W-L | Latest victory | Fastest victory | Variance |");
        report.AppendLine("| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |");

        for (var index = 0; index < MissionCatalog.All.Count; index++)
        {
            var mission = MissionCatalog.All[index];
            var stats = telemetry.StatsFor(mission.Id);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {index + 1} | {mission.Title} | {FormatDuration(stats.TargetSeconds)} | {stats.Attempts} | " +
                $"{stats.Victories}-{stats.Defeats} | {FormatOptional(stats.LatestVictorySeconds)} | " +
                $"{FormatOptional(stats.FastestVictorySeconds)} | " +
                $"{(stats.LatestVarianceSeconds is double variance ? FormatVariance(variance) : "—")} |");
        }

        return report.ToString();
    }

    public static string FormatDuration(double seconds)
    {
        var rounded = Math.Max(0, (int)Math.Round(seconds));
        var duration = TimeSpan.FromSeconds(rounded);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    public static string FormatVariance(double seconds)
    {
        var prefix = seconds >= 0 ? "+" : "−";
        return prefix + FormatDuration(Math.Abs(seconds));
    }

    private static string FormatOptional(double? seconds) =>
        seconds.HasValue ? FormatDuration(seconds.Value) : "—";
}
