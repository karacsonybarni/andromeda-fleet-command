using AndromedaFleetCommand.Core.Commands;
using AndromedaFleetCommand.Core.Model;
using AndromedaFleetCommand.Core.Multiplayer;
using AndromedaFleetCommand.Core.Missions;
using AndromedaFleetCommand.Core.Simulation;
using Godot;

namespace AndromedaFleetCommand.Game.Infrastructure;

public enum MultiplayerSessionState
{
    Offline,
    HostingLobby,
    Connecting,
    JoinedLobby,
    InMatch
}

/// <summary>
/// Owns the ENet peer and the host-authoritative multiplayer session. Clients send only intent;
/// the host resolves committed turns and distributes complete recovery snapshots.
/// </summary>
public sealed partial class MultiplayerManager : Node
{
    public const int DefaultPort = 7777;
    private const int MaximumClients = FleetLobby.MaximumPlayers - 1;

    private ENetMultiplayerPeer? _peer;
    private FleetLobby? _hostLobby;
    private AuthoritativeFleetSession? _hostSession;
    private string _pendingDisplayName = "Captain";
    private long _sequence;

    public event Action<FleetLobbySnapshot>? LobbyChanged;
    public event Action<MatchStartMessage>? MatchStarted;
    public event Action<AuthoritativeSnapshot>? SnapshotReceived;
    public event Action<string>? NoticeReceived;
    public event Action<MultiplayerSessionState>? StateChanged;

    public MultiplayerSessionState State { get; private set; }
    public FleetLobbySnapshot? Lobby { get; private set; }
    public AuthoritativeSnapshot? LatestSnapshot { get; private set; }
    public string LocalPlayerId { get; private set; } = string.Empty;
    public bool IsHost => _hostLobby is not null && Multiplayer.IsServer();
    public bool IsInMatch => State == MultiplayerSessionState.InMatch;
    public bool IsActive => State != MultiplayerSessionState.Offline;
    public int Port { get; private set; } = DefaultPort;
    public int CommittedTurn { get; private set; }
    public bool IsWaitingForTurn => IsInMatch && LatestSnapshot is not null &&
                                    CommittedTurn == LatestSnapshot.ServerTurn;

    public IReadOnlyList<string> LocalShipIds => Lobby?.Players
        .FirstOrDefault(player => player.PlayerId.Equals(LocalPlayerId, StringComparison.Ordinal))?.ShipIds ?? [];

