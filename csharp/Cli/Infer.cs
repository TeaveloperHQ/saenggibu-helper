using System.Diagnostics;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Saenggibu;

namespace Cli;

/// <summary>
/// Tier B 기반 — LLamaSharp 로 GGUF 를 로드하고 Qwen2.5 ChatML 로 greedy 생성.
/// config.py 와 동일한 로드 파라미터(n_ctx/n_threads/n_batch/gpu=0/mmap).
/// 사용: dotnet run --project Cli -- infer &lt;model.gguf&gt; ["프롬프트"]
/// </summary>
public static class Infer
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2)
        {
            Console.Error.WriteLine("사용: infer <model.gguf> [프롬프트]");
            return 2;
        }
        string modelPath = args[1];
        string userMsg = args.Length > 2 ? args[2]
            : "산과 염기 단원 실험 설계, 예상과 다른 결과 원인 토의";
        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"모델 없음: {modelPath}");
            return 2;
        }

        var sw = Stopwatch.StartNew();
        bool flash = Environment.GetEnvironmentVariable("SGB_FLASH") == "1";  // 기본 off(llama-cpp-python 기본과 정렬)
        var mp = new ModelParams(modelPath)
        {
            ContextSize = (uint)Config.NCtx,
            GpuLayerCount = Config.NGpuLayers,
            BatchSize = (uint)Config.NBatch,
            Threads = Config.NThreads,
            UseMemorymap = Config.UseMmap,
            UseMemoryLock = Config.UseMlock,
            FlashAttention = flash,
        };
        using var weights = LLamaWeights.LoadFromFile(mp);
        double loadSec = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"[load] {loadSec:F1}s  n_ctx={Config.NCtx} threads={Config.NThreads} gpu={Config.NGpuLayers}");

        // Qwen2.5 ChatML 수동 구성(모델 내장 템플릿과 동일 포맷)
        string system = "너는 대한민국 교사의 생기부 작성을 돕는 조력자다. 명사형 종결로 간결히 작성한다.";
        string prompt =
            $"<|im_start|>system\n{system}<|im_end|>\n" +
            $"<|im_start|>user\n{userMsg}<|im_end|>\n" +
            "<|im_start|>assistant\n";

        var executor = new StatelessExecutor(weights, mp);
        var inferParams = new InferenceParams
        {
            MaxTokens = 128,
            AntiPrompts = new[] { "<|im_end|>" },
            SamplingPipeline = new GreedySamplingPipeline(),
        };

        var sb = new StringBuilder();
        int tokens = 0;
        var genSw = Stopwatch.StartNew();
        await foreach (var piece in executor.InferAsync(prompt, inferParams))
        {
            sb.Append(piece);
            tokens++;
        }
        genSw.Stop();

        double toks = tokens / genSw.Elapsed.TotalSeconds;
        Console.WriteLine($"\n[출력]\n{sb.ToString().Trim()}");
        Console.WriteLine($"\n[gen] {tokens} pieces in {genSw.Elapsed.TotalSeconds:F1}s (~{toks:F1} piece/s)");
        return 0;
    }
}
