using System.Net.Http.Headers;

namespace Saenggibu;

/// <summary>
/// app/downloader.py 이식 — GGUF 모델 다운로드(이어받기·진행률·GGUF 매직 검증).
/// urllib → HttpClient. 범위 요청(Range)로 .part 이어받기.
/// </summary>
public static class Downloader
{
    public delegate void ProgressCb(long downloaded, long total);

    public static bool ModelExists(string? path) =>
        path != null && File.Exists(path) && new FileInfo(path).Length > 1_000_000;

    /// <summary>파일 앞 4바이트가 GGUF 매직인지(손상·오류페이지 탐지).</summary>
    public static bool LooksLikeGguf(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            Span<byte> buf = stackalloc byte[4];
            int read = f.Read(buf);
            return read == 4 && buf[0] == (byte)'G' && buf[1] == (byte)'G'
                   && buf[2] == (byte)'U' && buf[3] == (byte)'F';
        }
        catch (IOException) { return false; }
    }

    /// <summary>모델 다운로드(이어받기). approxBytes = 진행률 근사 총량.</summary>
    public static async Task<string> DownloadModelAsync(string url, string dest, long approxBytes,
        ProgressCb? progress = null, Func<bool>? shouldCancel = null, HttpClient? client = null)
    {
        if (ModelExists(dest)) return dest;
        string part = dest + ".part";
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;

        var http = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        // 206(부분)일 때만 이어붙임. 200(전체)이면 처음부터.
        bool resumed = existing > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        long baseLen = resumed ? existing : 0;
        long total = approxBytes;
        if (resp.Content.Headers.ContentLength is long clen) total = baseLen + clen;

        long downloaded = baseLen;
        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var f = new FileStream(part, resumed ? FileMode.Append : FileMode.Create,
                                            FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[256 * 1024];
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                if (shouldCancel?.Invoke() == true)
                    throw new OperationCanceledException("사용자가 다운로드를 취소했습니다.");
                await f.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                progress?.Invoke(downloaded, total);
            }
        }

        if (!LooksLikeGguf(part))
        {
            File.Delete(part);
            throw new InvalidDataException("다운로드한 파일이 올바른 모델(GGUF)이 아닙니다.");
        }
        if (new FileInfo(part).Length < (long)(approxBytes * 0.9))
            throw new InvalidDataException("모델 다운로드가 완료되지 않았습니다(파일이 너무 작음).");

        if (File.Exists(dest)) File.Delete(dest);
        File.Move(part, dest);
        return dest;
    }
}
