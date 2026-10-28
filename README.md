# 정보지킴

[![QA 러너 검사](https://github.com/seoloo/MLO2_3rd_Project/actions/workflows/qa.yml/badge.svg?branch=main)](https://github.com/seoloo/MLO2_3rd_Project/actions/workflows/qa.yml)

문자·URL에 담긴 위험 정황을 설명해 주는 앱과, 그 앱에서 나오는 이벤트를 처리하는 데이터 파이프라인을 함께 만드는 프로젝트입니다. 앱은 입력과 결과를 보여 주고, 프로젝트의 핵심 성과는 **데이터 처리가 정확하고 장애가 나도 복구된다는 것을 자동 QA로 증명**하는 데 있습니다.

> **현재 구현 상태 (2026.10.27):** QA 러너·보고서·데스크탑 앱·CI는 구현되어 있습니다. 실제 API·Kafka·MongoDB·Spark에 연결하는 QA 코드도 준비되어 있지만 통합 검증은 대기 중입니다. 앱·API·파이프라인·배포 환경은 설계 단계로, 아래 서비스 도식은 **목표 구조**입니다.

**바로 보기:** [QA 데스크톱 앱](#qa-desktop-ui) · [전체 구조](#전체-흐름) · [요청 순서](#검사-1건의-요청과-이벤트-전달-순서) · [데이터 처리](#data-processing) · [QA 검증](#qa-flow) · [진행 상황](#진행-상황) · [실행 방법](#시작하기)

## 무엇을 만드나

| 영역 | 내용 |
|---|---|
| 앱 | 문자를 공유하거나 붙여 넣으면 상황 질문(최대 3개)을 거쳐 판정과 근거, 대응 방법을 보여 줌 |
| 판정 | `고위험` / `주의` / `신호 미발견` / `판단 보류`. 판정을 못 내린 경우는 시스템 실패로 따로 표시 |
| 대상 상황 | 인증번호를 다른 사람에게 알려 달라는 요구, 외부 앱 설치 요구 |
| 데이터 파이프라인 | Kafka → Spark → MongoDB 메달리온 (Bronze → Silver → Gold) |
| 자동 QA | 입력값과 정답을 미리 정해 두고, 시스템 결과와 비교해 채점·보고서 작성 |

<a id="qa-desktop-ui"></a>

## 자동 QA 데스크톱 UI

첫 화면에서 확인하고 싶은 내용을 고르면 필요한 시험을 안내합니다. 실행 조건을 확인한 뒤 직접 시작하며, 완료 후에는 예상 정답·기준과 실제 값을 비교하고 다음 확인 사항을 볼 수 있습니다. 이전 실행은 이 PC에 저장한 보고서에서 읽습니다.

Windows 11 설정 앱 스타일로, [WPF UI](https://github.com/lepoco/wpfui) 4.3.0의 밝은 컨트롤(NavigationView 모양의 왼쪽 메뉴, `CardControl`, `InfoBar`, Fluent 아이콘)에 Slate·Indigo 색 조합을 입혔습니다. 시작하기 배너 배경은 선택한 20개 이미지 중 앱을 켤 때마다 한 장을 무작위로 표시합니다. 소스의 저작권·라이선스와 배경 출처는 `qa/src/QaRunner.Desktop/THIRD-PARTY-NOTICES.txt`에 기록했습니다.

처음 사용하는 경우 제목 표시줄의 **사용 안내** 또는 **F1**을 누르세요. 버튼·입력칸에 마우스를 잠시 올리면 기능과 입력 예시가 표시됩니다. 시작 방법은 [QA 사용 안내](qa/README.md#앱을-처음-사용하는-경우)에 있습니다.

공통 탐색, QA 실행, 결과 비교, 사례·검수 근거 조회, 로컬 공유와 보고서 이력을 구현했습니다. Runpod·EC2, 라벨 수정·승인, 세부 진행률·취소·설정 영구 저장은 후속 구현입니다.

디자인 검토용 PNG·PDF·HTML·압축 파일과 화면 캡처는 저장소에 보관하지 않습니다.

팀원에게 전달할 Windows 64비트 배포본은 `qa/publish-qa-desktop.cmd`로 만듭니다. 배경 20개를 포함해 실행 파일은 약 **27.3 MiB**, 전달용 ZIP은 약 **12.8 MiB**입니다. 실행 PC에는 .NET 10 Desktop Runtime(x64)이 필요합니다. Kafka·MongoDB 라이브러리와 Spark·Runpod·EC2 확장 구조는 유지하며, 배포 대상과 다른 운영체제의 바이너리만 해당 배포본에서 제외합니다. 자세한 내용은 [QA 배포 안내](qa/README.md#배포-파일로-실행하기)에 있습니다.

## 전체 흐름

앱에서 결과를 받는 경로와 데이터를 집계하는 경로를 나눕니다. **노란색은 구현 예정인 서비스**, **초록색은 구현된 QA 코드**입니다. 점선은 QA가 처리 결과를 조회하는 연결입니다. QA의 입력 경로는 아래 [검증 도식](#qa-flow)에 따로 표시했으며, 실제 서비스와의 연동 확인은 남아 있습니다.

```mermaid
flowchart TB
    subgraph service["① 사용자 판정 경로 · 구현 예정"]
        direction LR
        APP["React Native 앱<br/>문자 공유·붙여 넣기"] -->|"POST /checks"| API["FastAPI<br/>규칙 판정"]
        API -->|"함께 저장"| DB[("MongoDB<br/>checks + outbox")]
        API -->|"판정·근거·대응 안내"| APP
    end

    subgraph stream["② 비동기 집계 경로 · 구현 예정"]
        direction LR
        PUB["발행기<br/>outbox 전송"] --> KAFKA["Kafka<br/>scan-events"]
        KAFKA --> SPARK["Spark<br/>정제·중복 제거·집계"]
        SPARK --> MED[("MongoDB 메달리온<br/>Bronze · Silver · Gold · 격리")]
    end

    subgraph verification["③ 자동 검증 · QA 코드 구현됨"]
        direction LR
        QA["자동 QA 러너<br/>입력 주입·독립 정답 비교"] --> REPORT["HTML · JSON 보고서<br/>실행 기록 대시보드"]
    end

    service -->|"미전송 outbox를 발행기가 읽음"| stream
    stream -.->|"MongoDB 건수·집계 조회"| verification

    classDef planned fill:#fff7ed,stroke:#9a3412,color:#431407
    classDef ready fill:#ecfdf5,stroke:#047857,color:#064e3b
    class APP,API,DB,PUB,KAFKA,SPARK,MED planned
    class QA,REPORT ready
```

- 앱 결과는 API가 바로 돌려주고, 집계는 파이프라인이 비동기로 처리합니다.
- 이벤트에는 문자 원문·전체 URL·실제 인증정보를 넣지 않으며, Bronze에도 저장하지 않는 설계입니다. API의 `checks`에 원문을 보관할지는 [계약의 미확정 항목](docs/contract.md#7-정해야-할-것)입니다.
- 앱의 검사 결과는 `checks`, 운영 집계는 `gold_metrics`에서 조회하는 설계입니다. 운영 화면의 조회 API는 아직 확정하지 않았습니다.

### 검사 1건의 요청과 이벤트 전달 순서

연결 계약 초안 기준입니다. 앱이 결과를 받기 위해 Kafka·Spark 집계 완료까지 기다릴 필요는 없습니다.

```mermaid
sequenceDiagram
    autonumber
    participant App as 앱
    participant API as FastAPI
    participant DB as MongoDB
    participant Pub as 발행기
    participant Kafka as Kafka
    participant Spark as Spark

    App->>API: POST /checks (message)
    API->>API: 규칙 판정
    alt 판정·저장 성공
        API->>DB: 트랜잭션으로 checks + outbox 저장
        DB-->>API: 저장 완료
        API-->>App: 200 · 판정·근거·대응 안내
        Note over App,DB: 앱은 여기서 결과 표시
        Pub->>DB: pending outbox 읽기
        Pub->>Kafka: 이벤트 발행 (key = scanId)
        Kafka-->>Pub: 수신 확인 (ack)
        Pub->>DB: outbox를 sent로 표시
        Kafka->>Spark: 이벤트 전달
        Spark->>DB: Bronze → Silver / 격리 → Gold 처리
        Note over Pub,Kafka: 재전송도 같은 eventId 유지
    else 판정을 내리지 못한 시스템 실패
        API-->>App: 5xx · 시스템 실패 표시
        Note over API,DB: 판정 실패 이벤트는 만들지 않음
    end
```

`UNDECIDED`(판단 보류)는 정상 판정 코드이고, 시스템 실패는 오류 응답으로 구분합니다. `checks`와 `outbox`의 원자 저장을 위해 MongoDB 레플리카셋이 필요합니다.

## 실행안 요약 (v4.1)

4주 통합 실행안(v4.1, 2026.10.26)의 내용을 GitHub에서 바로 볼 수 있게 옮겨 둔 것입니다. 원본 PDF는 내부 문서라 저장소에 올리지 않습니다.

> **한 앱으로 합치고, 데이터 처리로 증명한다.** 문자·URL의 위험 정황을 설명하고, 이벤트 처리의 정확성·지연·복구를 검증합니다. (React Native + TypeScript · Kafka · Spark · MongoDB · 3명 · 4주)

### 범위: 한 앱 · 두 상황 · 최소 기능

- **대상 상황 두 가지:** 인증번호를 다른 사람에게 알려 달라는 요구, 외부 앱 설치 요구
- **기능:** 핵심 규칙 4~6개, 상황 질문 최대 3개, 근거와 대응 안내
- **판정:** 고위험 / 주의 / 신호 미발견 / 판단 보류를 구분하고, 시스템 실패는 따로 표시

| 팀원안에서 가져온 것 | 이유 |
|---|---|
| 상황·근거·개인정보 최소화 | URL 모양만으로 단정하지 않고 근거를 설명 |
| 공유 입력·결과·자동 검증 | 사용자 시연과 파이프라인 검증을 연결 |
| 우리 서비스의 오류·지연 (부분 수용) | API 오류·전송 적체·Spark 지연만, 별도 앱 없이 관찰 가능한 데이터 |

| 이번 달에 제외 → 후속 | 이유 |
|---|---|
| 결제 기능·외부 앱 감시 | 스미싱 검사와 다른 수집·정답 기준이 필요 |
| 사이트 방문·모델 추론 | 리디렉션·페이지 수집·Runpod 제외. 수집 환경과 검증 부담이 커짐 |
| 크래시·ANR·복잡한 화면 | 운영 화면 1개에서 오류·지연·적체만 봄 |
| 판정 수정·과거 재집계 | 첫 종결 결과를 고정하고, 재검사는 새 검사 ID로 |
| 발표 | 이번 실행안에서 제외, 이후 별도 진행 |

<a id="data-processing"></a>

### 메달리온: Kafka → Bronze → Silver → Gold (Spark query 1개)

아래는 Spark 작업에서 구현할 처리 규칙입니다. **Kafka 위치는 수신 기록의 고유 키**, **eventId는 중복 제거의 기준**, **occurredAt은 집계 구간의 기준**입니다.

```mermaid
flowchart TB
    K["Kafka 이벤트 수신"] --> B[("Bronze · bronze_events<br/>Kafka 위치별 보관")]
    B --> L{"수신 지연<br/>5분 초과?"}
    L -->|"예"| Q[("격리 · quarantine_events<br/>late_arrival / conflict")]
    L -->|"아니요"| D{"이미 수용한<br/>eventId인가?"}
    D -->|"아니요"| S[("Silver · silver_events<br/>정규화된 고유 이벤트")]
    D -->|"예"| C{"먼저 도착한<br/>내용과 같은가?"}
    C -->|"아니요 · conflict"| Q
    C -->|"예 · 단순 중복"| SKIP["Silver에 추가하지 않음<br/>Bronze 수신 기록은 유지"]
    S --> W["UTC 60초 구간 재계산<br/>occurredAt 기준"]
    W --> G[("Gold · gold_metrics<br/>절대값 upsert")]

    classDef bronze fill:#fff7ed,stroke:#9a3412,color:#431407
    classDef silver fill:#f1f5f9,stroke:#475569,color:#0f172a
    classDef gold fill:#fefce8,stroke:#a16207,color:#422006
    classDef isolated fill:#fef2f2,stroke:#b91c1c,color:#450a0a
    class B bronze
    class S silver
    class G gold
    class Q isolated
```

예를 들어 **같은 정상 이벤트를 총 3회 전송**하면 서로 다른 Kafka 위치의 Bronze 기록은 **3건**, Silver는 **1건**, 해당 Gold 구간에 더해질 검사 수는 **1건**입니다. 5분 초과 도착·동일 ID 상충은 Bronze에 수신 흔적을 남기고 격리합니다.

| 계층 | 컬렉션 | 하는 일 | 고유 기준 |
|---|---|---|---|
| Bronze | `bronze_events` | 받은 이벤트 보관. Kafka 위치·수신 시각, 재전송의 수신 흔적도 보존 | topic + partition + offset |
| Silver | `silver_events` | Spark로 스키마·시각 정규화, eventId 중복 제거 (현재 계약: 검사 1건 = 이벤트 1건) | eventId |
| Gold | `gold_metrics` | Spark로 60초 구간별 검사량·판정 분포 계산 (분모·실패율 정의는 미확정) | 구간 절대값 upsert (재실행 안전) |

원본은 수집용 최소 이벤트입니다. Bronze에도 문자 원문·전체 URL·실제 인증정보는 저장하지 않습니다. 앱 결과는 `checks`에서, 운영 집계는 Gold와 마지막 성공 시각에서 조회합니다.

<a id="qa-flow"></a>

### 자동 QA: 두 경로 입력 + 독립 정답 비교

QA는 문자로 API 판정을 확인하는 경로와, 이벤트를 Kafka에 직접 넣어 데이터 처리를 확인하는 경로를 따로 제공합니다. **정답 라벨은 API에 보내지 않고**, 파이프라인 정답은 QA가 전송 계획에서 독립 계산합니다.

```mermaid
flowchart TB
    subgraph verdict["① 판정 시험"]
        CASES["rules.json<br/>문자 + 정답 라벨"] -->|"문자만 전송"| API["판정 대상<br/>실제 API 또는 가짜 대상"]
        API -->|"실제 판정 / 시스템 실패"| VC["VerdictChecker<br/>판정 비교"]
        CASES -->|"정답 라벨"| VC
    end

    subgraph pipeline["② 파이프라인 시험"]
        SCENARIO["시나리오 JSON<br/>중복·역순·지연·상충"] --> PLAN["SendPlanner<br/>전송 계획"]
        PLAN -->|"시험 이벤트"| TARGET["실제 Kafka → Spark<br/>→ MongoDB<br/>또는 가짜 파이프라인"]
        PLAN --> EXPECT["독립 정답 계산기<br/>전송 계획에서 기대값 계산"]
        TARGET -->|"Bronze / Silver / 격리 건수<br/>Gold 구간별 total"| PC["PipelineChecker<br/>기대값과 실제값 비교"]
        EXPECT -->|"기대 건수·집계"| PC
    end

    VC --> REPORT["실행별 보고서<br/>HTML · JSON"]
    PC --> REPORT
    MEASURE["③ 부하 · ④ 복구 시험<br/>API·Gold p95 / 적체 / 회복 시간"] --> REPORT
    REPORT --> DASH["실행 기록 대시보드<br/>통과율·측정값 추이"]
```

- **대상 교체:** 연결 옵션이 없으면 가짜 대상, `--api-url`을 주면 실제 API, `--kafka`·`--mongo`를 주면 실제 파이프라인을 사용합니다. 채점기·보고서는 공통입니다.
- **현재 채점 범위:** 판정 코드·시스템 실패, Bronze·Silver·격리 건수, Gold 구간별 `total`을 비교합니다. 판정별 분포(`byVerdict`)·분모 검증과 앱 공유 E2E는 추가 구현·확인이 필요합니다.
- **CI 해석:** 현재 가짜 대상은 일부 오류를 의도적으로 내므로, 단위 테스트는 모두 통과해야 하지만 시연 시험의 종료 코드 `1`(채점 실패)은 허용하고 `2`(준비 오류)는 실패로 처리합니다. 초록 배지가 실제 서비스의 정확성이나 성능 달성을 뜻하지는 않습니다.

| 구분 | 목표 | 조건 |
|---|---|---|
| 정확성 (필수) | 중복 3회여도 집계 1회 | 역순·30초 지연 뒤에도 기대값과 일치. 같은 ID 상충·5분 초과 도착은 격리 |
| 반복 (필수) | API 100회 · 공유 E2E 10회 | 대표 경로 실패 0건, 재시도로 중복 생성 금지. 고정 규칙 평가 120개는 별도 보고 |
| 부하 (초기 목표) | API p95 3초 · Gold p95 10초 | API 동시 5개, 스트림 초당 5건 × 5분 = 1,500건. 오류율·적체·batch 시간 함께 측정 |
| 복구 (초기 목표) | 재가동 후 60초 안에 따라잡기 | 입력 유지 + Spark 30초 중단 후 재가동. 볼륨·checkpoint 유지 시 최종 집계 일치 |

증거로 남길 것: run_id · case_id · scan_id · event_id / 기대·실제 집계 / 오류·지연 / 화면 캡처. 기능 반복 시험·규칙 품질 평가·부하/복구 시험은 서로 구분하고, 표의 수치는 실측 전 목표입니다. 네 종류의 시험을 실행하는 [자동 QA 러너](qa/README.md)는 구현되어 있으며, API 100회·공유 E2E 10회·규칙 평가 120개 완료 여부와 실제 환경의 목표 달성은 별도 검증해야 합니다.

### 역할: 각자 코드·시험까지 담당

| 담당 | 맡는 일 | 협업 |
|---|---|---|
| A · 앱·화면·UI QA | React Native 공유·입력·결과, 운영 화면 1개·대표 공유 시험, 캡처·시연 영상·실행 안내 | B와 사례·문구 검수 |
| B · API·규칙·배포/CI | FastAPI·핵심 규칙 4~6개, checks/outbox·결과/집계 조회, EC2·Docker 배포·API 시험·CI | A와 라벨, C와 저장 경계 검토 |
| C · 파이프라인·데이터 QA | Kafka·Spark·Bronze/Silver/Gold, 정제·집계·중복·지연·재시작, 독립 계산기·부하/복구 측정 | 배포는 B, 증거 정리는 A와 연결 |

### 4주 일정: 총 20작업일, 매주 완료 증거 확보

개발과 자동 QA를 병행하고, 1주차 전체 연결 → 15일차 새 기능 종료로 진행합니다.

```mermaid
flowchart LR
    W1["1주차 · 1~5일<br/>계약·환경·첫 연결<br/>앱 1건 → Gold"] --> W2["2주차 · 6~10일<br/>필수 기능·QA 연결<br/>정상·의심·보류·실패 시연"]
    W2 --> W3["3주차 · 11~15일<br/>정확성·복구·실측<br/>15일차 새 기능 종료"]
    W3 --> W4["4주차 · 16~20일<br/>최종 QA·시연·예비일<br/>20일차 결과물 제출"]
```

작업일 기준 계획이며, 각 단계의 완료는 아래 증거로 확인합니다.

| 주차 | 목표 | 하는 일 | 완료 기준 |
|---|---|---|---|
| 1주차 (1~5일) | 계약·환경·첫 데이터 연결 | A 입력/결과·공유 검증 / B API·DB·배포/CI / C Kafka→Spark→B/S/G 최소 처리 | 앱 제출 1건 → Gold 조회 + 개발 사례 30개 |
| 2주차 (6~10일) | 필수 기능과 자동 QA 연결 | 상황·근거·운영 화면 / 두 상황 규칙·오류·재요청 / 정제·집계·중복·지연 시험 | 정상·의심·보류·실패 시연 + 자동 QA 첫 실행 |
| 3주차 (11~15일) | 정확성·복구·실측·기능 마감 | 공유 QA·고정 평가·로그 검사 / 저장 경계 종료·재시작 / 부하 측정·주요 실패 수정 | QA·복구 결과 + 결과 보고서 초안, 15일차 새 기능 종료 |
| 4주차 (16~20일) | 검증 보강·최종 QA·제출 | 16~17일 부하·복구 반복 측정·규칙 평가 120개·구성도 / 18일 최종 QA·시연 영상 / 19일 예비일 | 20일 APK·코드·실행 안내·보고서 제출 |

### 지연되면 기능 폭을 줄이고 연결·검증은 유지

| 점검일 | 확인할 것 |
|---|---|
| 3일차 | 실기기에서 API 호출 |
| 5일차 | 앱 → Gold 도달 |
| 10일차 | 전체 흐름·QA |
| 15일차 | 새 기능 종료 |

- **줄일 것:** 화면 장식, 상세 차트, 규칙 6개 → 4개
- **지킬 것:** 대표 공유, 두 상황, 메달리온 저장·집계 정확성, 재시작 복구
- 하루 투입 시간·기초 경험은 착수 때 확인하고, 미달 수치는 조건·원인과 함께 공개합니다.

### 1주차 계약에서 확정할 것

아래 항목은 [연결 계약 초안(docs/contract.md)](docs/contract.md)에 초안 값과 함께 정리되어 있고, 맨 아래 체크 목록으로 하나씩 확정합니다.

| 항목 | 내용 |
|---|---|
| MongoDB 레플리카셋 | checks + outbox 원자 저장의 전제 (단일 노드도 레플리카셋) |
| event_id ↔ scan_id | 검사 1건당 이벤트 수와 중복 판정 기준 |
| 격리 컬렉션 | 같은 ID 상충·5분 초과 도착을 사유와 함께 보관 |
| Gold 갱신 방식 | 이벤트 시각 60초 구간, 배치마다 해당 구간 재계산 후 upsert |
| 분모 정의 | 시스템 실패·판단 보류의 분모 포함 여부 |
| checks 저장 범위 | 문자 원문·URL 저장 여부와 보관 기간 |

## 기술 스택

| 영역 | 기술 |
|---|---|
| 앱 | React Native, TypeScript |
| API | FastAPI, MongoDB |
| 파이프라인 | Kafka, Spark, MongoDB |
| 배포 | EC2, Docker |
| 자동 QA | C#, .NET 10 (시연용 데스크탑 앱은 WPF) |

## 폴더 구조

| 폴더 | 내용 | 담당 | 상태 |
|---|---|---|---|
| [`docs/contract.md`](docs/contract.md) | **연결 계약**: 판정 API, Kafka 이벤트, MongoDB 컬렉션, 처리 규칙 | 전원 | 초안 (확정 필요) |
| [`app/`](app/README.md) | React Native 앱 | A | 준비됨 (작업 전) |
| [`api/`](api/README.md) | FastAPI · 판정 규칙 · 발행기 | B | 준비됨 (작업 전) |
| [`pipeline/`](pipeline/README.md) | Kafka · Spark · 메달리온 | C | 준비됨 (작업 전) |
| [`infra/`](infra/README.md) | docker-compose, EC2 배포 | B · C | 준비됨 (작업 전) |
| [`qa/`](qa/README.md) | 자동 QA 러너 | QA | 가짜 대상으로 완료, 실제 연결 코드 준비 |

각 폴더의 README에 **여기에 둘 것, 지켜야 할 계약, QA로 확인하는 법, 작업 브랜치 이름**이 적혀 있습니다. 작업을 시작하기 전에 자기 폴더의 README와 연결 계약을 먼저 읽어 주세요.

## 진행 상황

| 영역 | 상태 |
|---|---|
| 자동 QA 러너 | 판정 시험·보고서·API 호출 코드 완료. 파이프라인 시험과 부하·복구 시험(p95, 회복 시간)은 가짜 파이프라인으로 동작하고, Kafka·MongoDB 연결 대기. 시연용 데스크탑 앱(Windows)으로 화면에서 바로 돌려 볼 수 있고, 실행 기록 대시보드로 추이를 볼 수 있음. 푸시할 때마다 GitHub Actions가 빌드·테스트를 자동 실행. 자세한 내용은 [qa/README.md](qa/README.md) |
| 앱 · API · 파이프라인 | 예정 |

## 시작하기

자동 QA의 후속 개발은 `qa` 브랜치에서 준비합니다. 데스크탑 앱의 **시험 문자·공유 기록** 화면과 로컬 공유 API 뼈대를 추가했으며, 실제 Runpod·EC2 연결은 프로젝트 착수 후 진행합니다. 자세한 구조와 예제 실행은 [QA 연결 스켈레톤 안내](qa/docs/collaboration-skeleton.md)를 참고하세요. 기존 실행안에는 Runpod이 제외되어 있으므로, QA 사례 생성용 확장은 별도 초안으로 관리합니다.

영역별 실행 방법은 각 폴더의 README에 있습니다.

- 자동 QA 러너: [qa/README.md](qa/README.md)

## QA 결과 보기 · QA 러너 쓰기 (팀원용)

실행 파일을 주고받지 않습니다. 결과만 볼 사람은 웹 주소로 보고, 직접 돌릴 사람은 저장소에서 바로 켭니다.

### 결과만 보기: 웹 대시보드

**https://seoloo.github.io/MLO2_3rd_Project/** 를 즐겨찾기해 두고 열면 됩니다. 설치할 것이 없고 휴대폰에서도 열립니다.

<img src="docs/images/qa-dashboard.png" alt="실행 기록 대시보드" width="720">

자동 QA가 무엇을 왜 하는지는 [QA 한눈에 보기](qa/README.md#한눈에-보기), 지금 어디까지 왔는지는 [QA 로드맵](qa/README.md#진행-상황)에 그림으로 정리되어 있습니다.

- 기본 브랜치에 QA 코드가 푸시될 때마다 GitHub Actions가 시험을 돌리고 대시보드를 새로 올립니다.
- 통과율 추이, 부하·복구 측정값, 실행마다의 상세 보고서를 볼 수 있습니다.

### 직접 돌리기: 저장소에서 바로 실행 (Windows)

처음 한 번만 준비합니다.

```bash
winget install Microsoft.DotNet.SDK.10
```

```bash
git clone https://github.com/seoloo/MLO2_3rd_Project.git
```

그다음부터는 **`qa/run-qa-desktop.cmd`를 더블클릭**하면 됩니다. 최신 코드를 받고(`git pull`), 빌드한 뒤, QA 러너 데스크탑 앱을 켭니다. 항상 최신 버전이 실행되므로 따로 업데이트할 필요가 없습니다.

자기 API를 시험하려면 앱의 **판정 API 주소** 칸에 주소(예: `http://localhost:8000`)를 넣고 실행합니다. 자세한 사용법은 [qa/README.md](qa/README.md)에 있습니다.

### 저장소 관리자가 처음 한 번 할 일

웹 대시보드가 올라가려면 GitHub 저장소에서 **Settings → Pages → Build and deployment → Source**를 **GitHub Actions**로 바꿔야 합니다. 바꾸기 전에는 검사의 "대시보드 공개" 단계가 실패로 표시됩니다.

## 협업 규칙

- **브랜치**: `main`에서 작업 단위로 `feature/<작업 이름>` 브랜치를 만들고, 끝나면 PR로 `main`에 합친 뒤 지웁니다. PR을 열면 확인 목록(템플릿)이 자동으로 채워집니다.
- **계약**: 다른 영역과 주고받는 형식을 바꿀 때는 [docs/contract.md](docs/contract.md)를 먼저 고치고, 영향받는 담당에게 PR 리뷰를 받습니다.
- **커밋 메시지**: 한국어로, 무엇을 했는지 사람이 읽기 쉽게 씁니다. `feat:` 같은 영어 접두어는 붙이지 않습니다.
- **코드**: 이름은 무엇인지 바로 알 수 있게 짓고, 클래스·함수에는 무엇이고 왜 있는지 주석을 답니다.
- **README**: 코드, 실행 방법, 폴더가 바뀌면 해당 README도 같은 커밋에서 고칩니다.
- **자동 검사**: 푸시하면 GitHub Actions가 검사합니다. 커밋 옆에 ✘가 뜨면 Actions 탭에서 원인을 보고 고칩니다.
- **문서**: 실행안 PDF 같은 내부 문서는 `docs/plan/`에 두며 git에 올리지 않습니다. 팀이 함께 봐야 하는 내용은 이 README의 [실행안 요약](#실행안-요약-v41)에 옮겨 둡니다.
