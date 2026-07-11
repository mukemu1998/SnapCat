using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace SnapCat.Infrastructure.Services;

public sealed record OllamaRuntimeStatus(
    bool IsInstalled,
    string ExecutablePath,
    bool IsServiceAvailable,
    IReadOnlyList<string> InstalledModels,
    string ModelDirectory,
    string Message);

public sealed record OllamaPullProgress(string Status, long? CompletedBytes, long? TotalBytes)
{
    public double? ProgressPercent => TotalBytes is > 0 && CompletedBytes is not null
        ? Math.Clamp(CompletedBytes.Value * 100d / TotalBytes.Value, 0d, 100d)
        : null;
}

public sealed class OllamaRuntimeService
{
    private static readonly Uri TagsEndpoint = new("http://127.0.0.1:11434/api/tags");
    private static readonly Uri PullEndpoint = new("http://127.0.0.1:11434/api/pull");
    private readonly HttpClient _httpClient;

    public OllamaRuntimeService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<OllamaRuntimeStatus> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var executablePath = FindExecutablePath();
        var modelDirectory = GetConfiguredModelDirectory();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var response = await _httpClient.GetAsync(TagsEndpoint, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new OllamaRuntimeStatus(
                    !string.IsNullOrWhiteSpace(executablePath),
                    executablePath,
                    false,
                    [],
                    modelDirectory,
                    "已找到 Ollama，但本地服务暂不可用。请启动 Ollama 后重新检测。");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var models = document.RootElement.TryGetProperty("models", out var modelsElement)
                && modelsElement.ValueKind == JsonValueKind.Array
                ? modelsElement.EnumerateArray()
                    .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            return new OllamaRuntimeStatus(
                !string.IsNullOrWhiteSpace(executablePath),
                executablePath,
                true,
                models,
                modelDirectory,
                models.Count == 0 ? "Ollama 已就绪，但尚未下载模型。" : $"Ollama 已就绪，已发现 {models.Count} 个本地模型。"
            );
        }
        catch (HttpRequestException)
        {
            return new OllamaRuntimeStatus(
                !string.IsNullOrWhiteSpace(executablePath),
                executablePath,
                false,
                [],
                modelDirectory,
                string.IsNullOrWhiteSpace(executablePath)
                    ? "未检测到 Ollama。请先从官方页面安装，再返回此处继续。"
                    : "检测到 Ollama，但服务尚未启动。请从开始菜单启动 Ollama 后重新检测。"
            );
        }
        catch (OperationCanceledException)
        {
            return new OllamaRuntimeStatus(
                !string.IsNullOrWhiteSpace(executablePath),
                executablePath,
                false,
                [],
                modelDirectory,
                string.IsNullOrWhiteSpace(executablePath)
                    ? "未检测到 Ollama。请先从官方页面安装，再返回此处继续。"
                    : "检测到 Ollama，但服务尚未启动。请从开始菜单启动 Ollama 后重新检测。"
            );
        }
    }

    public async Task<OllamaRuntimeStatus> EnsureServiceAvailableAsync(CancellationToken cancellationToken = default)
    {
        var initialStatus = await ProbeAsync(cancellationToken);
        if (initialStatus.IsServiceAvailable || !initialStatus.IsInstalled)
        {
            return initialStatus;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = initialStatus.ExecutablePath,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception exception)
        {
            return initialStatus with
            {
                Message = $"检测到 Ollama，但无法自动启动本地服务：{exception.Message}"
            };
        }

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(350, cancellationToken);
            var status = await ProbeAsync(cancellationToken);
            if (status.IsServiceAvailable)
            {
                return status with { Message = "Ollama 本地服务已自动启动并准备就绪。" };
            }
        }

        return initialStatus with
        {
            Message = "检测到 Ollama，但本地服务启动超时。请从开始菜单启动 Ollama 后重试。"
        };
    }

    public async Task PullModelAsync(
        string modelName,
        Action<OllamaPullProgress>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PullEndpoint)
        {
            Content = JsonContent.Create(new
            {
                name = modelName,
                stream = true
            })
        };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.GetString() ?? "Ollama 返回未知下载错误。");
            }

            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? "正在下载"
                : "正在下载";
            var completed = root.TryGetProperty("completed", out var completedElement)
                && completedElement.TryGetInt64(out var completedBytes)
                ? completedBytes
                : (long?)null;
            var total = root.TryGetProperty("total", out var totalElement)
                && totalElement.TryGetInt64(out var totalBytes)
                ? totalBytes
                : (long?)null;
            reportProgress?.Invoke(new OllamaPullProgress(status, completed, total));
        }
    }

    public static string GetConfiguredModelDirectory()
    {
        return Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("OLLAMA_MODELS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
    }

    public static void SetModelDirectory(string directory)
    {
        Environment.SetEnvironmentVariable("OLLAMA_MODELS", directory, EnvironmentVariableTarget.User);
    }

    private static string FindExecutablePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }
}
