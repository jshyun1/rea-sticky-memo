# 분석 보고서

분석일: 2026-10-27 (Asia/Seoul). 분석 자료는 제공된 원본 파일과 이번 세션의 REA Evidence, 그 Evidence를 따라 원본 바이트에서 추출한 데이터뿐입니다. 기존 `Only_Codex_agent` 결과나 외부 웹 자료를 사용하지 않았습니다.

## 입력과 연결

작업 시작 시 `With_REA`에는 파일이 없었습니다. 제공 파일은 `Microsoft Sticky Notes Installer.exe` 한 개이며 사용자가 이 파일이 맞다고 확인했습니다. 별도의 `StickyNotes.exe`를 임의로 찾거나 다른 설치본을 분석하지 않았습니다.

| 항목 | 확인 값 | 근거 |
|---|---|---|
| 입력 크기 | 813,088 bytes | E1, LOCAL-01 |
| 입력 SHA-256 | `646f1719fcae077b5f1ed356d71baf552713c4c5f378bfdd5e04710f62e847f6` | E1, [최종 불변 확인](integrity.json) |
| REA 연결 | MCP 호출 성공, rea-agents / server 6.7.0 | [binary_session 응답](evidence/rea-session.json) |
| REA 관리 코드 도구 | .NET 정적 식별·메타데이터·CIL·호출 관계 분석 가능 | session capabilities, E1–E7 |
| 네이티브 엔진 | Ghidra 미설정, Hopper 실행 파일 없음, IDA/Kuna 미설정 | session analysis_provider_candidates |

`open:false`는 열린 네이티브 타깃이 없다는 뜻이며 연결 실패가 아닙니다. 관리 코드 분석 도구는 명시적 경로를 받으므로 `open_binary` 없이 사용했습니다. 원본이 CIL 어셈블리이므로 사용 가능한 REA 관리 코드 정적 분석을 선택했고, 불필요한 엔진 설치는 하지 않았습니다.

## 결론과 근거

| ID | 결론 | 관찰 / 추론 / 한계 | 확인 근거 |
|---|---|---|---|
| R1 | 파일은 PE32/CLI, IL-only AnyCPU 어셈블리 `StoreInstaller`, 버전 `22609.1501.4.0`이다. | 정적 관찰. PE machine x86과 managed AnyCPU를 구분한다. | E1: module `StoreInstaller.exe`, MVID `e5003582-3cf2-41f9-93b3-9c9059cb8961`, CLI flags=1 |
| R2 | .NET Framework 4.7.2를 대상으로 하고 WPF 관련 어셈블리를 참조한다. | 어셈블리 속성과 참조 관찰. 실행 가능한 실제 의존성 충족 여부는 원본에 대해 미검증. | E1: TargetFrameworkAttribute, PresentationFramework/PresentationCore/WindowsBase/System.Xaml 참조 |
| R3 | 이 설치 파일에 내장된 제품 정보는 Microsoft Sticky Notes를 가리킨다. | 원본 바이트 관찰. 내장 메타데이터의 주장이고 서명 진위는 검증하지 않았다. | LOCAL-01: title, productId=`9NBLGGH4QGHW`, PFN=`Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe`, installerType=`WindowsUpdate` |
| R4 | 설치 UI의 주 동작 코드는 상태에 따라 설치·재시도·취소·제품 실행으로 분기한다. | CIL 호출 관계 관찰. 실제 설치 성공이나 클릭 결과는 관찰하지 않았다. | E5/E7: `HandleActionButtonClickAsync` token `0x0600037d`, body file offset 60176, IL size 106. IL +42 → RetryInstallAsync, +51/+60 → InstallProductAsync, +70/+80 → CancelInstallationAsync, +89 → LaunchProductAsync |
| R5 | 설치 진행률·일시 정지·완료·오류 상태를 처리하는 코드가 있다. | 메서드·enum·문자열 및 CIL 관찰. 메모 저장 상태를 의미하지 않는다. | E5: UpdateProgressText `0x06000385`, file offset 60680, size 1078. E6: InstallState fields; E4: InstallationStarting/InstallationPaused/InstallationDownloadingPercent |
| R6 | Store로 전환하는 URI 구성과 실행 호출이 있다. | 코드 관찰. 해당 URI를 열거나 네트워크 요청하지 않았다. | E5/E7: Launcher.LaunchStore `0x060002f8`, offset 51260, size 174. IL +141은 E4의 `ms-windows-store://pdp`를 로드. OpenStore/RedirectToStore도 메서드 목록에 존재 |
| R7 | 제품 태그 추출은 인증서 확장을 조회하고, 길이 필드·RSA VerifyData·DeflateStream·JSON 역직렬화 API를 사용하는 구조다. | 호출 관계와 CIL 관찰. 원본의 검증 코드를 실행하지 않았고 RSA/Authenticode 검증 성공을 주장하지 않는다. | E5/E7: TagExtractor.RetrieveMetadata / ExtractMetadata `0x060002ed` / `.cctor` `0x060002ef`. E4 #US `0x70004a45` = `MSStoreTag001` |
| R8 | PE 리소스 트리에는 아이콘 8개, 그룹 아이콘 1개, 버전 1개, 매니페스트 1개가 관찰된다. 관리 리소스는 5개다. | REA가 해당 트리를 완전히 읽은 범위의 관찰. 리소스 전체를 UI/앱 본체로 해석하지 않는다. | E8: resources 11, examined_entries 26, icon size matches. E1: StoreInstaller.g.resources 및 4개 .bin |
| R9 | 확보한 근거로 메모 앱의 CRUD·자동 저장·복원·다중 창의 구체적 동작은 확정할 수 없다. | 명시적 미확인. 설치 코드/상태와 제품 이름만으로 메모 동작을 추론하지 않는다. | E2의 전체 메타데이터/CIL 목록과 위 설치 중심 코드, 제공 입력이 설치 파일 하나인 범위. 앱 패키지는 미수집, 원본 런타임은 미실행 |

