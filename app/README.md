# app — React Native 앱 (담당: A)

문자를 공유하거나 붙여 넣으면 상황 질문을 거쳐 판정·근거·대응 안내를 보여 주는 앱과, 오류·지연·적체를 보는 운영 화면 1개입니다.

## 여기에 둘 것

- React Native + TypeScript 앱 (공유 받기, 입력, 결과 화면, 운영 화면)
- 이 폴더만의 실행 방법과 테스트

## 지켜야 할 계약

[docs/contract.md](../docs/contract.md)의 아래 장을 따릅니다.

| 장 | 내용 |
|---|---|
| 2. 판정 값 | API는 영문 코드(`HIGH_RISK` 등)를 주고, 화면에는 한글(고위험 등)로 보여 줌 |
| 3. 판정 API | `POST /checks` 요청·응답. 결과 화면은 `verdict`, `evidence`, `guidance`를 씀. 오류 응답(5xx)은 "시스템 실패"로 판정 결과와 따로 표시 |
| 5-2. `gold_metrics` | 운영 화면이 읽을 집계 (읽는 방법은 C와 정하기, 계약 문서 7장) |

## QA와의 관계

- 앱이 보내는 요청과 QA 러너가 보내는 요청은 같은 API(`POST /checks`)입니다. QA 판정 시험이 통과하면 앱에서도 같은 판정이 나옵니다.
- 판정 시험 케이스(`qa/cases/rules.json`)의 정답 라벨은 A와 B가 함께 정합니다.
- 시연 화면 캡처가 필요하면 QA 데스크탑 앱의 캡처 모드를 쓸 수 있습니다 ([qa/README.md](../qa/README.md)).

## 작업 브랜치

`main`에서 `feature/app-...` 브랜치를 만들어 작업하고 PR로 합칩니다. 예: `feature/app-share-input`
