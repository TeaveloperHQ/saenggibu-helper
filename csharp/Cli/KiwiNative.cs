using System.Runtime.InteropServices;
using Saenggibu;

namespace Cli;

/// <summary>
/// Kiwi C-API(libkiwi) P/Invoke — IKiwi 구현. kiwipiepy와 동일 엔진(v0.23.2)+모델이면
/// 형태소·태그가 exact 일치. 네이티브 libkiwi.so/kiwi.dll 이 로드 경로에 있어야 함.
/// </summary>
public sealed class KiwiNative : IKiwi, IDisposable
{
    private const string Lib = "kiwi";

    // kiwipiepy 기본값과 정렬: init = BUILD_DEFAULT(15) | MODEL_TYPE_CONG(0x400) = 1039
    private const int InitOptions = 15 | 0x0400;
    // analyze match = Match.ALL(63) | Z_CODA(1<<23); normalize_coda=False
    private const int MatchOptions = 63 | (1 << 23);

    // capi.h 의 kiwi_analyze_option_t 구조체(by-value 전달)
    [StructLayout(LayoutKind.Sequential)]
    private struct AnalyzeOption
    {
        public int match_options;
        public IntPtr blocklist;
        public int open_ending;
        public int allowed_dialects;
        public float dialect_cost;
        public IntPtr typo_transformer;
        public float typo_threshold;
    }

    [DllImport(Lib)] private static extern IntPtr kiwi_init([MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath, int numThreads, int options, int enabledDialects);
    [DllImport(Lib)] private static extern IntPtr kiwi_analyze(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int topN, AnalyzeOption option, IntPtr pretokenized);
    [DllImport(Lib)] private static extern int kiwi_res_word_num(IntPtr res, int index);
    [DllImport(Lib)] private static extern IntPtr kiwi_res_form(IntPtr res, int index, int num);
    [DllImport(Lib)] private static extern IntPtr kiwi_res_tag(IntPtr res, int index, int num);
    [DllImport(Lib)] private static extern int kiwi_res_position(IntPtr res, int index, int num);
    [DllImport(Lib)] private static extern int kiwi_res_close(IntPtr res);
    [DllImport(Lib)] private static extern int kiwi_close(IntPtr handle);
    [DllImport(Lib)] private static extern IntPtr kiwi_version();
    [DllImport(Lib)] private static extern IntPtr kiwi_error();
    [DllImport(Lib)] private static extern IntPtr kiwi_new_joiner(IntPtr handle, int lmSearch);
    [DllImport(Lib)] private static extern int kiwi_joiner_add(IntPtr joiner, [MarshalAs(UnmanagedType.LPUTF8Str)] string form, [MarshalAs(UnmanagedType.LPUTF8Str)] string tag, int option);
    [DllImport(Lib)] private static extern IntPtr kiwi_joiner_get(IntPtr joiner);
    [DllImport(Lib)] private static extern int kiwi_joiner_close(IntPtr joiner);

    private IntPtr _h;

    public static string Version() => Marshal.PtrToStringUTF8(kiwi_version()) ?? "?";

    public KiwiNative(string modelPath, int numThreads = -1)
    {
        _h = kiwi_init(modelPath, numThreads, InitOptions, 0);
        if (_h == IntPtr.Zero)
            throw new InvalidOperationException("kiwi_init 실패: " + (Marshal.PtrToStringUTF8(kiwi_error()) ?? "?"));
    }

    public IReadOnlyList<(string form, string tag)> Tokenize(string text)
    {
        var opt = new AnalyzeOption
        {
            match_options = MatchOptions,
            blocklist = IntPtr.Zero,
            open_ending = 0,
            allowed_dialects = 0,
            dialect_cost = 3.0f,
            typo_transformer = IntPtr.Zero,
            typo_threshold = 2.5f,
        };
        IntPtr res = kiwi_analyze(_h, text, 1, opt, IntPtr.Zero);
        if (res == IntPtr.Zero)
            throw new InvalidOperationException("kiwi_analyze 실패: " + (Marshal.PtrToStringUTF8(kiwi_error()) ?? "?"));
        try
        {
            int n = kiwi_res_word_num(res, 0);       // 최상위 후보(index 0)
            var outp = new List<(string, string)>(n);
            for (int i = 0; i < n; i++)
            {
                string form = Marshal.PtrToStringUTF8(kiwi_res_form(res, 0, i)) ?? "";
                string tag = Marshal.PtrToStringUTF8(kiwi_res_tag(res, 0, i)) ?? "";
                outp.Add((form, tag));
            }
            return outp;
        }
        finally { kiwi_res_close(res); }
    }

    public IReadOnlyList<(string form, string tag, int start)> TokenizeFull(string text)
    {
        IntPtr res = kiwi_analyze(_h, text, 1, MakeOpt(), IntPtr.Zero);
        if (res == IntPtr.Zero)
            throw new InvalidOperationException("kiwi_analyze 실패");
        try
        {
            int n = kiwi_res_word_num(res, 0);
            var outp = new List<(string, string, int)>(n);
            for (int i = 0; i < n; i++)
                outp.Add((Marshal.PtrToStringUTF8(kiwi_res_form(res, 0, i)) ?? "",
                          Marshal.PtrToStringUTF8(kiwi_res_tag(res, 0, i)) ?? "",
                          kiwi_res_position(res, 0, i)));
            return outp;
        }
        finally { kiwi_res_close(res); }
    }

    private AnalyzeOption MakeOpt() => new()
    {
        match_options = MatchOptions, blocklist = IntPtr.Zero, open_ending = 0,
        allowed_dialects = 0, dialect_cost = 3.0f, typo_transformer = IntPtr.Zero, typo_threshold = 2.5f,
    };

    public string Join(IReadOnlyList<(string form, string tag)> morphs)
    {
        IntPtr j = kiwi_new_joiner(_h, 1);           // lm_search=1 (kiwipiepy 기본)
        if (j == IntPtr.Zero)
            throw new InvalidOperationException("kiwi_new_joiner 실패");
        try
        {
            foreach (var (form, tag) in morphs)
                kiwi_joiner_add(j, form, tag, 0);
            return Marshal.PtrToStringUTF8(kiwi_joiner_get(j)) ?? "";
        }
        finally { kiwi_joiner_close(j); }
    }

    public void Dispose()
    {
        if (_h != IntPtr.Zero) { kiwi_close(_h); _h = IntPtr.Zero; }
    }
}
