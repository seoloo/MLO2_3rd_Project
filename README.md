# 정보지킴

[![QA 러너 검사](https://github.com/seoloo/MLO2_3rd_Project/actions/workflows/qa.yml/badge.svg?branch=feature/qa-runner)](https://github.com/seoloo/MLO2_3rd_Project/actions/workflows/qa.yml)

문자·URL에 담긴 위험 정황을 설명해 주는 앱과, 그 앱에서 나오는 이벤트를 처리하는 데이터 파이프라인을 함께 만드는 프로젝트입니다. 앱은 입력과 결과를 보여 주고, 프로젝트의 핵심 성과는 **데이터 처리가 정확하고 장애가 나도 복구된다는 것을 자동 QA로 증명**하는 데 있습니다.

## 무엇을 만드나

| 영역 | 내용 |
|---|---|
| 앱 | 문자를 공유하거나 붙여 넣으면 상황 질문(최대 3개)을 거쳐 판정과 근거, 대응 방법을 보여 줌 |
| 판정 | `고위험` / `주의` / `신호 미발견` / `판단 보류`. 판정을 못 내린 경우는 시스템 실패로 따로 표시 |
| 대상 상황 | 인증번호를 다른 사람에게 알려 달라는 요구, 외부 앱 설치 요구 |
| 데이터 파이프라인 | Kafka → Spark → MongoDB 메달리온 (Bronze → Silver → Gold) |
| 자동 QA | 입력값과 정답을 미리 정해 두고, 시스템 결과와 비교해 채점·보고서 작성 |

## 전체 흐름

```
React Native 앱 ──▶ FastAPI + MongoDB ──▶ 발행기 ──▶ Kafka ──▶ Spark ──▶ MongoDB 메달리온
 문자 입력·결과       규칙 판정, 결과 즉시 반환          scan_id 기준      Bronze: 받은 이벤트 보관
                      checks + outbox 저장                                Silver: 정제·중복 제거
                                                                         Gold:   60초 구간 집계

자동 QA 러너 ── 입력을 넣고 ──▶ (API · Kafka) ── 결과를 읽어 ──▶ 정답과 비교 ──▶ 보고서
```

- 앱 결과는 API가 바로 돌려주고, 집계는 파이프라인이 비동기로 처리합니다.
- 문자 원문, 전체 URL, 실제 인증정보는 파이프라인 어디에도 저장하지 않습니다.

## 기술 스택

| 영역 | 기술 |
|---|---|
| 앱 | React Native, TypeScript |
| API | FastAPI, MongoDB |
| 파이프라인 | Kafka, Spark, MongoDB |
| 배포 | EC2, Docker |
| 자동 QA | C#, .NET 10 (시연용 데스크탑 앱은 WPF) |

## 폴더 구조

| 폴더 | 내용 | 상태 |
|---|---|---|
| [`qa/`](qa/README.md) | 자동 QA 러너 | 진행 중 |
| `app/` | React Native 앱 | 예정 |
| `api/` | FastAPI · 판정 규칙 | 예정 |
| `pipeline/` | Kafka · Spark · 메달리온 | 예정 |
| `infra/` | docker-compose 등 실행 환경 | 예정 |

## 진행 상황

| 영역 | 상태 |
|---|---|
| 자동 QA 러너 | 판정 시험·보고서·API 호출 코드 완료. 파이프라인 시험과 부하·복구 시험(p95, 회복 시간)은 가짜 파이프라인으로 동작하고, Kafka·MongoDB 연결 대기. 시연용 데스크탑 앱(Windows)으로 화면에서 바로 돌려 볼 수 있음. 푸시할 때마다 GitHub Actions가 빌드·테스트를 자동 실행. 자세한 내용은 [qa/README.md](qa/README.md) |
| 앱 · API · 파이프라인 | 예정 |

## 시작하기

영역별 실행 방법은 각 폴더의 README에 있습니다.

- 자동 QA 러너: [qa/README.md](qa/README.md)

## 협업 규칙

- **브랜치**: 작업 단위로 `feature/<작업 이름>` 브랜치를 만들고, 끝나면 PR로 `main`에 합친 뒤 지웁니다.
- **커밋 메시지**: 한국어로, 무엇을 했는지 사람이 읽기 쉽게 씁니다. `feat:` 같은 영어 접두어는 붙이지 않습니다.
- **코드**: 이름은 무엇인지 바로 알 수 있게 짓고, 클래스·함수에는 무엇이고 왜 있는지 주석을 답니다.
- **README**: 코드, 실행 방법, 폴더가 바뀌면 해당 README도 같은 커밋에서 고칩니다.
- **자동 검사**: 푸시하면 GitHub Actions가 검사합니다. 커밋 옆에 ✘가 뜨면 Actions 탭에서 원인을 보고 고칩니다.
- **문서**: 실행안 PDF 같은 내부 문서는 `docs/plan/`에 두며 git에 올리지 않습니다.
