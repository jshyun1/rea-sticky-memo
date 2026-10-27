# Sticky Memo PoC · With_REA

Windows용 독립 스티커 메모 PoC입니다. 이번 세션에서 소스와 테스트를 새로 작성했습니다. `Only_Codex_agent`의 소스·문서·테스트·실행 파일은 읽거나 재사용하지 않았습니다.

제공된 원본은 `Microsoft Sticky Notes Installer.exe`입니다(사용자 확인). REA로 설치 프로그램의 정체와 설치 관련 코드를 분석했습니다. 메모 앱 본체의 동작은 확인하지 못했으므로, 아래 기능은 **사용자가 요청한 기능을 독립 구현한 것**입니다. 원본과의 동작 동등성을 주장하지 않습니다.

- 여러 독립 메모 창, 생성·수정·영구 삭제
- 입력/이동/크기/색상 변경 후 450ms 동안 변화가 없으면 자동 저장
- 앱 재실행 시 저장한 메모 전체 복원
- 메모 목록, 창 다시 열기, 네 가지 색상, Ctrl+N / Ctrl+S
- 완성된 임시 파일 교체, 이전 버전 백업, 같은 저장 폴더의 중복 실행 방지

## 바로 실행

Windows 10/11과 .NET Framework 4.8 이상을 전제로 합니다. 실제 검증 환경은 Windows NT 10.0.26200.0, .NET Framework 4.8.1 계열입니다. 다른 Windows 버전은 미수행입니다.

압축 패키지를 로컬 폴더에 풀고 `run.cmd` 또는 `artifacts\StickyMemo.exe`를 실행하세요. 빌드된 실행 파일을 포함했습니다. Microsoft 계정이나 네트워크는 필요하지 않습니다.

```powershell
.\artifacts\StickyMemo.exe
# 별도 저장 폴더 지정
.\artifacts\StickyMemo.exe --data-dir "$PWD\my-poc-data"
```

메모 목록의 **+ 새 메모** 또는 메모 창의 **+**로 생성합니다. 메모 창의 X는 창만 닫고, 목록에서 다시 열 수 있습니다. **삭제**는 확인 후 영구 삭제합니다. 메모 목록 창의 X는 모든 메모를 저장하고 앱을 종료합니다. 빈 저장소로 시작하면 빈 목록을 표시합니다.

기본 저장 위치는 `%LOCALAPPDATA%\WithREA\StickyMemoPoC\notes.json`입니다. `.bak`은 직전 저장본입니다. Microsoft Sticky Notes의 데이터는 찾거나 읽지 않습니다. 저장 실패 시 메모 아래에 실패 상태를 표시하고 2초 후 재시도합니다. 미저장 상태에서는 창 닫기와 앱 종료를 취소합니다.

## 빌드와 테스트

외부 NuGet 패키지나 추가 SDK 없이 Windows 기본 .NET Framework C# 컴파일러를 사용합니다. Windows PowerShell에서:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

산출물은 `artifacts\StickyMemo.exe`, `artifacts\StickyMemo.Tests.exe`입니다. 테스트는 `artifacts\test-runs\<실행별 고유 폴더>`에만 가상 메모를 만듭니다. UI 테스트에는 Windows 데스크톱 세션이 필요합니다.

이 작업에서 사용한 WSL 실행 명령은 [작업 기록](docs/WORK_LOG.md)에 있습니다. Linux에서 EXE를 직접 실행하는 앱은 아닙니다.

## 제출 파일

| 경로 | 내용 |
|---|---|
| `src/` | 신규 C# 소스: 저장소, 시작점, Windows Forms UI |
| `tests/Tests.cs` | 신규 저장·오류·실제 UI·별도 프로세스 복원 테스트 |
| `build.ps1`, `run.cmd` | 빌드·실행 |
| [분석 보고서](docs/ANALYSIS.md) | 원본 확인 기능, 구현 기능, 미구현·불확실성, 결론별 근거 |
| [설계 설명](docs/DESIGN.md) | 데이터 모델, 저장·창 수명주기와 제한 |
| [테스트 결과](docs/TEST_RESULTS.md) | 최종 9개 그룹 통과, 미수행 항목 |
| [작업 기록](docs/WORK_LOG.md) | 실제 도구·작업·실패와 수정 이력 |
| `docs/evidence/` | REA 원본 Evidence 11개, 연결 상태, 원본 메타데이터 추출 결과 |
| `docs/preview.png` | Windows UI 테스트 중 각 폼을 렌더링해 합성한 복원 화면 |
| `docs/integrity.json`, `SHA256SUMS.txt` | 원본 불변 확인 및 제출 파일 해시 |

원본 설치/다운로드/실행, 원본 앱의 메모 DB·자동 저장 주기·창 동작 검증은 **미수행**입니다. 이번 결과를 원본 UI의 정확한 복제나 원본 데이터 호환 제품으로 해석하면 안 됩니다.
