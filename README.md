# ScrapingLab

AI가 작성한 C# 스크래핑 코드를 실행하고 디버깅하기 위한 .NET 10 콘솔 프로젝트입니다.
현재는 HTML 요청과 파일 저장의 기본 골격을 제공합니다. 대상 사이트가 정해지면 데이터 추출, 페이지 순회, 인증 등의 코드를 추가합니다.

프로젝트 위치: `C:\Users\User\source\repos\ScrapingLab`

## 실행

이 작업 폴더에서 실행합니다. .NET 10 SDK가 필요합니다.

```powershell
dotnet build ScrapingLab.sln --configuration Debug
dotnet run --project ScrapingLab
```

인자 없이 실행하면 로컬 `Samples/sample.html`을 읽습니다.
실제 페이지를 수집할 때는 URL을 인자로 전달합니다.

```powershell
dotnet run --project ScrapingLab -- "https://example.com"
```

HTML은 실행 폴더의 `artifacts/page.html`에 저장되며, 다음 실행 시 덮어씁니다.
콘솔에는 요청 URL, HTTP 상태, HTML 길이, 저장 경로, 소요 시간이 표시됩니다.
Ctrl+C로 작업을 중단할 수 있습니다.

## 디버깅

### Visual Studio

1. `ScrapingLab.sln`을 엽니다. .NET 10을 지원하는 Visual Studio가 필요합니다.
2. `Program.cs`의 HTML 저장 부분이나 `Scraping/Scraper.cs`에 중단점을 겁니다.
3. F5로 로컬 샘플을 실행합니다.
4. 실제 URL을 사용하려면 프로젝트 디버그 프로필의 명령줄 인수에 URL을 입력합니다.

### VS Code

1. 이 작업 폴더를 열고 Microsoft C# 확장을 설치합니다.
2. 실행 및 디버그에서 `ScrapingLab: 로컬 샘플` 또는 `ScrapingLab: URL 수집`을 선택합니다.
3. 중단점을 걸고 F5를 누릅니다. URL 수집 설정은 실행할 때 URL을 입력받습니다.

HTTP 오류나 파일 오류의 최초 발생 지점을 보고 싶다면 디버거의 예외 설정에서 해당 예외가 발생할 때 중단하도록 설정합니다.

## 코드 위치

| 파일 | 역할 |
| --- | --- |
| `ScrapingLab/Program.cs` | 실행 인자, 로컬 샘플, HTML 저장, 오류 출력 |
| `ScrapingLab/Scraping/Scraper.cs` | HttpClient로 HTML 요청 |
| `ScrapingLab/Samples/sample.html` | 네트워크 없이 실행할 수 있는 샘플 |
| `.vscode/launch.json` | VS Code 디버깅 설정 |

`HttpClient`는 서버가 반환한 HTML을 가져옵니다. 브라우저에서 JavaScript가 실행된 뒤 생성되는 데이터는 대상 사이트에 맞춰 API 호출 또는 브라우저 자동화 코드를 추가해야 합니다.
