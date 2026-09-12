# DungeonVM.LLM

`DungeonVM.Simulator`가 집계한 아이템 채택률에서 **5% 미만 저채택 아이템**을 감지하면 OpenAI/Claude API를
JSON Mode로 호출해 레시피·스탯 재조정안을 제안받는 오프라인 밸런싱 모듈입니다. API 키가 없는 환경(CI, 로컬)에서는
네트워크 호출 없이 감지 결과만 로그로 남기고 조용히 건너뜁니다.

## 동작 흐름

1. [`RecipeOptimizer.DetectLowAdoption`](RecipeOptimizer.cs) — 채택률 5%(`AdoptionThreshold`) 미만 아이템 필터링
2. [`PromptTemplates/BalancePromptTemplate.cs`](PromptTemplates/BalancePromptTemplate.cs) — 시스템/유저 프롬프트 구성
   (JSON 배열만 출력하도록 강제)
3. [`ILlmClient`](ILlmClient.cs) 구현체([`AnthropicClient`](AnthropicClient.cs) 또는 [`OpenAiClient`](OpenAiClient.cs))로 호출
4. 응답을 [`BalanceSuggestion`](BalanceSuggestion.cs) 목록으로 파싱 (코드펜스로 감싸진 응답도 방어적으로 처리)
5. `RecipeOptimizer.SaveSuggestionsAsync`로 `balance_suggestions.json`에 저장

## API 키 설정

둘 중 하나만 설정하면 됩니다. `DungeonVM.Simulator`는 Anthropic을 우선 시도하고, 키가 없으면 OpenAI로 폴백합니다.

```bash
export ANTHROPIC_API_KEY="sk-ant-..."
# 또는
export OPENAI_API_KEY="sk-..."
```

둘 다 없으면 `ILlmClient.IsConfigured`가 `false`가 되어 `RecipeOptimizer.OptimizeAsync`가 네트워크 호출 없이
바로 빈 목록을 반환합니다 — 저채택 아이템 감지 결과는 콘솔에 그대로 출력되니, 수동으로 밸런스 조정 여부를 판단할 수 있습니다.

## 다른 LLM 제공자 추가하기

[`ILlmClient`](ILlmClient.cs) 인터페이스만 구현하면 됩니다(`ProviderName`, `IsConfigured`, `CompleteJsonAsync`).
`AnthropicClient`/`OpenAiClient`가 참고할 수 있는 최소 구현 예시입니다.

## 임계값/모델 조정

- 저채택 판정 임계값: [`RecipeOptimizer.AdoptionThreshold`](RecipeOptimizer.cs) (기본 0.05)
- 사용 모델: `AnthropicClient`/`OpenAiClient` 생성자의 `model` 파라미터 (기본 `claude-sonnet-5` / `gpt-4o-mini`)
