<#
  Antilag: tek komutla yayın.   .\build.ps1          testler + yayın + kurulum dosyası
                                .\build.ps1 -Sessiz  aynısı, ama düşük öncelikle (derleme sırasında bilgisayarı yormaz)
                                .\build.ps1 -Testsiz yalnızca yayın + kurulum
                                .\build.ps1 -Canli   canlı testler de çalışır (gerçek Windows'a dokunur; CS2 KAPALI olmalı)
                                .\build.ps1 -Yayinla "Bu sürümde neler değişti"   kurulum dosyasını imzalar ve GitHub'da herkese açık sürüm olarak yayınlar;
                                                      kurulu her Antilag bir sonraki açılışta "Yeni sürüm var" bildirimini gösterir.
                                                      Önce commit ve push yapılmış olmalı (yayın tam o kaynaktan üretilir).
  Sürüm numarası tek yerden gelir: src\CS2Hazirla\CS2Hazirla.csproj içindeki <Version>.
  Çıktılar: publish\Antilag.exe  ve  dist\Antilag-Setup.exe  (ikisi de git'e girmez).
#>
param(
    [switch]$Testsiz,
    [switch]$Sessiz,
    [switch]$Canli,
    [string]$Yayinla = ''
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

if ($Sessiz) { try { [System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'Idle' } catch { } }

$csproj = Join-Path $root 'src\CS2Hazirla\CS2Hazirla.csproj'
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Sürüm numarası bulunamadı: $csproj" }
Write-Host "Antilag $version" -ForegroundColor Cyan

$repo = 'fara1ka/antilag'
$tag = "v$version"

function Step($ad, [scriptblock]$is) {
    Write-Host "`n== $ad" -ForegroundColor Cyan
    & $is
    if ($LASTEXITCODE -ne 0) { throw "$ad başarısız oldu (çıkış kodu $LASTEXITCODE)" }
}

# ---- yayın öncesi denetimler: uzun işler başlamadan, hiçbir şey yayınlanmadan önce
if ($Yayinla -ne '') {
    if ($Testsiz -or $Canli) { throw '-Yayinla, testleri atlayan (-Testsiz) ya da canlı testli (-Canli) bir derlemeyle birlikte kullanılamaz.' }
    Step 'Yayın öncesi denetim' {
        gh auth status 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'gh (GitHub CLI) giriş yapmamış: önce "gh auth login".' }
        $vis = gh repo view $repo --json visibility --jq .visibility
        if ($vis -ne 'PUBLIC') { throw "Depo herkese açık değil ($vis): güncelleme kartları yayını göremez. Önce depoyu herkese açık yap." }
        if (git status --porcelain) { throw 'Kaydedilmemiş değişiklik var: önce commit et.' }
        git fetch origin main --quiet
        if ($LASTEXITCODE -ne 0) { throw 'git fetch başarısız.' }
        if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw "Son commit GitHub'a gönderilmemiş: önce push et." }
        if (git ls-remote --tags origin $tag) { throw "$tag etiketi zaten yayında. Sürüm numarasını artır." }
        if (git rev-parse -q --verify "refs/tags/$tag") {
            if ((git rev-parse "$tag^{commit}") -ne (git rev-parse HEAD)) { throw "Yerel $tag etiketi başka bir commit'i gösteriyor: silip yeniden oluştur (git tag -d $tag)." }
        }
        $global:LASTEXITCODE = 0
    }
}

if (-not $Testsiz) {
    if ($Canli) {
        # korumalı işlemlere tutamak açılmadığını gerçek makinede doğrulayan test de burada; CS2 açıksa kendiliğinden başarısız olur (tasarım gereği)
        Step 'Testler (canlı testler dahil)' { dotnet test tests\CS2Hazirla.Tests --nologo }
    }
    else {
        # canlı testler (gerçek Windows'a dokunanlar) çıkarılır: CS2 açıkken zaten çalışmazlar
        Step 'Testler' { dotnet test tests\CS2Hazirla.Tests --nologo --filter "FullyQualifiedName!~Live" }
    }
}

Step 'Yayın (tek .exe)' {
    dotnet publish src\CS2Hazirla\CS2Hazirla.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish --nologo
}
$exe = Join-Path $root 'publish\Antilag.exe'
if (-not (Test-Path $exe)) { throw "publish\Antilag.exe oluşmadı" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 bulunamadı (winget install JRSoftware.InnoSetup)" }

Step 'Kurulum dosyası' {
    # ISCC 260 karakterden uzun yolları açamaz: klasör çok derindeyse kısa bir geçici klasörde derle
    if ($root.Length -gt 120) {
        $tmp = Join-Path $env:TEMP 'antilag-iscc'
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force "$tmp\installer", "$tmp\publish", "$tmp\dist", "$tmp\src\CS2Hazirla\Assets" | Out-Null
        Copy-Item installer\Antilag.iss "$tmp\installer\"
        Copy-Item src\CS2Hazirla\Assets\app.ico "$tmp\src\CS2Hazirla\Assets\"
        Copy-Item $exe "$tmp\publish\"
        Copy-Item LICENSE, THIRD-PARTY-NOTICES.md "$tmp\"
        Copy-Item licenses "$tmp\licenses" -Recurse
        & $iscc "/DAppVersion=$version" "$tmp\installer\Antilag.iss"
        if ($LASTEXITCODE -eq 0) {
            New-Item -ItemType Directory -Force dist | Out-Null
            Copy-Item "$tmp\dist\*" dist\ -Force
        }
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
    else {
        & $iscc "/DAppVersion=$version" installer\Antilag.iss
    }
}

Write-Host "`nHazır:" -ForegroundColor Green
Get-ChildItem dist -Filter "Antilag-Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
    ForEach-Object { "{0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }

# ---- yayın: yalnızca -Yayinla ile. İmzalar, GitHub'da TASLAK sürüm açar, dosyaları denetler, etiketler, yayınlar ve herkese açık adresten gerçekten indirip doğrular.
if ($Yayinla -ne '') {
    $setup = Join-Path $root 'dist\Antilag-Setup.exe'
    $manifest = Join-Path $root 'dist\latest.json'
    $notesFile = Join-Path $root 'dist\notlar.txt'

    # kurulu kopyaların güvendiği anahtarlar son yayınlanan sürümün kaynağındadır; imza anahtarı onlardan biri olmalı ve hiçbiri listeden düşmemeli
    $previous = git tag --list 'v*' --sort=-v:refname | Where-Object { $_ -ne $tag } | Select-Object -First 1
    $onceki = if ($previous) { @('--onceki', $previous) } else { @() }

    Step 'İmza' {
        # Setup dosyasının kendi sürümü csproj'daki sürümle aynı olmalı; aksi halde araç imzalamaz
        dotnet run --project tools\Yayinla -c Release -- imzala $setup --beklenen $version --cikis $manifest @onceki
    }
    Step 'İmza doğrulaması' {
        dotnet run --project tools\Yayinla -c Release -- dogrula $manifest $setup
    }
    Step 'GitHub sürümü (taslak)' {
        # önce taslak: iki dosya da eklenip denetlenmeden kimsenin "en son sürümü" olmaz. Notlar bir dosyadan okunur (PowerShell 5.1 tırnak işaretlerini bozar).
        $hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        $notes = "$Yayinla`n`nKaynak kod: https://github.com/$repo/tree/$tag`nSHA-256: $hash"
        [IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding $false))
        gh release create $tag $setup $manifest --repo $repo --target (git rev-parse HEAD) --draft --title "Antilag $version" --notes-file $notesFile
    }
    Step 'Taslağı denetle' {
        $assets = gh release view $tag --repo $repo --json assets | ConvertFrom-Json
        $size = @{}
        foreach ($a in $assets.assets) { $size[$a.name] = [long]$a.size }
        if (-not $size.ContainsKey('Antilag-Setup.exe') -or -not $size.ContainsKey('latest.json')) { throw "Taslakta Antilag-Setup.exe ve latest.json birlikte yok: $($size.Keys -join ', ')" }
        if ($size['Antilag-Setup.exe'] -ne (Get-Item $setup).Length) { throw 'Taslaktaki Antilag-Setup.exe dosyasının boyutu yerel dosyayla aynı değil.' }
        if ($size['latest.json'] -ne (Get-Item $manifest).Length) { throw 'Taslaktaki latest.json dosyasının boyutu yerel dosyayla aynı değil.' }
        $global:LASTEXITCODE = 0
    }
    Step 'Etiket' {
        if (-not (git rev-parse -q --verify "refs/tags/$tag")) { git tag -a $tag -m "Antilag $version (Antilag-Setup.exe bu kaynaktan üretildi)" }
        git push origin $tag
    }
    Step 'Yayınla' {
        gh release edit $tag --repo $repo --draft=false --latest
    }
    Step 'Yayın kontrolü' {
        # uygulamanın kendisi gibi, herkese açık adresten en yeni sürüme bakar ve kurulum dosyasını gerçekten indirip imzalıyla karşılaştırır;
        # GitHub'ın "en son" adresi yayından sonra birkaç saniye geç kalabilir: bir dakikaya kadar yeniden dener
        $ok = $false
        for ($try = 1; $try -le 12 -and -not $ok; $try++) {
            $out = dotnet run --project tools\Yayinla -c Release -- kontrol --surum 0.0.1 --indir
            $ok = ($LASTEXITCODE -eq 0) -and (($out -join ' ') -match [regex]::Escape($version))
            if (-not $ok) { Start-Sleep -Seconds 5 }
        }
        $out
        if (-not $ok) { throw "Sürüm yayınlandı ama herkese açık adresten henüz doğrulanamadı: birkaç dakika sonra elle dene: tools\Yayinla kontrol --surum 0.0.1 --indir" }
        $global:LASTEXITCODE = 0
    }
    Write-Host "`nYayınlandı: https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
}
