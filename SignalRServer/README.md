# SignalRServer - Hyunmu.Service 프로토콜 매핑

## 🔗 연결 정보

- **Hub 경로**: `/chathub`
- **기본 포트**: 5000
- **프로토콜**: HTTP

## 📡 지원하는 메서드들

### 1. 기본 채팅 메서드

#### `SendMessage(string user, string message)`
- **설명**: 모든 클라이언트에게 메시지 전송
- **매개변수**: 
  - `user`: 메시지 발신자
  - `message`: 전송할 메시지
- **반환값**: `Task`
- **사용처**: Hyunmu.Service에서 일반 메시지 전송

#### `JoinGroup(string groupName)`
- **설명**: 특정 그룹에 참가
- **매개변수**: `groupName`: 참가할 그룹명
- **반환값**: `Task`
- **사용처**: Hyunmu.Service에서 그룹 기반 통신

#### `SendToGroup(string groupName, string user, string message)`
- **설명**: 특정 그룹에만 메시지 전송
- **매개변수**: 
  - `groupName`: 대상 그룹명
  - `user`: 메시지 발신자
  - `message`: 전송할 메시지
- **반환값**: `Task`
- **사용처**: Hyunmu.Service에서 그룹별 메시지 전송

#### `LeaveGroup(string groupName)`
- **설명**: 특정 그룹에서 나가기
- **매개변수**: `groupName`: 나갈 그룹명
- **반환값**: `Task`
- **사용처**: Hyunmu.Service에서 그룹 탈퇴

### 2. Hyunmu.Service 전용 메서드

#### `Heartbeat()`
- **설명**: 클라이언트 연결 상태 확인 (하트비트)
- **매개변수**: 없음
- **반환값**: `Task<object>`
- **응답 형식**:
  ```json
  {
    "Status": "OK",
    "Timestamp": "2024-01-01T00:00:00Z",
    "ConnectionId": "connection-id",
    "Message": "하트비트 응답"
  }
  ```
- **사용처**: Hyunmu.Service의 연결 상태 모니터링

#### `HealthCheck()`
- **설명**: 서버 전체 상태 확인
- **매개변수**: 없음
- **반환값**: `Task<object>`
- **응답 형식**:
  ```json
  {
    "Status": "Healthy",
    "Timestamp": "2024-01-01T00:00:00Z",
    "ConnectedClients": 5,
    "ServerUptime": "00:30:00",
    "Message": "서버 정상 동작 중"
  }
  ```
- **사용처**: Hyunmu.Service의 서버 상태 모니터링

#### `GetConnectionStats()`
- **설명**: 현재 연결 통계 정보 반환
- **매개변수**: 없음
- **반환값**: `Task<object>`
- **응답 형식**:
  ```json
  {
    "ConnectionId": "connection-id",
    "ConnectedClients": 5,
    "ServerStartTime": "2024-01-01T00:00:00Z",
    "CurrentTime": "2024-01-01T00:30:00Z",
    "Uptime": "00:30:00"
  }
  ```
- **사용처**: Hyunmu.Service의 연결 통계 수집

#### `BroadcastBulkMessages(object[] messages)`
- **설명**: 대량의 메시지를 모든 클라이언트에게 전송
- **매개변수**: `messages`: 전송할 메시지 배열
- **반환값**: `Task<bool>`
- **응답**: 성공 시 `true`, 실패 시 `false`
- **사용처**: Hyunmu.Service에서 대량 데이터 전송

## 📨 이벤트 (클라이언트 수신)

### `ReceiveMessage(string sender, string message)`
- **설명**: 서버에서 전송하는 모든 메시지 수신
- **매개변수**:
  - `sender`: 메시지 발신자 (시스템, 서버, 사용자 등)
  - `message`: 메시지 내용
- **사용처**: Hyunmu.Service에서 서버 메시지 수신

## 🔄 연결 생명주기

### 1. 연결 시 (`OnConnectedAsync`)
- 클라이언트 ID를 연결된 클라이언트 목록에 추가
- 모든 클라이언트에게 새 연결 알림 전송
- UI에 연결된 클라이언트 수 업데이트

### 2. 연결 해제 시 (`OnDisconnectedAsync`)
- 클라이언트 ID를 연결된 클라이언트 목록에서 제거
- 모든 클라이언트에게 연결 해제 알림 전송
- UI에 연결된 클라이언트 수 업데이트

## 🚀 사용 예시

### Hyunmu.Service에서 서버 연결
```csharp
// 연결
await signalRManager.ConnectAsync("http://localhost:5000/chathub");

// 하트비트 전송
var heartbeat = await signalRManager.SendHeartbeatAsync();

// 헬스체크
var health = await signalRManager.InvokeAsync<object>("HealthCheck");

// 메시지 전송
await signalRManager.InvokeAsync("SendMessage", "Hyunmu", "안녕하세요!");

// 그룹 참가
await signalRManager.InvokeAsync("JoinGroup", "monitoring");

// 그룹 메시지 전송
await signalRManager.InvokeAsync("SendToGroup", "monitoring", "Hyunmu", "그룹 메시지");
```

## ⚠️ 주의사항

1. **메서드명 일치**: 클라이언트에서 호출하는 메서드명이 서버의 메서드명과 정확히 일치해야 함
2. **매개변수 순서**: 메서드 호출 시 매개변수 순서가 서버 정의와 일치해야 함
3. **반환값 타입**: `InvokeAsync<T>()` 사용 시 올바른 타입을 지정해야 함
4. **연결 상태**: 메서드 호출 전 연결 상태 확인 필요

## 🔧 문제 해결

### "Method does not exist" 오류
- 서버에 해당 메서드가 정의되어 있는지 확인
- 메서드명 철자 확인
- 매개변수 개수와 타입 확인

### 연결 실패
- 서버가 실행 중인지 확인
- 포트 번호 확인
- 방화벽 설정 확인

### 메시지 수신 안됨
- `ReceiveMessage` 이벤트 핸들러 등록 확인
- 서버에서 올바른 이벤트명으로 전송하는지 확인
