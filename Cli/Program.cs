using System.Text;
using BinaryViewer.Core;

namespace BinaryViewer.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected or dumb terminal */ }

        string? langArg = args.FirstOrDefault(a => a.StartsWith("--lang="));
        if (langArg != null && S.TryParse(langArg[7..], out var language)) S.Current = language;

        var files = args.Where(a => !a.StartsWith('-')).ToList();
        bool help = args.Any(a => a is "-h" or "--help");
        if (files.Count == 0 || help)
        {
            Usage();
            return help ? 0 : 1;          // asking for help is not an error
        }

        bool report = args.Any(a => a is "--report" or "-r");
        bool json = args.Any(a => a is "--json" or "-j");
        bool strings = args.Any(a => a is "--strings" or "-s");
        if (!report && !json && !strings) report = json = true;      // sensible default

        int minLen = 6;
        string? minArg = args.FirstOrDefault(a => a.StartsWith("--min="));
        if (minArg != null && int.TryParse(minArg[6..], out int parsed)) minLen = Math.Max(1, parsed);

        int exit = 0;
        foreach (string file in files)
        {
            if (files.Count > 1) Console.WriteLine($"===== {file} =====");
            exit |= TextReport.Write(Console.Out, Console.Error, file, report, json, strings, minLen);
        }
        return exit;
    }

    private static void Usage()
    {
        Console.WriteLine(S.Current == Language.Korean ? """
            binview - 바이너리 헤더 / JSON 분석기

            사용법:
              binview <파일> [옵션...]

            옵션:
              -r, --report    헤더 시그니처, 통계, 필드 후보, ZIP 구조, 앞 256바이트 덤프
              -j, --json      파일에 박혀 있는 JSON 영역 탐지 (UTF-8 / UTF-16LE)
              -s, --strings   ASCII / UTF-16 문자열 추출
                  --min=N     문자열 최소 길이 (기본 6)
                  --lang=ko   출력 언어 (ko / en, 기본값은 시스템 설정)
              -h, --help      이 도움말

            옵션을 주지 않으면 --report --json 과 같습니다.
            """ : """
            binview - binary header / JSON analyser

            Usage:
              binview <file> [options...]

            Options:
              -r, --report    header signatures, statistics, field candidates, ZIP layout, first 256 bytes
              -j, --json      find JSON regions embedded in the file (UTF-8 / UTF-16LE)
              -s, --strings   extract ASCII / UTF-16 strings
                  --min=N     minimum string length (default 6)
                  --lang=en   output language (ko / en, defaults to the system setting)
              -h, --help      this help

            With no options it behaves like --report --json.
            """);
    }
}
