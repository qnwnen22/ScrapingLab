# ScrapingLab

.NET 10 C# 상품 수집 실험 프로젝트입니다. URL을 입력하면 사이트 수집기가 데이터를 공통 `Product` 모델로 변환하고 JSON을 콘솔에 출력합니다.

## 실행

프로젝트 폴더 `C:\Users\User\source\repos\ScrapingLab`에서 실행합니다.

```powershell
dotnet run --project ScrapingLab -- "https://www.amazon.com/dp/B0FC2C34GY?th=1&psc=1"
```

인자 없이 실행하면 콘솔에서 URL 한 줄을 입력받습니다. 현재 Amazon US 상품 URL을 지원합니다.
표준 출력에는 `Product` JSON만 출력하며 오류 메시지는 표준 오류로 출력합니다.

## 코드 구조

```text
ScrapingLab/
  Program.cs                         URL 입력 → 수집기 호출 → JSON 출력
  Models/                            공통 Product 관련 클래스
    Product.cs, Price.cs
    Option.cs, Combination.cs
    OptionValue.cs, Independency.cs
  Collects/
    ICollect.cs                      사이트별 수집 계약
    CollectFactory.cs                URL에 맞는 수집기 선택
    Amazon/
      AmazonCollect.cs               Amazon 수집 진입점
      AmazonPageClient.cs            HTML 요청
      AmazonProductParser.cs         Amazon 원문 추출
      AmazonProductMapper.cs         Product 모델 변환
      AmazonVariantPriceCollector.cs 옵션별 가격 확인
      AmazonUrl.cs                   URL 및 ASIN 처리
      Models/                       Amazon 전용 원문·매핑·응답 모델
```

`Models`의 속성 이름과 구조는 사용자가 정의한 `Product` 모델을 그대로 사용합니다.
새 사이트는 `Collects/<사이트>/`에서 `ICollect`를 구현하고, 해당 사이트 전용 모델을 그 안의 `Models/`에 둡니다. URL 선택 규칙은 `CollectFactory`에 추가합니다.

## 수집기 직접 사용

```csharp
using ScrapingLab.Collects;
using ScrapingLab.Collects.Amazon;
using ScrapingLab.Models;

var url = "https://www.amazon.com/dp/B0FC2C34GY?th=1&psc=1";
ICollect collect = CollectFactory.Create(url);
Product product = collect.GetProduct(url);

// 현재 페이지의 선택 가격만 수집합니다.
Product selectedOnly = new AmazonCollect(collectVariantPrices: false).GetProduct(url);
```

계약은 `Product GetProduct(string url, string? html = null)`입니다.
Amazon 기본 수집은 원문에서 확인한 실제 자식 ASIN 조합마다 순서대로 요청해 `Option.Independencies`의 가격을 채웁니다. 각 요청 사이에는 1초 간격이 있습니다.
요청 ASIN과 응답의 선택 ASIN이 일치할 때만 가격을 대입하며, 확인하지 못한 조합의 가격은 `null`로 남습니다. 차단 또는 요청 제한이 발생하면 추가 옵션 요청을 중단합니다.

## 저장된 HTML 파싱

```csharp
var html = File.ReadAllText(@"C:\saved\page.html");
ICollect collect = new AmazonCollect();
Product product = collect.GetProduct(url, html);
```

HTML을 전달하면 네트워크 요청 없이 해당 HTML만 파싱합니다. 이 경우 기본 설정과 관계없이 옵션별 추가 요청을 하지 않습니다.
CLI는 URL 입력만 받으며 결과 파일을 자동 저장하지 않습니다. 저장이 필요하면 `Product`를 직렬화하여 파일에 기록하면 됩니다.

## 디버깅

Visual Studio에서는 `ScrapingLab.sln`을 열고 `Amazon 상품 수집` 또는 `URL 입력` 프로필로 실행합니다.
VS Code에서는 `ScrapingLab: 아마존 상품 수집` 또는 `ScrapingLab: URL 입력` 구성을 선택합니다.

중단점 위치:

- `AmazonCollect.GetProduct`: 사이트 수집 시작 및 반환 값
- `AmazonPageClient.FetchPageAsync`: HTTP 요청과 원문 응답
- `AmazonProductParser.Parse`: 상품 데이터 추출
- `AmazonProductMapper.Map`: 공통 `Product`와 실제 옵션 조합 매핑
- `AmazonVariantPriceCollector.CollectAsync`: 자식 ASIN별 가격 확인 및 대입

가격의 통화는 실제 응답에 표시된 값을 사용합니다. 선택 상품 갤러리는 `ItemImages`, 실제 설명 문장은 `Description`, 상품 설명/A+ 모듈 HTML은 `DetailHtml`에 매핑합니다.
