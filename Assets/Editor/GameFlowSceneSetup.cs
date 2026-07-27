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
    private const string Stage02ScenePath = StagesFolder + "/Stage02_BlockCarry.unity";
    private const string Stage03ScenePath = StagesFolder + "/Stage03_KeyRelay.unity";
    private const string Stage04ScenePath = StagesFolder + "/Stage04_EscapeCountdown.unity";

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
        CreateStage02Scene();
        CreateStage03Scene();
        CreateStage04Scene();
        ConfigureBuildSettingsScenes();
        Debug.Log("[FlowSetup] Bootstrap/Lobby/Stage01-04 씬 생성 + Build Settings 구성 전체 완료.");
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/1. Create Bootstrap Scene")]
    public static void CreateBootstrapScene()
    {
        NetworkSetupMenu.EnsureFolder(ScenesFolder);
        LoadThemeFonts();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 유일하게 AudioListener를 갖는 카메라 -- Bootstrap은 절대 언로드되지 않으므로
        // 이 하나로 Lobby/Stage 어디를 오가든 항상 충분하다 (CreateOrthoCamera 주석 참고).
        CreateOrthoCamera(false, withAudioListener: true);

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
        flow.stageSceneNames = new[]
        {
            "Stage01_Gatekeeper",
            "Stage02_BlockCarry",
            "Stage03_KeyRelay",
            "Stage04_EscapeCountdown",
        };
        flow.lobbySceneName = "Lobby";
        flow.minPlayersToStart = 1;
        flow.lobbyCountdownSeconds = 5f;
        flow.resultsDisplaySeconds = 4f;

        // 오디오/일시정지/접속-끊김 UI -- 전부 Bootstrap 씬(절대 언로드 안 됨)에
        // 배치해서 Lobby/Stage 어디서든 동일하게 동작한다. BuildConnectUI보다 먼저
        // 만드는 이유는, 접속 화면에도 설정 버튼을 하나 두어 PauseMenuUI의 설정
        // 패널을 곧바로 열 수 있게 하기 위함 -- 그러려면 그 시점에 PauseMenuUI
        // 인스턴스가 이미 존재해야 한다.
        GameObject audioManagerObj = new GameObject("AudioManager");
        audioManagerObj.AddComponent<AudioManager>();

        BuildLoadingScreenUI();
        PauseMenuUI pauseMenu = BuildPauseMenuUI();
        BuildConnectionStatusUI();

        BuildConnectUI(nmObj, pauseMenu);

        EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        ReopenAndResaveToFixNetworkObjectHashes(BootstrapScenePath);
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

        // EventSystem은 여기서 만들지 않는다 -- Bootstrap(절대 언로드 안 됨)이 이미
        // 하나를 갖고 있고, Lobby는 항상 Bootstrap과 함께 로드되므로 그걸로 충분하다.
        // 예전엔 여기서도 하나 더 만들어서, 씬 생성 시점(단독 NewScene)에는 "없으니까
        // 만든다"는 이 검사가 통과했지만 런타임에는 항상 Bootstrap 것과 중복돼
        // "There are 2 event systems" 경고가 매 프레임 쌓였다.
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

        BuildWorldMapUI(canvasObj.transform);

        EditorSceneManager.SaveScene(scene, LobbyScenePath);
        ReopenAndResaveToFixNetworkObjectHashes(LobbyScenePath);
        Debug.Log($"[FlowSetup] Lobby 씬 생성 완료: {LobbyScenePath}");
    }

    // 예전엔 Docs/Stages 설계 문서만 보여주는 미연결 프로토타입(Assets/scense/main.unity)
    // 이었던 WorldMapUI/StageIntroUI/StageCatalog를 실제 Lobby 씬에 붙인다. "스테이지
    // 선택" 버튼 -> 4개 스테이지 노드가 늘어선 맵 카드 -> 노드 클릭 시 상세 카드 ->
    // "이 스테이지로 시작"을 누르면 GameFlowManager.RequestSelectStageServerRpc로
    // 다음 스테이지를 예약한다. 아무도 안 누르면 예전처럼 순서대로 자동 진행되므로,
    // 이미 5봇 테스트로 검증된 자동 순환 흐름은 전혀 바뀌지 않는다.
    private static void BuildWorldMapUI(Transform canvasTransform)
    {
        StageCatalog catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>("Assets/StageData/StageCatalog.asset");
        if (catalog == null)
        {
            Debug.LogWarning("[FlowSetup] Assets/StageData/StageCatalog.asset을 찾을 수 없어 월드맵 UI를 건너뜁니다.");
            return;
        }

        Button openMapButton = CreateMenuButton(canvasTransform, "OpenMapButton", new Vector2(0f, -460f), "스테이지 선택");

        // 스크림 전체(WorldMapUI가 이 오브젝트에 붙는다) -- Open/Close가 이 오브젝트
        // 자체를 켜고 끈다. 안쪽의 맵 카드/인트로 카드는 그 상태에서 서로만 전환된다.
        GameObject overlayRoot = new GameObject("WorldMapOverlay");
        overlayRoot.transform.SetParent(canvasTransform, false);
        RectTransform overlayRect = overlayRoot.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image overlayScrim = overlayRoot.AddComponent<Image>();
        overlayScrim.sprite = NetworkSetupMenu.GetOrCreatePlaceholderSprite();
        overlayScrim.color = UITheme.ColorScrim;

        WorldMapUI worldMap = overlayRoot.AddComponent<WorldMapUI>();
        worldMap.catalog = catalog;

        // 맵 카드 -- 4개 스테이지 노드를 가로로 나열.
        GameObject mapCard = new GameObject("MapCard");
        mapCard.transform.SetParent(overlayRoot.transform, false);
        RectTransform mapCardRect = mapCard.AddComponent<RectTransform>();
        mapCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        mapCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        mapCardRect.sizeDelta = new Vector2(1400f, 580f);
        Image mapCardImage = mapCard.AddComponent<Image>();
        mapCardImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_CardPanel.png", UITheme.ColorBg, UITheme.ColorFg, 40f, 6f);
        mapCardImage.type = Image.Type.Sliced;

        CreateLabel(mapCard.transform, "MapTitle", new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(600f, 70f),
            44, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold).text = "스테이지 선택";

        float[] nodeXs = { -525f, -175f, 175f, 525f };
        int nodeCount = catalog.stages != null ? Mathf.Min(4, catalog.stages.Length) : 0;
        for (int i = 0; i < nodeCount; i++)
        {
            StageDefinition stage = catalog.stages[i];
            Button nodeButton = CreateMenuButton(mapCard.transform, $"StageNode{i + 1}", new Vector2(nodeXs[i], 40f),
                stage != null ? stage.titleEn : $"STAGE {i + 1}");
            StageNodeButton nodeScript = nodeButton.gameObject.AddComponent<StageNodeButton>();
            nodeScript.worldMap = worldMap;
            nodeScript.stageIndex = i;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(nodeButton.onClick, nodeScript.NotifyClicked);
        }

        Button closeMapButton = CreateMenuButton(mapCard.transform, "CloseMapButton", new Vector2(0f, -230f), "닫기");
        UnityEditor.Events.UnityEventTools.AddPersistentListener(closeMapButton.onClick, worldMap.CloseMap);

        // 인트로 카드 -- 노드를 클릭하면 맵 카드 대신 이게 뜬다.
        GameObject introCard = new GameObject("StageIntroCard");
        introCard.transform.SetParent(overlayRoot.transform, false);
        RectTransform introCardRect = introCard.AddComponent<RectTransform>();
        introCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        introCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        introCardRect.sizeDelta = new Vector2(900f, 580f);
        Image introCardImage = introCard.AddComponent<Image>();
        introCardImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_CardPanel.png", UITheme.ColorBg, UITheme.ColorFg, 40f, 6f);
        introCardImage.type = Image.Type.Sliced;

        Text introTitle = CreateLabel(introCard.transform, "IntroTitle", new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(700f, 70f),
            44, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);
        Text introSubtitle = CreateLabel(introCard.transform, "IntroSubtitle", new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(700f, 50f),
            28, UITheme.ColorIce, TextAnchor.MiddleCenter, bodyBoldFont, FontStyle.Bold);
        Text introTheme = CreateLabel(introCard.transform, "IntroTheme", new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(700f, 44f),
            24, UITheme.ColorFg, TextAnchor.MiddleCenter, bodyMediumFont);
        Text introPlayerCount = CreateLabel(introCard.transform, "IntroPlayerCount", new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(700f, 40f),
            22, UITheme.ColorFg, TextAnchor.MiddleCenter, bodyMediumFont);
        Text introDescription = CreateLabel(introCard.transform, "IntroDescription", new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(760f, 160f),
            24, UITheme.ColorFg, TextAnchor.UpperLeft, bodyMediumFont);
        introDescription.verticalOverflow = VerticalWrapMode.Overflow;

        Button startButton = CreateMenuButton(introCard.transform, "StartStageButton", new Vector2(-170f, -240f), "이 스테이지로 시작");
        Button backButton = CreateMenuButton(introCard.transform, "BackToMapButton", new Vector2(170f, -240f), "뒤로");

        StageIntroUI stageIntro = introCard.AddComponent<StageIntroUI>();
        stageIntro.titleText = introTitle;
        stageIntro.subtitleText = introSubtitle;
        stageIntro.descriptionText = introDescription;
        stageIntro.themeTagText = introTheme;
        stageIntro.playerCountText = introPlayerCount;
        stageIntro.worldMap = worldMap;

        UnityEditor.Events.UnityEventTools.AddPersistentListener(startButton.onClick, stageIntro.OnStartPressed);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(backButton.onClick, worldMap.ReturnToMap);

        worldMap.mapPanel = mapCard;
        worldMap.stageIntro = stageIntro;

        UnityEditor.Events.UnityEventTools.AddPersistentListener(openMapButton.onClick, worldMap.OpenMap);

        introCard.SetActive(false);
        overlayRoot.SetActive(false);
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

        // 골 존: 6개 스폰 지점(-6..6)뿐 아니라 BotController의 기본 Patrol 왕복
        // 범위(spawn ± patrolHalfWidth=3, 즉 -9..9)까지 전부 덮는 폭으로 잡는다.
        // 처음엔 스폰 범위(16.5)만 덮었는데, 그러면 스폰 직후 짧은 순간에는 전원이
        // 우연히 안에 있어 클리어되지만, 그 순간을 놓치고 봇들이 Patrol로 흩어지기
        // 시작하면 x=6 스폰 봇은 x=9까지 나가 존 밖으로 벗어나 버려 "전원 동시 도달"
        // 조건이 다시는 우연히 맞지 않는 경우를 실측했다(5봇 테스트, inZone이 4에서
        // 못 넘고 계속 떨어짐). Patrol 전체 왕복 범위를 덮어야 언제 체크하든 항상
        // 성립한다.
        GameObject goalZone = new GameObject("GoalZone1");
        goalZone.transform.position = new Vector3(0f, -2.2f, 0f);
        goalZone.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(goalZone, tintableSprite, new Vector2(19f, 1.6f));
        goalZone.GetComponent<SpriteRenderer>().color = new Color(
            UITheme.ColorBgSecondary.r, UITheme.ColorBgSecondary.g, UITheme.ColorBgSecondary.b, 0.5f);
        BoxCollider2D goalCollider = goalZone.AddComponent<BoxCollider2D>();
        goalCollider.isTrigger = true;
        goalCollider.size = new Vector2(19f, 1.6f);
        goalZone.AddComponent<NetworkObject>();
        GoalZoneNGO goalScript = goalZone.AddComponent<GoalZoneNGO>();

        GameObject stageManagerObj = new GameObject("StageManager");
        StageManagerNGO stageManager = stageManagerObj.AddComponent<StageManagerNGO>();
        stageManager.goalZone = goalScript;

        BuildStageUI(goalScript, "Stage 1 · Gatekeeper");

        EditorSceneManager.SaveScene(scene, Stage01ScenePath);
        ReopenAndResaveToFixNetworkObjectHashes(Stage01ScenePath);
        Debug.Log($"[FlowSetup] Stage01 씬 생성 완료: {Stage01ScenePath}");
    }

    // 스테이지 이름 배지 + 클리어 배너를 한 캔버스에 만든다.
    //  - 이름 배지: 화면 위쪽에 항상 떠 있는 작은 라벨. 게임 월드 위에 겹치므로 배경
    //    패널 없이 두꺼운 아웃라인만으로 어떤 배경에서도 읽히게 한다 (플랫 채움 +
    //    굵은 테두리라는 UI Style Guide의 "배지" 형태를 텍스트에 적용한 것).
    //  - 클리어 배너: GoalZoneNGO.stageCleared를 지켜보다가(StageClearUI) 카드 패널을
    //    띄운다. GameFlowManager가 결과 화면(4초)을 보여주는 동안 화면에 남아 있다가,
    //    다음 스테이지 씬이 로드되면 이 오브젝트 자체가 통째로 사라지며 자연스럽게 정리된다.
    private static void BuildStageUI(GoalZoneNGO goalZone, string stageLabel)
    {
        // EventSystem은 여기서도 만들지 않는다 -- CreateLobbyScene()과 동일한 이유로,
        // 스테이지 씬은 항상 Bootstrap과 함께 로드되므로 Bootstrap의 EventSystem 하나면
        // 충분하다 (Bootstrap은 절대 언로드되지 않음).
        GameObject canvasObj = new GameObject("StageUICanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObj.AddComponent<GraphicRaycaster>();

        Text stageLabelText = CreateLabel(canvasObj.transform, "StageLabel",
            new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(1000f, 70f),
            40, UITheme.ColorWhite, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);
        stageLabelText.text = stageLabel.ToUpperInvariant();
        Outline stageLabelOutline = stageLabelText.gameObject.AddComponent<Outline>();
        stageLabelOutline.effectColor = UITheme.ColorFg;
        stageLabelOutline.effectDistance = new Vector2(2f, -2f);

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

        Text clearText = CreateLabel(banner.transform, "ClearText", new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(600f, 150f),
            90, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);

        Text clearSubText = CreateLabel(banner.transform, "ClearSubText", new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(560f, 60f),
            26, UITheme.ColorFg, TextAnchor.MiddleCenter, bodyMediumFont);

        StageClearUI clearUI = canvasObj.AddComponent<StageClearUI>();
        clearUI.goalZone = goalZone;
        clearUI.clearBanner = banner;
        clearUI.clearText = clearText;
        clearUI.clearSubText = clearSubText;
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/5. Create Stage02 Scene (Block Carry)")]
    public static void CreateStage02Scene()
    {
        NetworkSetupMenu.EnsureFolder(StagesFolder);
        LoadThemeFonts();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateOrthoCamera(true);

        Color dirtColor = new Color(0.45f, 0.32f, 0.2f);
        Color grassColor = new Color(0.55f, 0.78f, 0.35f);
        // 스폰 지점(-6~6)과 블록 초기 위치 사이에 확실한 여유를 둔다 -- 처음엔 블록을
        // x=4(폭 5, 1.5~6.5)에 뒀다가 스폰 지점 3.6/6이 블록 몸통 안쪽에 겹쳐 봇이
        // 스폰과 동시에 블록에 박히는 문제를 실제로 겪었다.
        CreateGroundWithGrassCap(new Vector3(6.5f, -3f, 0f), new Vector3(31f, 1f, 1f), dirtColor, grassColor);
        CreateGroundWithGrassCap(new Vector3(31f, -3f, 0f), new Vector3(8f, 1f, 1f), dirtColor, grassColor);
        // 구덩이(22~27) 아래 안전망 -- 5봇 테스트에서 봇이 다리(블록)를 건너다가
        // 서로 부대껴 살짝 밀려나 구덩이로 떨어지는 경우를 실측했다. 안전망이 없으면
        // 바닥 없이 Y좌표가 -수십만까지 무한히 떨어져(실측: -294802) 그 봇은 영원히
        // 게임에 복귀하지 못한다. Stage03의 시소 밑 안전망과 동일한 처리.
        CreateGroundWithGrassCap(new Vector3(24.5f, -8f, 0f), new Vector3(8f, 1f, 1f), dirtColor, UITheme.ColorBgSecondary);

        CreateSpawnPoints();

        Sprite badgeSprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_ButtonPrimary.png", UITheme.ColorPrimary, UITheme.ColorWhite);
        Sprite tintableSprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_RoundedWhite.png", Color.white, Color.white);

        GameObject targetMarker = new GameObject("BlockTarget");
        // 블록 y=-3.0: 바닥 표면(GroundLeft top = -3+0.5 = -2.5)과 블록 윗면(중심
        // -3.0 + 높이 절반 0.5 = -2.5)이 정확히 일치해야 한다. 처음엔 -2.2로 뒀는데
        // 그러면 블록 윗면이 바닥보다 0.8만큼 높아 봇이 다리를 건너다가 이 턱에
        // 막혀 멈춰 섰다(랜덤 타이머로만 점프하는 BotController는 이 턱을 못 넘음).
        targetMarker.transform.position = new Vector3(24.5f, -3.0f, 0f);

        // 돌덩이: 구덩이(x=22~27) 왼쪽에서 시작해, 밀려서 구덩이 중앙까지 가면 다리가
        // 되어 반대편으로 건널 수 있게 한다. "높은 벽을 스택으로 넘는다"는 설계
        // 문서(02-block-carry.md) 원안 대신 "구덩이를 다리로 잇는다"로 단순화했다 --
        // 봇 점프는 랜덤 타이머라 정밀 스택 타이밍을 요구하는 원안은 봇으로 신뢰도
        // 있게 검증할 수 없다(bot-coordination.md 5절). 미는 인원 게이팅이라는 핵심
        // 협동 기믹은 그대로 유지했다.
        GameObject block = new GameObject("PushableBlock1");
        block.transform.position = new Vector3(16f, -3.0f, 0f);
        block.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(block, badgeSprite, new Vector2(5f, 1f));
        // Kinematic Rigidbody2D 필수 -- 이거 없이 transform.position을 직접 바꾸면
        // Unity 2D 물리 엔진이 이 콜라이더를 "정적"으로 취급해서, 옆에 붙어 미는
        // 플레이어(다이나믹 바디)와의 충돌 해석이 어긋난다. 5봇 테스트에서 실제로
        // 밀던 인원이 3명을 채워 블록이 일부 움직이다가, 그 다음부터 겹침이 0으로
        // 고정되고 다시는 회복되지 않는 걸 확인했다(PushableBlockNGO 주석 참고).
        Rigidbody2D blockRb = block.AddComponent<Rigidbody2D>();
        blockRb.bodyType = RigidbodyType2D.Kinematic;
        blockRb.gravityScale = 0f;
        BoxCollider2D blockSolid = block.AddComponent<BoxCollider2D>();
        blockSolid.size = new Vector2(5f, 1f);
        // 트리거를 솔리드 콜라이더보다 "미는 반대편(왼쪽)"으로만 훨씬 크게 잡는다
        // (offset으로 중심을 왼쪽으로 밀고, size를 그만큼 키움). 처음엔 폭만 +2.5로
        // 좌우 균등하게 키웠는데, 그러면 블록 앞에서 대기하는 인원이 몸 폭(~0.9)만큼
        // 밀집해서 서야 하는 "대기 공간"이 솔리드 콜라이더 바로 앞 1.25유닛뿐이라
        // 요구 인원(3명)이 물리적으로 다 들어가지 못하고 2명에서 정체됐다(5봇
        // 테스트로 실측). 왼쪽으로 6유닛의 대기 공간을 넉넉히 확보한다.
        BoxCollider2D blockTrigger = block.AddComponent<BoxCollider2D>();
        blockTrigger.isTrigger = true;
        blockTrigger.offset = new Vector2(-3f, 0f);
        blockTrigger.size = new Vector2(11f, 1.6f);
        block.AddComponent<NetworkObject>();
        block.AddComponent<NetworkTransform>();
        PushableBlockNGO blockScript = block.AddComponent<PushableBlockNGO>();
        blockScript.targetPoint = targetMarker.transform;
        // 블록 윗면은 봇이 다리로 밟고 건너는 "바닥"이다 -> Ground 레이어로 둬야 그 위에
        // 선 봇의 groundCheck(서버 점프 검증)가 걸려 점프가 통과한다. 이게 없으면 블록
        // 위에서의 점프가 서버에서 조용히 거부돼, 블록/바닥 이음매 끼임을 점프로 넘지
        // 못하고 봇 한 명이 영구 정체 -> 전원 동시 도달 조건이 영영 안 맞았다(5봇 테스트
        // 근본 원인). Ground×Player 충돌 매트릭스는 이미 켜져 있어(좌우 바닥과 동일)
        // 밀기/서기 물리와 트리거 겹침 카운트는 그대로다.
        int blockGroundLayer = LayerMask.NameToLayer("Ground");
        if (blockGroundLayer >= 0) block.layer = blockGroundLayer;

        GameObject goalZone = new GameObject("GoalZone1");
        goalZone.transform.position = new Vector3(31f, -2.2f, 0f);
        goalZone.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(goalZone, tintableSprite, new Vector2(6f, 1.6f));
        goalZone.GetComponent<SpriteRenderer>().color = new Color(
            UITheme.ColorBgSecondary.r, UITheme.ColorBgSecondary.g, UITheme.ColorBgSecondary.b, 0.5f);
        BoxCollider2D goalCollider = goalZone.AddComponent<BoxCollider2D>();
        goalCollider.isTrigger = true;
        goalCollider.size = new Vector2(6f, 1.6f);
        goalZone.AddComponent<NetworkObject>();
        GoalZoneNGO goalScript = goalZone.AddComponent<GoalZoneNGO>();

        GameObject stageManagerObj = new GameObject("StageManager");
        StageManagerNGO stageManager = stageManagerObj.AddComponent<StageManagerNGO>();
        stageManager.goalZone = goalScript;

        BuildStageUI(goalScript, "Stage 2 · Block Carry");

        EditorSceneManager.SaveScene(scene, Stage02ScenePath);
        ReopenAndResaveToFixNetworkObjectHashes(Stage02ScenePath);
        Debug.Log($"[FlowSetup] Stage02 씬 생성 완료: {Stage02ScenePath}");
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/6. Create Stage03 Scene (Key Relay)")]
    public static void CreateStage03Scene()
    {
        NetworkSetupMenu.EnsureFolder(StagesFolder);
        LoadThemeFonts();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateOrthoCamera(true);

        Color dirtColor = new Color(0.45f, 0.32f, 0.2f);
        Color grassColor = new Color(0.55f, 0.78f, 0.35f);
        CreateGroundWithGrassCap(new Vector3(0f, -3f, 0f), new Vector3(18f, 1f, 1f), dirtColor, grassColor);
        CreateGroundWithGrassCap(new Vector3(20f, -3f, 0f), new Vector3(10f, 1f, 1f), dirtColor, grassColor);
        // 시소 아래로 떨어져도 무한정 추락하지 않게 하는 안전망 -- 이 스테이지는 실패
        // 조건이 없으므로(hasFailCondition 미사용), 떨어진 봇이 소프트락에 빠지는 걸
        // 막는 최소한의 장치. 완전한 복귀 경로는 사람 플레이테스트로 검증할 부분이다.
        CreateGroundWithGrassCap(new Vector3(12f, -7f, 0f), new Vector3(8f, 1f, 1f), dirtColor, UITheme.ColorBgSecondary);

        CreateSpawnPoints();

        Sprite badgeSprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_ButtonPrimary.png", UITheme.ColorPrimary, UITheme.ColorWhite);
        Sprite tintableSprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_RoundedWhite.png", Color.white, Color.white);

        // 열쇠: 스폰 근처에서 아무나 주울 수 있다.
        GameObject key = new GameObject("Key1");
        key.transform.position = new Vector3(0f, -2.2f, 0f);
        key.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(key, badgeSprite, new Vector2(0.6f, 0.6f));
        key.AddComponent<NetworkObject>();
        key.AddComponent<NetworkTransform>();
        CarryableKeyNGO keyScript = key.AddComponent<CarryableKeyNGO>();

        // 문: 열쇠가 문 앞 범위 안에 들어와야 열린다(누가 들고 있는지는 무관). CoopDoorNGO와
        // 같은 tintable(흰색) 스프라이트를 써서 KeyDoorNGO가 런타임에 색만 바꿔 칠한다.
        GameObject door = new GameObject("KeyDoor1");
        door.transform.position = new Vector3(7f, -1.7f, 0f);
        door.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(door, tintableSprite, new Vector2(0.45f, 1.3f));
        BoxCollider2D doorCollider = door.AddComponent<BoxCollider2D>();
        doorCollider.size = new Vector2(0.45f, 1.3f);
        door.AddComponent<NetworkObject>();
        KeyDoorNGO doorScript = door.AddComponent<KeyDoorNGO>();
        doorScript.key = keyScript;

        // 시소: 구덩이(x=9~15)를 가로지르는 회전 플랫폼. 좌/우 자식 트리거로 인원을
        // 세고, 불균형이 심하면 기운다.
        GameObject seesaw = new GameObject("Seesaw1");
        seesaw.transform.position = new Vector3(12f, -2.5f, 0f);
        seesaw.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(seesaw, badgeSprite, new Vector2(6f, 0.4f));
        BoxCollider2D seesawCollider = seesaw.AddComponent<BoxCollider2D>();
        seesawCollider.size = new Vector2(6f, 0.4f);
        seesaw.AddComponent<NetworkObject>();
        seesaw.AddComponent<NetworkTransform>();
        SeesawPlatformNGO seesawScript = seesaw.AddComponent<SeesawPlatformNGO>();
        // 시소 윗면도 봇이 밟고 건너는 바닥 -> Ground 레이어(그 위에서 점프가 서버 검증을
        // 통과하도록). 아래에서 만드는 좌/우 판정용 자식 트리거(SeesawSideZone)는 레이어를
        // 상속하지 않으므로 기본 레이어 그대로 남아 인원 카운트에는 영향이 없다.
        int seesawGroundLayer = LayerMask.NameToLayer("Ground");
        if (seesawGroundLayer >= 0) seesaw.layer = seesawGroundLayer;

        GameObject leftZone = new GameObject("SeesawLeftZone");
        leftZone.transform.SetParent(seesaw.transform, false);
        leftZone.transform.localPosition = new Vector3(-1.5f, 0.3f, 0f);
        BoxCollider2D leftCollider = leftZone.AddComponent<BoxCollider2D>();
        leftCollider.isTrigger = true;
        leftCollider.size = new Vector2(2.7f, 1.2f);
        SeesawSideZone leftZoneScript = leftZone.AddComponent<SeesawSideZone>();
        leftZoneScript.platform = seesawScript;
        leftZoneScript.isLeftSide = true;

        GameObject rightZone = new GameObject("SeesawRightZone");
        rightZone.transform.SetParent(seesaw.transform, false);
        rightZone.transform.localPosition = new Vector3(1.5f, 0.3f, 0f);
        BoxCollider2D rightCollider = rightZone.AddComponent<BoxCollider2D>();
        rightCollider.isTrigger = true;
        rightCollider.size = new Vector2(2.7f, 1.2f);
        SeesawSideZone rightZoneScript = rightZone.AddComponent<SeesawSideZone>();
        rightZoneScript.platform = seesawScript;
        rightZoneScript.isLeftSide = false;

        GameObject leftHold = new GameObject("SeesawLeftHold");
        leftHold.transform.position = new Vector3(10.5f, -2.1f, 0f);
        GameObject rightHold = new GameObject("SeesawRightHold");
        rightHold.transform.position = new Vector3(13.5f, -2.1f, 0f);
        seesawScript.leftHoldPoint = leftHold.transform;
        seesawScript.rightHoldPoint = rightHold.transform;

        GameObject goalZone = new GameObject("GoalZone1");
        goalZone.transform.position = new Vector3(20f, -2.2f, 0f);
        goalZone.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(goalZone, tintableSprite, new Vector2(6f, 1.6f));
        goalZone.GetComponent<SpriteRenderer>().color = new Color(
            UITheme.ColorBgSecondary.r, UITheme.ColorBgSecondary.g, UITheme.ColorBgSecondary.b, 0.5f);
        BoxCollider2D goalCollider = goalZone.AddComponent<BoxCollider2D>();
        goalCollider.isTrigger = true;
        goalCollider.size = new Vector2(6f, 1.6f);
        goalZone.AddComponent<NetworkObject>();
        GoalZoneNGO goalScript = goalZone.AddComponent<GoalZoneNGO>();

        GameObject stageManagerObj = new GameObject("StageManager");
        StageManagerNGO stageManager = stageManagerObj.AddComponent<StageManagerNGO>();
        stageManager.goalZone = goalScript;

        BuildStageUI(goalScript, "Stage 3 · Key Relay");

        EditorSceneManager.SaveScene(scene, Stage03ScenePath);
        ReopenAndResaveToFixNetworkObjectHashes(Stage03ScenePath);
        Debug.Log($"[FlowSetup] Stage03 씬 생성 완료: {Stage03ScenePath}");
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/7. Create Stage04 Scene (Escape Countdown)")]
    public static void CreateStage04Scene()
    {
        NetworkSetupMenu.EnsureFolder(StagesFolder);
        LoadThemeFonts();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateOrthoCamera(true);

        CreateGroundWithGrassCap(new Vector3(10f, -3f, 0f), new Vector3(40f, 1f, 1f),
            new Color(0.45f, 0.32f, 0.2f), new Color(0.55f, 0.78f, 0.35f));

        CreateSpawnPoints();

        Sprite tintableSprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_RoundedWhite.png", Color.white, Color.white);

        // 위험지대: 설계 문서는 "바닥에서 차오르는 용암 또는 좌측에서 다가오는 벽"을
        // 동등하게 제시한다(04-escape-countdown.md). 여기서는 벽 쪽으로 구현했다 --
        // RisingHazardNGO 참고 주석에 이유가 있다. 경고색은 UI Style Guide 토큰
        // 범위 밖의 의도적 예외: "닿으면 죽는다"는 신호는 브랜드 그린/블루로는
        // 전달되지 않는다.
        GameObject hazard = new GameObject("RisingHazard1");
        hazard.transform.position = new Vector3(-9f, 0f, 0f);
        hazard.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(hazard, tintableSprite, new Vector2(1f, 8f));
        hazard.GetComponent<SpriteRenderer>().color = new Color(0.9f, 0.25f, 0.15f, 0.85f);
        BoxCollider2D hazardCollider = hazard.AddComponent<BoxCollider2D>();
        hazardCollider.isTrigger = true;
        hazardCollider.size = new Vector2(1f, 8f);
        hazard.AddComponent<NetworkObject>();
        hazard.AddComponent<NetworkTransform>();
        RisingHazardNGO hazardScript = hazard.AddComponent<RisingHazardNGO>();

        GameObject goalZone = new GameObject("GoalZone1");
        goalZone.transform.position = new Vector3(25f, -2.2f, 0f);
        goalZone.AddComponent<SpriteRenderer>();
        ApplySlicedSprite(goalZone, tintableSprite, new Vector2(6f, 1.6f));
        goalZone.GetComponent<SpriteRenderer>().color = new Color(
            UITheme.ColorBgSecondary.r, UITheme.ColorBgSecondary.g, UITheme.ColorBgSecondary.b, 0.5f);
        BoxCollider2D goalCollider = goalZone.AddComponent<BoxCollider2D>();
        goalCollider.isTrigger = true;
        goalCollider.size = new Vector2(6f, 1.6f);
        goalZone.AddComponent<NetworkObject>();
        GoalZoneNGO goalScript = goalZone.AddComponent<GoalZoneNGO>();

        GameObject stageManagerObj = new GameObject("StageManager");
        StageManagerNGO stageManager = stageManagerObj.AddComponent<StageManagerNGO>();
        stageManager.goalZone = goalScript;
        stageManager.hasFailCondition = true;
        stageManager.hazard = hazardScript;
        hazardScript.stageManager = stageManager;

        BuildStageUI(goalScript, "Stage 4 · Escape Countdown");

        EditorSceneManager.SaveScene(scene, Stage04ScenePath);
        ReopenAndResaveToFixNetworkObjectHashes(Stage04ScenePath);
        Debug.Log($"[FlowSetup] Stage04 씬 생성 완료: {Stage04ScenePath}");
    }

    // 원래 Alteruna 템플릿의 기본 Unity 아이콘을 그대로 쓰고 있었다 -- 빌드된 .exe가
    // 작업표시줄/탐색기에서 다른 아무 Unity 프로젝트와 똑같이 보이는 건 "완성된 상용
    // 게임"이 아니라 "프로토타입"이라는 인상을 가장 먼저 준다. NetworkSetupMenu의
    // 라운드 스프라이트 생성과 같은 방식(픽셀 단위 SDF 유사 거리 계산)으로, 플레이어
    // 캐릭터 스프라이트와 톤을 맞춘(초록 채움 + 어두운 테두리 링 + 흰 눈 2개) 원형
    // 배지 아이콘을 절차적으로 생성해 Standalone 빌드 타겟에 등록한다.
    [MenuItem("Tools/Coop Setup/Branding/Generate App Icon")]
    public static void GenerateAppIcon()
    {
        int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone);
        if (sizes == null || sizes.Length == 0) sizes = new[] { 256 };

        Texture2D[] icons = new Texture2D[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            icons[i] = DrawAppIconTexture(Mathf.Max(16, sizes[i]));
        }

        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, icons);
        Debug.Log($"[FlowSetup] 앱 아이콘 {icons.Length}개 크기로 생성 완료 (Standalone).");
    }

    private static Texture2D DrawAppIconTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerRadius = size * 0.47f;
        float ringInnerRadius = size * 0.36f;
        float eyeOffsetX = size * 0.13f;
        float eyeOffsetY = size * 0.05f;
        float eyeRadius = Mathf.Max(1f, size * 0.05f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float dist = Vector2.Distance(p, center);

                Color color;
                if (dist > outerRadius)
                {
                    color = new Color(0f, 0f, 0f, 0f);
                }
                else if (dist > ringInnerRadius)
                {
                    color = UITheme.ColorFg;
                }
                else
                {
                    bool leftEye = Vector2.Distance(p, center + new Vector2(-eyeOffsetX, eyeOffsetY)) < eyeRadius;
                    bool rightEye = Vector2.Distance(p, center + new Vector2(eyeOffsetX, eyeOffsetY)) < eyeRadius;
                    color = (leftEye || rightEye) ? UITheme.ColorWhite : UITheme.ColorPrimary;
                }

                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    [MenuItem("Tools/Coop Setup/Multiplayer Flow/4. Configure Build Settings Scenes")]
    public static void ConfigureBuildSettingsScenes()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(BootstrapScenePath, true),
            new EditorBuildSettingsScene(LobbyScenePath, true),
            new EditorBuildSettingsScene(Stage01ScenePath, true),
            new EditorBuildSettingsScene(Stage02ScenePath, true),
            new EditorBuildSettingsScene(Stage03ScenePath, true),
            new EditorBuildSettingsScene(Stage04ScenePath, true),
        };
        Debug.Log("[FlowSetup] Build Settings 씬 목록: Bootstrap(0, 부팅 씬) -> Lobby -> Stage01~04.");
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

    // AddComponent<NetworkObject>() 직후 같은 배치 실행 안에서 바로 SaveScene()하면,
    // NetworkObject.OnValidate()의 GlobalObjectIdHash 계산이 아직 안 끝난 상태(0)로
    // 저장돼버린다 -- 씬 하나에 scene-placed NetworkObject가 2개 이상이면 전부 해시
    // 0으로 저장되고, 나중에 그 씬을 로드할 때 NGO가 "이미 같은 GlobalObjectIdHash를
    // 가진 오브젝트가 있다"는 예외를 던진다(헤드리스 스모크 테스트로 Stage01의
    // CoopButton1/CoopDoor1/GoalZone1이 전부 해시 0으로 저장된 걸 실측 확인).
    // 저장한 씬을 곧바로 다시 열고 한 번 더 저장하면, 그 사이의 씬 로드가 각
    // NetworkObject의 OnValidate를 다시 트리거해서 정상적인 고유 해시가 계산된다
    // -- 실측으로 검증된 수정법. 모든 Create*Scene() 메서드가 이 저장 직후 호출한다.
    private static void ReopenAndResaveToFixNetworkObjectHashes(string path)
    {
        Scene reopened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(reopened, path);
    }

    // withAudioListener는 Bootstrap의 카메라(CreateBootstrapScene)에서만 true로 준다.
    // Bootstrap은 절대 언로드되지 않으므로 그 AudioListener 하나가 Lobby/Stage가
    // 오가는 내내 항상 살아있다 -- 모든 SFX/음악을 spatialBlend=0(비공간 2D)으로
    // 재생하는 AudioManager 입장에서는 리스너 위치 자체가 무의미하므로, 스테이지마다
    // 카메라를 따로 둬도 리스너는 하나만 있으면 충분하다. 예전엔 스테이지 카메라마다
    // 이 컴포넌트를 하나씩 더 붙여서, Lobby/Stage가 Bootstrap과 함께 로드될 때마다
    // "There are 2 audio listeners" 경고가 매 프레임 콘솔/서버 로그에 쌓이는 문제가
    // 있었다(실측: 헤드리스 스모크 테스트 로그가 수백만 줄로 불어남).
    private static void CreateOrthoCamera(bool withFollow, bool withAudioListener = false)
    {
        GameObject camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        Camera cam = camObj.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6f;
        if (withAudioListener)
        {
            camObj.AddComponent<AudioListener>();
        }
        if (withFollow)
        {
            camObj.AddComponent<CoopCameraFollow>();
        }
    }

    private static void BuildConnectUI(GameObject networkManagerObj, PauseMenuUI pauseMenu)
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

        // 화면 우하단 작은 설정 버튼 -- 예전엔 음량 조절이 ESC로 여는 일시정지
        // 메뉴에만 있어서, 아직 접속도 안 한 상태에서 "귀 아픈데 어떻게 줄이지"를
        // 알아낼 방법이 키보드 단축키를 우연히 눌러보는 것뿐이었다. 접속 전에도
        // 바로 보이는 버튼 하나로 발견 가능하게 한다.
        if (pauseMenu != null)
        {
            GameObject settingsButtonObj = new GameObject("ConnectScreenSettingsButton");
            settingsButtonObj.transform.SetParent(canvasObj.transform, false);
            RectTransform settingsButtonRect = settingsButtonObj.AddComponent<RectTransform>();
            settingsButtonRect.anchorMin = new Vector2(1f, 0f);
            settingsButtonRect.anchorMax = new Vector2(1f, 0f);
            settingsButtonRect.pivot = new Vector2(1f, 0f);
            settingsButtonRect.anchoredPosition = new Vector2(-40f, 40f);
            settingsButtonRect.sizeDelta = new Vector2(160f, 56f);

            Image settingsButtonImage = settingsButtonObj.AddComponent<Image>();
            settingsButtonImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_ButtonPrimary.png", UITheme.ColorIce, UITheme.ColorWhite);
            settingsButtonImage.type = Image.Type.Sliced;

            Button settingsButton = settingsButtonObj.AddComponent<Button>();
            settingsButton.targetGraphic = settingsButtonImage;

            Text settingsLabel = CreateLabel(settingsButtonObj.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 56f),
                24, UITheme.ColorWhite, TextAnchor.MiddleCenter, bodyBoldFont, FontStyle.Bold);
            settingsLabel.text = "설정";

            UnityEditor.Events.UnityEventTools.AddPersistentListener(settingsButton.onClick, pauseMenu.OpenSettingsDirectly);
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

    // 일시정지/설정/접속-끊김 오버레이가 공통으로 쓰는 배지형 버튼 -- ConnectButton과
    // 동일한 UI_ButtonPrimary.png 스타일(초록 채움 + 흰 테두리)을 재사용한다.
    private static Button CreateMenuButton(Transform parent, string name, Vector2 anchoredPosition, string label)
    {
        GameObject buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);
        RectTransform rect = buttonObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(320f, 70f);
        rect.anchoredPosition = anchoredPosition;

        Image image = buttonObj.AddComponent<Image>();
        image.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_ButtonPrimary.png", UITheme.ColorPrimary, UITheme.ColorWhite);
        image.type = Image.Type.Sliced;

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = image;

        Text text = CreateLabel(buttonObj.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 70f),
            28, UITheme.ColorWhite, TextAnchor.MiddleCenter, bodyBoldFont, FontStyle.Bold);
        text.text = label;

        return button;
    }

    // 표준 Unity UI Slider 계층(Background/Fill Area/Fill/Handle Slide Area/Handle)을
    // 스타일 가이드 톤(초록 채움, 옅은 민트 트랙)으로 코드에서 조립한다 -- 인스펙터로
    // 손으로 만드는 대신, 이 프로젝트의 다른 UI와 마찬가지로 전부 스크립트로 생성된다.
    private static Slider CreateSlider(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float initialValue)
    {
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);
        RectTransform sliderRect = sliderObj.AddComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRect.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRect.sizeDelta = size;
        sliderRect.anchoredPosition = anchoredPosition;

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.direction = Slider.Direction.LeftToRight;

        GameObject background = new GameObject("Background");
        background.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRect = background.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.2f);
        bgRect.anchorMax = new Vector2(1f, 0.8f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImage = background.AddComponent<Image>();
        bgImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_SliderTrack.png", UITheme.ColorBgSecondary, UITheme.ColorFg, 12f, 3f);
        bgImage.type = Image.Type.Sliced;

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.2f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.8f);
        fillAreaRect.offsetMin = new Vector2(6f, 0f);
        fillAreaRect.offsetMax = new Vector2(-6f, 0f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = fill.AddComponent<Image>();
        fillImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_SliderFill.png", UITheme.ColorPrimary, UITheme.ColorPrimary, 12f, 0f);
        fillImage.type = Image.Type.Sliced;

        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0f, 0f);
        handleAreaRect.anchorMax = new Vector2(1f, 1f);
        handleAreaRect.offsetMin = new Vector2(12f, 0f);
        handleAreaRect.offsetMax = new Vector2(-12f, 0f);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(24f, size.y);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_SliderHandle.png", UITheme.ColorWhite, UITheme.ColorFg, 10f, 3f);
        handleImage.type = Image.Type.Sliced;

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.SetValueWithoutNotify(initialValue);

        return slider;
    }

    // ESC로 여는 일시정지 메뉴 + 설정 서브패널. Bootstrap 씬에 배치되어 Lobby/Stage
    // 전환과 무관하게 항상 존재한다. 온라인 협동 게임이라 Time.timeScale은 건드리지
    // 않는다 (PauseMenuUI.cs 헤더 주석 참고).
    private static PauseMenuUI BuildPauseMenuUI()
    {
        LoadThemeFonts();

        GameObject canvasObj = new GameObject("PauseMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObj.AddComponent<GraphicRaycaster>();

        PauseMenuUI pauseMenu = canvasObj.AddComponent<PauseMenuUI>();

        GameObject pausePanel = new GameObject("PausePanel");
        pausePanel.transform.SetParent(canvasObj.transform, false);
        RectTransform pauseRect = pausePanel.AddComponent<RectTransform>();
        pauseRect.anchorMin = Vector2.zero;
        pauseRect.anchorMax = Vector2.one;
        pauseRect.offsetMin = Vector2.zero;
        pauseRect.offsetMax = Vector2.zero;
        Image scrim = pausePanel.AddComponent<Image>();
        scrim.sprite = NetworkSetupMenu.GetOrCreatePlaceholderSprite();
        scrim.color = UITheme.ColorScrim;

        GameObject card = new GameObject("Card");
        card.transform.SetParent(pausePanel.transform, false);
        RectTransform cardRect = card.AddComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(480f, 460f);
        Image cardImage = card.AddComponent<Image>();
        cardImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_CardPanel.png", UITheme.ColorBg, UITheme.ColorFg, 40f, 6f);
        cardImage.type = Image.Type.Sliced;

        CreateLabel(card.transform, "PauseTitle", new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(400f, 60f),
            40, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold).text = "일시정지";

        Button resumeButton = CreateMenuButton(card.transform, "ResumeButton", new Vector2(0f, 70f), "계속하기");
        Button settingsButton = CreateMenuButton(card.transform, "SettingsButton", new Vector2(0f, -20f), "설정");
        Button quitButton = CreateMenuButton(card.transform, "QuitButton", new Vector2(0f, -110f), "게임 종료");

        UnityEditor.Events.UnityEventTools.AddPersistentListener(resumeButton.onClick, pauseMenu.OnResumeClicked);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(settingsButton.onClick, pauseMenu.OnSettingsClicked);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(quitButton.onClick, pauseMenu.OnQuitClicked);

        // 설정 서브패널 -- 같은 스크림 위, Pause 카드와 같은 자리에 겹쳐 뜬다.
        GameObject settingsPanel = new GameObject("SettingsPanel");
        settingsPanel.transform.SetParent(pausePanel.transform, false);
        RectTransform settingsRect = settingsPanel.AddComponent<RectTransform>();
        settingsRect.anchorMin = new Vector2(0.5f, 0.5f);
        settingsRect.anchorMax = new Vector2(0.5f, 0.5f);
        settingsRect.sizeDelta = new Vector2(480f, 460f);
        Image settingsImage = settingsPanel.AddComponent<Image>();
        settingsImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_CardPanel.png", UITheme.ColorBg, UITheme.ColorFg, 40f, 6f);
        settingsImage.type = Image.Type.Sliced;

        CreateLabel(settingsPanel.transform, "SettingsTitle", new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(400f, 60f),
            40, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold).text = "설정";

        CreateLabel(settingsPanel.transform, "MasterLabel", new Vector2(0.5f, 0.5f), new Vector2(-140f, 90f), new Vector2(200f, 36f),
            24, UITheme.ColorFg, TextAnchor.MiddleLeft, bodyMediumFont).text = "전체 음량";
        Slider masterSlider = CreateSlider(settingsPanel.transform, "MasterSlider", new Vector2(100f, 90f), new Vector2(220f, 24f), 1f);

        CreateLabel(settingsPanel.transform, "MusicLabel", new Vector2(0.5f, 0.5f), new Vector2(-140f, 20f), new Vector2(200f, 36f),
            24, UITheme.ColorFg, TextAnchor.MiddleLeft, bodyMediumFont).text = "음악";
        Slider musicSlider = CreateSlider(settingsPanel.transform, "MusicSlider", new Vector2(100f, 20f), new Vector2(220f, 24f), 0.5f);

        CreateLabel(settingsPanel.transform, "SfxLabel", new Vector2(0.5f, 0.5f), new Vector2(-140f, -50f), new Vector2(200f, 36f),
            24, UITheme.ColorFg, TextAnchor.MiddleLeft, bodyMediumFont).text = "효과음";
        Slider sfxSlider = CreateSlider(settingsPanel.transform, "SfxSlider", new Vector2(100f, -50f), new Vector2(220f, 24f), 0.8f);

        Button backButton = CreateMenuButton(settingsPanel.transform, "BackButton", new Vector2(0f, -170f), "뒤로");

        UnityEditor.Events.UnityEventTools.AddPersistentListener(backButton.onClick, pauseMenu.CloseSettings);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(masterSlider.onValueChanged, pauseMenu.OnMasterVolumeChanged);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(musicSlider.onValueChanged, pauseMenu.OnMusicVolumeChanged);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(sfxSlider.onValueChanged, pauseMenu.OnSfxVolumeChanged);

        settingsPanel.SetActive(false);
        pauseMenu.pausePanel = pausePanel;
        pauseMenu.settingsPanel = settingsPanel;
        pauseMenu.masterSlider = masterSlider;
        pauseMenu.musicSlider = musicSlider;
        pauseMenu.sfxSlider = sfxSlider;

        pausePanel.SetActive(false);
        return pauseMenu;
    }

    // Lobby<->Stage 씬 전환(Unload -> 전원 대기 -> Load) 사이의 공백을 채우는 로딩
    // 오버레이. sortingOrder를 기본 UI보다는 높고 일시정지/접속-끊김 오버레이보다는
    // 낮게 잡아, 둘 중 하나가 동시에 열려 있어도 더 급한 정보가 항상 위에 뜨게 한다.
    private static void BuildLoadingScreenUI()
    {
        LoadThemeFonts();

        GameObject canvasObj = new GameObject("LoadingScreenCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObj.AddComponent<GraphicRaycaster>();

        LoadingScreenUI loadingScreen = canvasObj.AddComponent<LoadingScreenUI>();

        GameObject overlayPanel = new GameObject("OverlayPanel");
        overlayPanel.transform.SetParent(canvasObj.transform, false);
        RectTransform overlayRect = overlayPanel.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image bg = overlayPanel.AddComponent<Image>();
        bg.sprite = NetworkSetupMenu.GetOrCreatePlaceholderSprite();
        bg.color = UITheme.ColorBgSecondary;

        Text message = CreateLabel(overlayPanel.transform, "MessageLabel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 100f),
            36, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);
        message.text = "로딩 중";

        loadingScreen.overlayPanel = overlayPanel;
        loadingScreen.messageText = message;

        overlayPanel.SetActive(false);
    }

    // 의도치 않은 접속 끊김(서버 다운/네트워크 문제)을 알려주는 전체 화면 오버레이.
    // sortingOrder를 일시정지 메뉴보다 높여서, 일시정지가 열려 있는 도중에 끊겨도
    // 이 오버레이가 항상 위에 뜬다.
    private static void BuildConnectionStatusUI()
    {
        LoadThemeFonts();

        GameObject canvasObj = new GameObject("ConnectionStatusCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObj.AddComponent<GraphicRaycaster>();

        ConnectionStatusUI statusUI = canvasObj.AddComponent<ConnectionStatusUI>();

        GameObject overlayPanel = new GameObject("OverlayPanel");
        overlayPanel.transform.SetParent(canvasObj.transform, false);
        RectTransform overlayRect = overlayPanel.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image scrim = overlayPanel.AddComponent<Image>();
        scrim.sprite = NetworkSetupMenu.GetOrCreatePlaceholderSprite();
        scrim.color = UITheme.ColorScrimUrgent;

        GameObject card = new GameObject("Card");
        card.transform.SetParent(overlayPanel.transform, false);
        RectTransform cardRect = card.AddComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(520f, 360f);
        Image cardImage = card.AddComponent<Image>();
        cardImage.sprite = NetworkSetupMenu.GetOrCreateRoundedRectSprite("Assets/Sprites/UI_CardPanel.png", UITheme.ColorBg, UITheme.ColorFg, 40f, 6f);
        cardImage.type = Image.Type.Sliced;

        Text message = CreateLabel(card.transform, "MessageLabel", new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(440f, 140f),
            30, UITheme.ColorFg, TextAnchor.MiddleCenter, bodyBoldFont, FontStyle.Bold);
        message.text = "서버와의 연결이 끊어졌습니다.";

        Button retryButton = CreateMenuButton(card.transform, "RetryButton", new Vector2(0f, -40f), "재접속");
        Button quitButton = CreateMenuButton(card.transform, "QuitButton", new Vector2(0f, -120f), "게임 종료");

        UnityEditor.Events.UnityEventTools.AddPersistentListener(retryButton.onClick, statusUI.OnRetryClicked);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(quitButton.onClick, statusUI.OnQuitClicked);

        statusUI.overlayPanel = overlayPanel;
        statusUI.messageText = message;

        overlayPanel.SetActive(false);
    }
}
#endif
