namespace DungeonVM.LLM;

/// <summary>키가 잘못됐거나 폐기된 경우(401/403)처럼 재시도해도 소용없는 인증 오류. 호출하는 쪽이 루프를 즉시 멈추는 데 쓴다.</summary>
public sealed class LlmAuthException : Exception
{
    public LlmAuthException(string message) : base(message) { }
}
