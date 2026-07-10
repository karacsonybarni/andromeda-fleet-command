using Godot;

namespace AndromedaFleetCommand.Game.Infrastructure;

public interface IPlatformServices : IDisposable
{
    string Name { get; }
    bool IsAvailable { get; }
    void RunCallbacks();
    void UnlockAchievement(string achievementId);
}

public static class PlatformServicesFactory
{
    public static IPlatformServices Create()
    {
        var steam = new SteamPlatformServices();
        if (steam.IsAvailable) return steam;
        steam.Dispose();
        return new LocalPlatformServices();
    }
}

public sealed class LocalPlatformServices : IPlatformServices
{
    public string Name => "Local development";
    public bool IsAvailable => true;
    public void RunCallbacks() { }
    public void UnlockAchievement(string achievementId) { }
    public void Dispose() { }
}

public sealed class SteamPlatformServices : IPlatformServices
{
    private GodotObject? _steam;

    public SteamPlatformServices()
    {
        try
        {
            if (!Engine.HasSingleton("Steam")) return;
            _steam = Engine.GetSingleton("Steam");
            _steam?.Call("steamInitEx");
            IsAvailable = _steam is not null;
        }
        catch (Exception)
        {
            _steam = null;
            IsAvailable = false;
        }
    }

    public string Name => IsAvailable ? "Steam" : "Steam unavailable";
    public bool IsAvailable { get; private set; }

    public void RunCallbacks()
    {
        if (IsAvailable) _steam?.Call("run_callbacks");
    }

    public void UnlockAchievement(string achievementId)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(achievementId)) return;
        _steam?.Call("setAchievement", achievementId);
        _steam?.Call("storeStats");
    }

    public void Dispose()
    {
        _steam = null;
        IsAvailable = false;
    }
}
