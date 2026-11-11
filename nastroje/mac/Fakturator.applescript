-- Fakturator.app (macOS): aplikace bez okna, ktera spusti lokalni server Fakturatoru a otevre ho v prohlizeci.
-- Sestavuje se v GitHub Actions (osacompile -s = zustava bezet v Docku). Texty dialogu vraci spust.sh (UTF-8), zdroj je ASCII.
-- Klik na ikonu v Docku = otevrit Fakturator v prohlizeci, Ukoncit = zastavit server.
property skript : ""

on text_(klic)
	return do shell script skript & " text " & klic
end text_

on run
	set skript to quoted form of POSIX path of (path to resource "spust.sh")
	set stav to "hotovo"
	try
		set stav to do shell script skript & " stav"
	end try
	if stav is "novy" then
		set tPrevest to text_("prevest")
		set odpoved to button returned of (display dialog text_("uvod") buttons {text_("nacisto"), tPrevest} default button 2 with title "Fakturator")
		if odpoved is tPrevest then
			try
				set slozka to POSIX path of (choose folder with prompt text_("vyber"))
				do shell script skript & " prevest " & quoted form of slozka
			on error zprava number cislo
				if cislo is not -128 then display alert text_("chyba_prevod") message zprava
			end try
		end if
	end if
	try
		set mojePid to do shell script "echo $PPID"
		do shell script skript & " start " & mojePid
	on error zprava
		display alert text_("chyba_start") message zprava
		quit
	end try
end run

on reopen
	try
		do shell script skript & " otevri"
	end try
end reopen

on idle
	-- server skoncil sam (napr. chyba): ukoncit i aplikaci, at v Docku nezustane ikona bez programu
	try
		if (do shell script skript & " bezi") is not "ano" then quit
	end try
	return 5
end idle

on quit
	try
		do shell script skript & " stop"
	end try
	continue quit
end quit
