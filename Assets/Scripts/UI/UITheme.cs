using UnityEngine;

// CLAUDE.md의 "UI Style Guide" 절에 있는 디자인 토큰을 코드로 옮긴 단일 출처.
// 새 UI를 만들 때 색/폰트 값을 여기서 가져다 쓰고, 직접 하드코딩하지 않는다 --
// 값 자체는 https://picoparkgame.com/en/pp2/ 의 실제 bundle.css에서 그대로 추출한 것이다.
public static class UITheme
{
    // 색상 -- PICO PARK 2 시리즈 컬러(초록). PP1(주황)/Super Pico Park(파랑)와 섞지 않는다.
    public static readonly Color ColorPrimary = new Color32(0x48, 0xad, 0x15, 0xff);
    public static readonly Color ColorIce = new Color32(0x4d, 0xd6, 0xfa, 0xff);
    public static readonly Color ColorBg = new Color32(0xff, 0xff, 0xff, 0xff);
    public static readonly Color ColorBgSecondary = new Color32(0xe9, 0xfb, 0xdf, 0xff);
    public static readonly Color ColorFg = new Color32(0x13, 0x2e, 0x05, 0xff);
    public static readonly Color ColorWhite = new Color32(0xff, 0xff, 0xff, 0xff);
    public static readonly Color ColorHighlightRing = new Color32(0xbf, 0xff, 0xde, 0xff);

    // 모달 오버레이 뒤에 까는 반투명 검정 스크림 -- 밝은 UI Style Guide의 유일한
    // 예외(hazard 빨간색과 같은 성격): 모달 포커스를 위한 어두운 오버레이는 브랜드
    // 팔레트와 무관하다. 접속 끊김처럼 더 급한 정보는 Urgent로 더 어둡게 깐다.
    public static readonly Color ColorScrim = new Color(0f, 0f, 0f, 0.55f);
    public static readonly Color ColorScrimUrgent = new Color(0f, 0f, 0f, 0.7f);

    // 스타일 가이드가 명시하는 "다양성은 새 색을 늘리는 게 아니라 이 토큰들의
    // 명도/불투명도를 섞는 데서 나온다"를 실제로 구현하는 유일한 통로 -- 새 UI가
    // 옅은/흐린 톤이 필요할 때마다 new Color(token.r, token.g, token.b, alpha)를
    // 각자 인라인으로 만들지 않고 이걸 거치게 한다.
    public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

    // 모양 -- 버튼/카드 기본 라운드는 6px, 배지형 버튼의 테두리는 3px.
    public const int CornerRadiusDefault = 6;
    public const int CornerRadiusSmall = 3;
    public const int BorderThicknessDefault = 3;

    // 폰트 -- Dosis는 라틴 전용이라 제목/로고 등 영문에만 쓴다. 한글이 들어가는
    // 버튼/라벨은 M PLUS 1p를 쓴다 (한글 글리프를 지원하는 유일한 테마 폰트).
    public const string FontHeadingPath = "Assets/Fonts/Dosis-ExtraBold.ttf";
    public const string FontBodyBoldPath = "Assets/Fonts/MPLUS1p-Bold.ttf";
    public const string FontBodyMediumPath = "Assets/Fonts/MPLUS1p-Medium.ttf";
}
