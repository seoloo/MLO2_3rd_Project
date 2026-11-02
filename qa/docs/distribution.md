# 팀원 배포

[← QA 안내로 돌아가기](../README.md)

설치부터 첫 시험까지 따라 하는 자세한 설명은 [설치·사용 안내](install-and-use.md)에 있습니다. 이 절은 QA 담당이 배포 방식을 관리할 때 보는 요약입니다.

| 하고 싶은 것 | 방법 | 필요한 것 |
|---|---|---|
| 결과만 보기 | 웹 대시보드 **https://seoloo.github.io/MLO2_3rd_Project/** 열기 | 없음 (브라우저만) |
| 배포 파일로 시험 돌리기 | `QA-windows-x64.zip`을 풀고 `QaRunner.Desktop.exe` 실행 | Windows x64, .NET 10 Desktop Runtime(x64) |
| 직접 시험 돌리기 (예: 내 API 시험) | `qa/run-qa-desktop.cmd` 더블클릭 | 처음 한 번: .NET 10 SDK 설치, 저장소 clone |

## 결과만 보기: 웹 대시보드

- 기본 브랜치(`main`)에 푸시할 때마다 GitHub Actions가 네 가지 시험을 돌리고, 실행 기록을 `qa-history` 브랜치에 쌓은 뒤 대시보드를 GitHub Pages에 올립니다.
- 대시보드의 실행 번호를 누르면 그 실행의 상세 보고서가 열립니다.
- 웹 대시보드는 CI 기록만 보여 줍니다. 각자 PC에서 돌린 기록은 각자의 `qa/reports/dashboard.html`에 쌓입니다.

## 배포 파일로 실행하기

QA 담당자가 `qa/publish-qa-desktop.cmd`를 더블클릭하면 `qa/dist/QA-windows-x64.zip`이 만들어집니다. .NET 10 SDK가 필요하며, 이 명령은 Git에서 코드를 받거나 푸시하지 않습니다.

1. ZIP을 [GitHub Releases](https://github.com/seoloo/MLO2_3rd_Project/releases)에 새 릴리스로 올려 링크를 공유합니다. 태그는 `qa-v<주>.<부>.<수>` 형식(예: `qa-v1.0.0`)으로 붙이고, 바뀐 점을 릴리스 설명에 적습니다. 메신저로 ZIP을 직접 보내면 누가 어느 버전을 쓰는지 알기 어렵습니다.
2. 팀원은 ZIP을 쓸 수 있는 폴더에 **전부 압축 해제**합니다. ZIP에는 실행 파일, `cases` 폴더, 라이선스 안내, [설치·사용 안내](install-and-use.md)가 들어 있습니다.
3. 처음 한 번 .NET 10 Desktop Runtime(x64)을 설치하고 `QaRunner.Desktop.exe`를 실행합니다. SDK는 필요하지 않습니다.

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10
```

실행 기록은 압축을 푼 폴더의 `reports/`에 저장됩니다. 배포 파일에는 팀원 간 원격 동기화 기능이 없습니다.

2026-10-29 v1.1.0 로컬 Release 빌드 기준 크기입니다. 기능이 늘어나면 달라집니다.

| 구성 | 크기 |
|---|---:|
| JPEG 배경 20개 합계 (실행 파일에 포함) | 3.23 MiB |
| Windows x64 실행 파일 | 27.59 MiB |
| 배포 폴더 전체 (실행 파일 · cases · 라이선스 안내) | 27.60 MiB |
| 전달용 ZIP (설치·사용 안내 포함) | 12.74 MiB |

- 배포본은 Windows x64만 묶고 .NET 실행 환경은 따로 설치합니다. 다른 운영체제용 바이너리는 소스 의존성을 유지한 채 이 배포본에서만 뺍니다.
- Kafka·MongoDB 라이브러리, Spark 연결 코드, Runpod·EC2 확장 구조는 삭제하지 않습니다. Windows용 Kafka 네이티브 라이브러리는 실행 파일에 포함하고, UI 점검에서 실제 로드를 확인합니다.
- WPF와 이후 확장을 위해 트리밍은 쓰지 않습니다.
- `dist/`·빌드 결과·ZIP은 Git에 올리지 않습니다. 앱이 실제로 쓰는 압축 배경 이미지는 소스에 포함합니다.

## 직접 돌리기: run-qa-desktop.cmd

처음 한 번 .NET 10 SDK를 설치하고 저장소를 받습니다.

```bash
winget install Microsoft.DotNet.SDK.10
```

```bash
git clone https://github.com/seoloo/MLO2_3rd_Project.git
```

그다음부터는 탐색기에서 `qa/run-qa-desktop.cmd`를 **더블클릭**합니다. 매번 ① 최신 코드 받기(`git pull`, 고치던 파일이 있으면 안내만 하고 계속) → ② 데스크탑 앱 빌드 → ③ 앱 실행 순서로 동작합니다. 앱이 이미 켜져 있으면 빌드할 수 없으니 먼저 닫으라고 안내합니다. 빌드가 실패하면 오류를 보여 주고 멈추므로, 그 내용을 QA 담당에게 알려 주세요.
