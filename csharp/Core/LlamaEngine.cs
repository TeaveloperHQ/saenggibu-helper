using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Saenggibu;

namespace Saenggibu;

/// <summary>app/engine.py 의 complete 이식 — LLamaSharp로 ChatML 완성. ILlmEngine 구현.</summary>
public sealed class LlamaEngine : ILlmEngine, IDisposable
{
    private readonly LLamaWeights _weights;
    private readonly ModelParams _mp;

    public LlamaEngine(string modelPath)
    {
        _mp = new ModelParams(modelPath)
        {
            ContextSize = (uint)Config.NCtx,
            GpuLayerCount = Config.NGpuLayers,
            BatchSize = (uint)Config.NBatch,
            Threads = Config.NThreads,
            FlashAttention = false,
        };
        _weights = LLamaWeights.LoadFromFile(_mp);
    }

    public string Complete(string system, string user, int maxTokens, double temperature) =>
        Run($"<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{user}<|im_end|>\n<|im_start|>assistant\n",
            maxTokens, temperature);

    public string CompleteMessages(IReadOnlyList<Engine.Message> messages, int maxTokens, double temperature)
    {
        var sb = new StringBuilder();
        foreach (var m in messages) sb.Append($"<|im_start|>{m.Role}\n{m.Content}<|im_end|>\n");
        sb.Append("<|im_start|>assistant\n");
        return Run(sb.ToString(), maxTokens, temperature);
    }

    private string Run(string prompt, int maxTokens, double temperature)
    {
        var ex = new StatelessExecutor(_weights, _mp);
        var ip = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = new[] { "<|im_end|>" },
            SamplingPipeline = temperature <= 0
                ? new GreedySamplingPipeline()
                : new DefaultSamplingPipeline { Temperature = (float)temperature, TopP = (float)Config.DefaultTopP },
        };
        return Task.Run(async () =>
        {
            var sb = new StringBuilder();
            await foreach (var t in ex.InferAsync(prompt, ip)) sb.Append(t);
            return sb.ToString();
        }).GetAwaiter().GetResult().Trim();
    }

    public void Dispose() => _weights.Dispose();
}