    public Team? LocalTeam => Lobby?.Players
        .FirstOrDefault(player => player.PlayerId.Equals(LocalPlayerId, StringComparison.Ordinal))?.Team;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        Close(false);
    }

    public LobbyActionResult Host(MultiplayerMode mode, string displayName, int port = DefaultPort)
    {
        Close(false);
        if (port is < 1 or > 65535) return new(false, "Port must be between 1 and 65535");

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, MaximumClients, 3);
        if (error != Error.Ok)
        {
            peer.Dispose();
            return new(false, $"Could not host on UDP port {port}: {error}");
        }

        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        Port = port;
        LocalPlayerId = Multiplayer.GetUniqueId().ToString();
        _hostLobby = new(LocalPlayerId, displayName, mode);
        var lobby = _hostLobby.Snapshot();
        Lobby = lobby;
        ChangeState(MultiplayerSessionState.HostingLobby);
        LobbyChanged?.Invoke(lobby);
        return new(true, $"Hosting on UDP port {port}");
    }

    public LobbyActionResult Join(string address, string displayName, int port = DefaultPort)
    {
        Close(false);
        if (string.IsNullOrWhiteSpace(address)) return new(false, "Enter the host address");
        if (port is < 1 or > 65535) return new(false, "Port must be between 1 and 65535");

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(address.Trim(), port, 3);
        if (error != Error.Ok)
        {
            peer.Dispose();
            return new(false, $"Could not connect to {address}:{port}: {error}");
        }

        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        Port = port;
        _pendingDisplayName = NormalizeDisplayName(displayName);
        ChangeState(MultiplayerSessionState.Connecting);
        return new(true, $"Connecting to {address}:{port}…");
    }

    public LobbyActionResult SetMode(MultiplayerMode mode)
    {
        if (!IsHost || _hostLobby is null) return new(false, "Only the host can change the mode");
        var result = _hostLobby.SetMode(mode);
        if (result.Accepted) PublishLobby();
        return result;
    }

    public LobbyActionResult SetCooperativeMission(MissionId missionId)
    {
        if (!IsHost || _hostLobby is null) return new(false, "Only the host can change the mission");
        var result = _hostLobby.SetCooperativeMission(missionId);
        if (result.Accepted) PublishLobby();
        return result;
    }

    public LobbyActionResult StartMatch()
    {
        if (!IsHost || _hostLobby is null) return new(false, "Only the host can start the match");
        var (result, session) = _hostLobby.StartMatch();
        if (!result.Accepted || session is null) return result;

        BeginHostedMatch(session);
        return result;
    }

    public LobbyActionResult Rematch()
    {
        if (!IsHost || _hostLobby is null || _hostSession is null)
            return new(false, "Only the host can start a rematch");
        BeginHostedMatch(_hostLobby.CreateRematch());
        return new(true, "Rematch started");
    }

    public CommandAdmission SendControl(string shipId, ManualInput input, bool activateAbility = false)
    {
        if (!IsInMatch || string.IsNullOrWhiteSpace(LocalPlayerId))
            return new(false, "No multiplayer match is active");
        if (IsWaitingForTurn) return new(false, $"Already ready for turn {CommittedTurn}");
        var frame = new NetworkControlFrame(++_sequence, LatestSnapshot?.ServerTurn ?? 1,
            LocalPlayerId, shipId, input, activateAbility);
        if (IsHost && _hostSession is not null)
        {
            var admission = _hostSession.SubmitControl(frame);
            if (admission.Accepted) PublishSnapshot(_hostSession.Snapshot());
            return admission;
        }
        RpcId(1, nameof(SubmitControlRpc), MultiplayerWire.Serialize(frame));
        return new(true, activateAbility ? "Ability queued" : "Maneuver sent");
    }

    public CommandAdmission SendCommand(FleetCommand command, string selectedShipId, BattleSimulation simulation)
    {
        if (!IsInMatch || string.IsNullOrWhiteSpace(LocalPlayerId))
            return new(false, "No multiplayer match is active");
        if (IsWaitingForTurn) return new(false, $"Already ready for turn {CommittedTurn}");

        var owned = LocalShipIds.Select(simulation.FindShip).Where(ship => ship is { IsAlive: true })
            .Cast<Ship>().ToArray();
        var subjects = command.ShipSelector.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? owned
            : command.ShipSelector.Equals("selected", StringComparison.OrdinalIgnoreCase)
                ? owned.Where(ship => ship.Id.Equals(selectedShipId, StringComparison.Ordinal)).ToArray()
                : owned.Where(ship => ship.Name.Contains(command.ShipSelector, StringComparison.OrdinalIgnoreCase) ||
                                      ship.Class.ToString().Contains(command.ShipSelector,
                                          StringComparison.OrdinalIgnoreCase)).ToArray();
        if (subjects.Length == 0) return new(false, $"You do not control a ship matching “{command.ShipSelector}”");

        foreach (var ship in subjects)
        {
            var networkCommand = new NetworkFleetCommand(++_sequence, LatestSnapshot?.ServerTurn ?? 1,
                LocalPlayerId, ship.Id, command.Action, command.TargetSelector, command.Destination);
            if (IsHost && _hostSession is not null)
            {
                var admission = _hostSession.Submit(networkCommand);
                if (!admission.Accepted) return admission;
            }
            else
            {
                RpcId(1, nameof(SubmitCommandRpc), MultiplayerWire.Serialize(networkCommand));
            }
        }
        if (IsHost && _hostSession is not null) PublishSnapshot(_hostSession.Snapshot());
        return new(true, subjects.Length == 1
            ? $"Order planned for {subjects[0].Name}"
            : $"Orders planned for {subjects.Length} assigned ships");
    }

    public TurnCommitResult CommitTurn()
    {
        if (!IsInMatch || string.IsNullOrWhiteSpace(LocalPlayerId))
            return new(false, false, "No multiplayer match is active");
        if (IsWaitingForTurn) return new(false, false, $"Already ready for turn {CommittedTurn}");
        var commit = new NetworkTurnCommit(++_sequence, LatestSnapshot?.ServerTurn ?? 1, LocalPlayerId);
        if (IsHost && _hostSession is not null)
        {
            var result = _hostSession.CommitTurn(commit);
            if (result.Accepted)
            {
                CommittedTurn = commit.ClientTurn;
                PublishSnapshot(result.Snapshot ?? _hostSession.Snapshot());
            }
            return result;
        }
        RpcId(1, nameof(CommitTurnRpc), MultiplayerWire.Serialize(commit));
        CommittedTurn = commit.ClientTurn;
        return new(true, false, $"Ready for turn {commit.ClientTurn}; waiting for host");
    }

    public void Close(bool notify = true)
    {
        var peer = _peer;
        _peer = null;
        // Detach the SceneTree from ENet before releasing the native peer. Godot
        // otherwise tries to remove peer signals from an already-disposed object.
        if (Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer)
            Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        if (peer is not null)
        {
            peer.Close();
            peer.Dispose();
        }
        _hostLobby = null;
        _hostSession = null;
        Lobby = null;
        LatestSnapshot = null;
        LocalPlayerId = string.Empty;
        CommittedTurn = 0;
        _sequence = 0;
        if (State != MultiplayerSessionState.Offline) ChangeState(MultiplayerSessionState.Offline);
        if (notify) NoticeReceived?.Invoke("Multiplayer session closed");
    }

    private void BeginHostedMatch(AuthoritativeFleetSession session)
    {
        _hostSession = session;
        if (_peer is not null) _peer.RefuseNewConnections = true;
        var lobby = _hostLobby!.Snapshot();
        var snapshot = session.Snapshot();
        Lobby = lobby;
        LatestSnapshot = snapshot;
        CommittedTurn = 0;
        ChangeState(MultiplayerSessionState.InMatch);
        var start = new MatchStartMessage(lobby, snapshot);
        MatchStarted?.Invoke(start);
        Rpc(nameof(ReceiveMatchStartRpc), MultiplayerWire.Serialize(start));
        PublishSnapshot(snapshot);
    }

    private void PublishLobby()
    {
        if (_hostLobby is null) return;
        var lobby = _hostLobby.Snapshot();
        Lobby = lobby;
        LobbyChanged?.Invoke(lobby);
        Rpc(nameof(ReceiveLobbyRpc), MultiplayerWire.Serialize(lobby));
    }

    private void PublishSnapshot(AuthoritativeSnapshot snapshot)
    {
        LatestSnapshot = snapshot;
        if (snapshot.ServerTurn > CommittedTurn) CommittedTurn = 0;
        SnapshotReceived?.Invoke(snapshot);
        Rpc(nameof(ReceiveSnapshotRpc), MultiplayerWire.Serialize(snapshot));
    }

    private void SendNotice(long peerId, string notice) => RpcId(peerId, nameof(ReceiveNoticeRpc), notice);

    private void OnPeerConnected(long peerId)
    {
        if (IsHost) NoticeReceived?.Invoke($"Peer {peerId} connected");
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (!IsHost || _hostLobby is null) return;
        var playerId = peerId.ToString();
        if (_hostSession?.UnassignPlayer(playerId) == true) PublishSnapshot(_hostSession.Snapshot());
        var result = _hostLobby.RemovePlayer(playerId);
        if (result.Accepted)
        {
            PublishLobby();
            NoticeReceived?.Invoke($"Captain {peerId} disconnected; bots resumed their ships");
        }
    }

    private void OnConnectedToServer()
    {
        LocalPlayerId = Multiplayer.GetUniqueId().ToString();
        var request = new JoinRequest(_pendingDisplayName, MultiplayerWire.ProtocolVersion);
        RpcId(1, nameof(RequestJoinRpc), MultiplayerWire.Serialize(request));
    }

    private void OnConnectionFailed()
    {
        NoticeReceived?.Invoke("Connection failed");
        Close(false);
    }

    private void OnServerDisconnected()
    {
        NoticeReceived?.Invoke("The host closed the session");
        Close(false);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestJoinRpc(string payload)
    {
        if (!IsHost || _hostLobby is null) return;
        var peerId = Multiplayer.GetRemoteSenderId();
        if (!MultiplayerWire.TryDeserialize<JoinRequest>(payload, out var request) || request is null)
        {
            SendNotice(peerId, "Invalid join request");
            return;
        }
        if (request.ProtocolVersion != MultiplayerWire.ProtocolVersion)
        {
            SendNotice(peerId, "This build uses a different multiplayer protocol version");
            return;
        }
        var result = _hostLobby.TryAddPlayer(peerId.ToString(), request.DisplayName);
        SendNotice(peerId, result.Message);
        if (result.Accepted) PublishLobby();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveLobbyRpc(string payload)
    {
        if (!MultiplayerWire.TryDeserialize<FleetLobbySnapshot>(payload, out var lobby) || lobby is null) return;
        Lobby = lobby;
        if (State != MultiplayerSessionState.InMatch) ChangeState(MultiplayerSessionState.JoinedLobby);
        LobbyChanged?.Invoke(lobby);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMatchStartRpc(string payload)
    {
        if (!MultiplayerWire.TryDeserialize<MatchStartMessage>(payload, out var start) || start is null) return;
        Lobby = start.Lobby;
        LatestSnapshot = start.Snapshot;
        CommittedTurn = 0;
        ChangeState(MultiplayerSessionState.InMatch);
        MatchStarted?.Invoke(start);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = 2)]
    private void ReceiveSnapshotRpc(string payload)
    {
        if (!MultiplayerWire.TryDeserialize<AuthoritativeSnapshot>(payload, out var snapshot) || snapshot is null)
            return;
        if (LatestSnapshot is not null && snapshot.Revision <= LatestSnapshot.Revision) return;
        LatestSnapshot = snapshot;
        if (snapshot.ServerTurn > CommittedTurn) CommittedTurn = 0;
        SnapshotReceived?.Invoke(snapshot);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveNoticeRpc(string notice) => NoticeReceived?.Invoke(notice);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitControlRpc(string payload)
    {
        if (!IsHost || _hostSession is null ||
            !MultiplayerWire.TryDeserialize<NetworkControlFrame>(payload, out var frame) || frame is null)
            return;
        var sender = Multiplayer.GetRemoteSenderId().ToString();
        var trusted = frame with { PlayerId = sender };
        var result = _hostSession.SubmitControl(trusted);
        if (!result.Accepted) SendNotice(long.Parse(sender), result.Message);
        else PublishSnapshot(_hostSession.Snapshot());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitCommandRpc(string payload)
    {
        if (!IsHost || _hostSession is null ||
            !MultiplayerWire.TryDeserialize<NetworkFleetCommand>(payload, out var command) || command is null)
            return;
        var senderId = Multiplayer.GetRemoteSenderId();
        var trusted = command with { PlayerId = senderId.ToString() };
        var result = _hostSession.Submit(trusted);
        if (!result.Accepted) SendNotice(senderId, result.Message);
        else PublishSnapshot(_hostSession.Snapshot());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void CommitTurnRpc(string payload)
    {
        if (!IsHost || _hostSession is null ||
            !MultiplayerWire.TryDeserialize<NetworkTurnCommit>(payload, out var commit) || commit is null)
            return;
        var senderId = Multiplayer.GetRemoteSenderId();
        var trusted = commit with { PlayerId = senderId.ToString() };
        var result = _hostSession.CommitTurn(trusted);
        SendNotice(senderId, result.Message);
        if (result.Accepted) PublishSnapshot(result.Snapshot ?? _hostSession.Snapshot());
    }

    private void ChangeState(MultiplayerSessionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private static string NormalizeDisplayName(string displayName)
    {
        var normalized = new string((displayName ?? string.Empty).Where(character => !char.IsControl(character))
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Captain" : normalized;
    }
}
