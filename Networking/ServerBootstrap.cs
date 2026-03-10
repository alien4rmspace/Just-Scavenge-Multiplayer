using PlayFab;
using PlayFab.MultiplayerAgent;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-10000)]
public class ServerBootstrap : MonoBehaviour
{
    // ── Inspector ──
    [SerializeField] private GameObject[] _disableOnServer;
    [SerializeField] public float emptyServerTimeout = 120f;

    // ── Config ──
    private const int TargetFrameRate      = 30;
    private const int TargetFrameRateEmpty = 30;
    private const string GamePortName      = "game_port";
    private const int FallbackPort         = 7777;

    // ── State ──
    private bool _started;
    private int  _playerCount;
    private bool _emptyCountdown;
    private float _emptyTimer;

    // ── Unity Lifecycle ──

    // ServerBootstrap.cs — add local testing mode
    private void Awake()
    {
#if UNITY_SERVER
        bool localTesting = true; 

        if (localTesting)
        {
            ConfigurePerformance();
            var transport = NetworkManager.Singleton
                .GetComponent<UnityTransport>();
            transport.SetConnectionData("0.0.0.0", 7777, "0.0.0.0");
            NetworkManager.Singleton.StartServer();
            Debug.Log("Local test server started on port 7777");
            return;
        }

        // normal PlayFab path
        ConfigurePerformance();
        DisableClientObjects();
        InitializeGSDK();
#endif
    }

    private void Update()
    {
#if !UNITY_SERVER
        return;
#endif
        TickEmptyShutdown();
    }

    // ── Initialization ──

    private void ConfigurePerformance()
    {
        QualitySettings.vSyncCount    = 0;
        Application.targetFrameRate   = TargetFrameRateEmpty;
        Time.fixedDeltaTime           = 1f / 15f;
        Time.maximumDeltaTime         = 0.1f;
    }

    private void DisableClientObjects()
    {
        foreach (GameObject go in _disableOnServer)
            go.SetActive(false);
    }

    private void InitializeGSDK()
    {
#if UNITY_SERVER
        PlayFabMultiplayerAgentAPI.Start();
        PlayFabMultiplayerAgentAPI.OnShutDownCallback   += Application.Quit;
        PlayFabMultiplayerAgentAPI.OnServerActiveCallback += OnAllocated;
        PlayFabMultiplayerAgentAPI.OnAgentErrorCallback += (error) => Debug.LogError($"GSDK Error: {error}");
        PlayFabMultiplayerAgentAPI.ReadyForPlayers();
        Debug.Log("GSDK started, waiting for allocation...");
#endif
    }

    // ── Allocation ──

#if UNITY_SERVER
    private void OnAllocated()
    {
        if (_started) return;
        _started = true;

        Debug.Log("Server allocated — starting NGO...");
        LogNavMeshInfo();

        int port = ResolvePort();
        ConfigureTransport(port);
        StartNetworkServer(port);
    }

    private void LogNavMeshInfo()
    {
        var tri = NavMesh.CalculateTriangulation();
        Debug.Log($"NavMesh vertices: {tri.vertices.Length}");
    }

    private int ResolvePort()
    {
        var connectionInfo = PlayFabMultiplayerAgentAPI.GetGameServerConnectionInfo();

        foreach (var p in connectionInfo.GamePortsConfiguration)
        {
            Debug.Log($"Port config: name={p.Name} listen={p.ServerListeningPort}");
            if (p.Name == GamePortName)
                return p.ServerListeningPort;
        }

        Debug.LogError($"'{GamePortName}' not found in GamePortsConfiguration. Falling back to {FallbackPort}.");
        return FallbackPort;
    }

    private void ConfigureTransport(int port)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        transport.SetConnectionData("0.0.0.0", (ushort)port, "0.0.0.0");
        transport.ConnectionData.ServerListenAddress = "0.0.0.0";
        transport.ConnectionData.Port = (ushort)port;

        Debug.Log($"Transport configured — Address: {transport.ConnectionData.Address}, Port: {transport.ConnectionData.Port}");
    }

    private void StartNetworkServer(int port)
    {
        NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.StartServer();

        Debug.Log($"Server listening on UDP {port}. IsListening={NetworkManager.Singleton.IsListening}");
    }
#endif

    // ── Client Events ──

    private void OnClientConnected(ulong clientId)
    {
        _playerCount++;
        _emptyCountdown = false;
        Application.targetFrameRate = TargetFrameRate;
        Debug.Log($"Client connected: {clientId}. Total: {_playerCount}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        _playerCount = Mathf.Max(0, --_playerCount);
        Debug.Log($"Client disconnected: {clientId}. Total: {_playerCount}");

        if (_playerCount <= 0)
        {
            BeginEmptyShutdownCountdown();
        }
    }

    // ── Shutdown ──

    private void BeginEmptyShutdownCountdown()
    {
        _emptyCountdown             = true;
        _emptyTimer                 = emptyServerTimeout;
        Application.targetFrameRate = TargetFrameRateEmpty;
        Debug.Log($"Server empty. Shutting down in {emptyServerTimeout}s...");
    }

    private void TickEmptyShutdown()
    {
        if (!_emptyCountdown) return;

        _emptyTimer -= Time.deltaTime;
        if (_emptyTimer <= 0)
        {
            Debug.Log("Empty server timeout reached. Shutting down.");
            Application.Quit();
        }
    }
}