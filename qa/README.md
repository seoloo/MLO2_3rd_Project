# 정보지킴 자동화 QA 러너

입력값과 정답을 파일에 적어 두면, 프로그램이 입력값을 판정 대상에 넣고 돌아온 결과를 정답과 비교해 채점하는 C# 콘솔 프로그램입니다.

## 흐름

```
입력값 파일 ──(문자)──▶ 판정 대상 ──(실제 판정)──▶ 채점 ──▶ 결과
     └──────────────(정답)─────────────────────────┘
```

정답은 시스템을 거치지 않고 채점 단계로 바로 갑니다. 판정 대상은 지금 가짜 판정기이고, B의 FastAPI가 준비되면 그 부분만 바꿉니다.

## 실행 방법

.NET 10 SDK가 필요합니다. 명령은 모두 `qa/` 폴더에서 실행합니다.

```bash
dotnet run --project src/QaRunner
```

```bash
dotnet test
```

지금은 가짜 판정기가 "인증번호"만 보고 판정하므로 4건 중 2건이 실패하고 종료 코드 1로 끝납니다. 채점기가 틀린 판정을 잡아내는지 확인하려고 일부러 그렇게 둔 것입니다.

다른 입력값 파일이나 보고서 폴더를 쓰려면 `--` 뒤에 옵션을 붙입니다.

```bash
dotnet run --project src/QaRunner -- --cases cases/rules.json --reports reports
```

## 보고서

실행할 때마다 `reports/` 폴더에 실행 번호(시작 시각, 예: `2026-10-26-143015`)로 파일 두 개가 생깁니다. 이 폴더는 git에 올라가지 않습니다.

| 파일 | 용도 |
|---|---|
| `<실행 번호>.html` | 브라우저로 여는 보고서. 전체·통과·실패·통과율과 케이스별 결과 (실패한 케이스가 위) |
| `<실행 번호>.json` | 같은 내용을 다른 프로그램(대시보드, CI)이 읽는 용도 |

가짜 판정 대상으로 실행한 보고서에는 "실제 판정 규칙의 성능이 아닙니다"라는 안내가 붙습니다.

## 시험 케이스 추가

`cases/rules.json`에 한 줄을 추가하면 됩니다. 코드는 고치지 않습니다.

```json
{ "caseId": "R-005", "message": "입력할 문자", "expectedVerdict": "고위험" }
```

| 키 | 뜻 |
|---|---|
| `caseId` | 케이스 번호. 실패했을 때 찾는 데 씀 |
| `message` | 판정 대상에 넣을 문자 (입력값) |
| `expectedVerdict` | 정답 판정: `고위험`, `주의`, `신호 미발견`, `판단 보류` 중 하나 |

## 구조

| 위치 | 역할 |
|---|---|
| `cases/rules.json` | 입력값 + 정답 |
| `src/QaRunner/Models/TestCase.cs` | 시험 케이스 한 건 (케이스 번호, 문자, 정답) |
| `src/QaRunner/Models/TestResult.cs` | 채점 결과 한 건 (문자, 정답, 실제 판정, 통과 여부) |
| `src/QaRunner/Models/TestRun.cs` | 한 번 실행한 기록 (실행 번호, 시각, 결과 목록, 통과율) |
| `src/QaRunner/CommandLineOptions.cs` | `--cases`, `--reports` 옵션 읽기 |
| `src/QaRunner/TestCaseLoader.cs` | 입력값 파일 읽기 |
| `src/QaRunner/Verdicts/` | 판정 대상 (`IVerdictService`, 지금은 `FakeVerdictService`) |
| `src/QaRunner/VerdictChecker.cs` | 채점기: 정답 vs 실제 판정 |
| `src/QaRunner/Reports/` | JSON·HTML 보고서 저장 |
| `src/QaRunner/Program.cs` | 실행 순서 연결, 결과 출력, 종료 코드 |
| `tests/QaRunner.Tests/` | QA 프로그램 자체의 테스트 |

## 다음 단계

1. FastAPI가 준비되면 API를 호출하는 `IVerdictService` 구현을 만들어 `FakeVerdictService` 대신 연결
2. 파이프라인 시험 추가 (중복·역순·지연 주입, Bronze·Silver·Gold 비교)
