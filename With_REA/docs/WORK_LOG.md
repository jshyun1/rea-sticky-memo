# 실제 도구와 작업 기록

2026-10-27, Asia/Seoul. 원본 분석 자료는 제공된 설치 파일과 이번 세션 REA 근거로 한정했다. 기존 `Only_Codex_agent` 파일을 열거나 실행·재사용하지 않았다. 소스·테스트는 이번 세션에서 직접 작성했다.

## 준비와 연결

1. `exec_command`: `pwd`, 작업 루트 `ls -la`, 범위를 제한한 `rg --files`, 상위 `AGENTS.md` 존재 여부 확인. `With_REA` 파일 목록은 비어 있었다. 기존 비교 구현은 검색 제외했다.
2. `cat`/`sed`: `/home/jsh/.agents/skills/reverse-engineer-anything/SKILL.md`를 읽고 관리 PE/CLI 경로와 증거 보존 지침 적용. 스킬은 작업 절차이며 제품 분석 자료로 사용하지 않았다.
3. `ALL_TOOLS` 메타데이터에서 REA 도구 및 정확한 스키마 확인. 처음 도구 설명 출력은 과대하여 잘렸으므로 이후 선택한 도구만 조회했다.
4. REA `binary_session({})` 2회: 실제 연결 응답 성공. 두 번째 전체 응답을 `docs/evidence/rea-session.json`에 보존했다. 네이티브 엔진은 모두 가용하지 않지만 관리 코드 정적 분석은 가능했다. 설정 변경/도구 설치 없음.
5. `file`: 원본은 PE32 Mono/.NET assembly. `command -v` 등으로 도구 확인: Python3/g++/unzip 존재, Linux dotnet/Mono/Wine/MinGW/Xvfb는 발견되지 않았다. `openssl`은 존재만 확인했고 실행하지 않았다. Python tkinter/pefile/dnfile은 없고 pytest/PIL은 있었으나 이 PoC 테스트에는 사용하지 않았다.

## REA 분석 (원본 미실행)

| 도구 | 실제 인자/범위 | 수행 작업 / 보존 결과 |
|---|---|---|
| `inspect_managed_artifact` | 원본 절대 경로 | 어셈블리·CLR·참조·관리 리소스 식별 E1 |
| `inspect_managed_members` | 원본, `detail:summary` | 전체 메타데이터/CIL 분석 E2와 요약 Evidence. 완전한 결과는 REA ledger에 보존됨 |
| `inspect_analysis_view` | E2, managed-members/types, offset 0 limit 400 | 326개 타입 페이지 |
| `inspect_analysis_view` | E2, user_strings, offset 0 limit 1000 | 871개 문자열 E4 |
| `inspect_managed_members` | 원본, detail summary, 5개 exact type_names | Launcher, TagExtractor, ViewModels.InstallViewModel, Acquisition.InstallState, Acquisition.PackageRetrieval.PackageDownloader → E3 |
| `inspect_pe_resources` | 원본 절대 경로 | PE 리소스 11개 E8 |
| `inspect_analysis_view` | E3, methods/fields/call_edges 각각 offset 0 limit 1000 | 111/100/525개 행. E5/E6/E7 |
| `export_evidence_bundle` | `With_REA/docs/evidence/rea-bundle.json` | 11 records, 13,979,598 bytes. 요약 화면의 잘림을 완전한 파일로 보완 |
| `close_binary({})` | 세션 종료 정리 | result null. 활성 네이티브 타깃은 없었음 |

전체 타입/문자열을 출력한 일부 채팅 응답은 클라이언트 출력 한도로 잘렸다. 분석 성공을 완전한 출력으로 오인하지 않고 원래 레코드와 페이지를 저장한 번들을 보존했다. 최종 파일은 Python JSON 파싱으로 읽고 레코드 수/ID를 확인했다. 보고서의 결론에는 해당 ID·토큰·오프셋을 연결했다.

## 원본 바이트 보조 분석

