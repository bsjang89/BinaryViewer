using System.Globalization;

namespace BinaryViewer.Core;

public enum Language { Korean, English }

/// <summary>
/// Every user visible string, in Korean and English. Simple properties rather than resource
/// files so the two languages sit side by side and a typo is a compile error.
/// </summary>
public static class S
{
    private static Language _language = Detect();

    public static event Action? LanguageChanged;

    public static Language Current
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            LanguageChanged?.Invoke();
        }
    }

    private static bool En => _language == Language.English;

    private static Language Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase)
            ? Language.Korean
            : Language.English;

    public static bool TryParse(string? value, out Language language)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "ko" or "kr" or "korean" or "한국어": language = Language.Korean; return true;
            case "en" or "eng" or "english": language = Language.English; return true;
            default: language = _language; return false;
        }
    }

    // ---------------------------------------------------------------- toolbar
    public static string AppTitle => "Binary Viewer";
    public static string Open => En ? "Open" : "열기";
    public static string GoTo => En ? "Go to" : "이동";
    public static string Find => En ? "Find" : "찾기";
    public static string ScanJson => En ? "Scan JSON" : "JSON 스캔";
    public static string ExtractStrings => En ? "Extract strings" : "문자열 추출";
    public static string Stop => En ? "Stop" : "중지";
    public static string PerRow => En ? "Per row" : "줄당";
    public static string AutoRow => En ? "Auto" : "자동";
    public static string AutoScan => En ? "Auto scan" : "자동 스캔";
    public static string Reanalyze => En ? "Re-analyze" : "다시 분석";
    public static string Scan => En ? "Scan" : "스캔";
    public static string Extract => En ? "Extract" : "추출";
    public static string Save => En ? "Save" : "저장";
    public static string Copy => En ? "Copy" : "복사";
    public static string MinLength => En ? "Min length" : "최소 길이";
    public static string Filter => En ? "Filter" : "필터";

    public static string TipOpen => En ? "Ctrl/⌘ + O" : "Ctrl/⌘ + O";
    public static string TipGoTo => En ? "Ctrl/⌘ + G" : "Ctrl/⌘ + G";
    public static string TipFind => En ? "Ctrl/⌘ + F" : "Ctrl/⌘ + F";
    public static string TipScan => En ? "Ctrl/⌘ + J" : "Ctrl/⌘ + J";
    public static string TipThemeDark => En ? "Dark mode (click for light)" : "현재 다크 모드 (클릭하면 라이트)";
    public static string TipThemeLight => En ? "Light mode (click for dark)" : "현재 라이트 모드 (클릭하면 다크)";
    public static string TipLanguage => En ? "한국어로 전환" : "Switch to English";

    // ---------------------------------------------------------------- tabs and panes
    public static string TabAnalysis => En ? "Header" : "헤더 분석";
    public static string TabJson => "JSON";
    public static string TabStrings => En ? "Strings" : "문자열";
    public static string InspectorTitle => En ? "Data inspector · value at cursor" : "데이터 인스펙터 · 커서 위치 해석";

    public static string ColOffset => En ? "Offset" : "오프셋";
    public static string ColSize => En ? "Size" : "길이";
    public static string ColKind => En ? "Kind" : "종류";
    public static string ColEncoding => En ? "Encoding" : "인코딩";
    public static string ColPreview => En ? "Preview" : "미리보기";
    public static string ColText => En ? "Text" : "내용";
    public static string ColType => En ? "Type" : "형식";
    public static string ColLittle => En ? "Little endian" : "리틀 엔디언";
    public static string ColBig => En ? "Big endian" : "빅 엔디언";

    // ---------------------------------------------------------------- status bar
    public static string NoFile => En ? "No file" : "파일 없음";
    public static string Analyzing => En ? "Analyzing…" : "분석 중...";
    public static string ScanningJson => En ? "Scanning for JSON…" : "JSON 스캔 중...";
    public static string ExtractingStrings => En ? "Extracting strings…" : "문자열 추출 중...";
    public static string PatternNotFound => En ? "Pattern not found" : "찾는 패턴 없음";
    public static string MemoryMapped => En ? " (memory mapped)" : " (메모리 맵)";

    public static string OffsetStatus(long offset) =>
        En ? $"Offset 0x{offset:X8} ({offset:N0})" : $"오프셋 0x{offset:X8} ({offset:N0})";

    public static string SelectionStatus(long bytes) =>
        En ? $"  selected {bytes:N0} B" : $"  선택 {bytes:N0} B";

    public static string OpenedIn(string size, double ms) =>
        En ? $"{size} / opened in {ms:F1} ms" : $"{size} / 열기 {ms:F1} ms";

    public static string AnalyzedIn(double ms) =>
        En ? $"(analyzed in {ms:F0} ms)" : $"(분석 {ms:F0} ms)";

    public static string JsonNone(double ms) =>
        En ? $"No JSON found (scanned in {ms:F0} ms)" : $"JSON 없음 (스캔 {ms:F0} ms)";

    public static string JsonFound(int count, string size, double ms) =>
        En ? $"JSON ×{count:N0} / {size} (scanned in {ms:F0} ms)"
           : $"JSON {count:N0}건 / {size} (스캔 {ms:F0} ms)";

    public static string StringsFound(int count, double ms) =>
        En ? $"Strings ×{count:N0} (extracted in {ms:F0} ms)" : $"문자열 {count:N0}건 (추출 {ms:F0} ms)";

    public static string FoundAt(long offset) => En ? $"Found at 0x{offset:X}" : $"찾음: 0x{offset:X}";
    public static string SavedTo(string path) => En ? $"Saved: {path}" : $"저장됨: {path}";
    public static string TruncatedPreview(string size) =>
        En ? $"… (showing the first 4 MB of {size})" : $"... ({size} 중 앞부분 4 MB만 표시)";

    // ---------------------------------------------------------------- dialogs
    public static string OpenFileTitle => En ? "Open binary file" : "바이너리 파일 열기";
    public static string SaveJsonTitle => En ? "Save JSON region" : "JSON 영역 저장";
    public static string OpenFailed => En ? "Could not open file" : "열기 실패";
    public static string Ok => En ? "OK" : "확인";
    public static string Cancel => En ? "Cancel" : "취소";
    public static string Close => En ? "Close" : "닫기";
    public static string GoToTitle => En ? "Go to offset" : "오프셋으로 이동";
    public static string Hexadecimal => En ? "Hex" : "16진수";
    public static string Decimal => En ? "Decimal" : "10진수";
    public static string OutOfRange(long max) =>
        En ? $"Enter a value between 0 and 0x{max:X}." : $"0 ~ 0x{max:X} 범위의 값을 입력하세요.";
    public static string FindTitle => En ? "Find" : "찾기";
    public static string AsText => En ? "Text" : "텍스트";
    public static string AsHex => En ? "Hex (e.g. 50 4B 03 04)" : "Hex (예: 50 4B 03 04)";
    public static string IgnoreCase => En ? "Ignore case" : "대소문자 무시";
    public static string FindNext => En ? "Find next" : "다음 찾기";
    public static string FindPrevious => En ? "Find previous" : "이전 찾기";
    public static string NotHex => En ? "Not a valid hex pattern." : "16진수 형식이 아닙니다.";

    // ---------------------------------------------------------------- analysis report
    public static string RepFile => En ? "File      " : "파일      ";
    public static string RepSize => En ? "Size      " : "크기      ";
    public static string RepSignatures => En ? "[ Header signatures ]" : "[ 헤더 시그니처 ]";
    public static string RepNoSignature => En
        ? "  No known magic number - custom format, or raw data with no header."
        : "  알려진 매직 넘버 없음 - 커스텀 포맷이거나 헤더 없는 raw 데이터일 가능성.";
    public static string RepStats => En ? "[ Statistics ]" : "[ 통계 ]";
    public static string RepHeaderEntropy(int bytes) =>
        En ? $"  Header entropy ({bytes} B)".PadRight(29) + ": " : $"  헤더(첫 {bytes}B) 엔트로피 : ";
    public static string RepOverallEntropy => En ? "  Overall entropy            : " : "  전체 엔트로피              : ";
    public static string RepPrintable => En ? "  Printable characters       : " : "  출력 가능 문자 비율        : ";
    public static string RepZeros => En ? "  0x00 ratio                 : " : "  0x00 비율                  : ";
    public static string RepEncoding => En ? "  Encoding guess             : " : "  인코딩 추정                : ";
    public static string RepFields => En ? "[ Header field candidates (first 64 bytes) ]" : "[ 헤더 필드 후보 (첫 64바이트 해석) ]";
    public static string RepNoFields => En
        ? "  No obvious length, offset or tag fields."
        : "  눈에 띄는 길이/오프셋/태그 필드 없음.";
    public static string RepCarved => En ? "[ Signatures embedded inside ]" : "[ 내부에 박힌 시그니처 ]";
    public static string RepNone => En ? "  None." : "  없음.";

    public static string EntropyPacked => En ? "(likely compressed or encrypted)" : "(압축/암호화 가능성 높음)";
    public static string EntropyMixedHigh => En ? "(contains compressed regions)" : "(압축된 영역 다수 포함)";
    public static string EntropyMixed => En ? "(mixed binary data)" : "(혼합 이진 데이터)";
    public static string EntropyStructured => En ? "(structured or textual)" : "(구조적/텍스트 성향)";

    public static string EncUtf8Bom => En ? "UTF-8 (BOM)" : "UTF-8 (BOM)";
    public static string EncUtf32Le => "UTF-32 LE (BOM)";
    public static string EncUtf16Le => "UTF-16 LE (BOM)";
    public static string EncUtf16Be => "UTF-16 BE (BOM)";
    public static string EncUtf16LeNoBom => En ? "UTF-16 LE (no BOM)" : "UTF-16 LE (BOM 없음)";
    public static string EncUtf16BeNoBom => En ? "UTF-16 BE (no BOM)" : "UTF-16 BE (BOM 없음)";
    public static string EncTextUtf8 => En ? "text (UTF-8 / ASCII)" : "텍스트 (UTF-8/ASCII)";
    public static string EncTextAscii => En ? "text (ASCII or local code page)" : "텍스트 (ASCII 또는 로컬 코드페이지)";
    public static string EncBinary => En ? "binary data" : "이진 데이터";

    public static string FieldAsciiTag => En ? "ASCII tag (magic / chunk id candidate)" : "ASCII 태그 (매직/청크 ID 후보)";
    public static string FieldTotalSize => En ? "matches the file size -> length field" : "파일 전체 크기와 일치 → 길이 필드";
    public static string FieldRemaining => En ? "matches the bytes after this field -> length field" : "이 필드 이후 남은 바이트 수와 일치 → 길이 필드";
    public static string FieldSizeMinus8 => En ? "file size - 8 (RIFF style length)" : "파일 크기 - 8 (RIFF 계열 길이 필드)";
    public static string FieldOffset => En ? "points inside the file -> offset candidate" : "파일 내부를 가리키는 오프셋 후보";
    public static string FieldUnixTime(string when) => En ? $"possible Unix time: {when}Z" : $"Unix time 후보: {when}Z";

    // ---------------------------------------------------------------- zip
    public static string ZipHeader(bool zip64, int entries) => En
        ? $"[ ZIP container {(zip64 ? "(ZIP64) " : "")}- {entries} entries ]"
        : $"[ ZIP 컨테이너 {(zip64 ? "(ZIP64) " : "")}- 엔트리 {entries}개 ]";
    public static string ZipComment => En ? "  Comment: " : "  주석: ";
    public static string ZipUnreadable => En
        ? "  Could not read the central directory (damaged or split archive)."
        : "  중앙 디렉터리를 읽지 못했습니다 (손상되었거나 분할 아카이브).";
    public static string ZipEncrypted => En ? "encrypted" : "암호화";
    public static string ZipLegacyCrypto => En ? "ZipCrypto (legacy)" : "ZipCrypto (구형 암호)";
    public static string ZipAes(string bits, int version, string method) => En
        ? $"{bits} (AE-{version}, inner compression {method})"
        : $"{bits} (AE-{version}, 실제 압축 {method})";
    public static string ZipAllEncryptedNote1 => En
        ? "  Note: every entry is encrypted. Only names, sizes and timestamps are plaintext, so"
        : "  ※ 모든 엔트리가 암호화되어 있습니다. 파일명/크기/시각만 평문이고 내용은 암호문이므로";
    public static string ZipAllEncryptedNote2 => En
        ? "  string and JSON scans cannot see inside (hence the near 8.00 overall entropy)."
        : "     문자열·JSON 검색으로는 내부 값을 볼 수 없습니다 (전체 엔트로피가 8.00에 가까운 이유).";

    // ---------------------------------------------------------------- console
    public static string ConsoleTiming(double openMs, double totalMs) => En
        ? $"(opened in {openMs:F1} ms, {totalMs:F1} ms including analysis)"
        : $"(열기 {openMs:F1} ms, 분석 포함 {totalMs:F1} ms)";
    public static string ConsoleFirstBytes(int n) => En ? $"[ First {n} bytes ]" : $"[ 처음 {n} 바이트 ]";
    public static string ConsoleJson(int count, double ms) => En
        ? $"[ JSON regions: {count}, scanned in {ms:F1} ms ]"
        : $"[ JSON 영역 {count}건, 스캔 {ms:F1} ms ]";
    public static string ConsoleStrings(int count, double ms) => En
        ? $"[ Strings: {count:N0}, extracted in {ms:F1} ms ]"
        : $"[ 문자열 {count:N0}건, 추출 {ms:F1} ms ]";
    public static string ConsoleMore(int rest) => En ? $"  … and {rest} more" : $"  ... 외 {rest}건";
}
