#!/bin/bash
# Fakturator.app (macOS) - obsluha lokalniho serveru. Vola ji aplikace (Fakturator.applescript) a testy v GitHub Actions.
#   stav                 -> "novy" (prvni spusteni, slozka s daty jeste neni) | "hotovo"
#   prevest <slozka>     -> zkopiruje data/ a pdf-faktury/ ze stare slozky OpenFaktura
#   start <pid_aplikace> -> pripravi slozku, spusti server na pozadi a pocka, az odpovida
#   otevri               -> otevre bezici Fakturator v prohlizeci
#   bezi                 -> "ano" | "ne"
#   stop                 -> ukonci server
#   text <klic>          -> texty dialogu (UTF-8; zdroj AppleScriptu tak zustava ASCII)
# Program (server.js, web) se pri prvnim spusteni zkopiruje z aplikace do ~/Library/Application Support/Fakturator a dal se
# aktualizuje sam jako zip verze (aplikace v /Applications se sama menit nesmi). Data uzivatele jsou zvlast a viditelne:
# ~/Documents/Fakturator/data a ~/Documents/Fakturator/pdf-faktury (server.js je tam hleda podle OF_DATA_ROOT).
TADY="$(cd "$(dirname "$0")" && pwd)"
PROG="${FAKTURATOR_PROG:-$HOME/Library/Application Support/Fakturator}"
DATA="${FAKTURATOR_DATA:-$HOME/Documents/Fakturator}"
LOG="$PROG/fakturator.log"
PIDF="$PROG/fakturator.pid"

verze() { sed -n 's/.*"verze"[^"]*"\([0-9.]*\)".*/\1/p' "$1" 2>/dev/null | head -1; }
# program v aplikaci je novejsi nez nainstalovana kopie (uzivatel si stahl novou aplikaci)? 0 = ano
novejsi_v_aplikaci() { local a b; a="$(verze "$TADY/program/verze.json")"; b="$(verze "$PROG/verze.json")"; [ -n "$a" ] && [ "$a" != "$b" ] && [ "$(printf '%s\n%s\n' "$a" "$b" | sort -t. -k1,1n -k2,2n -k3,3n | tail -1)" = "$a" ]; }

node_cesta() { if [ "$(uname -m)" = "arm64" ]; then echo "$TADY/runtime/mac-arm64/node"; else echo "$TADY/runtime/mac-x64/node"; fi; }
bezi() { [ -f "$PIDF" ] && kill -0 "$(cat "$PIDF")" 2>/dev/null; }
adresa() { grep -o 'http://localhost:[0-9]*/' "$LOG" 2>/dev/null | tail -1; }

case "$1" in
  stav)
    if [ -d "$DATA/data" ]; then echo hotovo; else echo novy; fi ;;

  prevest)
    SRC="${2%/}"
    if [ ! -d "$SRC/data" ]; then
      echo "Ve vybrané složce není složka data. Vyber složku OpenFaktura, ve které jsou složky data a pdf-faktury." >&2; exit 2
    fi
    mkdir -p "$DATA" && cp -R "$SRC/data" "$DATA/" || { echo "Data se nepodařilo zkopírovat do $DATA. Povol aplikaci Fakturator přístup do Dokumentů (Nastavení systému → Soukromí a zabezpečení → Soubory a složky)." >&2; exit 2; }
    if [ -d "$SRC/pdf-faktury" ]; then cp -R "$SRC/pdf-faktury" "$DATA/"; fi
    echo prevedeno ;;

  start)
    if bezi; then U="$(adresa)"; [ -n "$U" ] && [ -z "$OF_NO_OPEN" ] && open "$U"; echo bezi; exit 0; fi
    mkdir -p "$PROG" || { echo "Nejde vytvořit složku $PROG." >&2; exit 3; }
    if [ ! -f "$PROG/server.js" ] || novejsi_v_aplikaci; then cp -R "$TADY/program/." "$PROG/" || { echo "Program se nepodařilo připravit ve složce $PROG." >&2; exit 3; }; fi
    mkdir -p "$DATA/data" "$DATA/pdf-faktury" 2>/dev/null || { echo "Nejde vytvořit složku $DATA. Povol aplikaci Fakturator přístup do Dokumentů (Nastavení systému → Soukromí a zabezpečení → Soubory a složky) a spusť ji znovu." >&2; exit 3; }
    NODE="$(node_cesta)"
    xattr -d com.apple.quarantine "$NODE" 2>/dev/null
    if [ ! -x "$NODE" ]; then echo "V aplikaci chybí část pro tento Mac ($(uname -m)). Stáhni Fakturator znovu z portálu." >&2; exit 3; fi
    : > "$LOG"
    nohup "$0" smycka "$2" >> "$LOG" 2>&1 < /dev/null &
    echo $! > "$PIDF"
    for _ in $(seq 1 60); do
      if [ -n "$(adresa)" ]; then echo spusteno; exit 0; fi
      bezi || break
      sleep 0.25
    done
    echo "Fakturator se nespustil. Podrobnosti jsou v souboru $LOG" >&2; exit 4 ;;

  smycka)
    # bezi na pozadi: navratovy kod 75 = server se po aktualizaci chce restartovat. Hlida aplikaci (pid v $2):
    # kdyz aplikace skonci, i nasilne, server se ukonci taky a nezustane bezet bez ikony v Docku.
    cd "$PROG" || exit 1
    export OF_DATA_ROOT="$DATA"
    NODE="$(node_cesta)"; APP_PID="$2"; NP=""
    trap '[ -n "$NP" ] && kill "$NP" 2>/dev/null; rm -f "$PIDF"; exit 0' TERM INT
    while true; do
      "$NODE" server.js & NP=$!
      while kill -0 "$NP" 2>/dev/null; do
        if [ -n "$APP_PID" ] && ! kill -0 "$APP_PID" 2>/dev/null; then kill "$NP" 2>/dev/null; wait "$NP" 2>/dev/null; rm -f "$PIDF"; exit 0; fi
        sleep 2
      done
      wait "$NP"; [ $? -eq 75 ] || break
    done
    rm -f "$PIDF" ;;

  otevri)
    U="$(adresa)"; [ -n "$U" ] && open "$U" ;;

  bezi)
    if bezi; then echo ano; else echo ne; fi ;;

  stop)
    if bezi; then kill "$(cat "$PIDF")" 2>/dev/null; fi
    rm -f "$PIDF" ;;

  text)
    case "$2" in
      uvod) printf '%s' "Vítej ve Fakturatoru.

Faktury a nastavení se ukládají do složky Dokumenty → Fakturator, hotová PDF do její podsložky pdf-faktury. Mac se za chvíli zeptá, jestli smí aplikace do Dokumentů: povol to.

Používal(a) jsi Fakturator už dřív ze složky OpenFaktura? Můžu z ní převést tvoje faktury a nastavení." ;;
      prevest) printf '%s' "Převést data" ;;
      nacisto) printf '%s' "Začít načisto" ;;
      vyber) printf '%s' "Vyber starou složku OpenFaktura (jsou v ní složky data a pdf-faktury):" ;;
      chyba_prevod) printf '%s' "Data se nepodařilo převést" ;;
      chyba_start) printf '%s' "Fakturator se nepodařilo spustit" ;;
    esac ;;

  *) echo "Neznámý příkaz: $1" >&2; exit 64 ;;
esac
