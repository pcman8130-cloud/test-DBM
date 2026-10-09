namespace DungeonVM.LLM.Tuning;

/// <summary>밸런스 파라미터 1개에 대한 변경 제안. Path는 "섹션.필드"(예: "wave.bigBossHealth", "weaponBaseDamage.Sword").</summary>
public sealed record ParameterChange(string Path, double NewValue, string Reasoning);

/// <summary>목표 범위를 벗어난(또는 참고용) 지표 1건. Status는 LOW(미달)/HIGH(초과).</summary>
public sealed record DeviationInfo(string Group, string Metric, string Status, double? Value, double? Min, double? Max, string? Detail);

/// <summary>LLM이 건드릴 수 있는 화이트리스트 파라미터와 현재 값/허용 범위.</summary>
public sealed record TunableParameter(string Path, double Current, double Min, double Max);

/// <summary>이전 반복 1회의 기록. LLM이 같은 실패를 반복하지 않도록 프롬프트에 포함된다.</summary>
public sealed record TuningAttempt(
    int Iteration, IReadOnlyList<ParameterChange> Changes, bool Accepted, double ScoreBefore, double ScoreAfter, string Note);

/// <summary>한 번의 제안 요청에 필요한 모든 입력.</summary>
public sealed record TuningContext(
    IReadOnlyList<DeviationInfo> Deviations,
    IReadOnlyList<DeviationInfo> InfoOnly,
    IReadOnlyList<TunableParameter> Tunables,
    IReadOnlyList<TuningAttempt> History,
    int MaxChanges,
    string GameNotes);

/// <summary>편차 → 수치 변경안 제안자. 실제 LLM 구현과, API 키 없이 루프를 검증하기 위한 규칙 기반 대역을 같은 인터페이스로 쓴다.</summary>
public interface ITuningProposer
{
    string Name { get; }

    Task<IReadOnlyList<ParameterChange>> ProposeAsync(TuningContext context, CancellationToken ct = default);
}
