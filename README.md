# 던전 자판기 (Dungeon Vending Machine)

캐주얼 머지 오토배틀러 + 로그라이트 디펜스 (PC 가로형 16:9)

핵심 분석 과제: **"고레벨 무기의 DPS/스킬이 강력함에도 불구하고, 유저는 왜 머지 그리드 병목·골드 해금 비용·
소켓 룬 소멸 패널티로 인해 중간 레벨(5~10레벨)에 안주하는가?"**

## 저장소 구조

Unity 프로젝트와 Unity에 의존하지 않는 C#/파이썬 도구를 분리해뒀습니다. Unity에서는 `Assets/`, `Packages/`,
`ProjectSettings/` 등 저장소 루트의 Unity 관련 폴더만 열면 되고, `backend/`는 Unity 에디터가 신경 쓸 필요가 없습니다.

```text
DungeonVM/
├── Assets/, Packages/, ProjectSettings/   # Unity 프로젝트 (UI/렌더링/연출 - 팀원 담당)
│
└── backend/                # Unity와 무관한 C# 도메인 엔진 + 시뮬레이터 + 밸런스 파이프라인
    ├── DungeonVM.sln
    ├── DungeonVM.Core/          # 순수 C# 도메인 엔진 (Unity 의존성 0%)
    ├── DungeonVM.Simulator/     # .NET 8 콘솔 - 가상 봇 3종 x 1,000회 = 3,000회 헤드리스 배치
    ├── DungeonVM.LLM/           # 저채택 아이템 감지 시 LLM 밸런싱 제안 모듈
    └── tools/
        ├── balance_pipeline/   # 엑셀 -> 밸런스 JSON 변환 파이썬 CLI
        └── balance_dashboard/  # 슬라이더로 값 조정 -> 즉시 재시뮬레이션하는 Streamlit 라이브 대시보드
```

각 프로젝트의 상세 내용은 하위 README를 참고하세요:
- [backend/DungeonVM.Core/README.md](backend/DungeonVM.Core/README.md) - 도메인 모델, 전투/머지/재화 로직, 수치 조정 위치
- [backend/DungeonVM.Simulator/README.md](backend/DungeonVM.Simulator/README.md) - 봇 3종 소개, 실행 방법, 리포트 읽는 법
- [backend/DungeonVM.LLM/README.md](backend/DungeonVM.LLM/README.md) - LLM 밸런싱 모듈 동작 방식, API 키 설정
- [backend/tools/balance_pipeline/README.md](backend/tools/balance_pipeline/README.md) - 엑셀→밸런스 JSON 파이프라인 사용법
- [backend/tools/balance_dashboard/README.md](backend/tools/balance_dashboard/README.md) - 라이브 밸런스 대시보드 사용법

## 빌드 / 실행

```bash
dotnet build backend/DungeonVM.sln
dotnet run --project backend/DungeonVM.Simulator/DungeonVM.Simulator.csproj -c Release
```

기본은 봇당 1,000회(총 3,000회)이며, 인자로 봇당 실행 횟수를 바꿀 수 있습니다.

```bash
dotnet run --project backend/DungeonVM.Simulator/DungeonVM.Simulator.csproj -c Release -- 50
```

## 스코프

- Unity 엔진 의존성(UnityEngine, MonoBehaviour 등) 0%
- UI 렌더링/스프라이트/DoTween 연출은 포함하지 않음 (팀원 담당)
- 캐릭터 슬롯 2→5 확장(로스터 성장)은 핵심 분석 과제와 직접 관련이 없어 시뮬레이터에서는 고정 2인 파티로 단순화
