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

        Text clearText = CreateLabel(banner.transform, "ClearText", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 220f),
            110, UITheme.ColorPrimary, TextAnchor.MiddleCenter, headingFont, FontStyle.Bold);

        StageClearUI clearUI = canvasObj.AddComponent<StageClearUI>();
        clearUI.goalZone = goalZone;
        clearUI.clearBanner = banner;
        clearUI.clearText = clearText;
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
        Debug.Log($"[FlowSetup] Stage04 씬 생성 완료: {Stage04ScenePath}");
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
