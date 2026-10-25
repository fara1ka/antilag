# Üçüncü taraf bildirimleri

Antilag, GPL-3.0-or-later lisansıyla dağıtılır (bkz. LICENSE). Aşağıdaki bileşenler kendi lisanslarıyla Antilag.exe'nin ya da kurulum dosyasının içinde gelir.

## Antilag.exe ve Antilag-Setup.exe içinde gelenler

| Bileşen | Sürüm | Lisans | Telif | Adres |
|---|---|---|---|---|
| .NET çalışma zamanı (CoreCLR ve temel sınıf kitaplıkları, Microsoft.NETCore.App.Runtime.win-x64) | 8.0.32 | MIT | (c) .NET Foundation and Contributors; (c) Microsoft Corporation | https://github.com/dotnet/runtime |
| .NET Windows Desktop çalışma zamanı (WPF, PresentationNative, wpfgfx, D3DCompiler_47_cor3, vcruntime140_cor3; Microsoft.WindowsDesktop.App.Runtime.win-x64) | 8.0.32 | MIT | (c) .NET Foundation and Contributors; (c) Microsoft Corporation | https://github.com/dotnet/wpf , https://github.com/dotnet/windowsdesktop |
| System.Management | 8.0.0 | MIT | (c) Microsoft Corporation | https://github.com/dotnet/runtime |
| System.CodeDom | 8.0.0 | MIT | (c) Microsoft Corporation | https://github.com/dotnet/runtime |
| System.ServiceProcess.ServiceController | 8.0.1 | MIT | (c) Microsoft Corporation | https://github.com/dotnet/runtime |
| System.Diagnostics.EventLog | 8.0.1 | MIT | (c) Microsoft Corporation | https://github.com/dotnet/runtime |
| Inno Setup (yalnızca kurulum dosyasının gövdesi; Türkçe çeviri: Kaya Zeren) | 6.7.3 | Inno Setup License | (c) 1997-2026 Jordan Russell; Portions (c) 2000-2026 Martijn Laan | https://jrsoftware.org/ , lisans: https://jrsoftware.org/files/is/license.txt |

MIT lisansı, yukarıdaki Microsoft / .NET bileşenleri için:

> The MIT License (MIT). Copyright (c) .NET Foundation and Contributors. Copyright (c) Microsoft Corporation.
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

.NET çalışma zamanının kendi üçüncü taraf bildirimleri (zlib, Brotli, Unicode verisi, mimalloc, musl vb.): licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt ; WPF'nin bildirimleri: licenses/dotnet-wpf-THIRD-PARTY-NOTICES.txt . Çevrimiçi: https://github.com/dotnet/runtime/blob/release/8.0/THIRD-PARTY-NOTICES.TXT ve https://github.com/dotnet/wpf/blob/release/8.0/THIRD-PARTY-NOTICES.TXT

Inno Setup bildirimi: kurulum dosyası Inno Setup ile üretilmiştir; kurulum gövdesindeki telif bildirimi değiştirilmeden bırakılmıştır.

## Yalnızca geliştirme ve test sırasında kullanılanlar (dağıtılmaz)

| Bileşen | Sürüm | Lisans | Adres |
|---|---|---|---|
| xunit, xunit.core, xunit.assert, xunit.extensibility.core, xunit.extensibility.execution | 2.9.2 | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.analyzers | 1.16.0 | Apache-2.0 | https://github.com/xunit/xunit.analyzers |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| xunit.abstractions | 2.0.3 | Apache-2.0 (xunit deposundaki license.txt) | https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk, Microsoft.TestPlatform.ObjectModel, Microsoft.TestPlatform.TestHost, Microsoft.CodeCoverage | 17.11.1 | MIT (CodeCoverage içinde Mono.Cecil, MIT) | https://github.com/microsoft/vstest |
| Newtonsoft.Json | 13.0.1 | MIT | https://www.newtonsoft.com/json |
| System.Reflection.Metadata | 1.6.0 | MIT | https://github.com/dotnet/corefx |
| System.Security.Cryptography.ProtectedData (yalnızca yayın aracı tools/Yayinla; dağıtılmaz) | 8.0.0 | MIT | https://github.com/dotnet/runtime |
| Microsoft.NET.ILLink.Tasks (yalnızca derleme aracı; kırpma açık değil) | 8.0.32 | MIT | https://github.com/dotnet/runtime |

## Yazı tipleri ve simgeler

Arayüz, Windows ile gelen yazı tiplerini adıyla kullanır (Segoe UI Variable, Segoe UI, Bahnschrift, Cascadia Mono, Consolas). Hiçbiri Antilag'a eklenmez ya da yeniden dağıtılmaz. Uygulama simgesi (src/CS2Hazirla/Assets/app.ico) tools/make-icon.ps1 ile kodla çizilir; arayüz simgeleri src/CS2Hazirla/Theme/Icons.xaml içindeki elle yazılmış çizgi yollarıdır. Üçüncü taraf resim, yazı tipi ya da simge kümesi yoktur.

## Ticari markalar ve sorumluluk reddi

Antilag bağımsız, topluluk tarafından geliştirilen bir projedir; aşağıda adı geçen şirketlerle bağlantılı değildir, onlar tarafından onaylanmamış, desteklenmemiş ya da sponsor edilmemiştir. Valve, Steam, Counter-Strike, CS2 ve VAC Valve Corporation'ın; FACEIT FACEIT Ltd'nin; Riot Games, Vanguard ve VALORANT Riot Games, Inc.'in; Discord Discord Inc.'in; Epic Games ve Easy Anti-Cheat Epic Games, Inc.'in; Microsoft, Windows, Xbox, Teams, OneDrive ve Edge Microsoft şirketler grubunun; Google ve Chrome Google LLC'nin; NVIDIA, AMD, Intel, Logitech, Razer, SteelSeries, Spotify, WhatsApp, Cloudflare ve Claude/Anthropic ile bu belgede ya da programda geçen diğer tüm ürün ve şirket adları ilgili sahiplerinin ticari markalarıdır. Bu adlar yalnızca ilgili programı tanımlamak için (örneğin hangi programın kapatılacağını göstermek için) kullanılır. "Antilag" adının AMD'nin Radeon Anti-Lag özelliğiyle bir ilgisi yoktur. Antilag, CS2'ye, Steam'e, FACEIT'e ya da başka bir oyuna veya hile korumasına kod sokmaz, onlara dokunmaz; yine de herhangi bir hile korumasının Antilag'ı kabul edeceğine dair garanti verilmez. Yazılım GPL-3.0 uyarınca "olduğu gibi" sunulur; kullanım kullanıcının sorumluluğundadır.
