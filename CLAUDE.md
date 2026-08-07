# CLAUDE.md

이 파일은 이 저장소에서 작업할 때 Claude Code(claude.ai/code)에게 제공되는 가이드입니다.

## 프로젝트 개요

이 프로젝트는 Universal Render Pipeline(2D 템플릿: `Assets/Settings/Renderer2D.asset`, `Lit2DSceneTemplate.scenetemplate`)을 사용하는 Unity **6000.3.21f1** 프로젝트입니다. 현재는 갓 생성된 프로젝트 뼈대 상태로, `Assets/`에는 기본 템플릿(`Scenes/SampleScene.unity`, `InputSystem_Actions.inputactions`, URP 설정)만 있고 **아직 게임플레이 코드는 없습니다** (`Assets/Scripts` 폴더 없음). `Assets/_Recovery/0.unity`는 Unity 자동 저장/충돌 복구용 씬이며 게임의 일부가 아닙니다.

## MCP Unity 통합

이 프로젝트에는 [mcp-unity](https://github.com/CoderGamester/mcp-unity)가 `Packages/mcp-unity-main`에 임베드되어 있습니다 (git/레지스트리 의존성이 아니라 로컬 임베드 패키지이며, `package.json`을 통해 Unity가 자동으로 인식합니다. `Packages/manifest.json`에 git 의존성으로 다시 추가하지 마세요 — 중복 패키지 충돌이 발생합니다). 이를 통해 Claude Code가 Unity 에디터를 직접 제어할 수 있는 도구를 갖게 됩니다: 씬/게임오브젝트/컴포넌트/머티리얼 관리, 테스트 실행, 메뉴 항목 실행, 플레이 모드 제어 등.

- 프로젝트 루트의 `.mcp.json`이 Claude Code용 서버를 등록합니다: `node Packages/mcp-unity-main/Server~/build/index.js`. 만약 mcp-unity가 임베드된 사본 대신 패키지 매니저를 통해 업데이트되면 이 경로가 `Library/PackageCache/com.gamelovers.mcp-unity@<hash>/...`로 바뀝니다 — 패키지 업데이트 후에는 반드시 다시 확인하세요.
- Unity 쪽 WebSocket 서버는 에디터와 함께 자동으로 시작됩니다 (`ProjectSettings/McpUnitySettings.json`의 `AutoStartServer: true`), 포트는 `8090`입니다. **MCP 도구가 동작하려면 Unity 에디터가 실행 중이어야 합니다** — `Tools > MCP Unity > Server Window`에서 상태를 확인하세요.
- 플레이 모드에서 `run_tests`가 연결 오류로 실패하면 `Edit > Project Settings > Editor > Enter Play Mode Settings`에서 **Reload Domain**을 비활성화하세요 (도메인 리로드가 WebSocket 브리지 연결을 끊습니다).
- 프로젝트 경로는 `C:\Users\1094s\My project`이며 공백을 포함합니다. mcp-unity는 이를 지원하지만, 연결 문제가 발생할 경우 가장 먼저 확인해볼 사항으로 언급되어 있습니다.
- MCP 브리지 자체(게임이 아니라)를 수정할 때는 mcp-unity 패키지 내부 구조에 대한 자세한 내용은 `Packages/mcp-unity-main/CLAUDE.md`를 참고하세요.

## 개발 명령어

아직 별도의 빌드/CI 파이프라인은 없습니다. 표준 Unity 워크플로우를 따릅니다:

- **프로젝트 열기**: Unity Hub 또는 직접 `"C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe" -projectPath "C:\Users\1094s\My project"`
- **테스트**: `Window > General > Test Runner` (EditMode 탭) — 아직 게임 테스트는 없으며, `com.unity.test-framework`만 설치되어 있습니다.
- MCP 도구 사용 시(에디터가 실행 중일 때): `recompile_scripts`, `run_tests`, `execute_menu_item` 등 — Claude Code로 프로젝트를 다룰 때는 수동으로 에디터를 클릭하는 대신 이 도구들을 우선 사용하세요.

## 향후 작업 참고사항

기존 스크립트 구조가 없으므로 `Assets/Scripts/` 같은 폴더 컨벤션이 이미 존재한다고 가정하지 마세요 — 게임플레이 코드를 추가할 때 표준 Unity C# 컨벤션에 따라 필요한 폴더를 새로 만드세요.
