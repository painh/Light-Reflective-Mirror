# Light Reflective Mirror (LRM) Relay Server

## 프로젝트 구조

- `ServerProject-DONT-IMPORT-INTO-UNITY/` - LRM 릴레이 서버 (C# .NET)
- `LoadBalancerProject-DONT-IMPORT-INTO-UNITY/` - 로드밸런서 서버
- `UnityProject/` - Unity 클라이언트 프로젝트

## 빌드

```bash
cd ServerProject-DONT-IMPORT-INTO-UNITY
dotnet build
dotnet publish -c Release
```

## 주요 파일

- `LRM/Program/Program.cs` - 메인 진입점
- `LRM/Endpoint.cs` - REST API 엔드포인트 (대시보드, stats, logs 등)
- `LRM/LogStore.cs` - 로그 저장소
- `LRM/web/index.html` - 대시보드 UI

## API 엔드포인트

- `GET /` - 대시보드
- `GET /api/stats` - 서버 통계
- `GET /api/servers` - 공개 룸 목록
- `GET /api/logs?count=100&before={id}` - 서버 로그 조회
- `GET /api/health` - 헬스체크

## 배포

배포 방법은 [CLAUDE.local.md](./CLAUDE.local.md) 참조 (gitignore 처리됨)
