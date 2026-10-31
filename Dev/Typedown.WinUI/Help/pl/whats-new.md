# Co nowego w Caret

Najważniejsze zmiany, od najnowszych do najstarszych. Każda funkcja ma własny temat w tym menu; ta strona tylko mówi, gdzie szukać.

## Wersja 2.10: skany, zrzuty ekranu i przejrzystszy pasek boczny

- **Edycja** > **Wklej tekst z obrazu (OCR)** odczytuje zrzut ekranu lub obraz ze schowka (Win+Shift+S go tworzy) przez OCR systemu Windows, na Twoim komputerze, i wkleja tekst w miejsce kursora. Gdy na ekranie nie ma dokumentu, tekst otwiera się jako nowa notatka; jeśli zmienisz dokument w trakcie odczytu obrazu, tekst zostanie skopiowany do schowka.
- Na stronie **Dokumenty i skany** włącz **Czytaj także obrazy w folderach (OCR)**, a folder, który wybierzesz lub upuścisz, przekaże do OCR swoje obrazy (PNG, JPG, BMP, TIFF) oprócz dokumentów. Na początku jest wyłączone, a folder z 30 obrazami lub więcej pyta przed rozpoczęciem. Opcje OCR są teraz na stronie na pierwszym miejscu, a wiersz pod strefą upuszczania wymienia skany i obrazy.
- Pasek boczny ma trzy grupy: **Konwertuj** (**Dokumenty i skany**, wcześniej *Konwertuj na Markdown*, **E-maile z programu Outlook** i **Faktury w jednej tabeli**), **Ten dokument** (**Recenzja**, **Znaczniki przemówienia** i, dla pliku XML, **Narzędzia XML**) oraz **Biblioteka**. **Zamaskuj dane osobowe...** jest też w panelu **Recenzja**, a karta na dole pokazuje wskazówkę.

## Wersja 2.9: Brazylia i Portugalia, narzędzia XML, skany

- Więcej dokumentów elektronicznych prostymi słowami: krajowy NFS-e Brazylii (faktura za usługi), CT-e (dokument przewozowy) i MDF-e (manifest), wraz ze zdarzeniami, oraz portugalski plik kontrolny SAF-T (PT), ze sprawdzeniem sum i podatków. Folder faktur (*Faktury w jednej tabeli*) może je mieszać: CT-e zastąpiony przez CT-e zastępczy jest wymieniony, ale nie wliczany do sum. Zobacz temat *Konwertuj na Markdown*.
- Pliki XML: struktura na liście *Konspekt* i na mapie dokumentu; fragment XML z kilkoma elementami obok siebie jest odczytywany i formatowany jako fragment; **Edycja** > **Wklej jako XML** wkłada zawartość schowka do notatki jako blok kodu i otwiera ją jako dokument XML; kliknięcie wartości w wersji czytelnej zaznacza ją w kodzie.
- **Edycja** > **Zamaskuj dane osobowe...** ukrywa adresy e-mail, numery telefonów, IBAN-y, numery kart, numery dokumentów i wpisane nazwiska w każdej notatce i pliku XML: w kopii albo w samej notatce (jedno Cofnij ją przywraca).
- PDF: konwertuj tylko niektóre strony (pole *Strony PDF* na stronie Konwertuj); czytaj skany i zdjęcia stron przez OCR systemu Windows, na Twoim komputerze; a przy eksporcie dodaj znak wodny, nagłówek, stopkę i numery stron (**Plik** > **Eksportuj** > **PDF...**).

## Wersja 2.7: pliki XML, prostymi słowami

- Czytaj plik XML jak dokument. Otwórz go, upuść na okno albo wybierz na stronie Konwertuj. Caret zna faktury elektroniczne Kolumbii (DIAN), Meksyku (CFDI), Brazylii (NF-e i NFC-e), Peru (SUNAT) i Chile (DTE) oraz inne faktury UBL, a ich kody objaśnia słowami (po angielsku, hiszpańsku lub portugalsku). Każdy inny XML staje się konspektem. Zobacz temat *Konwertuj na Markdown*.
- Folder z fakturami jako jedna tabela (strona Konwertuj, *Faktury w jednej tabeli*): Excel, CSV lub Markdown, z odjętymi fakturami korygującymi i duplikatami policzonymi raz.
- Edytuj pliki XML. Otwierają się jako kod, z czytelną wersją obok. Pasek prostymi słowami mówi, czy XML jest poprawnie zbudowany i gdzie nie jest; polecenie **Edycja** > **Formatuj XML** porządkuje tylko białe znaki; polecenie **Edycja** > **Sprawdź ze schematem...** porównuje plik z XSD. Plik jest zapisywany tak, jak był napisany. Zobacz *Edycja plików XML* w tym samym temacie.
- **Przejdź do pliku** (`Ctrl+K`) pokazuje pliki XML, a Windows proponuje Caret na liście *Otwórz za pomocą* dla plików .xml, .xsd, .xsl i .xslt.
- Obrazy podróżują z notatką: **Plik** > **Eksportuj** > **Markdown z obrazami (.zip)...**, zbierz obrazy obok notatki oraz **Akapit** > **Wykres z tabeli...**

