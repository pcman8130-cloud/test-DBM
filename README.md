# 던전 자판기 (Dungeon Vending Machine)

캐주얼 머지 오토배틀러 + 로그라이트 디펜스 (PC 가로형 16:9)

핵심 분석 과제: **"실시간 전투 압박과 공간 해금 비용 속에서 유저가 왜 고티어 대신 2티어 무기에 안주하는가?"**

## 저장소 구조

```text
DungeonVM/
├── Assets/, Packages/, ProjectSettings/   # Unity 프로젝트 (UI/렌더링/연출 - 팀원 담당)
│
├── DungeonVM.Core/         # 순수 C# 도메인 엔진 (Unity 의존성 0%)
├── DungeonVM.Simulator/    # .NET 8 콘솔 - 가상 봇 3종 x 1,000회 = 3,000회 헤드리스 배치
└── DungeonVM.LLM/          # 저채택 아이템 감지 시 LLM 밸런싱 제안 모듈
```

각 프로젝트의 상세 내용은 하위 README를 참고하세요:
- [DungeonVM.Core/README.md](DungeonVM.Core/README.md) - 도메인 모델, 전투/머지/재화 로직, 수치 조정 위치
- [DungeonVM.Simulator/README.md](DungeonVM.Simulator/README.md) - 봇 3종 소개, 실행 방법, 리포트 읽는 법
- [DungeonVM.LLM/README.md](DungeonVM.LLM/README.md) - LLM 밸런싱 모듈 동작 방식, API 키 설정

## 빌드 / 실행

```bash
dotnet build DungeonVM.sln
dotnet run --project DungeonVM.Simulator/DungeonVM.Simulator.csproj -c Release
```

기본은 봇당 1,000회(총 3,000회)이며, 인자로 봇당 실행 횟수를 바꿀 수 있습니다.

```bash
dotnet run --project DungeonVM.Simulator/DungeonVM.Simulator.csproj -c Release -- 50
```

## 스코프

- Unity 엔진 의존성(UnityEngine, MonoBehaviour 등) 0%
- UI 렌더링/스프라이트/DoTween 연출은 포함하지 않음 (팀원 담당)
- 캐릭터 슬롯 2→5 확장(로스터 성장)은 핵심 분석 과제와 직접 관련이 없어 시뮬레이터에서는 고정 2인 파티로 단순화
