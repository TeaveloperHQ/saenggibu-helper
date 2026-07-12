namespace Saenggibu;

/// <summary>
/// app/config.py 의 로직 파리티 상수. 경로(데이터 폴더 등)는 GUI 단계에서 다룬다.
/// 환경변수(SGB_*)와 기본값을 Python과 동일하게 읽는다.
/// </summary>
public static class Config
{
    public const string AppName = "생기부 도우미";
    public const string AppVersion = "0.1.0";

    public const string ModelFilename = "qwen2.5-7b-instruct-q4_k_m.gguf";
    public const string ModelUrl =
        "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf?download=true";
    public const long ModelApproxBytes = 4_683_073_344L;

    // LLM 파라미터 (config.py 와 동일 기본값)
    public static readonly int NCtx = EnvInt("SGB_N_CTX", 4096);
    public static readonly int NThreads = EnvInt("SGB_N_THREADS", Math.Max(2, (Environment.ProcessorCount) - 1));
    public static readonly int NBatch = EnvInt("SGB_N_BATCH", 256);
    public static readonly int NGpuLayers = EnvInt("SGB_N_GPU_LAYERS", 0);
    public const bool UseMmap = true;
    public const bool UseMlock = false;

    // 생성 기본값
    public const float DefaultTemperature = 0.7f;
    public const float DefaultTopP = 0.9f;
    public const int DefaultMaxTokens = 768;

    public const int FewshotK = 3;
    public const int SeedFewshotK = 2;

    public const int VariationBaseMax = 5;
    public static readonly double[] VariationTemps = { 0.6, 0.7, 0.8 };

    private static int EnvInt(string name, int fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return int.TryParse(v, out var n) ? n : fallback;
    }
}
