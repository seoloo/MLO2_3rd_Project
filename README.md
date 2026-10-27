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

영역별 실행 방법은 각 폴더의 README에 있습니다.

- 자동 QA 러너: [qa/README.md](qa/README.md)

## QA 결과 보기 · QA 러너 쓰기 (팀원용)

실행 파일을 주고받지 않습니다. 결과만 볼 사람은 웹 주소로 보고, 직접 돌릴 사람은 저장소에서 바로 켭니다.

### 결과만 보기: 웹 대시보드

**https://seoloo.github.io/MLO2_3rd_Project/** 를 즐겨찾기해 두고 열면 됩니다. 설치할 것이 없고 휴대폰에서도 열립니다.

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
- **문서**: 실행안 PDF 같은 내부 문서는 `docs/plan/`에 두며 git에 올리지 않습니다.
