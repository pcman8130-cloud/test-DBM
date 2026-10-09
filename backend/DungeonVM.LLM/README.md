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

## 튜닝 제안 모듈 (`Tuning/`)

위의 저채택 아이템 감지(`RecipeOptimizer`)와 별개로, 시뮬레이터의 **목표 대비 편차**를 입력으로 받아 `DefaultBalance.json`에
바로 적용 가능한 수치 변경안을 받는 모듈입니다(`DungeonVM.Simulator --tune`이 사용).

- [`TuningPromptTemplate`](Tuning/TuningPromptTemplate.cs) — 게임 구조 메모 + 목표를 벗어난 지표 + 조정 가능한 파라미터(현재값/허용 범위) +
  이전 시도 이력(채택/되돌림, 점수)을 담은 프롬프트. 출력은 `{"changes":[{"path","newValue","reasoning"}]}` JSON 객체로 강제
- [`TuningProposalParser`](Tuning/TuningProposalParser.cs) — 응답 파싱(코드펜스·숫자 문자열 허용, 형식이 깨지면 빈 목록)
- [`ITuningProposer`](Tuning/TuningModels.cs) / [`LlmTuningProposer`](Tuning/LlmTuningProposer.cs) — `ILlmClient`(Claude/OpenAI)로 호출하는 구현.
  화이트리스트 검증과 재시뮬레이션은 호출하는 쪽(시뮬레이터)이 담당한다

## API 키 설정

셋 중 하나만 설정하면 됩니다. `DungeonVM.Simulator`는 [`LlmClientFactory`](LlmClientFactory.cs)로 Anthropic → OpenAI → Gemini 순서로
키가 설정된 첫 제공자를 씁니다. 키는 환경 변수로만 설정하고 코드/파일에 넣지 마세요.

```bash
export ANTHROPIC_API_KEY="sk-ant-..."
# 또는
export OPENAI_API_KEY="sk-..."
# 또는 (무료 플랜 가능, https://aistudio.google.com 에서 발급)
export GEMINI_API_KEY="..."     # GOOGLE_API_KEY도 인식
```

PowerShell에서는 `$env:GEMINI_API_KEY = "..."` (해당 터미널 창에서만 유효).

**Gemini 참고**: 모델 이름이 자주 바뀌고 구버전은 신규 사용자에게 닫힙니다(예: `gemini-2.5-flash` → 404). `GeminiClient`는 기본으로
`gemini-3.5-flash` → `gemini-3.1-flash-lite` → `gemini-flash-latest` 순서로 시도하고, 무료 플랜에서 흔한 일시 오류(429/5xx)는
재시도한 뒤 다음 모델로 넘어갑니다. 특정 모델을 고정하려면 `GEMINI_MODEL` 환경 변수를 쓰세요. 키는 URL 쿼리로 전달되므로
오류 메시지에는 URL을 넣지 않습니다.

모두 없으면 `ILlmClient.IsConfigured`가 `false`가 되어 `RecipeOptimizer.OptimizeAsync`가 네트워크 호출 없이
바로 빈 목록을 반환합니다 — 저채택 아이템 감지 결과는 콘솔에 그대로 출력되니, 수동으로 밸런스 조정 여부를 판단할 수 있습니다.

## 다른 LLM 제공자 추가하기

[`ILlmClient`](ILlmClient.cs) 인터페이스만 구현하면 됩니다(`ProviderName`, `IsConfigured`, `CompleteJsonAsync`).
`AnthropicClient`/`OpenAiClient`가 참고할 수 있는 최소 구현 예시입니다.

## 임계값/모델 조정

- 저채택 판정 임계값: [`RecipeOptimizer.AdoptionThreshold`](RecipeOptimizer.cs) (기본 0.05)
- 사용 모델: `AnthropicClient`/`OpenAiClient` 생성자의 `model` 파라미터 (기본 `claude-sonnet-5` / `gpt-4o-mini`), Gemini는 위 설명 참고
