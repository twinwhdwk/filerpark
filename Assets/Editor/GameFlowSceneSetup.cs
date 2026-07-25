#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// Bootstrap(항상 로드) + Lobby(대기실) + Stage01(첫 스테이지)를 각각 독립된 씬
// 파일로 헤드리스 생성한다. 새 스테이지를 추가할 때는:
//   1) 이 파일에 CreateStageNN_Xxx() 하나를 더 만들고 (기존 CreateStage01Scene 복사해서
//      기믹만 바꾸면 됨 -- 씬 자체는 완전히 독립적이라 다른 씬에 영향 없음)
//   2) GameFlowManager.stageSceneNames 배열에 씬 이름 추가
//   3) ConfigureBuildSettingsScenes()의 목록에 추가
// 이 세 가지만 하면 되고, Bootstrap/Lobby/기존 스테이지는 전혀 건드릴 필요 없다.
public static class GameFlowSceneSetup
{
    private const string ScenesFolder = "Assets/Scenes";
    private const string StagesFolder = "Assets/Scenes/Stages";

    private const string BootstrapScenePath = ScenesFolder + "/Bootstrap.unity";
    private const string LobbyScenePath = ScenesFolder + "/Lobby.unity";
    private const string Stage01ScenePath = StagesFolder + "/Stage01_Gatekeeper.unity";

    private const string ServerAddress = "34.50.24.161";
    private const ushort ServerPort = 7777;

