# KING: The Last Piece

> 모든 백색 기물이 쓰러졌다. 보드 위에 남은 것은 킹 하나뿐.

체스 기물들과 싸우는 **라이트 소울라이크 아레나 액션** 게임입니다.
플레이어는 마지막 남은 **백색 킹**이 되어, 체스판 아레나 위에서 웨이브로 등장하는 흑색 기물들을 상대합니다.

- 엔진: Unity 6 (6000.3.23f1), URP, Input System
- 씬 설정 불필요: `SampleScene`(또는 아무 씬)에서 Play를 누르면 `GameBootstrap`이 아레나·플레이어·카메라·HUD를 런타임에 자동 생성합니다.

## 소울라이크 요소 (가볍게)

| 요소 | 구현 |
| --- | --- |
| 스태미나 | 공격·구르기에 소모, 행동 후 잠시 뒤 회복 |
| 구르기 | 무적 프레임(i-frame) 포함, 입력 없으면 백스텝 |
| 약/강 공격 | 약공격 2단 콤보, 강공격은 강인도(poise) 피해 큼 |
| 락온 | 대상 고정 카메라 + 스트레이프 이동 |
| 회복 아이템 | Royal Flask 3개, 사용 중 이동 느려지고 피격 시 낭비 |
| 텔레그래프 | 적 공격 전 붉은 발광 + 바닥 위험 범위 표시 |
| 강인도/경직 | 적의 poise를 깎으면 경직 → 반격 찬스 |
| 사망 | **CHECKMATE** 화면, 현재 웨이브부터 재도전 |
| 웨이브 사이 휴식 | 플라스크 리필 + 체력 일부 회복 (모닥불 대체) |

## 흑색 기물 (적)

체스 이동 규칙을 공격 패턴으로 옮겼습니다.

- **Pawn** – 느리게 접근해 전방 찌르기
- **Knight** – L자 홉으로 거리 조절 후, 킹의 위치로 도약 내려찍기 (원형 범위)
- **Bishop** – 킹의 대각선 위로 미끄러져 정렬한 뒤 보드 끝까지 대각선 돌진
- **Rook** – 같은 랭크/파일로 정렬해 직선 돌진, 근접 시 내려찍기, 공격 중 슈퍼아머
- **The Black Queen (보스)** – 연속 돌진, 도약, 광역 "Check" 내려찍기. 체력 50% 이하에서 폰 소환 + 공격 가속

## 웨이브

1. The Opening — 폰 ×3
2. Knight's Gambit — 나이트 + 폰 ×2
3. The Long Diagonal — 비숍 + 나이트 + 폰 ×2
4. Castle Walls — 룩 + 비숍 + 폰 ×2
5. **The Black Queen** — 보스전

기물은 실제 체스 초기 배치 칸(d7, g8, a8 …)에서 등장하고, 킹은 e1에서 시작합니다.

## 조작

| 동작 | 키보드/마우스 | 게임패드 |
| --- | --- | --- |
| 이동 | WASD | 왼쪽 스틱 |
| 카메라 | 마우스 | 오른쪽 스틱 |
| 약공격 | 좌클릭 / J | R1 |
| 강공격 | 우클릭 / K | R2 |
| 구르기 | Space | B / ○ |
| 락온 | Q / Tab / 휠클릭 | R3 |
| 회복 | R | X / □ |
| 시작·재도전 | Enter | A / Start |
| 일시정지 | Esc | Start |

## 구조

```
Assets/
  Resources/Models/White_Chess_King_v02.fbx   플레이어 킹 모델 (런타임 로드, 높이 자동 정규화)
  Scripts/
    Core/
      GameManager.cs   아레나 생성, 웨이브 진행, 충돌 분리, 히트스톱, 부트스트랩
      CameraRig.cs     3인칭 궤도 카메라 + 락온 + 화면 흔들림
      Hud.cs           체력/스태미나/플라스크, 보스 바, CHECKMATE 등 오버레이 (IMGUI)
      GameInput.cs     Input System 디바이스 래퍼
      PieceFactory.cs  프리미티브로 체스 기물 모델 생성 / 킹 FBX 로드
      MaterialLib.cs   런타임 URP Lit 머티리얼 캐시
      Telegraph.cs     공격 예고 바닥 표시
      Effects.cs       스파크·충격파 이펙트
    Combat/
      Actor.cs             체력 + 피격 플래시/발광 공통
      PlayerController.cs  킹: 이동·공격·구르기·회복·락온
      EnemyBase.cs         적 상태머신 (윈드업→공격→후딜, 경직, 사망)
      Enemies.cs           Pawn / Knight / Bishop / Rook / Queen AI
```