특히 `InstallState.RestoringData`는 설치 상태의 필드입니다. 이것을 “메모가 재실행 시 복원된다”는 증거로 사용하지 않았습니다. 내장 `autoUpdate=false` 역시 설치 페이로드의 필드이며 메모 자동 저장 설정이 아닙니다. E4의 별도 문자열 `9NBLGGH4NNS1`을 설치 대상 ID로 오인하지 않았으며 제품 ID는 추출된 제품 페이로드에서 확인했습니다.

## 원본 제품 메타데이터 복구

REA의 TagExtractor 정적 근거를 따라 [새로 작성한 추출기](../tools/extract_metadata.py)를 원본에 읽기 전용으로 실행했습니다. 자체 로컬 근거 `LOCAL-01`이며 REA가 직접 반환한 Evidence인 것처럼 표기하지 않습니다.

1. PE 보안 디렉터리 파일 범위: offset 783872, size 29216.
2. `MSStoreTag001` ASCII 태그 위치: 789739. 태그 길이 13 bytes.
3. little-endian 길이 256 → 서명 바이트 offset 789756.
4. 다음 길이 1756 → 압축 데이터 offset 790016.
5. raw DEFLATE 해제 결과: 18,275 bytes JSON. JSON 원문 바이트 SHA-256 `cbb17890b29362dd79d64f751aef093d8c13285c8779d672ea2951748353662a`.
6. 66개 언어의 제품 표시 정보, schema=4, 제품 ID/PFN/installerType 등을 확인.

추출기는 원본을 실행하거나 웹 URL을 열지 않습니다. [메타데이터와 오프셋](evidence/metadata/installer-metadata.json), [JSON 필드 보존본](evidence/metadata/installer-payload.json)을 제출합니다. JSON 보존본은 보기 좋게 다시 직렬화했으므로 해제 직후 원문 바이트 해시와 파일 자체의 해시는 다릅니다.

## 구현 범위