    // 최대 6인 기준 스폰 지점 x좌표. Lobby/Stage 씬이 공통으로 쓴다.
    private static readonly float[] SpawnXs = { -6f, -3.6f, -1.2f, 1.2f, 3.6f, 6f };

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/Run All (1-4)")]
    public static void RunAll()
    {
        CreateBootstrapScene();
        CreateLobbyScene();
        CreateStage01Scene();
        ConfigureBuildSettingsScenes();
        Debug.Log("[FlowSetup] Bootstrap/Lobby/Stage01 씬 생성 + Build Settings 구성 전체 완료.");
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/1. Create Bootstrap Scene")]
    public static void CreateBootstrapScene()
    {
        EnsureFolder(ScenesFolder);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateOrthoCamera(false);

        GameObject nmObj = new GameObject("NetworkManager");
        NetworkManager networkManager = nmObj.AddComponent<NetworkManager>();
        UnityTransport transport = nmObj.AddComponent<UnityTransport>();
        transport.ConnectionData.Address = ServerAddress;
        transport.ConnectionData.Port = ServerPort;
        // 기본값 128은 4~6명이 한꺼번에 접속하면서 씬 전환(로비->스테이지) 스폰/동기화
        // 패킷이 몰릴 때 "Receive queue is full"로 넘쳐서 패킷이 드롭되고, 그 결과
        // NGO의 씬 로드 완료 이벤트가 그 클라이언트를 영영 기다리며 멈추는 걸
        // 5봇 테스트에서 실제로 겪었다. 여유 있게 올려둔다.
        transport.MaxPacketQueueSize = 1024;
        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = true;
        nmObj.AddComponent<NetworkBootstrapper>();

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        if (playerPrefab != null)
        {
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
        }
        else
        {
            Debug.LogWarning("[FlowSetup] Assets/Prefabs/Player.prefab을 못 찾았습니다. Tools/Coop Setup/2. Create Player Prefab을 먼저 실행하세요.");
        }

        // GameFlowManager -- NetworkManager 오브젝트와는 별개의, 씬에 배치된
        // NetworkObject. 씬 배치 NetworkObject는 서버 시작 시 자동 스폰되므로
        // (CoopButtonNGO 등과 동일한 원리) NetworkVariable/ClientRpc를 여기서 문제없이
        // 쓸 수 있다 -- NetworkManager 오브젝트 자체는 스폰되는 대상이 아니라서
        // NetworkBehaviour를 못 올린다는 게 이 프로젝트에서 이미 한 번 부딪힌 함정.
        GameObject flowObj = new GameObject("GameFlowManager");
        flowObj.AddComponent<NetworkObject>();
        GameFlowManager flow = flowObj.AddComponent<GameFlowManager>();
        flow.stageSceneNames = new[] { "Stage01_Gatekeeper" };
        flow.lobbySceneName = "Lobby";
        flow.minPlayersToStart = 1;
        flow.lobbyCountdownSeconds = 5f;
        flow.resultsDisplaySeconds = 4f;

        BuildConnectUI(nmObj);

        EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        Debug.Log($"[FlowSetup] Bootstrap 씬 생성 완료: {BootstrapScenePath}");
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/2. Create Lobby Scene")]
    public static void CreateLobbyScene()
    {
        EnsureFolder(ScenesFolder);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateOrthoCamera(true);

        Sprite sprite = GetOrCreateWhiteSprite();

        GameObject ground = new GameObject("LobbyGround");
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0) ground.layer = groundLayer;
        ground.transform.position = new Vector3(0f, -3f, 0f);
        ground.transform.localScale = new Vector3(18f, 1f, 1f);
        SpriteRenderer groundSprite = ground.AddComponent<SpriteRenderer>();
        groundSprite.sprite = sprite;
        groundSprite.color = UITheme.ColorBgSecondary;
        BoxCollider2D groundCollider = ground.AddComponent<BoxCollider2D>();
        groundCollider.size = Vector2.one;

        CreateSpawnPoints();

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        GameObject canvasObj = new GameObject("LobbyCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObj.AddComponent<GraphicRaycaster>();

        Text statusText = CreateLabel(canvasObj.transform, "StatusText",
            new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(1000f, 140f),
            36, UITheme.ColorFg, TextAnchor.MiddleCenter);
        LobbyUI lobbyUI = canvasObj.AddComponent<LobbyUI>();
        lobbyUI.statusText = statusText;

        Text scoreText = CreateLabel(canvasObj.transform, "ScoreText",
            new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(380f, 420f),
            24, UITheme.ColorFg, TextAnchor.UpperRight);
        ScoreboardUI scoreboardUI = canvasObj.AddComponent<ScoreboardUI>();
        scoreboardUI.scoreText = scoreText;

        EditorSceneManager.SaveScene(scene, LobbyScenePath);
        Debug.Log($"[FlowSetup] Lobby 씬 생성 완료: {LobbyScenePath}");
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/3. Create Stage01 Scene")]
    public static void CreateStage01Scene()
    {
        EnsureFolder(StagesFolder);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateOrthoCamera(true);

        Sprite sprite = GetOrCreateWhiteSprite();
        int groundLayer = LayerMask.NameToLayer("Ground");

        GameObject ground = new GameObject("TestGround");
        if (groundLayer >= 0) ground.layer = groundLayer;
        ground.transform.position = new Vector3(0f, -3f, 0f);
        ground.transform.localScale = new Vector3(18f, 1f, 1f);
        SpriteRenderer groundSprite = ground.AddComponent<SpriteRenderer>();
        groundSprite.sprite = sprite;
        groundSprite.color = new Color(0.4f, 0.3f, 0.2f);
        BoxCollider2D groundCollider = ground.AddComponent<BoxCollider2D>();
        groundCollider.size = Vector2.one;

        CreateSpawnPoints();

        // 버튼: 중앙 부근에 둬서, 6개 스폰 지점 중 최소 중앙 2곳(±1.2)의 기본
        // patrolHalfWidth(3) 범위 안에 확실히 들어오게 한다 (그 외 스폰도 대부분 걸침).
        GameObject button = new GameObject("CoopButton1");
        button.transform.position = new Vector3(0f, -2.2f, 0f);
        SpriteRenderer buttonSprite = button.AddComponent<SpriteRenderer>();
        buttonSprite.sprite = sprite;
        buttonSprite.color = UITheme.ColorPrimary;
        button.transform.localScale = new Vector3(0.8f, 0.2f, 1f);
        BoxCollider2D buttonCollider = button.AddComponent<BoxCollider2D>();
        buttonCollider.isTrigger = true;
        button.AddComponent<NetworkObject>();
        CoopButtonNGO buttonScript = button.AddComponent<CoopButtonNGO>();

        GameObject door = new GameObject("CoopDoor1");
        door.transform.position = new Vector3(7f, -1.7f, 0f);
        SpriteRenderer doorSprite = door.AddComponent<SpriteRenderer>();
        doorSprite.sprite = sprite;
        door.transform.localScale = new Vector3(0.4f, 1.2f, 1f);
        door.AddComponent<BoxCollider2D>();
        door.AddComponent<NetworkObject>();
        CoopDoorNGO doorScript = door.AddComponent<CoopDoorNGO>();
        doorScript.requiredButtons = 1;

        UnityEditor.Events.UnityEventTools.AddPersistentListener(buttonScript.OnButtonPress, doorScript.AddPress);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(buttonScript.OnButtonRelease, doorScript.RemovePress);

        // 골 존: 6개 스폰 지점(-6..6)을 전부 덮는 폭으로 크게 잡는다 -- 봇이 4~6개일 때
        // Patrol 위상이 서로 어긋나 "동시에 한곳에 모이기"가 오래 걸리거나 안 맞을 수
        // 있으므로, 스폰 직후 낙하 위치만으로 이미 전원이 안에 들어오도록 보장한다.
        GameObject goalZone = new GameObject("GoalZone1");
        goalZone.transform.position = new Vector3(0f, -2.2f, 0f);
        SpriteRenderer goalSprite = goalZone.AddComponent<SpriteRenderer>();
        goalSprite.sprite = sprite;
        goalSprite.color = new Color(UITheme.ColorBgSecondary.r, UITheme.ColorBgSecondary.g, UITheme.ColorBgSecondary.b, 0.4f);
        goalZone.transform.localScale = new Vector3(16f, 1.5f, 1f);
        BoxCollider2D goalCollider = goalZone.AddComponent<BoxCollider2D>();
        goalCollider.isTrigger = true;
        goalZone.AddComponent<NetworkObject>();
        GoalZoneNGO goalScript = goalZone.AddComponent<GoalZoneNGO>();

        GameObject stageManagerObj = new GameObject("StageManager");
        StageManagerNGO stageManager = stageManagerObj.AddComponent<StageManagerNGO>();
        stageManager.goalZone = goalScript;

        EditorSceneManager.SaveScene(scene, Stage01ScenePath);
        Debug.Log($"[FlowSetup] Stage01 씬 생성 완료: {Stage01ScenePath}");
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/4. Configure Build Settings Scenes")]
    public static void ConfigureBuildSettingsScenes()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(BootstrapScenePath, true),
            new EditorBuildSettingsScene(LobbyScenePath, true),
            new EditorBuildSettingsScene(Stage01ScenePath, true),
        };
        Debug.Log("[FlowSetup] Build Settings 씬 목록: Bootstrap(0, 부팅 씬) -> Lobby -> Stage01_Gatekeeper.");
    }

    private static void CreateSpawnPoints()
    {
        foreach (float x in SpawnXs)
        {
            GameObject sp = new GameObject($"SpawnPoint_{x:0.0}");
            sp.tag = "SpawnPoint";
            sp.transform.position = new Vector3(x, 0f, 0f);
        }
    }

    private static void CreateOrthoCamera(bool withFollow)
    {
        GameObject camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        Camera cam = camObj.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6f;
        camObj.AddComponent<AudioListener>();
        if (withFollow)
        {
            camObj.AddComponent<CoopCameraFollow>();
        }
    }

    private static void BuildConnectUI(GameObject networkManagerObj)
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        GameObject canvasObj = new GameObject("ConnectCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject buttonObj = new GameObject("ConnectButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);
        RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(320f, 90f);
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = UITheme.ColorPrimary;

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        Text label = CreateLabel(buttonObj.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 90f),
            32, UITheme.ColorWhite, TextAnchor.MiddleCenter);
        label.text = "서버 접속";

        NetworkBootstrapper bootstrapper = networkManagerObj.GetComponent<NetworkBootstrapper>();
        if (bootstrapper != null)
        {
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, bootstrapper.ConnectToServer);
            bootstrapper.startMenuUI = canvasObj;
        }
    }

    private static Text CreateLabel(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition, Vector2 size,
        int fontSize, Color color, TextAnchor alignment)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Text text = obj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        return text;
    }

    private static Sprite GetOrCreateWhiteSprite()
    {
        const string path = "Assets/Sprites/PlayerPlaceholder.png";

        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
        {
            return existing;
        }

        EnsureFolder("Assets/Sprites");

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.white;
        }
        texture.SetPixels(pixels);
        texture.Apply();

        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 64;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string folderName = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
#endif
