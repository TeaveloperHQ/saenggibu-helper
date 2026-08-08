namespace Saenggibu;

/// <summary>
/// app/config.py 의 로직 파리티 상수. 경로(데이터 폴더 등)는 GUI 단계에서 다룬다.
/// 환경변수(SGB_*)와 기본값을 Python과 동일하게 읽는다.
/// </summary>
public static class Config
{
    public const string AppName = "생기부 도우미";
    public const string AppVersion = "0.1.0";

    // 배포 GGUF = 학교용 파인튜닝(natural-1849-dpo). 우리 Azure Blob(SAS, 읽기전용) 핀 고정.
    // (SGB_GGUF_URL 로 오버라이드 가능 — 모델 교체 시 URL만 바꾸면 앱이 새로 받음)
    public const string ModelFilename = "saenggibu-natural-1849-dpo-q4_k_m.gguf";
    public static readonly string ModelUrl = Environment.GetEnvironmentVariable("SGB_GGUF_URL")
        ?? "https://sgb50013120.blob.core.windows.net/dist/saenggibu-natural-1849-dpo-q4_k_m.gguf?se=2035-12-31T23%3A59%3A59Z&sp=r&spr=https&sv=2026-04-06&sr=b&sig=bQG6LO1B%2BHFPgLsUh5hw3%2F8ebw7wDzNKiIixFz%2Ftw8I%3D";
    public const long ModelApproxBytes = 4_683_073_472L;

    // Kiwi 형태소 모델(핀 고정 0.23.0) — 우리 Azure Blob(SAS, 읽기전용). exe 옆/데이터 폴더에 없으면 앱이 받아 kiwi_model/ 로 푼다.
    // (SGB_KIWI_MODEL_URL 로 오버라이드 가능 — 배포 시 URL 교체용)
    public const string KiwiModelDir = "kiwi_model";
    public static readonly string KiwiModelUrl = Environment.GetEnvironmentVariable("SGB_KIWI_MODEL_URL")
        ?? "https://sgb50013120.blob.core.windows.net/dist/kiwi_model_0.23.0.zip?se=2035-12-31T23%3A59%3A59Z&sp=r&spr=https&sv=2026-04-06&sr=b&sig=ALXQlYzyFACdQ8k55pS6g3SU4xppOgHVDQkO4g%2FFu6A%3D";
    public const long KiwiModelApproxBytes = 85_846_382L;

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
