# ScrapingLab

AI가 작성한 C# 스크래핑 코드를 실행하고 디버깅하기 위한 .NET 10 콘솔 프로젝트입니다.
Amazon US 상품 페이지의 HTML을 요청하고, AngleSharp로 상품 데이터를 추출하여 JSON으로 저장합니다.

프로젝트 위치: `C:\Users\User\source\repos\ScrapingLab`

## 실행

이 작업 폴더에서 실행합니다. .NET 10 SDK가 필요합니다.

```powershell
dotnet build ScrapingLab.sln --configuration Debug
dotnet run --project ScrapingLab
```

인자 없이 실행하면 로컬 `Samples/sample.html`을 읽습니다.
실제 페이지를 수집할 때는 URL을 인자로 전달합니다. 아마존 광고 추적 파라미터는 제거하고 선택 ASIN을 유지합니다.

```powershell
dotnet run --project ScrapingLab -- "https://www.amazon.com/dp/B0FC2C34GY?th=1&psc=1"
```

실행마다 `artifacts/<ASIN>/<UTC 실행 시각>/`에 결과를 보관합니다. 로컬 샘플이나 일반 URL은 ASIN 대신 `page`를 사용합니다.

| 파일 | 내용 |
| --- | --- |
| `page.html` | 받은 원본 HTML |
| `capture.json` | 실제 요청/최종 URL, HTTP 상태, 수집 시각, 소요 시간, 입력 방식 |
| `product.json` | 상품 데이터, 필드별 추출 근거, 누락·모호성 경고 |
| `metrics.json` | HTTP 요청·HTML 파싱·전체 실행 소요 시간 (프로세스 시작과 빌드 시간 제외) |
| `failure.json` | HTTP 오류 또는 상품 파싱 실패가 발생한 경우의 진단 |

결과 폴더는 Git에서 제외합니다. `--output`으로 결과 상위 폴더를 바꿀 수 있고 Ctrl+C로 작업을 중단할 수 있습니다.

## 저장된 HTML 재파싱

받은 HTML을 반복해서 파싱하면 네트워크 요청 없이 중단점으로 코드를 검토할 수 있습니다.

```powershell
dotnet run --project ScrapingLab -- --url "https://www.amazon.com/dp/B0FC2C34GY" --html "artifacts/B0FC2C34GY/<실행 시각>/page.html"
```

저장된 HTML 재파싱 결과의 HTTP 상태는 `null`입니다. 파일 수정 시각을 수집 시각으로 기록하며, 실제 HTTP 응답 상태는 원래 실행의 `capture.json`에서 확인합니다.

## 추출 항목과 범위

- 요청 ASIN, 선택 ASIN, 부모 ASIN, URL, 브랜드, 기본 상품명과 추가 상품명
- 표시 가격과 통화, 정가가 있는 경우 정가, 평점과 평가 수
- 선택 색상·사이즈, 현재 HTML에 노출된 옵션과 연결 ASIN
- 주요 이미지와 갤러리 URL, 상품 특징, 상세 정보, 설명 텍스트·이미지 URL, 카테고리
- 선택 상품의 배송비 안내 팝업에 표시된 상품가·배송비·예상 수입 비용·합계
- HTML에 있는 판매자, 발송 주체, 배송 목적지와 배송 안내

`evidence`는 주요 필드의 CSS 선택자 또는 내장 데이터 키와 원문을 저장합니다. 추출되지 않은 값은 `null` 또는 빈 목록으로 남기고, 확인할 내용은 `warnings`에 기록합니다.

가격은 상품의 주요 구매 영역에서 읽습니다. Amazon US URL이라도 배송 지역에 따라 KRW 같은 통화가 표시될 수 있어 페이지에 실제 표시된 통화를 저장합니다. `offerCharges`는 선택 상품의 배송비 팝업에서 표시된 금액을 그대로 읽습니다. 표시된 합계와 개별 금액의 반올림 합산값은 다를 수 있으며, 결제 총액은 별도로 계산하지 않습니다.

옵션 목록은 현재 응답에 노출된 옵션입니다. 각 옵션 페이지를 요청하여 모든 조합의 가격과 재고를 확인하는 단계는 포함하지 않습니다. 가격, 평가 수, 배송 정보는 수집 시점과 지역에 따라 바뀔 수 있습니다.

CAPTCHA나 로봇 확인 페이지, 상품명 없는 응답은 실패로 처리하고 원본을 보존합니다. 브라우저에서 JavaScript가 실행된 뒤 생성되는 데이터는 현재 HTTP 방식으로 확인되지 않을 수 있습니다.

## 디버깅

### Visual Studio

1. `ScrapingLab.sln`을 엽니다. .NET 10을 지원하는 Visual Studio가 필요합니다.
2. 디버그 프로필에서 `Amazon B0FC2C34GY`를 선택합니다.
3. `Scraping/AmazonProductParser.cs`의 `Parse` 또는 개별 추출 메서드에 중단점을 걸고 F5를 누릅니다.
4. HTML 재파싱은 디버그 프로필의 명령줄 인수에 `--url URL --html 파일경로`를 입력하여 실행합니다.
5. `Local sample` 프로필은 기본 실행 구조를 확인하는 로컬 샘플입니다.

### VS Code

1. 이 작업 폴더를 열고 Microsoft C# 확장을 설치합니다.
2. 실행 및 디버그에서 `ScrapingLab: 아마존 상품 B0FC2C34GY`를 선택합니다.
3. 중단점을 걸고 F5를 누릅니다.
4. 반복 검토는 `ScrapingLab: 저장된 아마존 HTML 재파싱`을 선택하여 저장된 `page.html` 경로를 입력합니다.

HTTP 오류나 파일 오류의 최초 발생 지점을 보고 싶다면 디버거의 예외 설정에서 해당 예외가 발생할 때 중단하도록 설정합니다.

## 코드 위치

| 파일 | 역할 |
| --- | --- |
| `ScrapingLab/Program.cs` | 수집/재파싱 실행, 원본·JSON 저장, 오류 출력 |
| `ScrapingLab/CommandLineOptions.cs` | 실행 인자 처리 |
| `ScrapingLab/Scraping/Scraper.cs` | HttpClient로 HTML 요청 |
| `ScrapingLab/Scraping/AmazonProductParser.cs` | 상품 HTML 파싱과 필드별 근거 |
| `ScrapingLab/Models/AmazonProduct.cs` | 추출 결과 모델 |
| `ScrapingLab/Samples/sample.html` | 네트워크 없이 실행할 수 있는 샘플 |
| `.vscode/launch.json` | VS Code 디버깅 설정 |