Python 표준 라이브러리 `struct`, `zlib`, `json`, `hashlib`, `pathlib`만 사용했다. REA에서 확인한 태그/CIL 호출을 따라 PE 보안 디렉터리와 태그를 읽었다. 처음에는 태그 바로 다음 블록을 압축 데이터로 가정해 해제 실패했다. REA의 두 길이 필드와 서명/데이터 순서를 따라 수정했고, 중간에 태그 길이를 12로 적은 일회성 탐색은 범위 오류로 실패했다. 실제 길이 `len(tag)=13`, 서명 256, 압축 1756을 적용하자 해제가 성공했다. 원본은 항상 `read_bytes()`로만 읽었다.

완성한 추출기 재현 명령(출력 폴더의 같은 이름 파일이 이미 있으면 덮어쓰지 않고 실패):

```bash
python3 With_REA/tools/extract_metadata.py \
  'Microsoft Sticky Notes Installer.exe' \
  With_REA/docs/evidence/metadata
```

태그 추출/압축 해제는 도구가 제공한 원본 근거를 따라 작성한 신규 보조 코드이며 원본 프로그램을 로드·실행하지 않았다. 로고 URL 등 내장 URL을 요청하지 않았다. RSA/Authenticode 검증 도구는 실행하지 않았다.

## 구현·빌드·검증

1. `apply_patch`: `src/NoteStore.cs`, `src/Program.cs`, `src/Windows.cs`, `tests/Tests.cs`, 빌드·실행 스크립트를 신규 작성. 원본 코드/리소스나 타 프로젝트 코드를 복사하지 않았다.
2. `/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe`: Windows bridge 첫 호출은 샌드박스 내부에서 `UtilBindVsockAnyPort ... socket failed`로 실행되지 않았다. 동일한 최소 환경 확인을 승인된 escalation으로 실행하여 Windows PowerShell 5.1 및 기본 C# 컴파일러 존재를 확인했다. REA 연결 장애는 아니었다.
3. Windows 기본 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`: `build.ps1`로 앱과 테스트를 컴파일했다. 외부 패키지 다운로드 없음.
4. 테스트 첫 실행 8/9. 앱 창 생성 시점을 기다리는 테스트 수정 후 9/9. 이후 UI 저장 오류/닫기 취소/재시도 테스트를 보강하고 최종 9/9. [상세 결과](TEST_RESULTS.md).
5. 실제 사용한 WSL → Windows 명령:

```bash
/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe \
  -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass \
  -File '\\wsl.localhost\Ubuntu-22.04\home\jsh\rea-sticky-memo\With_REA\build.ps1' \
  -Test > With_REA/docs/test-output.txt 2>&1
```

6. Windows PowerShell로 OS/CLR/컴파일러 파일 버전과 .NET Framework Release 레지스트리 값을 읽어 `windows-environment.json` 기록. 사용자 메모·계정 정보는 조회하지 않았다.
7. 테스트는 실제 WinForms 두 프로세스와 배포 앱 프로세스를 실행했다. 테스트 데이터는 `With_REA/artifacts/test-runs` 아래에만 작성했다. 테스트 종료 후 해당 프로세스를 남기지 않았다.
8. `view_image`: 생성한 `restored-windows.png`를 시각 확인. `DrawToBitmap` 합성이므로 실제 바탕화면이나 다른 앱을 캡처하지 않았다. 최종 실행 이미지를 `docs/preview.png`로 보존했다.
9. `sha256sum` 및 Python hashlib: 분석 전 REA 해시와 분석 후 파일 해시 일치 확인. `docs/integrity.json` 생성. 원본은 변경하지 않았고 사용자 데이터에 접근하지 않았다.
10. Python 표준 `zipfile`: 소스·문서·근거·Windows EXE를 묶고 파일별 SHA-256 목록 생성. 자체 테스트의 실행별 데이터 폴더와 제공 원본 EXE는 배포 ZIP에서 제외했다. ZIP을 다시 열어 CRC·manifest를 검증했다.

`git status --short --untracked-files=no`를 읽기 전용으로 확인했으며, 커밋/설정 변경/배포/외부 전송은 하지 않았다. 원본 설치 실행·Store 접속·설치된 메모 앱 획득은 수행하지 않았다. 미수행 항목을 실행 성공으로 표기하지 않았다.
