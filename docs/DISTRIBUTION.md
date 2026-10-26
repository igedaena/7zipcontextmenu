# 저장소와 배포 파일 구성

소스는 Git으로 관리하고 실행 배포물은 GitHub Releases에 첨부합니다.

| 대상 | 포함 항목 | 용도 |
| --- | --- | --- |
| Git 저장소 | `src/`, `tools/`, `scripts/`, `portable/`, `installer/` | 셸 DLL·실행기·설정 소스, 검사 도구, 빌드·검증·등록 스크립트와 배포 안내 |
| Git 저장소 | `CMakeLists.txt`, `.vsconfig`, `.gitignore`, `.gitattributes` | 빌드 구성과 파일 관리 규칙 |
| Git 저장소 | `assets/sevenzip-extension.ico`, `assets/sevenzip-extension.png`, `assets/sevenzip-extension-source.png`, `assets/README.md` | 프로그램 아이콘과 재생성 원본 |
| Git 저장소 | `README.md`, `THIRD_PARTY_NOTICES.md`, `docs/BUILD.md`, `docs/RELEASES.md`, `docs/DISTRIBUTION.md` | 사용자·개발·배포 안내 |
| v0.4.1 릴리스 | `SevenZipExtension-Setup-0.4.1.exe` | 공용 설치형 |
| v0.4.1 릴리스 | `SevenZipExtension-Portable-0.4.1.zip` | 현재 사용자 등록형 포터블 |
| v0.4.1 릴리스 | `SHA256SUMS.txt` | 위 두 파일의 무결성 확인 |

## 업로드하지 않는 항목

- `.tools/`, `.review/`: 도구 설치본·다운로드·검사 결과·로그·로컬 상태 기록.
- `build/`, `out/`, `dist/`: 빌드 중간 파일과 로컬 패키징 결과. `dist`의 최종 배포물만 Releases에 별도 첨부합니다.
- `archive/`: 과거 개발 자료·이전 배포물·캐시의 보관 압축.
- `HANDOFF.md`, `docs/HANDOFF*`, `docs/chat-exports/`: 인계 문서·대화 및 첨부 이미지.
- 기존 `docs`의 작업계획·설계·보안 조사·개발환경·내부 검토 기록. 사용자에게 필요한 내용은 위 배포용 문서에 정리합니다.
- `assets/reference/`, `assets/icon-prompt.txt`, `assets/executable-icons-preview.png`: 외부 참고 이미지·생성 프롬프트·작업용 미리 보기.
- 루트 `메뉴 설치.cmd`, `메뉴 제거.cmd`: 로컬 보조 진입점. 배포 사용자는 설치 EXE 또는 포터블 ZIP을 사용합니다.
- `scripts/Launch-Setup.ps1`: 위 로컬 CMD 전용 실행 보조 파일. 로컬에서는 보존하고 Git 추적에서 제외합니다.
- 인증 자료, 개인 환경 설정, 에디터 캐시와 임시 파일.

루트는 `.gitignore`의 허용 목록으로 관리합니다. 내부 자료는 로컬에 보존하며 Git에서 제외합니다. 신규 디렉터리를 공유하려면 규칙과 실제 스테이징 목록을 함께 검토하세요.

프로젝트 라이선스는 소유자 결정 전까지 추가하지 않습니다. 저장소 공개 전 라이선스와 아이콘 권리 검토가 남아 있습니다.
