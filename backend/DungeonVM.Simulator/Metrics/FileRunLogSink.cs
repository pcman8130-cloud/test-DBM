using System.Text.Json;

namespace DungeonVM.Simulator.Metrics;

/// <summary>Supabase 등 외부 인프라 없이도 항상 동작하는 로컬 JSONL 로그 싱크(기본값).</summary>
public sealed class FileRunLogSink : IRunLogSink, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public FileRunLogSink(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writer = new StreamWriter(path, append: false) { AutoFlush = false };
    }

    public Task LogRunAsync(RunResult result, CancellationToken ct = default)
    {
        string line = JsonSerializer.Serialize(result);
        lock (_lock) _writer.WriteLine(line);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
