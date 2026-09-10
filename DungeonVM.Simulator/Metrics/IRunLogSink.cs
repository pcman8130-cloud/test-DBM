namespace DungeonVM.Simulator.Metrics;

/// <summary>개별 런 결과를 외부 저장소로 흘려보내는 싱크 추상화. 콘솔/파일/Supabase 등으로 교체 가능.</summary>
public interface IRunLogSink
{
    Task LogRunAsync(RunResult result, CancellationToken ct = default);
}
