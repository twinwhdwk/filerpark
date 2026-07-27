#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;

// Tools 메뉴에서 클릭 한 번으로 NetworkManager 오브젝트를 조립한다.
// GUI를 직접 조작할 수 없는 상황에서, Unity 자신의 오브젝트 모델(AddComponent 등)을
// 통해 만들기 때문에 씬 파일을 손으로 편집하는 것보다 안전하다.
public static class NetworkSetupMenu
{
    private const string ServerAddress = "34.50.24.161";
    private const ushort ServerPort = 7777;
    private const string BotExePathKey = "Filerpark.BotClientExePath";

    // NGO 2.x의 NetworkManager Inspector에는 (1.x와 달리) Play 모드에서 자동으로
    // 뜨는 Start Host/Server/Client 버튼이 없다. 그 존재 여부를 더 알아보는 대신,
    // 여기 메뉴 항목으로 바로 대체한다 -- Play 모드에서만 활성화된다.
    [MenuItem("Tools/Coop Setup/Debug: Start Host (Play 모드 중에만)")]
    public static void DebugStartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton이 없습니다. Play 모드인지, 씬에 NetworkManager가 있는지 확인하세요.");
            return;
        }

        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
        {
            Debug.LogWarning("이미 Host/Server/Client가 실행 중입니다.");
            return;
        }

        bool started = NetworkManager.Singleton.StartHost();
        Debug.Log(started ? "StartHost() 성공 -- 캐릭터가 스폰됐는지 Game 뷰를 확인하세요." : "StartHost() 실패.");
    }

    [MenuItem("Tools/Coop Setup/Debug: Start Host (Play 모드 중에만)", true)]
    public static bool ValidateDebugStartHost()
    {
        return EditorApplication.isPlaying;
    }

    [MenuItem("Tools/Coop Setup/1. Create NetworkManager")]
    public static void CreateNetworkManager()
    {
        GameObject existing = GameObject.Find("NetworkManager");
        if (existing != null)
        {
            Debug.LogWarning("NetworkManager 오브젝트가 이미 씬에 있어서 새로 만들지 않았습니다. 기존 오브젝트를 선택합니다.");
            Selection.activeGameObject = existing;
            return;
        }

        GameObject go = new GameObject("NetworkManager");

        NetworkManager networkManager = go.AddComponent<NetworkManager>();
        UnityTransport transport = go.AddComponent<UnityTransport>();

        transport.ConnectionData.Address = ServerAddress;
        transport.ConnectionData.Port = ServerPort;

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = true;

        go.AddComponent<NetworkBootstrapper>();

        Undo.RegisterCreatedObjectUndo(go, "Create NetworkManager");
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"NetworkManager 생성 완료 -- UnityTransport {ServerAddress}:{ServerPort}, NetworkBootstrapper 부착됨. Ctrl+S로 씬 저장하세요.");
    }

    [MenuItem("Tools/Coop Setup/2. Create Player Prefab")]
    public static void CreatePlayerPrefab()
    {
        EnsureSceneOpen();

        const string prefabFolder = "Assets/Prefabs";
        const string prefabPath = prefabFolder + "/Player.prefab";
        EnsureFolder(prefabFolder);

        Sprite characterSprite = GetOrCreatePlayerCharacterSprite();

        GameObject go = new GameObject("Player");
        go.tag = "Player";
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
        {
            go.layer = playerLayer;
        }

        SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = characterSprite;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.gravityScale = 3f;

        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.9f, 0.9f);

        go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkTransform>();

        PlayerMovementNGO movement = go.AddComponent<PlayerMovementNGO>();
        go.AddComponent<PlayerSetupNGO>();
        go.AddComponent<PlayerColorNGO>();
        go.AddComponent<PlayerNicknameNGO>();
        go.AddComponent<BotController>();

        // 머리 위 "P1"/"P2" 번호표 -- 색만으로는 구분하기 어려운 상황(작은 화면, 색약
        // 등)에서도 누가 누군지 바로 알아보게 한다. World Space 캔버스라 스케일을
        // 아주 작게(0.01) 잡아야 RectTransform의 UI 단위(수백)가 월드 유닛 몇 개로
        // 줄어든다.
        GameObject labelCanvasObj = new GameObject("NameLabelCanvas");
        labelCanvasObj.transform.SetParent(go.transform, false);
        labelCanvasObj.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        labelCanvasObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        Canvas labelCanvas = labelCanvasObj.AddComponent<Canvas>();
        labelCanvas.renderMode = RenderMode.WorldSpace;
        RectTransform labelCanvasRect = labelCanvasObj.GetComponent<RectTransform>();
        labelCanvasRect.sizeDelta = new Vector2(200f, 60f);

        GameObject labelTextObj = new GameObject("NameLabelText");
        labelTextObj.transform.SetParent(labelCanvasObj.transform, false);
        RectTransform labelTextRect = labelTextObj.AddComponent<RectTransform>();
        labelTextRect.anchorMin = Vector2.zero;
        labelTextRect.anchorMax = Vector2.one;
        labelTextRect.offsetMin = Vector2.zero;
        labelTextRect.offsetMax = Vector2.zero;
        Text labelText = labelTextObj.AddComponent<Text>();
        labelText.font = AssetDatabase.LoadAssetAtPath<Font>(UITheme.FontBodyBoldPath);
        labelText.fontSize = 40;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.fontStyle = FontStyle.Bold;
        labelText.color = Color.white;
        Outline labelOutline = labelTextObj.AddComponent<Outline>();
        labelOutline.effectColor = UITheme.ColorFg;
        labelOutline.effectDistance = new Vector2(2f, -2f);

        PlayerLabelNGO labelScript = go.AddComponent<PlayerLabelNGO>();
        labelScript.label = labelText;

        GameObject groundCheck = new GameObject("groundCheck");
        groundCheck.transform.SetParent(go.transform);
        groundCheck.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        movement.groundCheck = groundCheck.transform;

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0)
        {
            movement.groundLayer = 1 << groundLayer;
        }

        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(go, prefabPath, out bool success);
        Object.DestroyImmediate(go);

        if (!success || prefabAsset == null)
        {
            Debug.LogError("Player 프리팹 저장에 실패했습니다.");
            return;
        }

        GameObject networkManagerObj = GameObject.Find("NetworkManager");
        if (networkManagerObj != null)
        {
            NetworkManager networkManager = networkManagerObj.GetComponent<NetworkManager>();
            if (networkManager != null)
            {
                networkManager.NetworkConfig.PlayerPrefab = prefabAsset;
                EditorUtility.SetDirty(networkManagerObj);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }

        EditorUtility.SetDirty(prefabAsset);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Selection.activeObject = prefabAsset;

        Debug.Log($"Player 프리팹 생성 완료: {prefabPath}. NetworkManager의 Player Prefab 슬롯에 자동 연결을 시도했습니다 -- Inspector에서 실제로 채워졌는지 꼭 확인해주세요.");
    }

    [MenuItem("Tools/Coop Setup/3. Setup Main Camera")]
    public static void SetupMainCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("씬에 Main Camera가 없습니다. Hierarchy에 'MainCamera' 태그가 붙은 카메라가 있는지 확인하세요.");
            return;
        }

        mainCamera.orthographic = true;
        mainCamera.orthographicSize = 5f;

        GameObject camObj = mainCamera.gameObject;
        if (camObj.GetComponent<CoopCameraFollow>() == null)
        {
            camObj.AddComponent<CoopCameraFollow>();
        }

        EditorUtility.SetDirty(camObj);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("Main Camera를 Orthographic으로 바꾸고 CoopCameraFollow를 부착했습니다. Ctrl+S로 저장하세요.");
    }

    [MenuItem("Tools/Coop Setup/4. Create Test Ground")]
    public static void CreateTestGround()
    {
        if (GameObject.Find("TestGround") != null)
        {
            Debug.LogWarning("TestGround가 이미 씬에 있어서 새로 만들지 않았습니다.");
            return;
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
        {
            Debug.LogError("Ground 레이어가 없습니다. ProjectSettings/TagManager.asset을 확인하세요.");
            return;
        }

        GameObject ground = new GameObject("TestGround");
        ground.layer = groundLayer;
        ground.transform.position = new Vector3(0f, -3f, 0f);
        ground.transform.localScale = new Vector3(12f, 1f, 1f);

        SpriteRenderer spriteRenderer = ground.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetOrCreatePlaceholderSprite();
        spriteRenderer.color = new Color(0.4f, 0.3f, 0.2f);

        BoxCollider2D collider = ground.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;

        GameObject spawnPoint = new GameObject("SpawnPoint1");
        spawnPoint.tag = "SpawnPoint";
        spawnPoint.transform.position = new Vector3(0f, 0f, 0f);

        Undo.RegisterCreatedObjectUndo(ground, "Create Test Ground");
        Selection.activeGameObject = ground;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("TestGround(바닥)와 SpawnPoint1을 생성했습니다. Ctrl+S로 저장하세요.");
    }

    // 실제 빌드에서는 Editor의 Tools 메뉴가 존재하지 않으므로, StartClient()를 부를
    // 진짜 UI 버튼이 필요하다. 예전에는 TMP Essentials 임포트 문제 때문에 레거시
    // UI.Text + 내장 폰트로만 만들었는데, 레거시 Text도 커스텀 TrueType 폰트를 직접
    // 받을 수 있어(TMP 전용 기능이 아님) UI Style Guide의 실제 폰트(Dosis/M PLUS 1p)를
    // TMP 없이도 적용할 수 있다.
    //
    // CreateNetworkManager 등과 달리 "이미 있으면 스킵"이 아니라 "이미 있으면 다시
    // 칠한다" -- 테마 토큰이 바뀔 때마다 이 메뉴를 다시 실행해서 기존 씬에 반영하는
    // 용도이기 때문에 idempotent update로 동작해야 한다.
    [MenuItem("Tools/Coop Setup/5. Create/Update Connect UI")]
    public static void CreateConnectUI()
    {
        EnsureSceneOpen();

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(eventSystemObj, "Create EventSystem");
        }

        GameObject canvasObj = GameObject.Find("ConnectCanvas");
        bool isNewCanvas = canvasObj == null;
        if (isNewCanvas)
        {
            canvasObj = new GameObject("ConnectCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObj.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasObj, "Create Connect UI");
        }

        // 배경 패널: color-bg-secondary로 화면을 덮어서, 게임 월드가 그대로 비치는
        // 미완성처럼 보이지 않는 제대로 된 시작 화면으로 만든다. sibling index 0으로
        // 고정해 항상 다른 UI 뒤에 깔리게 한다.
        GameObject backgroundObj = FindChild(canvasObj.transform, "BackgroundPanel");
        if (backgroundObj == null)
        {
            backgroundObj = new GameObject("BackgroundPanel");
            backgroundObj.transform.SetParent(canvasObj.transform, false);
        }
        RectTransform backgroundRect = GetOrAddComponent<RectTransform>(backgroundObj);
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image backgroundImage = GetOrAddComponent<Image>(backgroundObj);
        backgroundImage.sprite = GetOrCreatePlaceholderSprite();
        backgroundImage.color = UITheme.ColorBgSecondary;
        backgroundObj.transform.SetSiblingIndex(0);

        // 타이틀: Dosis(라틴 전용) + 대문자 + primary 색 -- 로고/제목 자리. 한글은
        // Dosis에 글리프가 없어서 렌더가 깨지므로, 제목은 영문 워드마크로 둔다.
        GameObject titleObj = FindChild(canvasObj.transform, "TitleLabel");
        if (titleObj == null)
        {
            titleObj = new GameObject("TitleLabel");
            titleObj.transform.SetParent(canvasObj.transform, false);
        }
        RectTransform titleRect = GetOrAddComponent<RectTransform>(titleObj);
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(900f, 160f);
        titleRect.anchoredPosition = new Vector2(0f, 160f);
        Text titleText = GetOrAddComponent<Text>(titleObj);
        titleText.text = "FILER PARK";
        titleText.font = AssetDatabase.LoadAssetAtPath<Font>(UITheme.FontHeadingPath);
        titleText.fontSize = 96;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = UITheme.ColorPrimary;
        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;

        // 버튼: 라운드 사각형 + 3px 테두리 절차적 스프라이트(GetOrCreateRoundedRectSprite),
        // 라벨은 한글을 지원하는 M PLUS 1p Bold.
        GameObject buttonObj = FindChild(canvasObj.transform, "ConnectButton");
        if (buttonObj == null)
        {
            buttonObj = new GameObject("ConnectButton");
            buttonObj.transform.SetParent(canvasObj.transform, false);
        }
        RectTransform buttonRect = GetOrAddComponent<RectTransform>(buttonObj);
        buttonRect.sizeDelta = new Vector2(320f, 90f);
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;

        Image buttonImage = GetOrAddComponent<Image>(buttonObj);
        buttonImage.sprite = GetOrCreateRoundedRectSprite(
            "Assets/Sprites/UI_ButtonPrimary.png",
            UITheme.ColorPrimary,
            UITheme.ColorWhite);
        buttonImage.type = Image.Type.Sliced;

        Button button = GetOrAddComponent<Button>(buttonObj);
        button.targetGraphic = buttonImage;
        // 다시 실행해도 리스너가 중복으로 쌓이지 않도록 매번 새로 만든다.
        button.onClick = new Button.ButtonClickedEvent();

        GameObject textObj = FindChild(buttonObj.transform, "Label");
        if (textObj == null)
        {
            textObj = new GameObject("Label");
            textObj.transform.SetParent(buttonObj.transform, false);
        }
        RectTransform textRect = GetOrAddComponent<RectTransform>(textObj);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        Text label = GetOrAddComponent<Text>(textObj);
        label.text = "서버 접속";
        label.font = AssetDatabase.LoadAssetAtPath<Font>(UITheme.FontBodyBoldPath);
        label.fontSize = 32;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = UITheme.ColorWhite;

        GameObject networkManagerObj = GameObject.Find("NetworkManager");
        NetworkBootstrapper bootstrapper = networkManagerObj != null
            ? networkManagerObj.GetComponent<NetworkBootstrapper>()
            : null;

        if (bootstrapper != null)
        {
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, bootstrapper.ConnectToServer);
            bootstrapper.startMenuUI = canvasObj;
            EditorUtility.SetDirty(networkManagerObj);
        }
        else
        {
            Debug.LogWarning("NetworkManager를 못 찾아서 버튼 OnClick을 자동 연결하지 못했습니다. 먼저 '1. Create NetworkManager'를 실행하세요.");
        }

        Selection.activeGameObject = canvasObj;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("ConnectCanvas 생성/갱신 완료 -- UI Style Guide 색/폰트 적용, 버튼 클릭 시 NetworkBootstrapper.ConnectToServer() 호출.");
    }

    // internal: GameFlowSceneSetup.cs (같은 어셈블리, Assets/Editor/ 아래 asmdef 없음)도
    // 재사용한다 -- Bootstrap/Lobby/Stage 씬 UI를 만들 때 이 헬퍼들을 중복 구현하지 않기 위함.
    internal static GameObject FindChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        return child != null ? child.gameObject : null;
    }

    internal static T GetOrAddComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    private const string ScenePath = "Assets/scense/main.unity";

    // -executeMethod로 헤드리스 실행하면 Editor 실행 시점에 어떤 씬도 자동으로
    // 열리지 않는다 (사람이 GUI로 열어둔 씬을 그대로 쓰는 게 아니라 빈 임시 씬에서
    // 시작함) -- main.unity를 명시적으로 열어야 우리가 만든 NetworkManager 등을
    // 찾을 수 있다.
    private static void EnsureSceneOpen()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }

    [MenuItem("Tools/Coop Setup/6. Create Coop Test (Button/Door/GoalZone)")]
    public static void CreateCoopTest()
    {
        EnsureSceneOpen();

        if (GameObject.Find("CoopButton1") != null)
        {
            Debug.LogWarning("CoopButton1이 이미 씬에 있어서 새로 만들지 않았습니다.");
            return;
        }

        Sprite sprite = GetOrCreatePlaceholderSprite();

        // 버튼: color-primary. 밟으면 바로 문이 열리도록 requiredButtons=1로 둔다 --
        // 봇 2개가 동시에 서로 다른 버튼 두 개를 밟는 걸 보장할 방법이 없어서
        // (같은 스폰에서 출발해 같은 Patrol 로직을 타므로 사실상 같이 움직인다),
        // 이 자동 테스트에서는 "버튼 1개 -> 문" 파이프라인 자체의 동작 검증에 집중한다.
        GameObject button = new GameObject("CoopButton1");
        button.transform.position = new Vector3(2f, -2.2f, 0f);
        SpriteRenderer buttonSprite = button.AddComponent<SpriteRenderer>();
        buttonSprite.sprite = sprite;
        buttonSprite.color = UITheme.ColorPrimary;
        button.transform.localScale = new Vector3(0.6f, 0.2f, 1f);
        BoxCollider2D buttonCollider = button.AddComponent<BoxCollider2D>();
        buttonCollider.isTrigger = true;
        button.AddComponent<NetworkObject>();
        CoopButtonNGO buttonScript = button.AddComponent<CoopButtonNGO>();

        // 문: 색은 CoopDoorNGO.UpdateDoorVisuals가 OnNetworkSpawn 때 스스로 칠한다
        // (color-ice) -- 여기서는 크기/충돌체만 잡아준다.
        GameObject door = new GameObject("CoopDoor1");
        door.transform.position = new Vector3(4f, -1.7f, 0f);
        SpriteRenderer doorSprite = door.AddComponent<SpriteRenderer>();
        doorSprite.sprite = sprite;
        door.transform.localScale = new Vector3(0.4f, 1.2f, 1f);
        door.AddComponent<BoxCollider2D>();
        door.AddComponent<NetworkObject>();
        CoopDoorNGO doorScript = door.AddComponent<CoopDoorNGO>();
        doorScript.requiredButtons = 1;

        UnityEditor.Events.UnityEventTools.AddPersistentListener(buttonScript.OnButtonPress, doorScript.AddPress);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(buttonScript.OnButtonRelease, doorScript.RemovePress);

        // 골 존: color-bg-secondary, 반투명 트리거. BotController의 Patrol 범위
        // (스폰 기준 ±patrolHalfWidth, 기본 3유닛)를 폭 넉넉히 덮어서 봇들이
        // 패트롤하는 동안 자연스럽게 항상 안에 들어와 있도록 한다.
        GameObject goalZone = new GameObject("GoalZone1");
        goalZone.transform.position = new Vector3(0f, -2.2f, 0f);
        SpriteRenderer goalSprite = goalZone.AddComponent<SpriteRenderer>();
        goalSprite.sprite = sprite;
        goalSprite.color = new Color(UITheme.ColorBgSecondary.r, UITheme.ColorBgSecondary.g, UITheme.ColorBgSecondary.b, 0.4f);
        goalZone.transform.localScale = new Vector3(7f, 1.5f, 1f);
        BoxCollider2D goalCollider = goalZone.AddComponent<BoxCollider2D>();
        goalCollider.isTrigger = true;
        goalZone.AddComponent<NetworkObject>();
        GoalZoneNGO goalScript = goalZone.AddComponent<GoalZoneNGO>();

        GameObject networkManagerObj = GameObject.Find("NetworkManager");
        if (networkManagerObj != null)
        {
            StageManagerNGO stageManager = networkManagerObj.GetComponent<StageManagerNGO>();
            if (stageManager == null)
            {
                stageManager = networkManagerObj.AddComponent<StageManagerNGO>();
            }
            stageManager.goalZone = goalScript;
            EditorUtility.SetDirty(networkManagerObj);
        }
        else
        {
            Debug.LogWarning("NetworkManager를 못 찾아서 StageManagerNGO를 연결하지 못했습니다. 먼저 '1. Create NetworkManager'를 실행하세요.");
        }

        Undo.RegisterCreatedObjectUndo(button, "Create Coop Test");
        Selection.objects = new Object[] { button, door, goalZone };
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("CoopButton1 -> CoopDoor1 -> GoalZone1 배치 및 연결 완료, 씬 저장까지 마쳤습니다.");
    }

    // Docs/Stages/*.md 설계문서의 4개 미션을 StageDefinition 애셋으로 만든다.
    // 이미 있으면 값만 덮어써서(create-or-update) 문서를 고치고 다시 실행하면
    // 애셋도 같이 갱신되게 한다.
    [MenuItem("Tools/Coop Setup/7. Create Stage Catalog (4 Missions)")]
    public static void CreateStageCatalog()
    {
        const string folder = "Assets/StageData";
        EnsureFolder(folder);

        StageDefinition[] stages =
        {
            CreateOrUpdateStage(folder, "Stage01_Gatekeeper", "stage-01", 0,
                "GATEKEEPER", "문지기",
                "N-1개의 버튼을 동시에 밟고 있어야 문이 열린다. 자유로운 1명이 먼저 통과하고, 버튼 담당들이 릴레이로 합류한다.",
                StageTheme.SimultaneousSwitches, 4, 6),
            CreateOrUpdateStage(folder, "Stage02_BlockCarry", "stage-02", 1,
                "BLOCK CARRY", "돌덩이 운반",
                "여럿이 함께 밀어야 움직이는 돌덩이로 발판을 만들고, 그 위로 스택해서 높은 벽을 넘는다.",
                StageTheme.StackAndPush, 4, 6),
            CreateOrUpdateStage(folder, "Stage03_KeyRelay", "stage-03", 2,
                "KEY RELAY", "열쇠 릴레이",
                "공유 열쇠를 릴레이로 날라 문을 열고, 양쪽 무게를 맞춰야 하는 시소 다리를 건넌다.",
                StageTheme.CarryAndBalance, 4, 6),
            CreateOrUpdateStage(folder, "Stage04_EscapeCountdown", "stage-04", 3,
                "ESCAPE COUNTDOWN", "탈출 카운트다운",
                "서서히 차오르는 용암을 피해 전원이 발맞춰 골까지 가야 한다. 낙오자가 생기면 전원 실패.",
                StageTheme.SharedSurvival, 4, 6),
            CreateOrUpdateStage(folder, "Stage05_TwinGatekeeper", "stage-05", 4,
                "TWIN GATEKEEPER", "쌍둥이 문지기",
                "서로 다른 두 사람이 각자의 버튼을 동시에 밟고 있어야 문이 열린다. 문지기보다 한 단계 더 엄격한 동시 입력.",
                StageTheme.SimultaneousSwitches, 4, 6),
        };

        string catalogPath = folder + "/StageCatalog.asset";
        StageCatalog catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(catalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<StageCatalog>();
            AssetDatabase.CreateAsset(catalog, catalogPath);
        }
        catalog.stages = stages;
        EditorUtility.SetDirty(catalog);

        AssetDatabase.SaveAssets();
        Selection.activeObject = catalog;

        Debug.Log($"스테이지 카탈로그 생성/갱신 완료: {catalogPath} ({stages.Length}개 스테이지, Docs/Stages/ 설계문서와 대응).");
    }

    private static StageDefinition CreateOrUpdateStage(string folder, string assetName, string stageId, int order,
        string titleEn, string subtitleKr, string descriptionKr, StageTheme theme, int minPlayers, int maxPlayers)
    {
        string path = $"{folder}/{assetName}.asset";
        StageDefinition stage = AssetDatabase.LoadAssetAtPath<StageDefinition>(path);
        if (stage == null)
        {
            stage = ScriptableObject.CreateInstance<StageDefinition>();
            AssetDatabase.CreateAsset(stage, path);
        }

        stage.stageId = stageId;
        stage.order = order;
        stage.titleEn = titleEn;
        stage.subtitleKr = subtitleKr;
        stage.descriptionKr = descriptionKr;
        stage.theme = theme;
        stage.minPlayers = minPlayers;
        stage.maxPlayers = maxPlayers;
        // assetName은 모든 호출부에서 실제 게임플레이 씬 이름과 그대로 일치한다
        // ("Stage01_Gatekeeper" 등) -- GameFlowManager.stageSceneNames가 참조하는
        // 이름과 같은 문자열이므로 별도 파라미터 없이 재사용한다. 예전엔 이 필드를
        // 애셋 YAML에 직접 손으로 채워 넣었는데, 이 프로젝트의 "전부 스크립트로
        // 생성한다" 원칙(CLAUDE.md "Editor automation")을 깨는 방식이었다 -- 이
        // 메뉴를 다시 실행해도 다음부터는 항상 올바르게 채워진다.
        stage.sceneName = assetName;

        EditorUtility.SetDirty(stage);
        return stage;
    }

    // "게임내 맵 화면"(스테이지 노드 선택) + "게임 스테이지 화면"(선택한 스테이지 상세)을
    // 한 캔버스에 같이 만든다. 실제 게임플레이 로딩은 아직 없으므로(Docs/Stages는 설계
    // 문서 단계) 시작 버튼은 로그만 남기는 자리표시자다 -- WorldMapUI/StageIntroUI의
    // 코드 주석 참고.
    [MenuItem("Tools/Coop Setup/8. Create World Map + Stage Intro UI")]
    public static void CreateWorldMapUI()
    {
        EnsureSceneOpen();

        StageCatalog catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>("Assets/StageData/StageCatalog.asset");
        if (catalog == null || catalog.stages == null || catalog.stages.Length == 0)
        {
            Debug.LogError("StageCatalog가 없습니다. 먼저 '7. Create Stage Catalog (4 Missions)'를 실행하세요.");
            return;
        }

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(eventSystemObj, "Create EventSystem");
        }

        GameObject canvasObj = GameObject.Find("WorldMapCanvas");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("WorldMapCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObj.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasObj, "Create World Map UI");
        }

        WorldMapUI worldMap = GetOrAddComponent<WorldMapUI>(canvasObj);
        worldMap.catalog = catalog;

        // ---- 맵 패널 ----
        GameObject mapPanel = FindChild(canvasObj.transform, "MapPanel");
        if (mapPanel == null)
        {
            mapPanel = new GameObject("MapPanel");
            mapPanel.transform.SetParent(canvasObj.transform, false);
        }
        RectTransform mapPanelRect = GetOrAddComponent<RectTransform>(mapPanel);
        mapPanelRect.anchorMin = Vector2.zero;
        mapPanelRect.anchorMax = Vector2.one;
        mapPanelRect.offsetMin = Vector2.zero;
        mapPanelRect.offsetMax = Vector2.zero;
        Image mapBg = GetOrAddComponent<Image>(mapPanel);
        mapBg.sprite = GetOrCreatePlaceholderSprite();
        mapBg.color = UITheme.ColorBgSecondary;
        worldMap.mapPanel = mapPanel;

        Text mapTitleText = CreateOrGetLabel(mapPanel.transform, "MapTitle", new Vector2(0f, 380f), new Vector2(1200f, 140f),
            UITheme.FontHeadingPath, 64, UITheme.ColorPrimary, TextAnchor.MiddleCenter);
        mapTitleText.text = "STAGE SELECT";
        mapTitleText.fontStyle = FontStyle.Bold;

        // 노드 4개를 가로로 나열. Pico Park 2의 WORLD 모드 스테이지 선택 화면을 참고한 배치.
        int nodeCount = catalog.stages.Length;
        const float spacing = 340f;
        float startX = -spacing * (nodeCount - 1) / 2f;

        for (int i = 0; i < nodeCount; i++)
        {
            StageDefinition stage = catalog.stages[i];
            string nodeName = $"StageNode_{i}";

            GameObject nodeObj = FindChild(mapPanel.transform, nodeName);
            if (nodeObj == null)
            {
                nodeObj = new GameObject(nodeName);
                nodeObj.transform.SetParent(mapPanel.transform, false);
            }
            RectTransform nodeRect = GetOrAddComponent<RectTransform>(nodeObj);
            nodeRect.anchorMin = new Vector2(0.5f, 0.5f);
            nodeRect.anchorMax = new Vector2(0.5f, 0.5f);
            nodeRect.sizeDelta = new Vector2(280f, 200f);
            nodeRect.anchoredPosition = new Vector2(startX + spacing * i, 0f);

            Image nodeImage = GetOrAddComponent<Image>(nodeObj);
            nodeImage.sprite = GetOrCreateRoundedRectSprite("Assets/Sprites/UI_ButtonPrimary.png", UITheme.ColorPrimary, UITheme.ColorWhite);
            nodeImage.type = Image.Type.Sliced;

            Button nodeButton = GetOrAddComponent<Button>(nodeObj);
            nodeButton.targetGraphic = nodeImage;
            nodeButton.onClick = new Button.ButtonClickedEvent();

            StageNodeButton nodeScript = GetOrAddComponent<StageNodeButton>(nodeObj);
            nodeScript.worldMap = worldMap;
            nodeScript.stageIndex = i;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(nodeButton.onClick, nodeScript.NotifyClicked);

            Text nodeLabel = CreateOrGetLabel(nodeObj.transform, "Label", Vector2.zero, new Vector2(260f, 180f),
                UITheme.FontBodyBoldPath, 26, UITheme.ColorWhite, TextAnchor.MiddleCenter);
            nodeLabel.supportRichText = true;
            nodeLabel.text = $"{i + 1}. {stage.titleEn}\n<size=20>{stage.subtitleKr}</size>";
        }

        // ---- 스테이지 인트로 패널 (노드 클릭 시 표시, 기본은 숨김) ----
        GameObject introPanel = FindChild(canvasObj.transform, "StageIntroPanel");
        if (introPanel == null)
        {
            introPanel = new GameObject("StageIntroPanel");
            introPanel.transform.SetParent(canvasObj.transform, false);
        }
        RectTransform introRect = GetOrAddComponent<RectTransform>(introPanel);
        introRect.anchorMin = Vector2.zero;
        introRect.anchorMax = Vector2.one;
        introRect.offsetMin = Vector2.zero;
        introRect.offsetMax = Vector2.zero;
        Image introBg = GetOrAddComponent<Image>(introPanel);
        introBg.sprite = GetOrCreatePlaceholderSprite();
        introBg.color = UITheme.ColorBg;

        StageIntroUI stageIntro = GetOrAddComponent<StageIntroUI>(introPanel);
        worldMap.stageIntro = stageIntro;

        Text introTitle = CreateOrGetLabel(introPanel.transform, "IntroTitle", new Vector2(0f, 260f), new Vector2(1100f, 140f),
            UITheme.FontHeadingPath, 80, UITheme.ColorPrimary, TextAnchor.MiddleCenter);
        introTitle.fontStyle = FontStyle.Bold;
        stageIntro.titleText = introTitle;

        Text introSubtitle = CreateOrGetLabel(introPanel.transform, "IntroSubtitle", new Vector2(0f, 170f), new Vector2(1100f, 80f),
            UITheme.FontBodyBoldPath, 40, UITheme.ColorFg, TextAnchor.MiddleCenter);
        stageIntro.subtitleText = introSubtitle;

        Text introTheme = CreateOrGetLabel(introPanel.transform, "IntroTheme", new Vector2(0f, 110f), new Vector2(1100f, 50f),
            UITheme.FontBodyMediumPath, 26, UITheme.ColorIce, TextAnchor.MiddleCenter);
        stageIntro.themeTagText = introTheme;

        Text introDescription = CreateOrGetLabel(introPanel.transform, "IntroDescription", new Vector2(0f, 0f), new Vector2(1100f, 160f),
            UITheme.FontBodyMediumPath, 28, UITheme.ColorFg, TextAnchor.MiddleCenter);
        stageIntro.descriptionText = introDescription;

        Text introPlayerCount = CreateOrGetLabel(introPanel.transform, "IntroPlayerCount", new Vector2(0f, -100f), new Vector2(1100f, 50f),
            UITheme.FontBodyMediumPath, 26, UITheme.ColorFg, TextAnchor.MiddleCenter);
        stageIntro.playerCountText = introPlayerCount;

        // 시작/뒤로 버튼 -- ConnectButton과 동일한 라운드 스프라이트 재사용.
        Sprite roundedSprite = GetOrCreateRoundedRectSprite("Assets/Sprites/UI_ButtonPrimary.png", UITheme.ColorPrimary, UITheme.ColorWhite);

        GameObject startBtnObj = FindChild(introPanel.transform, "StartButton");
        if (startBtnObj == null)
        {
            startBtnObj = new GameObject("StartButton");
            startBtnObj.transform.SetParent(introPanel.transform, false);
        }
        RectTransform startRect = GetOrAddComponent<RectTransform>(startBtnObj);
        startRect.anchorMin = new Vector2(0.5f, 0.5f);
        startRect.anchorMax = new Vector2(0.5f, 0.5f);
        startRect.sizeDelta = new Vector2(260f, 80f);
        startRect.anchoredPosition = new Vector2(150f, -230f);
        Image startImage = GetOrAddComponent<Image>(startBtnObj);
        startImage.sprite = roundedSprite;
        startImage.type = Image.Type.Sliced;
        Button startButton = GetOrAddComponent<Button>(startBtnObj);
        startButton.targetGraphic = startImage;
        startButton.onClick = new Button.ButtonClickedEvent();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(startButton.onClick, stageIntro.OnStartPressed);
        Text startLabel = CreateOrGetLabel(startBtnObj.transform, "Label", Vector2.zero, new Vector2(260f, 80f),
            UITheme.FontBodyBoldPath, 30, UITheme.ColorWhite, TextAnchor.MiddleCenter);
        startLabel.text = "시작";

        GameObject backBtnObj = FindChild(introPanel.transform, "BackButton");
        if (backBtnObj == null)
        {
            backBtnObj = new GameObject("BackButton");
            backBtnObj.transform.SetParent(introPanel.transform, false);
        }
        RectTransform backRect = GetOrAddComponent<RectTransform>(backBtnObj);
        backRect.anchorMin = new Vector2(0.5f, 0.5f);
        backRect.anchorMax = new Vector2(0.5f, 0.5f);
        backRect.sizeDelta = new Vector2(260f, 80f);
        backRect.anchoredPosition = new Vector2(-150f, -230f);
        Image backImage = GetOrAddComponent<Image>(backBtnObj);
        backImage.sprite = roundedSprite;
        backImage.type = Image.Type.Sliced;
        Button backButton = GetOrAddComponent<Button>(backBtnObj);
        backButton.targetGraphic = backImage;
        backButton.onClick = new Button.ButtonClickedEvent();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(backButton.onClick, worldMap.ReturnToMap);
        Text backLabel = CreateOrGetLabel(backBtnObj.transform, "Label", Vector2.zero, new Vector2(260f, 80f),
            UITheme.FontBodyBoldPath, 30, UITheme.ColorWhite, TextAnchor.MiddleCenter);
        backLabel.text = "뒤로";

        introPanel.SetActive(false);

        Selection.activeGameObject = canvasObj;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"WorldMapCanvas 생성/갱신 완료 -- 스테이지 노드 {nodeCount}개 + 인트로 패널 연결됨. " +
            "실제 스테이지 게임플레이 로딩은 아직 미구현(Docs/Stages 설계 문서 단계)이라 '시작' 버튼은 로그만 남깁니다.");
    }

    internal static Text CreateOrGetLabel(Transform parent, string name, Vector2 anchoredPosition, Vector2 size,
        string fontPath, int fontSize, Color color, TextAnchor alignment)
    {
        GameObject obj = FindChild(parent, name);
        if (obj == null)
        {
            obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
        }

        RectTransform rect = GetOrAddComponent<RectTransform>(obj);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        Text text = GetOrAddComponent<Text>(obj);
        text.font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        return text;
    }

    // 사람 파트너 없이 협동 기믹을 반복 테스트하기 위한 봇 클라이언트 실행기.
    // 실제로는 빌드된 클라이언트 실행 파일을 -bot 인자로 여러 개 띄우는 것 --
    // BotProcess/BotController가 각 프로세스 안에서 자동 접속 + 자동 조작을 담당한다.
    // 순서: Client 빌드 1회 -> 경로 지정 -> Editor Play 모드에서 Start Host -> 봇 실행.
    [MenuItem("Tools/Coop Setup/Bot Simulation/Set Bot Client Build Path...")]
    public static void SetBotClientPath()
    {
        string current = EditorPrefs.GetString(BotExePathKey, "");
        string startDir = !string.IsNullOrEmpty(current) ? Path.GetDirectoryName(current) : Application.dataPath;
        string path = EditorUtility.OpenFilePanel("봇으로 쓸 클라이언트 빌드(.exe) 선택", startDir, "exe");
        if (string.IsNullOrEmpty(path)) return;

        EditorPrefs.SetString(BotExePathKey, path);
        Debug.Log($"봇 클라이언트 경로 설정 완료: {path}");
    }

    [MenuItem("Tools/Coop Setup/Bot Simulation/Launch 1 Bot Client")]
    public static void LaunchOneBot() => LaunchBots(1);

    [MenuItem("Tools/Coop Setup/Bot Simulation/Launch 2 Bot Clients")]
    public static void LaunchTwoBots() => LaunchBots(2);

    [MenuItem("Tools/Coop Setup/Bot Simulation/Launch 3 Bot Clients")]
    public static void LaunchThreeBots() => LaunchBots(3);

    [MenuItem("Tools/Coop Setup/Bot Simulation/Stop All Bot Clients")]
    public static void StopAllBots()
    {
        string exePath = EditorPrefs.GetString(BotExePathKey, "");
        if (string.IsNullOrEmpty(exePath))
        {
            Debug.LogWarning("봇 클라이언트 경로가 지정되어 있지 않아 무엇을 종료할지 알 수 없습니다.");
            return;
        }

        string processName = Path.GetFileNameWithoutExtension(exePath);
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(processName);
        int killed = 0;
        foreach (System.Diagnostics.Process p in processes)
        {
            try
            {
                p.Kill();
                killed++;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"프로세스 종료 실패 (PID {p.Id}): {e.Message}");
            }
        }

        Debug.Log($"봇 클라이언트 {killed}개를 종료했습니다. (같은 이름의 다른 실행 중인 창도 함께 종료됨에 유의)");
    }

    private static void LaunchBots(int count)
    {
        string exePath = EditorPrefs.GetString(BotExePathKey, "");
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Debug.LogError("봇 클라이언트 빌드 경로가 설정되지 않았습니다. 먼저 'Bot Simulation/Set Bot Client Build Path...'로 지정하세요 " +
                "(File > Build Settings > Windows, Mac, Linux 로 일반 클라이언트를 한 번 빌드해두어야 합니다).");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-bot -serverip 127.0.0.1 -serverport 7777",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(startInfo);
        }

        Debug.Log($"봇 클라이언트 {count}개를 실행했습니다 (127.0.0.1:7777로 접속 시도). " +
            "Editor에서 Play 모드로 들어가 'Debug: Start Host'를 먼저 눌러야 접속에 성공합니다.");
    }

    // 라운드 사각형 + 테두리 UI 스프라이트를 절차적으로 만든다 (SDF 기반 rounded-box
    // 거리함수 -- Inigo Quilez의 공식). 9-slice로 늘어나도 모서리가 안 뭉개지도록
    // spriteBorder를 radius+border만큼 잡아준다. UI Style Guide의 "배지/스티커"
    // 버튼 모양(라운드 6px급, 굵은 테두리)을 표현하는 용도.
    internal static Sprite GetOrCreateRoundedRectSprite(string path, Color fillColor, Color borderColor,
        float radius = 28f, float borderThickness = 10f)
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
        {
            return existing;
        }

        EnsureFolder(Path.GetDirectoryName(path).Replace("\\", "/"));

        const int size = 128;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        Vector2 innerHalfSize = center - new Vector2(radius, radius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - innerHalfSize;
                float outsideDist = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                float insideDist = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
                float d = outsideDist + insideDist - radius;

                float outerAlpha = Mathf.Clamp01(0.5f - d);
                // borderT: d가 -borderThickness보다 바깥쪽(테두리 쪽)이면 1(테두리색),
                // 안쪽으로 더 들어가면 0(채움색) -- outerAlpha와 부호가 반대라 따로 계산.
                float borderT = Mathf.Clamp01(0.5f + (d + borderThickness));
                Color color = Color.Lerp(fillColor, borderColor, borderT);
                color.a = outerAlpha;

                pixels[y * size + x] = color;
            }
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
            importer.spritePixelsPerUnit = 100;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            float border = radius + borderThickness;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static readonly int[] EyeSides = { -1, 1 };

    // UI Style Guide의 "둥글고 단순한 블롭 + 굵은 외곽선 + 단순한 점 눈" 아트 스타일을
    // 그대로 따르는 플레이어 캐릭터 스프라이트. 몸통은 흰색으로 그려서
    // PlayerColorNGO의 색 곱연산(tint)이 그대로 먹히게 하고, 외곽선/눈은 거의 검정으로
    // 그려서 어떤 플레이어 색이 곱해져도(검정*무엇이든=검정에 가까움) 항상 또렷하게
    // 남도록 한다.
    internal static Sprite GetOrCreatePlayerCharacterSprite()
    {
        const string path = "Assets/Sprites/PlayerCharacter.png";

        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
        {
            return existing;
        }

        EnsureFolder("Assets/Sprites");

        const int size = 64;
        const float bodyRadius = size * 0.40f;
        const float outlineThickness = size * 0.05f;
        const float eyeRadius = size * 0.075f;
        const float eyeOffsetX = size * 0.16f;
        const float eyeOffsetY = size * 0.10f;
        const float highlightRadius = size * 0.022f;

        Color outlineColor = UITheme.ColorFg;
        Color eyeColor = UITheme.ColorFg;
        Color highlightColor = Color.white;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float distFromCenter = Vector2.Distance(p, center);
                float bodyAlpha = Mathf.Clamp01(0.5f - (distFromCenter - bodyRadius));

                Color pixelColor = Color.clear;
                if (bodyAlpha > 0f)
                {
                    bool isOutlineBand = distFromCenter > bodyRadius - outlineThickness;
                    Color fill = isOutlineBand ? outlineColor : Color.white;
                    pixelColor = new Color(fill.r, fill.g, fill.b, bodyAlpha);
                }

                foreach (int side in EyeSides)
                {
                    Vector2 eyeCenter = center + new Vector2(side * eyeOffsetX, eyeOffsetY);
                    float eyeDist = Vector2.Distance(p, eyeCenter);
                    float eyeAlpha = Mathf.Clamp01(0.5f - (eyeDist - eyeRadius));
                    if (eyeAlpha > 0f)
                    {
                        pixelColor = Color.Lerp(pixelColor, eyeColor, eyeAlpha);
                        pixelColor.a = Mathf.Max(pixelColor.a, eyeAlpha);
                    }

                    Vector2 highlightCenter = eyeCenter + new Vector2(-eyeRadius * 0.35f, eyeRadius * 0.35f);
                    float highlightDist = Vector2.Distance(p, highlightCenter);
                    float highlightAlpha = Mathf.Clamp01(0.5f - (highlightDist - highlightRadius));
                    if (highlightAlpha > 0f)
                    {
                        pixelColor = Color.Lerp(pixelColor, highlightColor, highlightAlpha);
                        pixelColor.a = Mathf.Max(pixelColor.a, highlightAlpha);
                    }
                }

                pixels[y * size + x] = pixelColor;
            }
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
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    internal static Sprite GetOrCreatePlaceholderSprite()
    {
        const string folder = "Assets/Sprites";
        const string path = folder + "/PlayerPlaceholder.png";

        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
        {
            return existing;
        }

        EnsureFolder(folder);

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

    internal static void EnsureFolder(string path)
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
