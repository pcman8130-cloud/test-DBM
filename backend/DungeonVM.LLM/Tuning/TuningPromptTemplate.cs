using System.Globalization;
using System.Text;

namespace DungeonVM.LLM.Tuning;

/// <summary>목표 대비 편차를 넘겨 "화이트리스트 안에서의 수치 변경안"을 JSON으로 받는 프롬프트.</summary>
public static class TuningPromptTemplate
{
    public static string BuildSystemPrompt(int maxChanges) =>
        "당신은 캐주얼 머지 오토배틀러 로그라이트 디펜스 게임 '던전 자판기'의 밸런스 디자이너입니다. " +
        "시뮬레이터(균형형 봇 배치 실행)가 측정한 지표가 디자이너가 정한 목표 범위를 벗어나 있습니다. " +
        "'조정 가능한 파라미터' 목록 안의 수치만 바꿔서 지표를 목표 범위로 되돌리는 변경안을 제안하세요.\n" +
        "규칙:\n" +
        $"1. 변경은 최대 {maxChanges}개까지만 제안합니다(한 번에 너무 많이 바꾸면 어떤 변경이 효과를 냈는지 알 수 없습니다).\n" +
        "2. path는 반드시 목록에 있는 값을 그대로 쓰고, newValue는 해당 [min, max] 범위 안의 숫자여야 합니다. 범위를 벗어나거나 목록에 없는 제안은 자동으로 거부됩니다.\n" +
        "3. 한 번에 현재 값의 ±20% 이내로 조정하는 것을 권장합니다(큰 점프는 다른 지표를 망가뜨리기 쉽습니다).\n" +
        "4. 하나의 파라미터가 여러 스테이지에 공통으로 적용됩니다(예: bigBoss* 는 10/20/30스테 전부). 서로 반대 방향의 편차를 한 파라미터로 동시에 해결할 수는 없으니, 어느 쪽을 우선할지 reasoning에 밝히세요.\n" +
        "5. '이전 시도'에서 악화되었거나 거부된 변경을 같은 방향으로 반복하지 마세요.\n" +
        "6. '참고 지표'는 현재 목록의 파라미터로는 직접 조정할 수 없으니 변경 근거로 삼지 마세요.\n" +
        "7. reasoning은 반드시 한국어로, 어떤 지표를 어느 방향으로 얼마나 움직이려는 것인지 한두 문장으로 쓰세요.\n" +
        "반드시 다음 형식의 JSON 객체 하나만 출력하고, JSON 밖에 다른 문장을 쓰지 마세요: " +
        "{\"changes\":[{\"path\":string,\"newValue\":number,\"reasoning\":string}]}";

    public static string BuildUserPrompt(TuningContext ctx)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("## 게임 구조 메모");
        sb.AppendLine(ctx.GameNotes);
        sb.AppendLine();

        sb.AppendLine("## 목표를 벗어난 지표 (이걸 해소하는 것이 목적)");
        if (ctx.Deviations.Count == 0)
            sb.AppendLine("(없음)");
        foreach (var d in ctx.Deviations)
            sb.AppendLine(FormatDeviation(d));
        sb.AppendLine();

        if (ctx.InfoOnly.Count > 0)
        {
            sb.AppendLine("## 참고 지표 (현재 파라미터로 직접 조정 불가 — 점수에 반영되지 않음)");
            foreach (var d in ctx.InfoOnly)
                sb.AppendLine(FormatDeviation(d));
            sb.AppendLine();
        }

        sb.AppendLine("## 조정 가능한 파라미터 (path / 현재값 / 허용 범위)");
        foreach (var t in ctx.Tunables)
            sb.AppendLine($"- {t.Path} / {t.Current.ToString("G6", inv)} / [{t.Min.ToString("G6", inv)}, {t.Max.ToString("G6", inv)}]");
        sb.AppendLine();

        if (ctx.History.Count > 0)
        {
            sb.AppendLine("## 이전 시도 (점수는 낮을수록 좋음)");
            foreach (var h in ctx.History)
            {
                string result = h.Accepted ? "채택" : "되돌림";
                string changes = h.Changes.Count == 0
                    ? "(유효한 변경 없음)"
                    : string.Join(", ", h.Changes.Select(c => $"{c.Path}→{c.NewValue.ToString("G6", inv)}"));
                sb.AppendLine($"- #{h.Iteration} [{result}] {changes} | 점수 {h.ScoreBefore.ToString("F3", inv)}→{h.ScoreAfter.ToString("F3", inv)} | {h.Note}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"위 편차를 줄이는 변경안을 최대 {ctx.MaxChanges}개, JSON 객체 형식으로 제안하세요.");
        return sb.ToString();
    }

    private static string FormatDeviation(DeviationInfo d)
    {
        var inv = CultureInfo.InvariantCulture;
        string value = d.Value is { } v ? v.ToString("G4", inv) : "-";
        string min = d.Min is { } lo ? lo.ToString("G4", inv) : "";
        string max = d.Max is { } hi ? hi.ToString("G4", inv) : "";
        string status = d.Status == "LOW" ? "목표 미달(너무 낮음)" : "목표 초과(너무 높음)";
        string detail = string.IsNullOrEmpty(d.Detail) ? "" : $" [{d.Detail}]";
        return $"- [{d.Group}] {d.Metric}: 측정 {value}, 목표 {min}~{max} → {status}{detail}";
    }
}