| 기능 | 원본에서 확인한 범위 | PoC 결과 / 근거 |
|---|---|---|
| 메모 생성·수정·삭제 | 메모 앱 본체 미확인 | 구현. UUID 메모, 다중 행 텍스트, 삭제 확인. T1/T8 |
| 자동 저장 | 원본 주기/방식 미확인 | 구현. UI 변화 후 450ms debounce, 실패 시 2초 재시도. T8 |
| 재시작 복원 | 원본 저장 형식/복원 조건 미확인 | 구현. 전용 JSON, 별도 프로세스에서 두 창과 본문 복원. T1/T8 |
| 여러 메모 창 | 원본 본체의 창 수명주기 미확인 | 구현. 메모별 Form, 목록에서 다시 열기, 창 중복 방지. T8 |
| 창 위치·크기·색상 | 원본 동작 미확인 | 구현. 저장소 round-trip T1; 다른 모니터/DPI 환경은 미수행 |
| 설치·업데이트·취소·Store/제품 실행 | 설치 코드/상태 정적 확인 R4–R6 | 재구현하지 않음. 메모 PoC 우선 범위 밖 |
| 설치 태그 서명 검증 | 관련 API 호출 R7 | 재구현하지 않음. 추출은 정적 데이터 확인만 |
| 로그인·클라우드 동기화·이미지·서식·검색·휴지통·트레이 | 제공 입력으로 존재/구현을 확정하지 않음 | 미구현. 원본 확인 기능 목록에 포함하지 않음 |
| 원본 메모 DB 읽기/마이그레이션 | 원본 데이터 미접근 | 미구현. 전용 저장소만 사용 |

PoC의 UX, JSON 스키마, debounce 시간, 백업, 목록 창, 색상은 이번 구현의 설계 선택입니다. 디컴파일 코드나 원본 리소스를 앱에 복사하지 않았습니다.

## 근거 색인

완전한 11개 Evidence는 [rea-bundle.json](evidence/rea-bundle.json)에 있습니다. 아래 E번호는 보고서용 별칭이며 실제 ID를 대신 생성한 REA ID가 아닙니다. [JSON 색인](evidence/index.json)도 제공합니다.

| 별칭 | Evidence ID | 내용 |
|---|---|---|
| E1 | `ev_97d55f8c16a4291b052638d9a1d7a2b7bd5a1d8afc4e4dcbb335c705a88ae645` | 관리 어셈블리 식별·참조·리소스 |
| E2 | `ev_5b1c0a14837e7fc9686c83e72e8733a636c077c8fab978401f3d20e45108e36f` | 전체 managed members; 326 types, 2127 methods, 8200 call edges, 871 user strings |
| E3 | `ev_1c6dce445fbaa84e9f0b32671370b33de04eba9cdc4de1907a25f6b367a5e101` | 5개 관심 타입 상세; 111 methods, 525 call edges |
| E4 | `ev_4557573cb1b92ca14b230526495e8896bbca687f4375121f5d0b952d4e386ecd` | E2의 user_strings 871개 페이지 |
| E5 | `ev_1e523c03a61b7e4630be39bfbb233aaac1d212a872b1b618256d8f67dc663866` | E3의 메서드/CIL anchors 페이지 |
| E6 | `ev_25a97c3eba1448bc1c5a40c14340c0ace33962efdbb196543d4f911f0126eb28` | E3의 fields 페이지 |
| E7 | `ev_2bd01362f6ddd87810af9efe8f3874fb43793f19d2549b0b289ba80b090937c8` | E3의 call_edges 페이지 |
| E8 | `ev_e13f8c72ddc90124e94a4ef3df25b4b5f48d490df73f8656202f90492a67368e` | PE 리소스 트리 |

나머지 3개는 E2/E3 요약 및 타입 페이지입니다. 토큰과 오프셋은 위 SHA-256/MVID 빌드에만 유효합니다. managed coverage complete는 이 파일에서 도구가 지원하는 메타데이터/CIL 읽기 범위의 완전성으로, 앱 동작 전체나 간접 호출 해결의 완전성을 뜻하지 않습니다. 정적 분석은 실행 관찰과 구분했습니다.
