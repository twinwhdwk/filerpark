#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Netcode;
using Unity.Netcode.Components;
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
        NetworkSetupMenu.EnsureFolder(ScenesFolder);
        LoadThemeFonts();

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

    // UITheme.cs 폰트 애셋 -- 매번 AssetDatabase.LoadAssetAtPath를 부르지 않도록 캐시.
    private static Font headingFont;
    private static Font bodyBoldFont;
    private static Font bodyMediumFont;

    private static void LoadThemeFonts()
    {
        if (headingFont == null) headingFont = AssetDatabase.LoadAssetAtPath<Font>(UITheme.FontHeadingPath);
        if (bodyBoldFont == null) bodyBoldFont = AssetDatabase.LoadAssetAtPath<Font>(UITheme.FontBodyBoldPath);
        if (bodyMediumFont == null) bodyMediumFont = AssetDatabase.LoadAssetAtPath<Font>(UITheme.FontBodyMediumPath);
    }

    // 흙 블록 + 살짝 밝은 "잔디" 캡을 얹은 바닥. Lobby/Stage가 공통으로 쓴다 --
    // 밋밋한 단색 사각형보다 저비용으로 "블록 위에 뭔가 자란" 느낌을 준다.
    private static void CreateGroundWithGrassCap(Vector3 position, Vector3 scale, Color dirtColor, Color grassColor)
    {
        Sprite sprite = NetworkSetupMenu.GetOrCreatePlaceholderSprite();
        int groundLayer = LayerMask.NameToLayer("Ground");

        GameObject ground = new GameObject("Ground");
        if (groundLayer >= 0) ground.layer = groundLayer;
        ground.transform.position = position;
        ground.transform.localScale = scale;
        SpriteRenderer groundSprite = ground.AddComponent<SpriteRenderer>();
        groundSprite.sprite = sprite;
        groundSprite.color = dirtColor;
        BoxCollider2D groundCollider = ground.AddComponent<BoxCollider2D>();
        groundCollider.size = Vector2.one;

        GameObject grassCap = new GameObject("GrassCap");
        grassCap.transform.SetParent(ground.transform, false);
        grassCap.transform.localPosition = new Vector3(0f, 0.5f, -0.01f);
        grassCap.transform.localScale = new Vector3(1f, 0.08f, 1f);
        SpriteRenderer grassSprite = grassCap.AddComponent<SpriteRenderer>();
        grassSprite.sprite = sprite;
        grassSprite.color = grassColor;
    }

    // 월드 스페이스 오브젝트(버튼/문/골존)에 라운드 사각형 스프라이트를 9-slice로
    // 붙인다. transform.localScale로 늘리면 모서리까지 같이 늘어나 뭉개지므로,
    // SpriteRenderer.drawMode = Sliced + size로 늘려야 라운드가 유지된다.
    private static void ApplySlicedSprite(GameObject go, Sprite sprite, Vector2 worldSize)
    {
        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = worldSize;
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/2. Create Lobby Scene")]
    public static void CreateLobbyScene()
    {
        NetworkSetupMenu.EnsureFolder(ScenesFolder);
        LoadThemeFonts();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateOrthoCamera(true);

        CreateGroundWithGrassCap(new Vector3(0f, -3f, 0f), new Vector3(18f, 1f, 1f), UITheme.ColorBgSecondary, UITheme.ColorPrimary);

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

        Text lobbyTitle = CreateLabel(canvasObj.transform, "LobbyTitle",
            new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(900f, 110f),
            56, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);
        lobbyTitle.text = "LOBBY";

        Text statusText = CreateLabel(canvasObj.transform, "StatusText",
            new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1100f, 180f),
            40, UITheme.ColorFg, TextAnchor.MiddleCenter, bodyMediumFont);
        statusText.supportRichText = true;
        LobbyUI lobbyUI = canvasObj.AddComponent<LobbyUI>();
        lobbyUI.statusText = statusText;

        // 스코어보드 카드: 라운드 사각형 패널(흰 바탕 + primary 테두리) 위에
        // 헤더 + 플레이어별 색상 점(ScoreboardUI가 PlayerColorNGO 팔레트로 칠함) 목록.
        GameObject scorePanel = new GameObject("ScorePanel");
        scorePanel.transform.SetParent(canvasObj.transform, false);
        RectTransform scorePanelRect = scorePanel.AddComponent<RectTransform>();
        scorePanelRect.anchorMin = new Vector2(1f, 1f);
        scorePanelRect.anchorMax = new Vector2(1f, 1f);
        scorePanelRect.pivot = new Vector2(1f, 1f);
        scorePanelRect.anchoredPosition = new Vector2(-40f, -40f);
        scorePanelRect.sizeDelta = new Vector2(420f, 460f);
        Image scorePanelImage = scorePanel.AddComponent<Image>();
        scorePanelImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_CardPanel.png", UITheme.ColorBg, UITheme.ColorPrimary, 40f, 6f);
        scorePanelImage.type = Image.Type.Sliced;

        Text scoreHeader = CreateLabel(scorePanel.transform, "ScoreHeader",
            new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(380f, 56f),
            30, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);

        Text scoreText = CreateLabel(scorePanel.transform, "ScoreText",
            new Vector2(0f, 1f), new Vector2(30f, -100f), new Vector2(360f, 340f),
            26, UITheme.ColorFg, TextAnchor.UpperLeft, bodyMediumFont);
        scoreText.verticalOverflow = VerticalWrapMode.Overflow;
        scoreText.supportRichText = true;

        ScoreboardUI scoreboardUI = canvasObj.AddComponent<ScoreboardUI>();
        scoreboardUI.headerText = scoreHeader;
        scoreboardUI.scoreText = scoreText;

        EditorSceneManager.SaveScene(scene, LobbyScenePath);
        Debug.Log($"[FlowSetup] Lobby 씬 생성 완료: {LobbyScenePath}");
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/3. Create Stage01 Scene")]
    public static void CreateStage01Scene()
    {
        NetworkSetupMenu.EnsureFolder(StagesFolder);
        LoadThemeFonts();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateOrthoCamera(true);

        // 흙색 바닥 + 살짝 밝은 캡 (Lobby와 동일한 헬퍼, 톤만 "흙" 쪽으로).
        CreateGroundWithGrassCap(new Vector3(0f, -3f, 0f), new Vector3(18f, 1f, 1f),
            new Color(0.45f, 0.32f, 0.2f), new Color(0.55f, 0.78f, 0.35f));

        CreateSpawnPoints();

        // 라운드 사각형 스프라이트 두 종류:
        //  - badgeSprite: primary 채움 + white 테두리. 색이 고정된 오브젝트(버튼)용.
        //  - tintableSprite: 채움/테두리 모두 흰색인 "무늬 없는" 라운드 도형. 런타임에
        //    SpriteRenderer.color를 곱연산으로 계속 바꾸는 오브젝트(문)는 이걸 써야
        //    두 색이 뒤섞여 지저분해지지 않는다 -- badgeSprite를 곱하면 테두리(흰색)와
        //    채움(초록)이 서로 다른 색으로 물들어 버린다.
        Sprite badgeSprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_ButtonPrimary.png", UITheme.ColorPrimary, UITheme.ColorWhite);
        Sprite tintableSprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_RoundedWhite.png", Color.white, Color.white);

        // 버튼: 중앙 부근에 둬서, 6개 스폰 지점 중 최소 중앙 2곳(±1.2)의 기본
        // patrolHalfWidth(3) 범위 안에 확실히 들어오게 한다 (그 외 스폰도 대부분 걸침).
        GameObject button = new GameObject("CoopButton1");
        button.transform.position = new Vector3(0f, -2.2f, 0f);
        button.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(button, badgeSprite, new Vector2(0.9f, 0.22f));
        BoxCollider2D buttonCollider = button.AddComponent<BoxCollider2D>();
        buttonCollider.isTrigger = true;
        buttonCollider.size = new Vector2(0.9f, 0.22f);
        button.AddComponent<NetworkObject>();
        CoopButtonNGO buttonScript = button.AddComponent<CoopButtonNGO>();

        GameObject door = new GameObject("CoopDoor1");
        door.transform.position = new Vector3(7f, -1.7f, 0f);
        door.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(door, tintableSprite, new Vector2(0.45f, 1.3f));
        BoxCollider2D doorCollider = door.AddComponent<BoxCollider2D>();
        doorCollider.size = new Vector2(0.45f, 1.3f);
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
        goalZone.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(goalZone, tintableSprite, new Vector2(16.5f, 1.6f));
        goalZone.GetComponent<SpriteRenderer>().color = new Color(
            UITheme.ColorBgSecondary.r, UITheme.ColorBgSecondary.g, UITheme.ColorBgSecondary.b, 0.5f);
        BoxCollider2D goalCollider = goalZone.AddComponent<BoxCollider2D>();
        goalCollider.isTrigger = true;
        goalCollider.size = new Vector2(16.5f, 1.6f);
        goalZone.AddComponent<NetworkObject>();
        GoalZoneNGO goalScript = goalZone.AddComponent<GoalZoneNGO>();

        GameObject stageManagerObj = new GameObject("StageManager");
        StageManagerNGO stageManager = stageManagerObj.AddComponent<StageManagerNGO>();
        stageManager.goalZone = goalScript;

        BuildStageClearBanner(goalScript);

        EditorSceneManager.SaveScene(scene, Stage01ScenePath);
        Debug.Log($"[FlowSetup] Stage01 씬 생성 완료: {Stage01ScenePath}");
    }

    // 클리어 배너: GoalZoneNGO.stageCleared를 지켜보다가(StageClearUI) 카드 패널을
    // 띄운다. GameFlowManager가 결과 화면(4초)을 보여주는 동안 화면에 남아 있다가,
    // 다음 스테이지 씬이 로드되면 이 오브젝트 자체가 통째로 사라지며 자연스럽게 정리된다.
    private static void BuildStageClearBanner(GoalZoneNGO goalZone)
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        GameObject canvasObj = new GameObject("StageUICanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject banner = new GameObject("ClearBanner");
        banner.transform.SetParent(canvasObj.transform, false);
        RectTransform bannerRect = banner.AddComponent<RectTransform>();
        bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
        bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
        bannerRect.sizeDelta = new Vector2(640f, 260f);
        Image bannerImage = banner.AddComponent<Image>();
        bannerImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_CardPanel.png", UITheme.ColorBg, UITheme.ColorPrimary, 40f, 6f);
        bannerImage.type = Image.Type.Sliced;

        Text clearText = CreateLabel(banner.transform, "ClearText", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 220f),
            110, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);

        StageClearUI clearUI = canvasObj.AddComponent<StageClearUI>();
        clearUI.goalZone = goalZone;
        clearUI.clearBanner = banner;
        clearUI.clearText = clearText;
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
        LoadThemeFonts();

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

        // 배경: 게임 월드가 그대로 비치는 미완성 화면이 아니라 제대로 된 시작
        // 화면으로 만든다 (UI Style Guide: color-bg-secondary).
        GameObject bgObj = new GameObject("BackgroundPanel");
        bgObj.transform.SetParent(canvasObj.transform, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.sprite = NetworkSetupMenu.GetOrCreatePlaceholderSprite();
        bgImage.color = UITheme.ColorBgSecondary;

        // 로고: Dosis(라틴 전용) 대문자 -- 한글은 Dosis에 글리프가 없어 워드마크로 둔다.
        Text title = CreateLabel(canvasObj.transform, "TitleLabel", new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1000f, 160f),
            96, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);
        title.text = "FILER PARK";
        title.horizontalOverflow = HorizontalWrapMode.Overflow;

        Text subtitle = CreateLabel(canvasObj.transform, "SubtitleLabel", new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(1000f, 60f),
            28, UITheme.ColorFg, TextAnchor.MiddleCenter, bodyMediumFont);
        subtitle.text = "PICO PARK 스타일 협동 퍼즐 플랫포머";

        GameObject buttonObj = new GameObject("ConnectButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);
        RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(320f, 90f);
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_ButtonPrimary.png", UITheme.ColorPrimary, UITheme.ColorWhite);
        buttonImage.type = Image.Type.Sliced;

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        Text label = CreateLabel(buttonObj.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 90f),
            32, UITheme.ColorWhite, TextAnchor.MiddleCenter, bodyBoldFont, FontStyle.Bold);
        label.text = "서버 접속";

        NetworkBootstrapper bootstrapper = networkManagerObj.GetComponent<NetworkBootstrapper>();
        if (bootstrapper != null)
        {
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, bootstrapper.ConnectToServer);
            bootstrapper.startMenuUI = canvasObj;
        }
    }

    private static Text CreateLabel(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition, Vector2 size,
        int fontSize, Color color, TextAnchor alignment, Font font = null, FontStyle fontStyle = FontStyle.Normal)
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
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.fontStyle = fontStyle;
        return text;
    }
}
#endif