## Wersja 2.6: okno przychodzi do Ciebie

- **Paleta poleceń** (`Ctrl+Shift+P`, **Widok** > **Paleta poleceń**): wpisz kilka liter tego, co chcesz zrobić, polecenia, ustawienia lub notatki, i naciśnij Enter.
- **Panel inspektora** (`Ctrl+Shift+I`, **Widok** > **Panel inspektora**): kolumna obok notatki z jej konspektem, znacznikami recenzji, czasem przemówienia, wszystkimi trafieniami wyszukiwania i **Historia** notatki, czyli wcześniejsze zapisane wersje, które można porównać i przywrócić. Kopie zapisanych notatek zostają na Twoim komputerze.
- **Mapa dokumentu** (**Widok** > **Mapa dokumentu**): cienki pasek przy prawej krawędzi pokazuje, w którym miejscu notatki jesteś i gdzie są nagłówki, komentarze i zmiany.
- Pasek stanu, który mówi to, co ważne: kiedy plik został zapisany, liczba słów i czas czytania, zaznaczenie, znaczniki recenzji. Segment, który nie ma nic do powiedzenia, nie jest pokazywany.
- Mały pasek narzędzi nad zaznaczonymi słowami, ze zwykłymi formatami i przyciskiem, który wykonuje **Kopiuj jako tekst WhatsApp**.
- Upuść pliki w dowolnym miejscu okna (Word, PDF, Excel, PowerPoint, CSV, e-maile lub folder), aby zamienić je na Markdown. Taca pokazuje zadania, z Otwórz, Pokaż w folderze, Porównaj i Ponów.
- Karty wyglądu (**Ustawienia** > **Wygląd**): zobacz schemat kolorów w podglądzie na żywo, znajdź to, co zmieniono, i wróć do schematu jednym kliknięciem.
- Najpierw klawiatura: naciśnij `Alt`, aby zobaczyć, który klawisz dociera do każdego przycisku, oraz `Ctrl+Shift+/`, aby zobaczyć listę wszystkich skrótów (**Skróty klawiaturowe** w menu Pomoc).
- Strona główna jako strona robocza: pliki, nad którymi pracowałeś, to, co możesz zacząć, i Twoje foldery, w trzech kolumnach.
- Poprawki: kolory całego okna od razu podążają za zmianą motywu, a podpowiedzi klawiszy znikają, gdy okno się przewija.

## Wersja 2.5: wyślij to jako plik Worda

**Plik** > **Eksportuj** > **Dokument Word (.docx)** zapisuje prawdziwy plik Worda na Twoim komputerze, z nagłówkami, listami, tabelami, obrazami, przypisami, spisem treści, śledzonymi zmianami i komentarzami. Word nie musi być zainstalowany. Zobacz temat *Eksport do Worda*.

## Wersja 2.0: napisz to, śledź zmiany, powiedz to

- Śledzenie zmian podczas edycji (**Recenzja**), komentarze, akceptowanie lub odrzucanie i porównywanie pliku z wcześniejszą wersją.
- **Znaczniki przemówienia** i **teleprompter** do wystąpień, z zegarem i próbą.
- Sprawdzanie pisowni, **Kopiuj jako tekst WhatsApp**, menu Pomoc oraz brazylijski portugalski, polski i hiszpański dla wszystkich krajów hiszpańskojęzycznych.

## Wersja 1.6: pliki z Eksploratora

Kliknięcie prawym przyciskiem Konwertuj na Markdown w Eksploratorze plików, kartę można upuścić w innym oknie Caret, menu kontekstowe w edytorze i znacznie lepsza konwersja prawdziwych plików PDF, Word, Excel, PowerPoint i e-maili.

## Wersja 1.5

Kartę można przeciągnąć do własnego okna, a pasek boczny przewija się i zwija. Maskowanie danych osobowych w e-mailach znajduje też imiona w zwrotach na początku i na końcu wiadomości.

## Wersja 1.4: karty i nowy wygląd

Kilka dokumentów w jednym oknie, strona Ustawień, schematy kolorów, układy i lepszy kontrast.

## Wersja 1.3: e-maile z programu Outlook

**E-maile z programu Outlook** zamieniają się w czysty Markdown, jeden plik na całą rozmowę, z zamaskowanymi danymi osobowymi, jeśli chcesz.

## Wersja 1.2: Konwertuj na Markdown

**Konwertuj na Markdown** czyta pliki Word, PDF, Excel i PowerPoint, doszły francuski i hiszpański, a polskie litery są poprawne w plikach, które nie są w UTF-8.

## Wersja 1.0

Sam edytor: edytor Markdown, który pokazuje tekst tak, jak będzie wyglądał, z tabelami, łączami i obrazami, ulubionymi i ostatnimi plikami, bez wysyłania czegokolwiek gdziekolwiek.
