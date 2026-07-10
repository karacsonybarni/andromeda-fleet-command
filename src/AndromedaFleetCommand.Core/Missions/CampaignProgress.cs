using System.Text.Json;

namespace AndromedaFleetCommand.Core.Missions;

public sealed record CampaignProgress(int HighestUnlockedMission, IReadOnlySet<MissionId> CompletedMissions)
{
    public static CampaignProgress New => new(0, new HashSet<MissionId>());

    public bool IsUnlocked(int missionIndex) =>
        missionIndex >= 0 && missionIndex < MissionCatalog.All.Count && missionIndex <= HighestUnlockedMission;

    public bool IsCompleted(MissionId missionId) => CompletedMissions.Contains(missionId);

    public CampaignProgress Complete(MissionId missionId)
    {
        var completed = CompletedMissions.ToHashSet();
        completed.Add(missionId);
        var completedIndex = MissionCatalog.IndexOf(missionId);
        var unlocked = Math.Min(MissionCatalog.All.Count - 1,
            Math.Max(HighestUnlockedMission, completedIndex + 1));
        return new(unlocked, completed);
    }
}

public sealed class CampaignProgressStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CampaignProgress Load()
    {
        if (!File.Exists(path)) return CampaignProgress.New;
        try
        {
            var save = JsonSerializer.Deserialize<CampaignSave>(File.ReadAllText(path), JsonOptions);
            if (save is null) return CampaignProgress.New;
            var highest = Math.Clamp(save.HighestUnlockedMission, 0, MissionCatalog.All.Count - 1);
            var completed = save.CompletedMissions
                .Where(id => Enum.IsDefined(id))
                .ToHashSet();
            return new(highest, completed);
        }
        catch (JsonException)
        {
            return CampaignProgress.New;
        }
    }

    public void Save(CampaignProgress progress)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        var save = new CampaignSave(progress.HighestUnlockedMission,
            progress.CompletedMissions.OrderBy(id => id).ToArray());
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(save, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private sealed record CampaignSave(int HighestUnlockedMission, MissionId[] CompletedMissions);
}
