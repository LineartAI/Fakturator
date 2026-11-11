# Zkouska Fakturator.exe: prevod dat, start, druhe spusteni, ukonceni, hlidani serveru, restart po aktualizaci.
# Bezi ve vlastnich slozkach a pod vlastnim jmenem instance, bez dialogu a prohlizece - nesaha na skutecna data. ASCII.
#   powershell -ExecutionPolicy Bypass -File nastroje\win\zkouska.ps1 -Exe <cesta k Fakturator.exe> -T <docasna slozka> [-Port 3320]
param([Parameter(Mandatory = $true)][string]$Exe, [Parameter(Mandatory = $true)][string]$T, [int]$Port = 3320)
$ErrorActionPreference = 'Stop'
$env:FAKTURATOR_INSTANCE = '.test'; Start-Process $Exe -ArgumentList '--ukoncit' -Wait   # uklid po predchozim nedokoncenem behu
Start-Sleep 1
if (Test-Path $T) { Remove-Item $T -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$T\stara\data\faktury", "$T\stara\pdf-faktury", "$T\spatna" | Out-Null
'{"supplier":{"name":"Test"}}' | Set-Content "$T\stara\data\nastaveni.json" -Encoding ASCII
'pdf' | Set-Content "$T\stara\pdf-faktury\Faktura 1.pdf" -Encoding ASCII
$env:FAKTURATOR_PROG = "$T\prog\program"; $env:FAKTURATOR_DATA = "$T\dok"; $env:FAKTURATOR_INSTANCE = '.test'; $env:FAKTURATOR_TICHO = '1'; $env:PORT = "$Port"
$chyby = 0
function Ok([string]$co, [bool]$vysledek) { if ($vysledek) { "OK    $co" } else { "CHYBA $co"; $script:chyby++ } }
function Bezi { try { (Invoke-WebRequest "http://localhost:$Port/api/verze" -UseBasicParsing -TimeoutSec 3).Content } catch { '' } }
function Uzly { @(Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.CommandLine -like "*$T*" }).Count }
function Cekej([scriptblock]$podminka, [int]$s = 20) { for ($i = 0; $i -lt $s * 4; $i++) { if (& $podminka) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function Kod([string[]]$a) { if ($a -and $a.Count) { $p = Start-Process $Exe -ArgumentList $a -PassThru -Wait } else { $p = Start-Process $Exe -PassThru -Wait }; return $p.ExitCode }

Ok 'prevod ze spatne slozky selze' ((Kod @('--prevest', "`"$T\spatna`"")) -eq 1)
Ok 'prevod ze stare slozky' ((Kod @('--prevest', "`"$T\stara`"")) -eq 0)
Ok 'prevedena data i PDF' ((Test-Path "$T\dok\data\nastaveni.json") -and (Test-Path "$T\dok\pdf-faktury\Faktura 1.pdf"))
Ok 'stara slozka zustala' (Test-Path "$T\stara\data\nastaveni.json")

$a = Start-Process $Exe -PassThru
Ok 'server nabehl (adresa.txt)' (Cekej { Test-Path "$T\prog\adresa.txt" })
$odp = Bezi; Ok "odpovida: $odp" ($odp -match '"verze"')
Ok 'program zkopirovan do pracovni slozky' (Test-Path "$T\prog\program\server.js")
Ok 'data ve slozce Dokumenty (log)' ((Get-Content "$T\prog\fakturator.log" -Raw -Encoding UTF8) -like "*$T\dok\data*")
Ok 'nastaveni ze stare slozky se nacetlo' ((Invoke-WebRequest "http://localhost:$Port/api/bootstrap" -UseBasicParsing).Content -match '"name":"Test"')
Ok 'druhe spusteni jen skonci' ((Kod @()) -eq 0 -and (Uzly) -eq 1 -and -not $a.HasExited)
Ok '--ukoncit vrati 0' ((Kod @('--ukoncit')) -eq 0)
Ok 'aplikace i server skoncily' ((Cekej { $a.HasExited -and (Uzly) -eq 0 } 10) -and (Bezi) -eq '')
Ok '--ukoncit bez bezici aplikace vrati 0' ((Kod @('--ukoncit')) -eq 0)

$a = Start-Process $Exe -PassThru
Ok 'druhy start' (Cekej { (Bezi) -ne '' })
Stop-Process -Id $a.Id -Force
Ok 'po nasilnem ukonceni aplikace skonci i server' (Cekej { (Uzly) -eq 0 -and (Bezi) -eq '' } 10)

(Get-Content "$T\prog\program\verze.json" -Raw) -replace '"verze"\s*:\s*"[0-9.]+"', '"verze": "0.0.1"' | Set-Content "$T\prog\program\verze.json" -Encoding ASCII
$a = Start-Process $Exe -PassThru
Ok 'start po zastarale kopii' (Cekej { (Bezi) -ne '' })
Ok 'novejsi aplikace prepsala starsi kopii programu' ((Get-Content "$T\prog\program\verze.json" -Raw) -notmatch '0\.0\.1')
[void](Kod @('--ukoncit')); [void](Cekej { $a.HasExited } 10)

# restart po aktualizaci: server skonci s kodem 75 a aplikace ho spusti znovu
$puvodni = "$T\prog\program\server-puvodni.js"; Move-Item "$T\prog\program\server.js" $puvodni
"const fs=require('fs'),p=require('path'),m=p.join(__dirname,'znacka');if(!fs.existsSync(m)){fs.writeFileSync(m,'1');process.exit(75);}require('./server-puvodni.js');" | Set-Content "$T\prog\program\server.js" -Encoding ASCII
$a = Start-Process $Exe -PassThru
Ok 'po kodu 75 se server spusti znovu' ((Cekej { (Bezi) -ne '' }) -and (Test-Path "$T\prog\program\znacka") -and -not $a.HasExited)
[void](Kod @('--ukoncit')); [void](Cekej { $a.HasExited } 10)
Ok 'na konci nic nebezi' ((Uzly) -eq 0 -and @(Get-Process Fakturator -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe }).Count -eq 0)
if ($chyby) { "VYSLEDEK: $chyby chyb"; exit 1 } else { 'VYSLEDEK: vse v poradku'; exit 0 }
