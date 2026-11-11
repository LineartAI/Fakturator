;  Instalator Fakturatoru pro Windows (Inno Setup 6). Instaluje jen pro prihlaseneho uzivatele, bez prav spravce:
;    {localappdata}\Programs\Fakturator   Fakturator.exe, runtime\win\node.exe, program\ (vychozi kopie programu)
;    zastupce na plose a v nabidce Start
;  Pracovni kopie programu (%LOCALAPPDATA%\Fakturator) vznika az pri spusteni a odinstalace ji smaze.
;  Data uzivatele (Dokumenty\Fakturator) instalator ani odinstalace NIKDY nemazou.
;  Preklad:  ISCC /DVerze=1.8.0 /DStavba=C:\cesta\stavba /Ovystup nastroje\win\Fakturator.iss
#ifndef Verze
  #define Verze "0.0.0"
#endif
#ifndef Stavba
  #define Stavba "..\..\stavba"
#endif

[Setup]
AppId={{7C1D7B0E-5E0B-4C61-9E7B-FA4C70A70B11}
AppName=Fakturator
AppVersion={#Verze}
AppVerName=Fakturator {#Verze}
AppPublisher=Lineart
AppPublisherURL=https://github.com/LineartAI/Fakturator
DefaultDirName={localappdata}\Programs\Fakturator
DisableDirPage=yes
DisableProgramGroupPage=yes
; stranka "Instalace je pripravena" zustava, aby mel pruvodce vzdy stejne kroky: Instalovat -> Dokoncit (videonavod na portalu)
DisableReadyPage=no
PrivilegesRequired=lowest
CloseApplications=no
OutputBaseFilename=Fakturator-instalace
SetupIconFile=Fakturator.ico
UninstallDisplayIcon={app}\Fakturator.exe
UninstallDisplayName=Fakturator
VersionInfoVersion={#Verze}
VersionInfoProductName=Fakturator
VersionInfoCompany=Lineart
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no

[Languages]
Name: "cs"; MessagesFile: "compiler:Languages\Czech.isl"

[Files]
Source: "{#Stavba}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{userdesktop}\Fakturator"; Filename: "{app}\Fakturator.exe"; Comment: "Fakturator"
Name: "{userprograms}\Fakturator"; Filename: "{app}\Fakturator.exe"; Comment: "Fakturator"

[Run]
Filename: "{app}\Fakturator.exe"; Description: "Spustit Fakturator"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Fakturator"

[Code]
// Bezici Fakturator se pred instalaci nove verze i pred odinstalaci sam ukonci (Fakturator.exe --ukoncit).
procedure UkonciBezici(Exe: String);
var
  Kod: Integer;
begin
  if FileExists(Exe) then
    Exec(Exe, '--ukoncit', '', SW_HIDE, ewWaitUntilTerminated, Kod);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  UkonciBezici(ExpandConstant('{app}\Fakturator.exe'));
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  UkonciBezici(ExpandConstant('{app}\Fakturator.exe'));
  Result := True;
end;
